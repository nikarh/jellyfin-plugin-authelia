using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
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
#pragma warning restore SA1649
#pragma warning restore SA1402

    /// <summary>
    /// Authelia Authenticator.
    /// </summary>
    public class Authenticator
    {
        private Regex cookieRe = new Regex("authelia_session=([^;]+);");

        /// <summary>
        /// Authenticate user.
        /// </summary>
        /// <param name="config">Plugin configuration.</param>
        /// <param name="username">Username to authenticate.</param>
        /// <param name="password">Password to authenticate.</param>
        /// <returns>A <see cref="ProviderAuthenticationResult"/> with the authentication result.</returns>
        /// <exception cref="AuthenticationException">Exception when failing to authenticate.</exception>
        public async Task<ProviderAuthenticationResult> Authenticate(PluginConfiguration config, string username, string password)
        {
            var cookieContainer = new CookieContainer();
            using (var handler = new HttpClientHandler() { CookieContainer = cookieContainer })
            using (var client = new HttpClient(handler) { BaseAddress = new Uri(config.AutheliaServer) })
            {
                var jsonBody = new JsonObject();
                jsonBody.Add("username", username);
                jsonBody.Add("password", password);
                jsonBody.Add("targetURL", config.JellyfinUrl);
                jsonBody.Add("requestMethod", "GET");
                jsonBody.Add("keepMeLoggedIn", true);

                var session = string.Empty;

                using (var content = new StringContent(jsonBody.ToString(), Encoding.UTF8, "application/json"))
                {
                    var response = await client.PostAsync("/api/firstfactor", content);

                    if (!response.IsSuccessStatusCode)
                    {
                        throw new AuthenticationException("Invalid username or password.");
                    }

                    var setCookie = response.Headers.GetValues("Set-Cookie").FirstOrDefault(string.Empty);
                    session = cookieRe.Match(setCookie).Groups[1].Value;
                }

                // Allow using internal authelia url instead of proxied
                cookieContainer.Add(new Uri(config.AutheliaServer), new Cookie("authelia_session", session));

                using (var request = new HttpRequestMessage(HttpMethod.Get, "/api/verify"))
                {
                    request.Headers.Add("X-Original-Url", config.JellyfinUrl);
                    request.Headers.Add("X-Forwarded-Method", "GET");
                    var accessResponse = await client.SendAsync(request);
                    if (!accessResponse.IsSuccessStatusCode)
                    {
                        throw new AuthenticationException("User doesn't have access to this service.");
                    }
                }

                try
                {
                    var userInfoResponse = await client.GetFromJsonAsync<UserInfoResponse>("/api/user/info");

                    return new ProviderAuthenticationResult
                    {
                        Username = username,
                        DisplayName = userInfoResponse.Data.DisplayName,
                    };
                }
                catch
                {
                    throw new AuthenticationException("Invalid username or password.");
                }
            }
        }
    }
}
