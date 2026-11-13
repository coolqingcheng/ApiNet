using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace PostNet.Proxy.Certificates;

public sealed class CertificateAuthority : IDisposable
{
    private readonly string _directory;
    private X509Certificate2? _root;
    private readonly ConcurrentDictionary<string, X509Certificate2> _hosts = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();
    public CertificateAuthority(string directory)
    {
        _directory = Path.Combine(directory, "certificates");
        Directory.CreateDirectory(_directory);
    }

    private X509Certificate2 Root
    {
        get
        {
            lock (_lock)
            {
                if (_root is not null)
                    return _root;
                var path = Path.Combine(_directory, "apinet-root.pfx");
                if (File.Exists(path))
                    return _root = X509CertificateLoader.LoadPkcs12FromFile(path, null, X509KeyStorageFlags.EphemeralKeySet | X509KeyStorageFlags.Exportable);
                using var key = RSA.Create(3072);
                var request = new CertificateRequest("CN=Post.NET Local Debugging CA", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
                request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
                request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
                _root = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(5));
                File.WriteAllBytes(path, _root.Export(X509ContentType.Pfx));
                if (!OperatingSystem.IsWindows())
                    File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                return _root;
            }
        }
    }

    public string ExportPublicCertificate()
    {
        var path = Path.Combine(_directory, "Post.NET-Root-CA.cer");
        File.WriteAllBytes(path, Root.Export(X509ContentType.Cert));
        return path;
    }

    public X509Certificate2 ForHost(string host) => _hosts.GetOrAdd(host, name =>
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=Post.NET Debug Endpoint", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        if (IPAddress.TryParse(name, out var ip))
            names.AddIpAddress(ip);
        else
            names.AddDnsName(name);
        request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        var usages = new OidCollection { new("1.3.6.1.5.5.7.3.1") };
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(usages, false));
        using var issued = request.Create(Root, DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddDays(30), RandomNumberGenerator.GetBytes(16));
        var certificate = issued.CopyWithPrivateKey(key);
        if (!OperatingSystem.IsWindows())
            return certificate;
        try
        {
            return X509CertificateLoader.LoadPkcs12(certificate.Export(X509ContentType.Pfx), null, X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.Exportable);
        }
        finally { certificate.Dispose(); }
    });

    public void Dispose()
    {
        foreach (var certificate in _hosts.Values)
            certificate.Dispose();
        _root?.Dispose();
    }
}
