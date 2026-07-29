using System.Security.Cryptography;
using System.Text;
using TaskRunner.Core.Shared.Security;
using Xunit;

namespace TaskRunner.Family.Tests.Security;

// 定义测试集合，禁止并发运行（避免环境变量冲突）
[Collection("AesApiKeyEncryption")]
public class AesApiKeyEncryptionTests : IDisposable
{
    private readonly string _originalEnvVar;
    private readonly string _originalDataDir;
    private readonly string _testKeyFilePath;
    private readonly string _testDataDir;

    public AesApiKeyEncryptionTests()
    {
        _originalEnvVar = Environment.GetEnvironmentVariable("YJ_ENCRYPTION_KEY");
        _originalDataDir = Environment.GetEnvironmentVariable("YJ_DATA_DIR");

        _testDataDir = Path.Combine(Path.GetTempPath(), $"aes_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDataDir);
        _testKeyFilePath = Path.Combine(_testDataDir, ".yj-key");

        Environment.SetEnvironmentVariable("YJ_DATA_DIR", _testDataDir);
        Environment.SetEnvironmentVariable("YJ_ENCRYPTION_KEY", null);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("YJ_ENCRYPTION_KEY", _originalEnvVar);
        Environment.SetEnvironmentVariable("YJ_DATA_DIR", _originalDataDir);

        if (Directory.Exists(_testDataDir))
        {
            Directory.Delete(_testDataDir, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    private void SetEncryptionKey(string key)
    {
        Environment.SetEnvironmentVariable("YJ_ENCRYPTION_KEY", key);
    }

    [Fact]
    public void Encrypt_EmptyString_ReturnsEmpty()
    {
        var result = AesApiKeyEncryption.Encrypt("");
        Assert.Equal("", result);
    }

    [Fact]
    public void Encrypt_Null_ReturnsEmpty()
    {
        var result = AesApiKeyEncryption.Encrypt(null!);
        Assert.Equal("", result);
    }

    [Fact]
    public void Encrypt_ValidKey_ReturnsEncryptedString()
    {
        SetEncryptionKey("test-encryption-key-12345");
        var plainText = "sk-test-api-key-12345";

        var encrypted = AesApiKeyEncryption.Encrypt(plainText);

        Assert.NotEmpty(encrypted);
        Assert.StartsWith("A:", encrypted);
    }

    [Fact]
    public void Encrypt_SamePlaintext_ProducesDifferentCiphertext()
    {
        SetEncryptionKey("test-encryption-key-12345");
        var plainText = "sk-test-api-key-12345";

        var encrypted1 = AesApiKeyEncryption.Encrypt(plainText);
        var encrypted2 = AesApiKeyEncryption.Encrypt(plainText);

        Assert.NotEqual(encrypted1, encrypted2);
    }

    [Fact]
    public void Decrypt_ValidEncryptedText_ReturnsOriginal()
    {
        SetEncryptionKey("test-encryption-key-12345");
        var plainText = "sk-test-api-key-12345";

        var encrypted = AesApiKeyEncryption.Encrypt(plainText);
        var decrypted = AesApiKeyEncryption.Decrypt(encrypted);

        Assert.Equal(plainText, decrypted);
    }

    [Fact]
    public void Decrypt_EmptyString_ReturnsEmpty()
    {
        var result = AesApiKeyEncryption.Decrypt("");
        Assert.Equal("", result);
    }

    [Fact]
    public void Decrypt_Null_ReturnsEmpty()
    {
        var result = AesApiKeyEncryption.Decrypt(null!);
        Assert.Equal("", result);
    }

    [Fact]
    public void Decrypt_InvalidPrefix_ReturnsEmpty()
    {
        var result = AesApiKeyEncryption.Decrypt("invalid:ciphertext");
        Assert.Equal("", result);
    }

    [Fact]
    public void Decrypt_WrongKey_ReturnsEmpty()
    {
        SetEncryptionKey("key-one-12345");
        var plainText = "sk-test-api-key-12345";
        var encrypted = AesApiKeyEncryption.Encrypt(plainText);

        SetEncryptionKey("key-two-67890");
        var decrypted = AesApiKeyEncryption.Decrypt(encrypted);

        Assert.NotEqual(plainText, decrypted);
    }

    [Fact]
    public void DecryptWithFingerprint_EmptyCipherText_ReturnsEmpty()
    {
        var fingerprint = SHA256.HashData(Encoding.UTF8.GetBytes("test"));
        var result = AesApiKeyEncryption.DecryptWithFingerprint("", fingerprint);
        Assert.Equal("", result);
    }

    [Fact]
    public void DecryptWithFingerprint_InvalidPrefix_ReturnsEmpty()
    {
        var fingerprint = SHA256.HashData(Encoding.UTF8.GetBytes("test"));
        var result = AesApiKeyEncryption.DecryptWithFingerprint("invalid", fingerprint);
        Assert.Equal("", result);
    }

    [Fact]
    public void DecryptWithFingerprint_WrongFingerprint_ReturnsEmpty()
    {
        SetEncryptionKey("correct-key");
        var plainText = "sk-test-api-key-12345";
        var encrypted = AesApiKeyEncryption.Encrypt(plainText);

        var wrongFingerprint = SHA256.HashData(Encoding.UTF8.GetBytes("wrong-key"));
        var decrypted = AesApiKeyEncryption.DecryptWithFingerprint(encrypted, wrongFingerprint);

        Assert.NotEqual(plainText, decrypted);
        Assert.Equal("", decrypted);
    }

    [Fact]
    public void DecryptWithFingerprint_CorrectFingerprint_ReturnsOriginal()
    {
        SetEncryptionKey("test-encryption-key");
        var plainText = "sk-test-api-key";
        var encrypted = AesApiKeyEncryption.Encrypt(plainText);
        var fingerprint = AesApiKeyEncryption.ResolveFingerprint();

        var decrypted = AesApiKeyEncryption.DecryptWithFingerprint(encrypted, fingerprint);

        Assert.Equal(plainText, decrypted);
    }

    [Fact]
    public void EncryptDecrypt_UnicodeText_WorksCorrectly()
    {
        SetEncryptionKey("test-key-unicode");
        var plainText = "API密钥: sk-测试-ключ-🔑";

        var encrypted = AesApiKeyEncryption.Encrypt(plainText);
        var decrypted = AesApiKeyEncryption.Decrypt(encrypted);

        Assert.Equal(plainText, decrypted);
    }

    [Fact]
    public void EncryptDecrypt_LongText_WorksCorrectly()
    {
        SetEncryptionKey("test-key-long");
        var plainText = new string('A', 10000);

        var encrypted = AesApiKeyEncryption.Encrypt(plainText);
        var decrypted = AesApiKeyEncryption.Decrypt(encrypted);

        Assert.Equal(plainText, decrypted);
    }

    [Fact]
    public void GenerateKeyFile_CreatesKeyFile()
    {
        Assert.False(File.Exists(_testKeyFilePath));

        var key = AesApiKeyEncryption.GenerateKeyFile();

        Assert.True(File.Exists(_testKeyFilePath));
        Assert.NotEmpty(key);
        Assert.Equal(64, key.Length);
    }

    [Fact]
    public void ResolveFingerprint_PreferEnvVarOverFile()
    {
        var envKey = "environment-variable-key";
        Environment.SetEnvironmentVariable("YJ_ENCRYPTION_KEY", envKey);

        var fingerprint = AesApiKeyEncryption.ResolveFingerprint();
        var expectedFingerprint = SHA256.HashData(Encoding.UTF8.GetBytes(envKey));

        Assert.Equal(expectedFingerprint, fingerprint);
    }

    [Fact]
    public void ResolveFingerprint_FallsBackToLegacy()
    {
        Environment.SetEnvironmentVariable("YJ_ENCRYPTION_KEY", null);

        var fingerprint = AesApiKeyEncryption.ResolveFingerprint();

        Assert.NotNull(fingerprint);
        Assert.Equal(32, fingerprint.Length);
    }
}
