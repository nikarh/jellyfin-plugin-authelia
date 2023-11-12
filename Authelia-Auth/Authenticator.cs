using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Jellyfin.Plugin.Authelia_Auth.Config;
using MediaBrowser.Controller.Authentication;

namespace Jellyfin.Plugin.Authelia_Auth
{
#pragma warning disable SA1649
#pragma warning disable SA1402
    /// <summary>
    /// Response data field.
    /// </summary>
    public class UserInfoResponseData
    {
        /// <summary>
        /// Gets users full name.
        /// </summary>
        [JsonPropertyName("display_name")]
        public string DisplayName { get; init; }
    }

    /// <summary>
    /// User info response.
    /// </summary>
    public class UserInfoResponse
    {
        /// <summary>
        /// Gets user info response data.
        /// </summary>
        [JsonPropertyName("data")]
        public UserInfoResponseData Data { get; init; }
    }


    public class UserInfo
    {
        /// <summary>
        /// User info object
        /// </summary>
     
        public string Username { get; set; };
        public string Email { get; set; };
        public string DisplayName { get; set; };
        public bool IsAdmin { get; set; };
        public bool IsUser { get; set; };
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
        public async UserInfo Authenticate(PluginConfiguration config, string username, string password)
        {
            var cookieContainer = new CookieContainer();
            using var handler = new HttpClientHandler()
            {
                CookieContainer = cookieContainer,
                ServerCertificateCustomValidationCallback = (message, cert, chain, _) =>
                {
                    if (!string.IsNullOrWhiteSpace(config.AutheliaRootCa))
                    {
                        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                        chain.ChainPolicy.CustomTrustStore.ImportFromPem(config.AutheliaRootCa);
                    }

                    return chain.Build(cert);
                }
            };
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

            using (var request = new HttpRequestMessage(HttpMethod.Get, "/api/verify"))
            {
                request.Headers.Add("X-Original-Url", config.JellyfinUrl);
                request.Headers.Add("X-Forwarded-Method", "GET");
                var accessResponse = await client.SendAsync(request);
                if (!accessResponse.IsSuccessStatusCode)
                {
                    throw new AuthenticationException("User doesn't have access to this service.");
                }
                UserInfo user = new UserInfo();
                if (accessResponse.Headers.TryGetValues("Remote-Groups", out var values))
                {
                    // Header values are in 'values' variable
                    foreach (var value in values)
                    {
                        if (value.Equals(config.AdminGroup))
                        {
                            user.IsAdmin = true;
                        }
                        else if (value.Equals(config.UserGroup)
                        {
                            user.IsUser = true;
                        }
                    }
                }
                if (accessResponse.Headers.TryGetValues("Remote-Email", out var values))
                {
                    // Header values are in 'values' variable
                    user.Email = values.FirstOrDefault();
                }
                if (accessResponse.Headers.TryGetValues("Remote-Name", out var values))
                {
                    // Header values are in 'values' variable
                    user.DisplayName = values.FirstOrDefault();
                }
                if (accessResponse.Headers.TryGetValues("Remote-User", out var values))
                {
                    // Header values are in 'values' variable
                    user.Username = values.FirstOrDefault();
                }
                return user;
            }
        }
    }
}
