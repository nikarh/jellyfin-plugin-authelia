using System;

namespace Jellyfin.Plugin.Authelia_Auth.Config
{
    /// <summary>
    /// Plugin Configuration.
    /// </summary>
    public class PluginConfiguration : MediaBrowser.Model.Plugins.BasePluginConfiguration
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="PluginConfiguration"/> class.
        /// </summary>
        public PluginConfiguration()
        {
            AutheliaServer = "http://authelia";
            JellyfinUrl = "http://jellyfin";
            AutheliaRootCa = string.Empty;
            CreateUserIfNotExists = true;
        }

        /// <summary>
        /// Gets or sets the Authelia server address.
        /// </summary>
        public string AutheliaServer { get; set; }

        /// <summary>
        /// Gets or sets the Authelia root CA certificate.
        /// </summary>
        public string AutheliaRootCa { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether a user will be created on successful authentication if it does not exist in Jellyfin.
        /// </summary>
        public bool CreateUserIfNotExists { get; set; }

        /// <summary>
        /// Gets or sets the jellyfin URL.
        /// </summary>
        public string JellyfinUrl { get; set; }
    }
}
