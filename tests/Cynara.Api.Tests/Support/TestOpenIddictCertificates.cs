using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Cynara.Api.Tests.Support;

/// <summary>Temporary PEM credentials used by production-environment tests.</summary>
internal sealed class TestOpenIddictCertificates : IDisposable
{
    private readonly string directory;

    private TestOpenIddictCertificates(string directory)
    {
        this.directory = directory;
        SigningCertificatePath = Path.Combine(directory, "signing.crt");
        SigningKeyPath = Path.Combine(directory, "signing.key");
        EncryptionCertificatePath = Path.Combine(directory, "encryption.crt");
        EncryptionKeyPath = Path.Combine(directory, "encryption.key");
    }

    public string SigningCertificatePath { get; }

    public string SigningKeyPath { get; }

    public string EncryptionCertificatePath { get; }

    public string EncryptionKeyPath { get; }

    public static TestOpenIddictCertificates Create()
    {
        var certificates = new TestOpenIddictCertificates(
            Path.Combine(Path.GetTempPath(), "cynara-openiddict-" + Guid.NewGuid()));
        Directory.CreateDirectory(certificates.directory);
        WriteCertificate(
            certificates.SigningCertificatePath,
            certificates.SigningKeyPath,
            "Cynara test signing certificate");
        WriteCertificate(
            certificates.EncryptionCertificatePath,
            certificates.EncryptionKeyPath,
            "Cynara test encryption certificate");
        return certificates;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
            // The temporary directory was already removed.
        }

        GC.SuppressFinalize(this);
    }

    private static void WriteCertificate(
        string certificatePath,
        string keyPath,
        string subject)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            new X500DistinguishedName("CN=" + subject),
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-5),
            DateTimeOffset.UtcNow.AddYears(1));

        File.WriteAllText(
            certificatePath,
            PemEncoding.Write(
                "CERTIFICATE",
                certificate.Export(X509ContentType.Cert)));
        File.WriteAllText(
            keyPath,
            PemEncoding.Write("PRIVATE KEY", rsa.ExportPkcs8PrivateKey()));
    }
}
