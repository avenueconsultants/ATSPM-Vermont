#region license
// Copyright 2026 Utah Departement of Transportation
// for IdentityApi - Identity.Business.Accounts/AccountService.cs
// 
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// 
// http://www.apache.org/licenses/LICENSE-2.
// 
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
#endregion

using Identity.Business.Tokens;
using Microsoft.AspNetCore.Identity;
using System.Security.Claims;
using Utah.Udot.Atspm.Data.Models;

namespace Identity.Business.Accounts
{
    public class AccountService : IAccountService
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly TokenService tokenService;
        private readonly RoleManager<IdentityRole> _roleManager;

        public AccountService(
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            TokenService tokenService,
            RoleManager<IdentityRole> roleManager)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            this.tokenService = tokenService;
            this._roleManager = roleManager;
        }

        public async Task<AccountResult> CreateUser(ApplicationUser user, string password)
        {
            var createUserResult = await _userManager.CreateAsync(user, password);

            if (createUserResult.Succeeded)
            {
                //await userManager.AddToRoleAsync(user, "User");
                if (user.Email == null)
                {
                    return new AccountResult(StatusCodes.Status400BadRequest, "", new List<string>(), "Email is required");
                }
                return await Login(user.Email, password);
            }

            return new AccountResult(StatusCodes.Status400BadRequest, "", new List<string>(),
                createUserResult.Errors.First().Description);
        }


        public async Task<AccountResult> Login(string email, string password, bool rememberMe = false)
        {
            var user = await _signInManager.UserManager.FindByEmailAsync(email);
            if (user == null)
            {
                return new AccountResult(StatusCodes.Status400BadRequest, "", new List<string>(), "User not found");
            }

            var result = await _signInManager.CheckPasswordSignInAsync(user, password, lockoutOnFailure: false);

            if (result.Succeeded)
            {
                var token = await tokenService.GenerateJwtTokenAsync(user);
                var viewClaims = await GetViewClaimsForUser(user);
                return new AccountResult(StatusCodes.Status200OK, token, viewClaims, null);
            }

            return new AccountResult(StatusCodes.Status400BadRequest, "", new List<string>(), "Incorrect username or password");
        }

        public async Task<AccountResult> HandleSsoRequest(ExternalLoginInfo info)
        {
            string token = "";
            List<string> viewClaims = new List<string>();

            var claims = info.Principal.Claims;
            var email = GetClaimValue(claims, ClaimTypes.Email, "email", "preferred_username", "upn");
            if (string.IsNullOrWhiteSpace(email))
            {
                var message = "Unable to access information from SSO, try again later";
                return new AccountResult(StatusCodes.Status400BadRequest, "", new List<string>(), message);
            }

            var fullName = GetClaimValue(claims, ClaimTypes.Name, "name");
            var firstName = GetClaimValue(claims, ClaimTypes.GivenName, "given_name");
            var lastName = GetClaimValue(claims, ClaimTypes.Surname, "family_name");

            if (string.IsNullOrWhiteSpace(firstName) || string.IsNullOrWhiteSpace(lastName))
            {
                (firstName, lastName) = SplitName(fullName);
            }

            if (string.IsNullOrWhiteSpace(firstName))
            {
                firstName = email.Split('@', 2)[0];
            }

            if (string.IsNullOrWhiteSpace(lastName))
            {
                lastName = ".";
            }

            var user = await _signInManager.UserManager.FindByEmailAsync(email);
            var linkedUser = await _userManager.FindByLoginAsync(info.LoginProvider, info.ProviderKey);

            if (user == null && linkedUser == null)
            {
                var createUserResult = await CreateUserAndLinkLogin(email, firstName, lastName, info);
                if (createUserResult.Succeeded)
                {
                    var newUser = await _signInManager.UserManager.FindByEmailAsync(email);
                    if (newUser == null)
                    {
                        return new AccountResult(StatusCodes.Status400BadRequest, "", new List<string>(), "Issue creating user");
                    }
                    await _signInManager.SignInAsync(newUser, isPersistent: false);
                    token = await tokenService.GenerateJwtTokenAsync(newUser);
                    viewClaims = await GetViewClaimsForUser(newUser);
                    return new AccountResult(StatusCodes.Status200OK, token, viewClaims, null);
                }
                return new AccountResult(StatusCodes.Status400BadRequest, "", new List<string>(), "Issue validating SSO");
            }

            if (user != null && linkedUser == null)
            {
                var linkResult = await _userManager.AddLoginAsync(user, info);
                if (linkResult.Succeeded)
                {
                    await _signInManager.SignInAsync(user, isPersistent: false);
                    token = await tokenService.GenerateJwtTokenAsync(user);
                    viewClaims = await GetViewClaimsForUser(user);
                    return new AccountResult(StatusCodes.Status200OK, token, viewClaims, null);
                }
                return new AccountResult(StatusCodes.Status400BadRequest, "", new List<string>(), "Issue linking external login");
            }

            if (linkedUser != null)
            {
                await _signInManager.SignInAsync(linkedUser, isPersistent: false);
                token = await tokenService.GenerateJwtTokenAsync(linkedUser);
                viewClaims = await GetViewClaimsForUser(linkedUser);
                return new AccountResult(StatusCodes.Status200OK, token, viewClaims, null);
            }

            return new AccountResult(StatusCodes.Status400BadRequest, "", new List<string>(), "Unhandled SSO scenario");
        }

        private async Task<IdentityResult> CreateUserAndLinkLogin(string email, string firstName, string lastName, ExternalLoginInfo info)
        {
            var newUser = new ApplicationUser
            {
                UserName = email,
                Email = email,
                Agency = "",
                FirstName = firstName,
                LastName = lastName
            };

            var createUserResult = await _userManager.CreateAsync(newUser);
            if (createUserResult.Succeeded)
            {
                return await _userManager.AddLoginAsync(newUser, info);
            }

            return createUserResult;
        }

        private static string? GetClaimValue(IEnumerable<Claim> claims, params string[] claimTypes)
        {
            return claimTypes
                .Select(claimType => claims.FirstOrDefault(c => c.Type == claimType)?.Value)
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        }

        private static (string? FirstName, string? LastName) SplitName(string? fullName)
        {
            if (string.IsNullOrWhiteSpace(fullName))
            {
                return (null, null);
            }

            var parts = fullName
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            if (parts.Length == 0)
            {
                return (null, null);
            }

            if (parts.Length == 1)
            {
                return (parts[0], null);
            }

            return (parts[0], string.Join(" ", parts.Skip(1)));
        }

        private async Task<List<string>> GetViewClaimsForUser(ApplicationUser user)
        {
            var claims = new List<string>();
            var roles = await _userManager.GetRolesAsync(user);
            if (roles.Contains("Admin"))
            {
                claims.Add("Admin");
            }
            else
            {
                foreach (var roleName in roles)
                {
                    var role = await _roleManager.FindByNameAsync(roleName);
                    var roleClaims = await _roleManager.GetClaimsAsync(role);
                    foreach (var roleClaim in roleClaims)
                    {
                        claims.Add(roleClaim.Value);
                    }
                }
            }

            return claims;
        }
    }
}
