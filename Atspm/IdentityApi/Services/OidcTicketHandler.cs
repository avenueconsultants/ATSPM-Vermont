#region license
// Copyright 2026 Utah Departement of Transportation
// for IdentityApi - IdentityApi.Services/OidcTicketHandler.cs
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

using Identity.Business.Accounts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using System.Security.Claims;
using Utah.Udot.Atspm.Data.Models;
using Utah.Udot.Atspm.Infrastructure.Services;

namespace IdentityApi.Services
{
    public class OidcTicketHandler : IOidcTicketHandler
    {
        private readonly IAccountService accountService;
        private readonly IConfiguration configuration;

        public OidcTicketHandler(IAccountService accountService, IConfiguration configuration)
        {
            this.accountService = accountService;
            this.configuration = configuration;
        }

        public async Task HandleTicketReceivedAsync(
            TicketReceivedContext context,
            string providerKey,
            string schemeName,
            string displayName)
        {
            var principal = context.Principal;
            if (principal == null)
            {
                RedirectWithError(context, "External login information not available.");
                return;
            }

            var providerUserId = principal.FindFirstValue(ClaimTypes.NameIdentifier);

            if (string.IsNullOrWhiteSpace(providerUserId))
            {
                RedirectWithError(context, "External login identifier not available.");
                return;
            }

            var info = new ExternalLoginInfo(principal, schemeName, providerUserId, displayName);
            var result = await accountService.HandleSsoRequest(info);

            var redirectUrl = result.Code == StatusCodes.Status200OK
                ? QueryHelpers.AddQueryString(GetSsoRedirectBase(), new Dictionary<string, string?>
                {
                    ["token"] = result.Token,
                    ["claims"] = string.Join(",", result.Claims)
                })
                : QueryHelpers.AddQueryString(GetSsoRedirectBase(), "error", result.Message ?? "Issue validating SSO");

            context.Response.Redirect(redirectUrl);
            context.HandleResponse();
        }

        private string GetSsoRedirectBase()
        {
            var atspmSite = configuration["AtspmSite"]?.TrimEnd('/');
            return $"{atspmSite}/sso-login";
        }

        private void RedirectWithError(TicketReceivedContext context, string errorMessage)
        {
            var redirectUrl = QueryHelpers.AddQueryString(GetSsoRedirectBase(), "error", errorMessage);
            context.Response.Redirect(redirectUrl);
            context.HandleResponse();
        }
    }
}
