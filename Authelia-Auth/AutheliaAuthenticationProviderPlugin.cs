using System;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Jellyfin.Data;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using MediaBrowser.Common;
using MediaBrowser.Controller.Authentication;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Cryptography;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Authelia_Auth
{
    /// <summary>
    /// Authelia Authentication Provider Plugin.
    /// </summary>
    public class AutheliaAuthenticationProviderPlugin : IAuthenticationProvider
    {
        private readonly IApplicationHost _applicationHost;
        private readonly ILogger<AutheliaAuthenticationProviderPlugin> _logger;
        private readonly ICryptoProvider _cryptoProvider;

        /// <summary>
        /// Initializes a new instance of the <see cref="AutheliaAuthenticationProviderPlugin"/> class.
        /// </summary>
        /// <param name="applicationHost">Instance of the <see cref="IApplicationHost"/> interface.</param>
        /// <param name="logger">Instance of the <see cref="ILogger{AutheliaAuthenticationProviderPlugin}"/> interface.</param>
        /// <param name="cryptoProvider">Instance of the <see cref="ILogger{ICryptoProvider}"/> interface.</param>
        public AutheliaAuthenticationProviderPlugin(
            IApplicationHost applicationHost,
            ILogger<AutheliaAuthenticationProviderPlugin> logger,
            ICryptoProvider cryptoProvider)
        {
            _logger = logger;
            _applicationHost = applicationHost;
            _cryptoProvider = cryptoProvider;
        }

        /// <summary>
        /// Gets plugin name.
        /// </summary>
        public string Name => "Authelia-Authentication";

        /// <summary>
        /// Gets a value indicating whether gets plugin enabled.
        /// </summary>
        public bool IsEnabled => true;

        /// <summary>
        /// Authenticate user against the ldap server.
        /// </summary>
        /// <param name="username">Username to authenticate.</param>
        /// <param name="password">Password to authenticate.</param>
        /// <returns>A <see cref="ProviderAuthenticationResult"/> with the authentication result.</returns>
        /// <exception cref="AuthenticationException">Exception when failing to authenticate.</exception>
        public async Task<ProviderAuthenticationResult> Authenticate(string username, string password)
        {
            var userManager = _applicationHost.Resolve<IUserManager>();
            var config = AutheliaPlugin.Instance.Configuration;

            var auth = await new Authenticator().Authenticate(config, username, password);
            CaptureCredentialForPasswordChangeFlow(username, password);

            User user;
            try
            {
                user = userManager.GetUserByName(username);
            }
            catch (Exception e)
            {
                _logger.LogError("User Manager could not find a user for Authelia User. {Error}", e);
                throw new AuthenticationException("Error completing Authelia login. Invalid username or password.");
            }

            if (config.CreateUserIfNotExists && user == null)
            {
                _logger.LogInformation("Authelia user doesn't exist, creating...");
                user = await userManager.CreateUserAsync(username).ConfigureAwait(false);

                user.AuthenticationProviderId = GetType().FullName;
                user.Password = _cryptoProvider.CreatePasswordHash(Convert.ToBase64String(RandomNumberGenerator.GetBytes(64))).ToString();
            }

            if (user == null)
            {
                throw new AuthenticationException("User does not exist in Jellyfin and auto-create is disabled.");
            }

            // Only manage admin permissions if the admin group is set in config
            if (!string.IsNullOrWhiteSpace(config.AutheliaAdminGroup))
            {
                user.SetPermission(PermissionKind.IsAdministrator, auth.IsAdmin);
            }

            return auth.AuthenticationResult;
        }

        /// <inheritdoc />
        public bool HasPassword(User user)
        {
            return true;
        }

        /// <inheritdoc />
        public async Task ChangePassword(User user, string newPassword)
        {
            if (string.IsNullOrWhiteSpace(newPassword))
            {
                throw new AuthenticationException("New password cannot be empty.");
            }

            var username = user.Username;
            var config = AutheliaPlugin.Instance.Configuration;
            var flowCredential = TakeCredentialForPasswordChangeFlow(username);

            if (flowCredential == null)
            {
                throw new AuthenticationException(
                    $"Unable to change password from Jellyfin because the current password was not provided "
                    + $"in this password-change request. "
                    + $"Enter your current password and retry, or change it directly in Authelia at "
                    + $"{BuildAutheliaPortalUrl(config.AutheliaServer)}.");
            }

            try
            {
                await new Authenticator().ChangePassword(config, username, flowCredential.GetPassword(), newPassword);
            }
            catch (PasswordChangeElevationRequiredException)
            {
                throw new AuthenticationException(
                    $"Authelia requires an elevated session before changing passwords. "
                    + $"Open {BuildAutheliaPortalUrl(config.AutheliaServer)} and change your password there.");
            }
            finally
            {
                flowCredential?.Clear();
            }
        }

        private static string BuildAutheliaPortalUrl(string server)
        {
            if (Uri.TryCreate(server, UriKind.Absolute, out var uri))
            {
                var path = uri.GetLeftPart(UriPartial.Path);
                return path.EndsWith("/", StringComparison.Ordinal) ? path : path + "/";
            }

            return server;
        }

        private void CaptureCredentialForPasswordChangeFlow(string username, string password)
        {
            if (!TryGetCurrentHttpContext(out var httpContext) || !IsPasswordChangeRoute(httpContext.Request.Path.Value ?? string.Empty))
            {
                return;
            }

            var key = BuildFlowCredentialKey(username);
            if (httpContext.Items.TryGetValue(key, out var existing) && existing is FlowCredential existingCredential)
            {
                existingCredential.Clear();
            }

            httpContext.Items[key] = new FlowCredential(username, password);
        }

        private FlowCredential TakeCredentialForPasswordChangeFlow(string username)
        {
            if (!TryGetCurrentHttpContext(out var httpContext))
            {
                return null;
            }

            var key = BuildFlowCredentialKey(username);

            if (!httpContext.Items.TryGetValue(key, out var value) || value is not FlowCredential credential)
            {
                return null;
            }

            httpContext.Items.Remove(key);

            if (!string.Equals(credential.Username, username, StringComparison.OrdinalIgnoreCase))
            {
                credential.Clear();
                return null;
            }

            return credential;
        }

        private bool TryGetCurrentHttpContext(out HttpContext httpContext)
        {
            httpContext = null;

            IHttpContextAccessor accessor;
            try
            {
                accessor = _applicationHost.Resolve<IHttpContextAccessor>();
            }
            catch
            {
                return false;
            }

            httpContext = accessor?.HttpContext;
            return httpContext != null;
        }

        private static bool IsPasswordChangeRoute(string requestPath)
        {
            if (string.IsNullOrWhiteSpace(requestPath))
            {
                return false;
            }

            var segments = requestPath
                .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            if (segments.Length < 2 || !segments[^1].Equals("Password", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (segments[^2].Equals("Users", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return segments.Length >= 3
                && segments[^3].Equals("Users", StringComparison.OrdinalIgnoreCase)
                && Guid.TryParse(segments[^2], out _);
        }

        private static string BuildFlowCredentialKey(string username)
        {
            return $"authelia-auth:password-flow:{username.ToLowerInvariant()}";
        }

        private sealed class FlowCredential
        {
            public FlowCredential(string username, string password)
            {
                Username = username;
                PasswordChars = password.ToCharArray();
            }

            public string Username { get; }

            private char[] PasswordChars { get; set; }

            public string GetPassword()
            {
                return new string(PasswordChars);
            }

            public void Clear()
            {
                if (PasswordChars.Length > 0)
                {
                    Array.Clear(PasswordChars, 0, PasswordChars.Length);
                    PasswordChars = Array.Empty<char>();
                }
            }
        }
    }
}
