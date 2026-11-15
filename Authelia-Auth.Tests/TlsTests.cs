using System;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Authelia_Auth;
using Jellyfin.Plugin.Authelia_Auth.Config;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Authelia_Auth.Tests;

/// <summary>
/// End-to-end tests running the authenticator against a fake Authelia served over real TLS.
/// </summary>
public class TlsTests
{
    private const string Host = "localhost";

    [Fact]
    public async Task AcceptsCertificateForHostSignedByConfiguredCa()
    {
        using var ca = CreateCa("Test Root CA");
        using var leaf = CreateLeaf(ca, Host);
        await using var server = await FakeAuthelia.StartAsync(leaf);

        var result = await new Authenticator().Authenticate(Config(server, ca), "alice", "secret");

        Assert.Equal("Alice", result.AuthenticationResult.DisplayName);
        Assert.Equal("alice", result.AuthenticationResult.Username);
        Assert.True(result.IsAdmin);
        Assert.Equal(1, server.FirstFactorRequests);
    }

    [Fact]
    public async Task AcceptsCertificateWithIntermediateSignedByConfiguredCa()
    {
        using var ca = CreateCa("Test Root CA");
        using var intermediate = CreateCa("Test Intermediate CA", ca);
        using var leaf = CreateLeaf(intermediate, Host);
        await using var server = await FakeAuthelia.StartAsync(leaf, intermediate);

        var result = await new Authenticator().Authenticate(Config(server, ca), "alice", "secret");

        Assert.Equal("Alice", result.AuthenticationResult.DisplayName);
        Assert.Equal(1, server.FirstFactorRequests);
    }

    [Fact]
    public async Task RejectsCertificateForOtherHostSignedByConfiguredCa()
    {
        using var ca = CreateCa("Test Root CA");
        using var leaf = CreateLeaf(ca, "other.example");
        await using var server = await FakeAuthelia.StartAsync(leaf);

        await Assert.ThrowsAnyAsync<Exception>(
            () => new Authenticator().Authenticate(Config(server, ca), "alice", "secret"));

        Assert.Equal(0, server.FirstFactorRequests);
    }

    [Fact]
    public async Task RejectsCertificateSignedByOtherCa()
    {
        using var ca = CreateCa("Test Root CA");
        using var otherCa = CreateCa("Other Root CA");
        using var leaf = CreateLeaf(otherCa, Host);
        await using var server = await FakeAuthelia.StartAsync(leaf);

        await Assert.ThrowsAnyAsync<Exception>(
            () => new Authenticator().Authenticate(Config(server, ca), "alice", "secret"));

        Assert.Equal(0, server.FirstFactorRequests);
    }

    [Fact]
    public async Task RejectsSelfSignedCertificateForHost()
    {
        using var ca = CreateCa("Test Root CA");
        using var selfSigned = CreateSelfSignedLeaf(Host);
        await using var server = await FakeAuthelia.StartAsync(selfSigned);

        await Assert.ThrowsAnyAsync<Exception>(
            () => new Authenticator().Authenticate(Config(server, ca), "alice", "secret"));

        Assert.Equal(0, server.FirstFactorRequests);
    }

    [Fact]
    public async Task RejectsUntrustedCertificateWithoutConfiguredCa()
    {
        using var ca = CreateCa("Test Root CA");
        using var leaf = CreateLeaf(ca, Host);
        await using var server = await FakeAuthelia.StartAsync(leaf);

        await Assert.ThrowsAnyAsync<Exception>(
            () => new Authenticator().Authenticate(Config(server, null), "alice", "secret"));

        Assert.Equal(0, server.FirstFactorRequests);
    }

    [Fact]
    public async Task RejectsCertificateForOtherHostWithoutConfiguredCa()
    {
        using var ca = CreateCa("Test Root CA");
        using var leaf = CreateLeaf(ca, "other.example");
        await using var server = await FakeAuthelia.StartAsync(leaf);

        await Assert.ThrowsAnyAsync<Exception>(
            () => new Authenticator().Authenticate(Config(server, null), "alice", "secret"));

        Assert.Equal(0, server.FirstFactorRequests);
    }

