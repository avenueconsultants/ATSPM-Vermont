#region license
// Copyright 2026 Utah Departement of Transportation
// for Infrastructure - Utah.Udot.Atspm.Infrastructure.Extensions/AuthenticationExtensions.cs
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

using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using System.Text;
using Utah.Udot.Atspm.Infrastructure.Configuration;
using Utah.Udot.Atspm.Infrastructure.Services;

namespace Utah.Udot.Atspm.Infrastructure.Extensions
{
    /// <summary>
    /// Helper extensions for <see cref="Microsoft.Extensions.Hosting"/> using <see cref="Microsoft.AspNetCore.Authentication"/> 
    /// </summary>
    public static class AuthenticationExtensions
    {
        /// <summary>
        /// Adds Atspm identity services if <see cref="Host"/> is not in <see cref="Environments.Development"/>
        /// </summary>
        /// <param name="services"></param>
        /// <param name="host"></param>
        /// <returns></returns>
        public static IServiceCollection AddAtspmIdentity(this IServiceCollection services, HostBuilderContext host)
        {
            //if (!host.HostingEnvironment.IsDevelopment())
            //{
            services.AddAtspmAuthentication(host);
            services.AddAtspmAuthorization();
            //}

            return services;
        }

        /// <summary>
        /// Add atspm authentication
        /// </summary>
        /// <param name="services"></param>
        /// <param name="host"></param>
        /// <returns></returns>
        public static IServiceCollection AddAtspmAuthentication(this IServiceCollection services, HostBuilderContext host)
        {
            var oidcProviders = new OidcProviderOptions();
            host.Configuration.GetSection("OidcProviders").Bind(oidcProviders.Providers);

            services.Configure<OidcProviderOptions>(options =>
            {
                options.Providers = oidcProviders.Providers;
            });

            services.Configure<CookiePolicyOptions>(options =>
            {
                options.MinimumSameSitePolicy = SameSiteMode.None;
                options.Secure = CookieSecurePolicy.Always;
            });

            var authenticationBuilder = services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = false,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = host.Configuration["Jwt:Issuer"],
                    ValidAudience = host.Configuration["Jwt:Audience"],
                    IssuerSigningKey = new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(host.Configuration["Jwt:Key"]))
                };
            });

            foreach (var providerEntry in oidcProviders.Providers)
            {
                var providerKey = providerEntry.Key;
                var provider = providerEntry.Value;

                if (string.IsNullOrWhiteSpace(provider.Authority) ||
                    string.IsNullOrWhiteSpace(provider.ClientId) ||
                    string.IsNullOrWhiteSpace(provider.ClientSecret) ||
                    string.IsNullOrWhiteSpace(provider.CallbackPath))
                {
                    continue;
                }

                var schemeName = string.IsNullOrWhiteSpace(provider.Scheme)
                    ? $"oidc-{providerKey}"
                    : provider.Scheme;
                var displayName = string.IsNullOrWhiteSpace(provider.DisplayName)
                    ? providerKey
                    : provider.DisplayName;
                var scopes = provider.Scopes.Count > 0
                    ? provider.Scopes
                    : new List<string> { "openid", "email", "profile" };
                var publicCallbackPath = provider.CallbackPath.StartsWith("/identity/", StringComparison.OrdinalIgnoreCase)
                    ? provider.CallbackPath
                    : $"/identity{provider.CallbackPath}";

                authenticationBuilder.AddOpenIdConnect(schemeName, options =>
                {
                    options.SignInScheme = IdentityConstants.ExternalScheme;
                    options.Authority = provider.Authority;
                    options.ClientId = provider.ClientId;
                    options.ClientSecret = provider.ClientSecret;
                    options.ResponseType = OpenIdConnectResponseType.IdToken;
                    options.SaveTokens = true;
                    options.Scope.Clear();
                    foreach (var scope in scopes.Where(s => !string.IsNullOrWhiteSpace(s)))
                    {
                        options.Scope.Add(scope);
                    }

                    options.CallbackPath = provider.CallbackPath;
                    options.NonceCookie.Path = publicCallbackPath;
                    options.CorrelationCookie.Path = publicCallbackPath;

                    options.GetClaimsFromUserInfoEndpoint = true;
                    options.UseTokenLifetime = true;
                    options.SkipUnrecognizedRequests = true;

                    options.Events = new OpenIdConnectEvents
                    {
                        OnRedirectToIdentityProvider = context =>
                        {
                            var b = new UriBuilder(context.ProtocolMessage.RedirectUri);
                            b.Scheme = "https";
                            if (!string.Equals(b.Host, "localhost", StringComparison.OrdinalIgnoreCase))
                            {
                                b.Port = -1;
                            }
                            else if (!string.Equals(b.Path, publicCallbackPath, StringComparison.OrdinalIgnoreCase))
                            {
                                b.Path = publicCallbackPath;
                            }
                            context.ProtocolMessage.RedirectUri = b.ToString();

                            return Task.CompletedTask;
                        },
                        OnTokenValidated = context =>
                        {
                            NormalizeStandardClaims(context.Principal);
                            return Task.CompletedTask;
                        },
                        OnTicketReceived = async context =>
                        {
                            var ticketHandler = context.HttpContext.RequestServices.GetService<IOidcTicketHandler>();
                            if (ticketHandler == null)
                            {
                                context.Fail("No OIDC ticket handler is registered.");
                                return;
                            }

                            await ticketHandler.HandleTicketReceivedAsync(context, providerKey, schemeName, displayName);
                        }
                    };
                });
            }

            authenticationBuilder.AddCookie(CookieAuthenticationDefaults.AuthenticationScheme);

            return services;
        }

        private static void NormalizeStandardClaims(ClaimsPrincipal? principal)
        {
            if (principal?.Identity is not ClaimsIdentity identity)
            {
                return;
            }

            EnsureClaim(identity, ClaimTypes.Email, "email", "preferred_username");
            EnsureClaim(identity, ClaimTypes.GivenName, "given_name");
            EnsureClaim(identity, ClaimTypes.Surname, "family_name");
            EnsureClaim(identity, ClaimTypes.NameIdentifier, "sub", "oid");
        }

        private static void EnsureClaim(ClaimsIdentity identity, string targetType, params string[] sourceTypes)
        {
            if (identity.HasClaim(c => c.Type == targetType))
            {
                return;
            }

            var sourceClaim = sourceTypes
                .Select(sourceType => identity.FindFirst(sourceType))
                .FirstOrDefault(claim => claim != null);

            if (sourceClaim == null || string.IsNullOrWhiteSpace(sourceClaim.Value))
            {
                return;
            }

            identity.AddClaim(new Claim(targetType, sourceClaim.Value, sourceClaim.ValueType, sourceClaim.Issuer));
        }

        /// <summary>
        /// Add atspm authorization
        /// </summary>
        /// <param name="services"></param>
        /// <returns></returns>
        public static IServiceCollection AddAtspmAuthorization(this IServiceCollection services)
        {
            services.AddAuthorization(options =>
            {
                options.AddPolicy("CanViewUsers", policy =>
                    policy.RequireAssertion(context =>
                        context.User.HasClaim(c =>
                            c.Type == ClaimTypes.Role && c.Value == "User:View" ||
                            c.Type == ClaimTypes.Role && c.Value == "Admin")));
                options.AddPolicy("CanEditUsers", policy =>
                    policy.RequireAssertion(context =>
                        context.User.HasClaim(c =>
                            c.Type == ClaimTypes.Role && c.Value == "User:Edit" ||
                            c.Type == ClaimTypes.Role && c.Value == "Admin")));
                options.AddPolicy("CanDeleteUsers", policy =>
                    policy.RequireAssertion(context =>
                        context.User.HasClaim(c =>
                            c.Type == ClaimTypes.Role && c.Value == "User:Delete" ||
                            c.Type == ClaimTypes.Role && c.Value == "Admin")));


                options.AddPolicy("CanViewRoles", policy =>
                   policy.RequireAssertion(context =>
                       context.User.HasClaim(c =>
                           c.Type == ClaimTypes.Role && c.Value == "Role:View" ||
                           c.Type == ClaimTypes.Role && c.Value == "Admin")));
                options.AddPolicy("CanEditRoles", policy =>
                   policy.RequireAssertion(context =>
                       context.User.HasClaim(c =>
                           c.Type == ClaimTypes.Role && c.Value == "Role:Edit" ||
                           c.Type == ClaimTypes.Role && c.Value == "Admin")));
                options.AddPolicy("CanDeleteRoles", policy =>
                   policy.RequireAssertion(context =>
                       context.User.HasClaim(c =>
                           c.Type == ClaimTypes.Role && c.Value == "Role:Delete" ||
                           c.Type == ClaimTypes.Role && c.Value == "Admin")));


                options.AddPolicy("CanViewLocationConfigurations", policy =>
                   policy.RequireAssertion(context =>
                       context.User.HasClaim(c =>
                           c.Type == ClaimTypes.Role && c.Value == "LocationConfiguration:View" ||
                           c.Type == ClaimTypes.Role && c.Value == "Admin")));
                options.AddPolicy("CanEditLocationConfigurations", policy =>
                   policy.RequireAssertion(context =>
                       context.User.HasClaim(c =>
                           c.Type == ClaimTypes.Role && c.Value == "LocationConfiguration:Edit" ||
                           c.Type == ClaimTypes.Role && c.Value == "Admin")));
                options.AddPolicy("CanDeleteLocationConfigurations", policy =>
                   policy.RequireAssertion(context =>
                       context.User.HasClaim(c =>
                           c.Type == ClaimTypes.Role && c.Value == "LocationConfiguration:Delete" ||
                           c.Type == ClaimTypes.Role && c.Value == "Admin")));


                options.AddPolicy("CanViewGeneralConfigurations", policy =>
                   policy.RequireAssertion(context =>
                       context.User.HasClaim(c =>
                           c.Type == ClaimTypes.Role && c.Value == "GeneralConfiguration:View" ||
                           c.Type == ClaimTypes.Role && c.Value == "Admin")));
                options.AddPolicy("CanEditGeneralConfigurations", policy =>
                   policy.RequireAssertion(context =>
                       context.User.HasClaim(c =>
                           c.Type == ClaimTypes.Role && c.Value == "GeneralConfiguration:Edit" ||
                           c.Type == ClaimTypes.Role && c.Value == "Admin")));
                options.AddPolicy("CanDeleteGeneralConfigurations", policy =>
                   policy.RequireAssertion(context =>
                       context.User.HasClaim(c =>
                           c.Type == ClaimTypes.Role && c.Value == "GeneralConfiguration:Delete" ||
                           c.Type == ClaimTypes.Role && c.Value == "Admin")));


                options.AddPolicy("CanViewData", policy =>
                   policy.RequireAssertion(context =>
                       context.User.HasClaim(c =>
                           c.Type == ClaimTypes.Role && c.Value == "Data:View" ||
                           c.Type == ClaimTypes.Role && c.Value == "Admin")));
                options.AddPolicy("CanEditData", policy =>
                   policy.RequireAssertion(context =>
                       context.User.HasClaim(c =>
                           c.Type == ClaimTypes.Role && c.Value == "Data:Edit" ||
                           c.Type == ClaimTypes.Role && c.Value == "Admin")));


                options.AddPolicy("CanViewWatchDog", policy =>
                   policy.RequireAssertion(context =>
                       context.User.HasClaim(c =>
                           c.Type == ClaimTypes.Role && c.Value == "Watchdog:View" ||
                           c.Type == ClaimTypes.Role && c.Value == "Admin")));
            });

            return services;
        }
    }
}
