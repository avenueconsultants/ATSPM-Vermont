#region license
// Copyright 2026 Utah Departement of Transportation
// for IdentityTests - Utah.Udot.Atspm.IdentityTests.Services/AccountServiceSsoTests.cs
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
// http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
#endregion

using Identity.Business.Accounts;
using Identity.Business.Tokens;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Moq;
using System.Security.Claims;
using Utah.Udot.Atspm.Data.Models;
using Xunit;

namespace Utah.Udot.Atspm.IdentityTests.Services
{
    public class AccountServiceSsoTests
    {
        [Fact]
        public async Task HandleSsoRequest_WithNewSsoUser_CreatesLinksAndSignsInUser()
        {
            var userManagerMock = BuildUserManagerMock();
            var signInManagerMock = BuildSignInManagerMock(userManagerMock);
            var roleManagerMock = BuildRoleManagerMock();
            var service = BuildService(userManagerMock, signInManagerMock, roleManagerMock);
            var email = "new.user@example.com";
            ApplicationUser? createdUser = null;

            userManagerMock.Setup(manager => manager.FindByEmailAsync(email))
                .ReturnsAsync(() => createdUser);
            userManagerMock.Setup(manager => manager.FindByLoginAsync("oidc-entra", "entra-user-1"))
                .ReturnsAsync((ApplicationUser?)null);
            userManagerMock.Setup(manager => manager.CreateAsync(It.IsAny<ApplicationUser>()))
                .Callback<ApplicationUser>(user => createdUser = user)
                .ReturnsAsync(IdentityResult.Success);
            userManagerMock.Setup(manager => manager.AddLoginAsync(It.IsAny<ApplicationUser>(), It.IsAny<UserLoginInfo>()))
                .ReturnsAsync(IdentityResult.Success);
            userManagerMock.Setup(manager => manager.GetRolesAsync(It.IsAny<ApplicationUser>()))
                .ReturnsAsync(new List<string>());
            signInManagerMock.Setup(manager => manager.SignInAsync(It.IsAny<ApplicationUser>(), false, null))
                .Returns(Task.CompletedTask);

            var result = await service.HandleSsoRequest(BuildExternalLoginInfo(
                providerUserId: "entra-user-1",
                email: email,
                firstName: "New",
                lastName: "User"));

            Assert.Equal(StatusCodes.Status200OK, result.Code);
            Assert.NotNull(createdUser);
            Assert.Equal(email, createdUser!.Email);
            Assert.Equal("New", createdUser.FirstName);
            Assert.Equal("User", createdUser.LastName);
            userManagerMock.Verify(manager => manager.CreateAsync(It.IsAny<ApplicationUser>()), Times.Once);
            userManagerMock.Verify(manager => manager.AddLoginAsync(It.IsAny<ApplicationUser>(), It.IsAny<UserLoginInfo>()), Times.Once);
            signInManagerMock.Verify(manager => manager.SignInAsync(createdUser, false, null), Times.Once);
        }

        [Fact]
        public async Task HandleSsoRequest_WithExistingEmailUser_LinksLoginAndSignsInUser()
        {
            var userManagerMock = BuildUserManagerMock();
            var signInManagerMock = BuildSignInManagerMock(userManagerMock);
            var roleManagerMock = BuildRoleManagerMock();
            var service = BuildService(userManagerMock, signInManagerMock, roleManagerMock);
            var existingUser = new ApplicationUser
            {
                Id = "user-1",
                Email = "existing.user@example.com",
                FirstName = "Existing",
                LastName = "User",
                UserName = "existing.user@example.com"
            };

            userManagerMock.Setup(manager => manager.FindByEmailAsync(existingUser.Email!))
                .ReturnsAsync(existingUser);
            userManagerMock.Setup(manager => manager.FindByLoginAsync("oidc-entra", "entra-user-2"))
                .ReturnsAsync((ApplicationUser?)null);
            userManagerMock.Setup(manager => manager.AddLoginAsync(existingUser, It.IsAny<UserLoginInfo>()))
                .ReturnsAsync(IdentityResult.Success);
            userManagerMock.Setup(manager => manager.GetRolesAsync(existingUser))
                .ReturnsAsync(new List<string>());
            signInManagerMock.Setup(manager => manager.SignInAsync(existingUser, false, null))
                .Returns(Task.CompletedTask);

            var result = await service.HandleSsoRequest(BuildExternalLoginInfo(
                providerUserId: "entra-user-2",
                email: existingUser.Email!,
                firstName: existingUser.FirstName,
                lastName: existingUser.LastName));

            Assert.Equal(StatusCodes.Status200OK, result.Code);
            userManagerMock.Verify(manager => manager.CreateAsync(It.IsAny<ApplicationUser>()), Times.Never);
            userManagerMock.Verify(manager => manager.AddLoginAsync(existingUser, It.IsAny<UserLoginInfo>()), Times.Once);
            signInManagerMock.Verify(manager => manager.SignInAsync(existingUser, false, null), Times.Once);
        }

