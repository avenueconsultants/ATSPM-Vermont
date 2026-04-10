#region license
// Copyright 2026 Utah Departement of Transportation
// for IdentityTests - Utah.Udot.Atspm.IdentityTests.Infrastructure/AuthenticationExtensionsTests.cs
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

using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using Utah.Udot.Atspm.Infrastructure.Extensions;
using Xunit;

namespace Utah.Udot.Atspm.IdentityTests.Infrastructure
{
    public class AuthenticationExtensionsTests
    {
        [Fact]
        public async Task AddAtspmAuthentication_WithMultipleProviders_RegistersEachOidcScheme()
        {
            var services = new ServiceCollection();
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Jwt:Key"] = "0123456789abcdef0123456789abcdef",
                    ["Jwt:Issuer"] = "identity-tests",
                    ["OidcProviders:utahid:Authority"] = "https://login.utah.gov",
                    ["OidcProviders:utahid:ClientId"] = "utahid-client",
                    ["OidcProviders:utahid:ClientSecret"] = "utahid-secret",
                    ["OidcProviders:utahid:CallbackPath"] = "/signin-utahid",
                    ["OidcProviders:entra:Authority"] = "https://login.microsoftonline.com/tenant/v2.0",
                    ["OidcProviders:entra:ClientId"] = "entra-client",
                    ["OidcProviders:entra:ClientSecret"] = "entra-secret",
                    ["OidcProviders:entra:CallbackPath"] = "/signin-entra",
                    ["OidcProviders:entra:Scheme"] = "entra-scheme"
                })
                .Build();

            services.AddLogging();
            services.AddAtspmAuthentication(BuildHostContext(configuration));

            var serviceProvider = services.BuildServiceProvider();
            var schemeProvider = serviceProvider.GetRequiredService<IAuthenticationSchemeProvider>();
            var schemes = await schemeProvider.GetAllSchemesAsync();

            Assert.Contains(schemes, scheme => scheme.Name == "oidc-utahid");
            Assert.Contains(schemes, scheme => scheme.Name == "entra-scheme");
        }

        private static HostBuilderContext BuildHostContext(IConfiguration configuration)
        {
            return new HostBuilderContext(new Dictionary<object, object?>())
            {
                Configuration = configuration,
                HostingEnvironment = Mock.Of<IHostEnvironment>(environment =>
                    environment.EnvironmentName == Environments.Production)
            };
        }
    }
}
