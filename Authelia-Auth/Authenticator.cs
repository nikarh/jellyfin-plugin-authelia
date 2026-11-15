using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Jellyfin.Plugin.Authelia_Auth.Config;
using MediaBrowser.Controller.Authentication;

namespace Jellyfin.Plugin.Authelia_Auth
{
#pragma warning disable SA1649
#pragma warning disable SA1402
    /// <summary>
    /// AutheliaUser is ProviderAuthenticationResult enriched with group information.
    /// </summary>
    public class AutheliaUser
    {
        /// <summary>
        /// Gets ProviderAuthenticationResult.
        /// </summary>
        public ProviderAuthenticationResult AuthenticationResult { get; init; }

        /// <summary>
        /// Gets a value indicating whether a user has admin privileges .
        /// </summary>
        public bool IsAdmin { get; init; }
    }
#pragma warning restore SA1649
#pragma warning restore SA1402

    /// <summary>
    /// Authelia Authenticator.
    /// </summary>
    public class Authenticator
    {
        /// <summary>
        /// Authenticate user.
        /// </summary>
        /// <param name="config">Plugin configuration.</param>
        /// <param name="username">Username to authenticate.</param>
        /// <param name="password">Password to authenticate.</param>
        /// <returns>A <see cref="ProviderAuthenticationResult"/> with the authentication result.</returns>
        /// <exception cref="AuthenticationException">Exception when failing to authenticate.</exception>
        public async Task<AutheliaUser> Authenticate(PluginConfiguration config, string username, string password)
        {
            var cookieContainer = new CookieContainer();
            using var handler = new HttpClientHandler()
            {
                CookieContainer = cookieContainer,
            };

            // Without a custom root CA the platform's default validation (chain and hostname) is used.
            if (!string.IsNullOrWhiteSpace(config.AutheliaRootCa))
            {
                var rootCa = config.AutheliaRootCa;
                handler.ServerCertificateCustomValidationCallback = (_, cert, chain, errors) =>
                    ValidateWithCustomRoot(rootCa, cert, chain, errors);
            }

            using var client = new HttpClient(handler) { BaseAddress = new Uri(config.AutheliaServer) };

            var jsonBody = new JsonObject
                {
                    { "username", username },
                    { "password", password },
                    { "targetURL", config.JellyfinUrl },
                    { "requestMethod", "GET" },
                    { "keepMeLoggedIn", true }
                };

            using (var content = new StringContent(jsonBody.ToString(), Encoding.UTF8, "application/json"))
            {
                var response = await client.PostAsync("/api/firstfactor", content);

                if (!response.IsSuccessStatusCode)
                {
                    throw new AuthenticationException("Invalid username or password.");
                }
            }

            using (var request = new HttpRequestMessage(HttpMethod.Get, "/api/authz/auth-request"))
            {
                request.Headers.Add("X-Original-URL", config.JellyfinUrl);
                request.Headers.Add("X-Original-Method", "GET");
                var accessResponse = await client.SendAsync(request);
                if (!accessResponse.IsSuccessStatusCode)
                {
                    throw new AuthenticationException("User doesn't have access to this service.");
                }

                var isAdmin = false;
                var displayName = string.Empty;

                if (accessResponse.Headers.TryGetValues("Remote-Groups", out var groups))
                {
                    isAdmin = groups.FirstOrDefault().Split(",").Any(e => e == config.AutheliaAdminGroup);
                }

                if (accessResponse.Headers.TryGetValues("Remote-Name", out var names))
                {
                    displayName = names.First();
                }
                else
                {
                    throw new AuthenticationException("Authelia didn't return a Remote-Name header.");
                }

                return new AutheliaUser
                {
                    AuthenticationResult = new ProviderAuthenticationResult
                    {
                        Username = username,
                        DisplayName = displayName,
                    },
                    IsAdmin = isAdmin
                };
            }
        }

        /// <summary>
        /// Validates the Authelia server certificate against a custom root CA.
        /// The hostname must still match the certificate, only the chain is validated against the custom root.
        /// </summary>
        /// <param name="rootCaPem">PEM encoded root CA certificate(s).</param>
        /// <param name="cert">Certificate presented by the server.</param>
        /// <param name="chain">Chain built by the platform.</param>
        /// <param name="errors">Errors reported by the platform.</param>
        /// <returns>True if the certificate is valid for the requested host and chains to the custom root.</returns>
        private static bool ValidateWithCustomRoot(string rootCaPem, X509Certificate2 cert, X509Chain chain, SslPolicyErrors errors)
        {
            if (cert == null || chain == null)
            {
                return false;
            }

            // Only chain errors may be overridden by the custom root, everything else
            // (hostname mismatch, missing certificate) is always fatal.
            if ((errors & ~SslPolicyErrors.RemoteCertificateChainErrors) != SslPolicyErrors.None)
            {
                return false;
            }

            chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            chain.ChainPolicy.CustomTrustStore.Clear();
            chain.ChainPolicy.CustomTrustStore.ImportFromPem(rootCaPem);

            return chain.Build(cert);
        }
    }
}