        [Fact]
        public async Task HandleSsoRequest_WithLinkedUser_SignsInLinkedUser()
        {
            var userManagerMock = BuildUserManagerMock();
            var signInManagerMock = BuildSignInManagerMock(userManagerMock);
            var roleManagerMock = BuildRoleManagerMock();
            var service = BuildService(userManagerMock, signInManagerMock, roleManagerMock);
            var linkedUser = new ApplicationUser
            {
                Id = "user-2",
                Email = "linked.user@example.com",
                FirstName = "Linked",
                LastName = "User",
                UserName = "linked.user@example.com"
            };

            userManagerMock.Setup(manager => manager.FindByEmailAsync(linkedUser.Email!))
                .ReturnsAsync((ApplicationUser?)null);
            userManagerMock.Setup(manager => manager.FindByLoginAsync("oidc-entra", "entra-user-3"))
                .ReturnsAsync(linkedUser);
            userManagerMock.Setup(manager => manager.GetRolesAsync(linkedUser))
                .ReturnsAsync(new List<string>());
            signInManagerMock.Setup(manager => manager.SignInAsync(linkedUser, false, null))
                .Returns(Task.CompletedTask);

            var result = await service.HandleSsoRequest(BuildExternalLoginInfo(
                providerUserId: "entra-user-3",
                email: linkedUser.Email!,
                firstName: linkedUser.FirstName,
                lastName: linkedUser.LastName));

            Assert.Equal(StatusCodes.Status200OK, result.Code);
            userManagerMock.Verify(manager => manager.AddLoginAsync(It.IsAny<ApplicationUser>(), It.IsAny<UserLoginInfo>()), Times.Never);
            signInManagerMock.Verify(manager => manager.SignInAsync(linkedUser, false, null), Times.Once);
        }

        private static AccountService BuildService(
            Mock<UserManager<ApplicationUser>> userManagerMock,
            Mock<SignInManager<ApplicationUser>> signInManagerMock,
            Mock<RoleManager<IdentityRole>> roleManagerMock)
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Jwt:Key"] = "0123456789abcdef0123456789abcdef",
                    ["Jwt:Issuer"] = "identity-tests",
                    ["Jwt:ExpireDays"] = "1"
                })
                .Build();

            var tokenService = new TokenService(configuration, signInManagerMock.Object, roleManagerMock.Object);
            return new AccountService(userManagerMock.Object, signInManagerMock.Object, tokenService, roleManagerMock.Object);
        }

        private static ExternalLoginInfo BuildExternalLoginInfo(
            string providerUserId,
            string email,
            string firstName,
            string lastName)
        {
            var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, providerUserId),
                new Claim(ClaimTypes.Email, email),
                new Claim(ClaimTypes.GivenName, firstName),
                new Claim(ClaimTypes.Surname, lastName),
                new Claim(ClaimTypes.Name, $"{firstName} {lastName}")
            }, "oidc"));

            return new ExternalLoginInfo(principal, "oidc-entra", providerUserId, "Microsoft Entra");
        }

        private static Mock<UserManager<ApplicationUser>> BuildUserManagerMock()
        {
            var userStoreMock = new Mock<IUserStore<ApplicationUser>>();
            return new Mock<UserManager<ApplicationUser>>(
                userStoreMock.Object,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null);
        }

        private static Mock<SignInManager<ApplicationUser>> BuildSignInManagerMock(
            Mock<UserManager<ApplicationUser>> userManagerMock)
        {
            return new Mock<SignInManager<ApplicationUser>>(
                userManagerMock.Object,
                Mock.Of<IHttpContextAccessor>(),
                Mock.Of<IUserClaimsPrincipalFactory<ApplicationUser>>(),
                null,
                null,
                null,
                null);
        }

        private static Mock<RoleManager<IdentityRole>> BuildRoleManagerMock()
        {
            var roleStoreMock = new Mock<IRoleStore<IdentityRole>>();
            return new Mock<RoleManager<IdentityRole>>(
                roleStoreMock.Object,
                null,
                null,
                null,
                null);
        }
    }
}
