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
    /// Indicates Authelia requires elevation before password change.
    /// </summary>
    public sealed class PasswordChangeElevationRequiredException : Exception
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="PasswordChangeElevationRequiredException"/> class.
        /// </summary>
        public PasswordChangeElevationRequiredException()
            : base("Authelia requires an elevated session to change passwords.")
        {
        }
    }

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
        private const string MessageInvalidCredentials = "Invalid username or password.";

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
            using var handler = CreateHandler(config, cookieContainer);
            using var client = new HttpClient(handler) { BaseAddress = new Uri(config.AutheliaServer) };

            await AuthenticateFirstFactor(client, config, username, password);

            using (var request = new HttpRequestMessage(HttpMethod.Get, "/api/authz/auth-request"))
            {
                request.Headers.Add("X-Original-URL", config.JellyfinUrl);
                request.Headers.Add("X-Original-Method", "GET");
                using var accessResponse = await client.SendAsync(request);
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
        /// Change user password.
        /// </summary>
        /// <param name="config">Plugin configuration.</param>
        /// <param name="username">Username to authenticate.</param>
        /// <param name="oldPassword">Current password.</param>
        /// <param name="newPassword">New password.</param>
        /// <returns>A <see cref="Task"/> representing asynchronous operation.</returns>
        /// <exception cref="AuthenticationException">Exception when failing to change password.</exception>
        public async Task ChangePassword(PluginConfiguration config, string username, string oldPassword, string newPassword)
        {
            var cookieContainer = new CookieContainer();
            using var handler = CreateHandler(config, cookieContainer);
            using var client = new HttpClient(handler) { BaseAddress = new Uri(config.AutheliaServer) };

            await AuthenticateFirstFactor(client, config, username, oldPassword);

            var payload = new JsonObject
            {
                { "old_password", oldPassword },
                { "new_password", newPassword }
            };

            using var content = new StringContent(payload.ToString(), Encoding.UTF8, "application/json");
            using var response = await client.PostAsync("/api/change-password", content);

            if (response.IsSuccessStatusCode)
            {
                return;
            }

            var errorBody = await response.Content.ReadAsStringAsync();

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                throw new AuthenticationException(MessageInvalidCredentials);
            }

            if (response.StatusCode == HttpStatusCode.Forbidden && IsElevationRequired(errorBody))
            {
                throw new PasswordChangeElevationRequiredException();
            }

            throw new AuthenticationException("Authelia failed to change the password.");
        }

        private static HttpClientHandler CreateHandler(PluginConfiguration config, CookieContainer cookieContainer)
        {
            return new HttpClientHandler()
            {
                CookieContainer = cookieContainer,
                ServerCertificateCustomValidationCallback = (message, cert, chain, sslPolicyErrors) =>
                {
                    return ValidateServerCertificate(config, cert as X509Certificate2, chain, sslPolicyErrors);
                }
            };
        }

        private static bool ValidateServerCertificate(PluginConfiguration config, X509Certificate2 cert, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            if (cert == null)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(config.AutheliaRootCa))
            {
                return sslPolicyErrors == SslPolicyErrors.None;
            }

            using var validationChain = chain ?? new X509Chain();

            validationChain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            validationChain.ChainPolicy.CustomTrustStore.Clear();
            validationChain.ChainPolicy.CustomTrustStore.ImportFromPem(config.AutheliaRootCa);
            if (!validationChain.Build(cert))
            {
                return false;
            }

            var disallowedErrors = sslPolicyErrors & ~SslPolicyErrors.RemoteCertificateChainErrors;
            return disallowedErrors == SslPolicyErrors.None;
        }

        private static async Task AuthenticateFirstFactor(HttpClient client, PluginConfiguration config, string username, string password)
        {
            var jsonBody = new JsonObject
            {
                { "username", username },
                { "password", password },
                { "targetURL", config.JellyfinUrl },
                { "requestMethod", "GET" },
                { "keepMeLoggedIn", true }
            };

            using var content = new StringContent(jsonBody.ToString(), Encoding.UTF8, "application/json");
            using var response = await client.PostAsync("/api/firstfactor", content);

            if (!response.IsSuccessStatusCode)
            {
                throw new AuthenticationException(MessageInvalidCredentials);
            }
        }

        private static bool IsElevationRequired(string errorBody)
        {
            if (string.IsNullOrWhiteSpace(errorBody))
            {
                return false;
            }

            JsonNode root;
            try
            {
                root = JsonNode.Parse(errorBody);
            }
            catch
            {
                return false;
            }

            if (root is not JsonObject rootObject)
            {
                return false;
            }

            if (!rootObject.TryGetPropertyValue("data", out var dataNode) || dataNode is not JsonObject dataObject)
            {
                return false;
            }

            if (!dataObject.TryGetPropertyValue("elevation", out var elevationNode) || elevationNode is null)
            {
                return false;
            }

            if (elevationNode is JsonValue jsonValue)
            {
                try
                {
                    return jsonValue.GetValue<bool>();
                }
                catch
                {
                    return false;
                }
            }

            return false;
        }
    }
}