    private static PluginConfiguration Config(FakeAuthelia server, X509Certificate2? rootCa) => new()
    {
        AutheliaServer = $"https://{Host}:{server.Port}",
        JellyfinUrl = "https://jellyfin.example",
        AutheliaRootCa = rootCa?.ExportCertificatePem() ?? string.Empty,
        AutheliaAdminGroup = "admins",
    };

    private static X509Certificate2 CreateCa(string name, X509Certificate2? issuer = null)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest($"CN={name}", key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

        var notBefore = DateTimeOffset.UtcNow.AddDays(-1);
        var notAfter = DateTimeOffset.UtcNow.AddDays(30);

        if (issuer == null)
        {
            return request.CreateSelfSigned(notBefore, notAfter);
        }

        using var cert = request.Create(issuer, notBefore, notAfter.AddDays(-1), RandomSerial());
        return cert.CopyWithPrivateKey(key);
    }

    private static X509Certificate2 CreateLeaf(X509Certificate2 issuer, string dnsName)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = LeafRequest(key, dnsName);
        request.CertificateExtensions.Add(X509AuthorityKeyIdentifierExtension.CreateFromCertificate(issuer, true, false));

        using var cert = request.Create(issuer, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(7), RandomSerial());
        return cert.CopyWithPrivateKey(key);
    }

    private static X509Certificate2 CreateSelfSignedLeaf(string dnsName)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return LeafRequest(key, dnsName).CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(7));
    }

    private static CertificateRequest LeafRequest(ECDsa key, string dnsName)
    {
        var request = new CertificateRequest($"CN={dnsName}", key, HashAlgorithmName.SHA256);
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName(dnsName);
        request.CertificateExtensions.Add(san.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, false));
        return request;
    }

    private static byte[] RandomSerial() => RandomNumberGenerator.GetBytes(16);

    /// <summary>
    /// Minimal fake of the Authelia endpoints used by the plugin, served over HTTPS on loopback.
    /// </summary>
    private sealed class FakeAuthelia : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private int _firstFactorRequests;

        private FakeAuthelia(WebApplication app, int port)
        {
            _app = app;
            Port = port;
        }

        public int Port { get; }

        public int FirstFactorRequests => Volatile.Read(ref _firstFactorRequests);

        public static async Task<FakeAuthelia> StartAsync(X509Certificate2 serverCert, params X509Certificate2[] intermediates)
        {
            // Reload through PKCS#12 so the key is usable by the TLS stack on every platform.
            var pfx = X509CertificateLoader.LoadPkcs12(serverCert.Export(X509ContentType.Pfx), null);
            var chain = new X509Certificate2Collection(intermediates.Select(c => X509CertificateLoader.LoadCertificate(c.RawData)).ToArray());

            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(kestrel =>
            {
                kestrel.Listen(IPAddress.Loopback, 0, listen => listen.UseHttps(https =>
                {
                    https.ServerCertificate = pfx;
                    https.ServerCertificateChain = chain;
                }));
            });

            var app = builder.Build();
            FakeAuthelia? fake = null;

            app.MapPost("/api/firstfactor", () =>
            {
                Interlocked.Increment(ref fake!._firstFactorRequests);
                return Results.Ok();
            });
            app.MapGet("/api/authz/auth-request", (HttpContext ctx) =>
            {
                ctx.Response.Headers["Remote-Name"] = "Alice";
                ctx.Response.Headers["Remote-Groups"] = "users,admins";
                return Results.Ok();
            });

            await app.StartAsync();

            var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses;
            var port = new Uri(addresses.Single()).Port;

            fake = new FakeAuthelia(app, port);
            return fake;
        }

        public async ValueTask DisposeAsync()
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }
}
