using Xunit;
using Jellyfin.Plugin.Authelia_Auth.Config;
using Jellyfin.Plugin.Authelia_Auth;

namespace Authelia_Auth.Tests;

public class AuthenticatorTests
{
    [Fact(Skip = "skip")]
    public async void AuthenticatorTest()
    {
        var config = new PluginConfiguration
        {
            JellyfinUrl = "http://jellyfin",
            AutheliaServer = "http://authelia"
        };

        var authenticator = new Authenticator();
        var result = await authenticator.Authenticate(config, "test", "test");
        Assert.Equal("test", result.AuthenticationResult.DisplayName);
    }
}
