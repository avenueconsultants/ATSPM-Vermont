#region license
// Copyright 2026 Utah Departement of Transportation
// for IdentityTests - Utah.Udot.Atspm.IdentityTests.Services/OidcTicketHandlerTests.cs
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
using IdentityApi.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Moq;
using System.Security.Claims;
using Xunit;

namespace Utah.Udot.Atspm.IdentityTests.Services
{
    public class OidcTicketHandlerTests
    {
        [Fact]
        public async Task HandleTicketReceivedAsync_WithNormalizedIdentifier_UsesNameIdentifierForProviderKey()
        {
            var accountServiceMock = new Mock<IAccountService>();
            accountServiceMock.Setup(service => service.HandleSsoRequest(It.IsAny<ExternalLoginInfo>()))
                .ReturnsAsync(new AccountResult(StatusCodes.Status200OK, "jwt-token", new List<string> { "Admin" }, null));

            var handler = BuildHandler(accountServiceMock);
            var context = BuildContext(new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "normalized-id"),
                new Claim("sub", "raw-sub"),
                new Claim("oid", "raw-oid")
            }, "oidc")));

            await handler.HandleTicketReceivedAsync(context, "entra", "oidc-entra", "Microsoft Entra");

            accountServiceMock.Verify(service => service.HandleSsoRequest(
                It.Is<ExternalLoginInfo>(info => info.ProviderKey == "normalized-id")), Times.Once);
            Assert.Equal("https://atspm.example/sso-login?token=jwt-token&claims=Admin", context.Response.Headers.Location.ToString());
        }

        [Fact]
        public async Task HandleTicketReceivedAsync_WithoutNormalizedIdentifier_RedirectsWithError()
        {
            var accountServiceMock = new Mock<IAccountService>();
            var handler = BuildHandler(accountServiceMock);
            var context = BuildContext(new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim("sub", "raw-sub-only")
            }, "oidc")));

            await handler.HandleTicketReceivedAsync(context, "entra", "oidc-entra", "Microsoft Entra");

            accountServiceMock.Verify(service => service.HandleSsoRequest(It.IsAny<ExternalLoginInfo>()), Times.Never);
            Assert.Equal(
                "https://atspm.example/sso-login?error=External+login+identifier+not+available.",
                context.Response.Headers.Location.ToString());
        }

        private static OidcTicketHandler BuildHandler(Mock<IAccountService> accountServiceMock)
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["AtspmSite"] = "https://atspm.example"
                })
                .Build();

            return new OidcTicketHandler(accountServiceMock.Object, configuration);
        }

        private static TicketReceivedContext BuildContext(ClaimsPrincipal principal)
        {
            var httpContext = new DefaultHttpContext();
            var scheme = new AuthenticationScheme("oidc-entra", "oidc-entra", typeof(OpenIdConnectHandler));
            var options = new OpenIdConnectOptions();
            var properties = new AuthenticationProperties();
            var ticket = new AuthenticationTicket(principal, properties, scheme.Name);

            return new TicketReceivedContext(httpContext, scheme, options, ticket);
        }
    }
}
