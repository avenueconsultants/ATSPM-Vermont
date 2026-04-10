#region license
// Copyright 2026 Utah Departement of Transportation
// for IdentityTests - Utah.Udot.Atspm.IdentityTests.Controllers/AccountControllerTests.cs
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
using Identity.Controllers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Moq;
using Utah.Udot.Atspm.Data.Models;
using Utah.Udot.Atspm.Infrastructure.Configuration;
using Utah.Udot.NetStandardToolkit.Services;
using Xunit;

namespace Utah.Udot.Atspm.IdentityTests.Controllers
{
    public class AccountControllerTests
    {
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

        private static AccountController BuildController(
            Mock<SignInManager<ApplicationUser>>? signInManagerMock = null,
            OidcProviderOptions? oidcProviderOptions = null)
        {
            var userManagerMock = BuildUserManagerMock();
            signInManagerMock ??= BuildSignInManagerMock(userManagerMock);

            var accountServiceMock = new Mock<IAccountService>();
            var emailServiceMock = new Mock<IEmailService>();

            return new AccountController(
                userManagerMock.Object,
                signInManagerMock.Object,
                accountServiceMock.Object,
                emailServiceMock.Object,
                Options.Create(oidcProviderOptions ?? new OidcProviderOptions()));
        }

        [Fact]
        public void ExternalLogin_WithConfiguredProvider_ReturnsChallengeForMatchingScheme()
        {
            var userManagerMock = BuildUserManagerMock();
            var signInManagerMock = BuildSignInManagerMock(userManagerMock);
            var expectedProperties = new AuthenticationProperties();

            signInManagerMock
                .Setup(manager => manager.ConfigureExternalAuthenticationProperties("oidc-utahid", "/", null))
                .Returns(expectedProperties);

            var controller = BuildController(
                signInManagerMock,
                new OidcProviderOptions
                {
                    Providers = new Dictionary<string, OidcProviderConfiguration>
                    {
                        ["utahid"] = new()
                        {
                            DisplayName = "Utah Id"
                        }
                    }
                });

            var result = controller.ExternalLogin("utahid");

            var challengeResult = Assert.IsType<ChallengeResult>(result);
            Assert.Equal(expectedProperties, challengeResult.Properties);
            Assert.Single(challengeResult.AuthenticationSchemes);
            Assert.Equal("oidc-utahid", challengeResult.AuthenticationSchemes[0]);
        }

        [Fact]
        public void ExternalLogin_WithCustomScheme_ReturnsChallengeForConfiguredScheme()
        {
            var userManagerMock = BuildUserManagerMock();
            var signInManagerMock = BuildSignInManagerMock(userManagerMock);
            var expectedProperties = new AuthenticationProperties();

            signInManagerMock
                .Setup(manager => manager.ConfigureExternalAuthenticationProperties("entra-scheme", "/", null))
                .Returns(expectedProperties);

            var controller = BuildController(
                signInManagerMock,
                new OidcProviderOptions
                {
                    Providers = new Dictionary<string, OidcProviderConfiguration>
                    {
                        ["entra"] = new()
                        {
                            DisplayName = "Microsoft Entra",
                            Scheme = "entra-scheme"
                        }
                    }
                });

            var result = controller.ExternalLogin("ENTRA");

            var challengeResult = Assert.IsType<ChallengeResult>(result);
            Assert.Equal(expectedProperties, challengeResult.Properties);
            Assert.Single(challengeResult.AuthenticationSchemes);
            Assert.Equal("entra-scheme", challengeResult.AuthenticationSchemes[0]);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("unknown")]
        public void ExternalLogin_WithUnknownProvider_ReturnsBadRequest(string? provider)
        {
            var controller = BuildController(
                oidcProviderOptions: new OidcProviderOptions
                {
                    Providers = new Dictionary<string, OidcProviderConfiguration>
                    {
                        ["utahid"] = new()
                    }
                });

            var result = controller.ExternalLogin(provider!);

            var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal("Unknown external login provider.", badRequestResult.Value);
        }
    }
}
