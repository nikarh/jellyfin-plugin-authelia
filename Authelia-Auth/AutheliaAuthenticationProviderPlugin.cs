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
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Authelia_Auth
{
    /// <summary>
    /// Authelia Authentication Provider Plugin.
    /// </summary>
    public class AutheliaAuthenticationProviderPlugin : IAuthenticationProvider, IRequiresResolvedUser
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
        public AutheliaAuthenticationProviderPlugin(IApplicationHost applicationHost, ILogger<AutheliaAuthenticationProviderPlugin> logger, ICryptoProvider cryptoProvider)
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
        public Task<ProviderAuthenticationResult> Authenticate(string username, string password)
        {
            return Authenticate(username, password, null);
        }

        /// <inheritdoc />
        public async Task<ProviderAuthenticationResult> Authenticate(string username, string password, User resolvedUser)
        {
            var userManager = _applicationHost.Resolve<IUserManager>();
            var config = AutheliaPlugin.Instance.Configuration;

            // Jellyfin 12+ resolves users case-insensitively and may already have the matching account.
            var user = resolvedUser;
            if (user == null)
            {
                try
                {
                    user = userManager.GetUserByName(username);
                }
                catch (Exception e)
                {
                    _logger.LogError("User Manager could not find a user for Authelia User. {Error}", e);
                    throw new AuthenticationException("Error completing Authelia login. Invalid username or password.");
                }
            }

            // Jellyfin stores the username with the casing it was created with, which is the
            // casing Authelia accepted. Authelia is case-sensitive, so prefer it over what was typed.
            var autheliaUsername = user?.Username ?? username;

            if (!string.Equals(autheliaUsername, username, StringComparison.Ordinal))
            {
                _logger.LogInformation(
                    "Signing in {EnteredUsername} as existing Authelia user {AutheliaUsername}.",
                    username,
                    autheliaUsername);
            }

            var auth = await new Authenticator().Authenticate(config, autheliaUsername, password).ConfigureAwait(false);

            var userNeedsUpdate = false;
            if (config.CreateUserIfNotExists && user == null)
            {
                _logger.LogInformation("Authelia user doesn't exist, creating...");
                user = await userManager.CreateUserAsync(autheliaUsername).ConfigureAwait(false);

                user.AuthenticationProviderId = GetType().FullName;
                user.Password = _cryptoProvider.CreatePasswordHash(Convert.ToBase64String(RandomNumberGenerator.GetBytes(64))).ToString();
                userNeedsUpdate = true;
            }

            // Only manage admin permissions if the admin group is set in config
            if (user != null && !string.IsNullOrWhiteSpace(config.AutheliaAdminGroup))
            {
                var isJellyfinAdmin = user.HasPermission(PermissionKind.IsAdministrator);
                if (isJellyfinAdmin != auth.IsAdmin)
                {
                    user.SetPermission(PermissionKind.IsAdministrator, auth.IsAdmin);
                    userNeedsUpdate = true;
                }
            }

            if (user != null && userNeedsUpdate)
            {
                await userManager.UpdateUserAsync(user).ConfigureAwait(false);
            }

            return auth.AuthenticationResult;
        }

        /// <inheritdoc />
        public Task ChangePassword(User user, string newPassword)
        {
            throw new NotImplementedException();
        }
    }
}
