using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace AiLocal.Node.Hosting;

/// <summary>
/// Generates and persists a self-signed certificate per node for the
/// additive HTTPS listener used for node-to-node traffic. This is not a real
/// PKI - the "cluster" HttpClient trusts any server certificate, so this buys
/// transport encryption, not certificate-based server identity. The shared
/// cluster token remains the actual authentication boundary.
/// </summary>
public static class TlsCertificateManager
{
    private static string CertPath => Path.Combine(SettingsPaths.DataDirectory, "node-tls.pfx");

    public static X509Certificate2 GetOrCreate(string nodeName)
    {
        var existing = TryLoad();
        if (existing is not null && existing.NotAfter > DateTime.UtcNow.AddDays(30))
            return existing;

        return Create(nodeName);
    }

    private static X509Certificate2? TryLoad()
    {
        try
        {
            if (!File.Exists(CertPath)) return null;
#pragma warning disable SYSLIB0057 // classic constructor - kept for broad SDK/runtime compatibility
            return new X509Certificate2(CertPath, (string?)null, X509KeyStorageFlags.Exportable);
#pragma warning restore SYSLIB0057
        }
        catch
        {
            return null;
        }
    }

    private static X509Certificate2 Create(string nodeName)
    {
        using var rsa = RSA.Create(2048);
        var subject = $"CN={SanitizeName(nodeName)}.ailocal.local";
        var request = new CertificateRequest(subject, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, critical: true));
        request.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], critical: false)); // TLS server auth

        var sanBuilder = new SubjectAlternativeNameBuilder();
        sanBuilder.AddDnsName("localhost");
        sanBuilder.AddDnsName(Environment.MachineName);
        sanBuilder.AddIpAddress(System.Net.IPAddress.Loopback);
        request.CertificateExtensions.Add(sanBuilder.Build());

        var notBefore = DateTimeOffset.UtcNow.AddDays(-1);
        var notAfter = DateTimeOffset.UtcNow.AddYears(2);
        using var cert = request.CreateSelfSigned(notBefore, notAfter);

        Directory.CreateDirectory(SettingsPaths.DataDirectory);
        var bytes = cert.Export(X509ContentType.Pfx);
        File.WriteAllBytes(CertPath, bytes);

        // Re-load from the exported bytes: CreateSelfSigned's result doesn't
        // always carry an exportable/persistable private key handle on every
        // platform, but re-importing the PFX we just wrote always does.
#pragma warning disable SYSLIB0057
        return new X509Certificate2(bytes, (string?)null, X509KeyStorageFlags.Exportable);
#pragma warning restore SYSLIB0057
    }

    private static string SanitizeName(string name)
    {
        var cleaned = new string(name.Where(c => char.IsLetterOrDigit(c) || c is '-' or '.').ToArray());
        return cleaned.Length > 0 ? cleaned : "node";
    }
}
