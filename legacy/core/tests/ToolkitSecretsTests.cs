using Toolkit.Core.Tests.Common;
using Xunit;
using Toolkit.Core.Infra;

namespace Toolkit.Core.Tests;

public sealed class ToolkitSecretsTests : TempDirectoryTestFixture
{
    // Python cryptography 生成的权威互操作向量（docs/Common_ToolkitConfig_Spec.md §4）
    private const string InteropVector = "enc:v1:AAECAwQFBgcICQoL6PzJ7ANH8aCXDRrV/IQQDS9nuneqxbZ04i384sXJncIFPxWR";
    private const string InteropPlain = "hello toolkit 密钥";

    private static byte[] InteropKey => Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
    private static byte[] InteropNonce => Enumerable.Range(0, 12).Select(i => (byte)i).ToArray();

    public ToolkitSecretsTests() : base("ToolkitSecretsTests") { }

    [Fact]
    public void EncryptDecrypt_RoundTrip()
    {
        ToolkitSecrets secrets = new ToolkitSecrets(InteropKey);

        string encrypted = secrets.Encrypt("中文密钥 plain-123");

        Assert.StartsWith(ToolkitSecrets.Prefix, encrypted);
        Assert.Equal("中文密钥 plain-123", secrets.Decrypt(encrypted));
    }

    [Fact]
    public void Decrypt_InteropVector_MatchesPythonOutput()
    {
        ToolkitSecrets secrets = new ToolkitSecrets(InteropKey);

        Assert.Equal(InteropPlain, secrets.Decrypt(InteropVector));
    }

    [Fact]
    public void Encrypt_FixedNonce_MatchesInteropVector()
    {
        ToolkitSecrets secrets = new ToolkitSecrets(InteropKey, fixedNonce: InteropNonce);

        Assert.Equal(InteropVector, secrets.Encrypt(InteropPlain));
    }

    [Fact]
    public void Decrypt_NonPrefixedInput_Throws()
    {
        ToolkitSecrets secrets = new ToolkitSecrets(InteropKey);

        Assert.Throws<FormatException>(() => secrets.Decrypt("plain-text-key"));
    }

    [Fact]
    public void FromKeyFile_FirstUse_GeneratesKeyFile_AndRoundTrips()
    {
        string keyFileFullPath = SystemIOWrapper.Combine(TempDir, "toolkit.key");

        ToolkitSecrets secrets = ToolkitSecrets.FromKeyFile(keyFileFullPath);

        Assert.True(SystemIOWrapper.FileExists(keyFileFullPath));
        Assert.Equal(32, SystemIOWrapper.ReadAllBytes(keyFileFullPath).Length);
        string encrypted = secrets.Encrypt("abc");
        // 再次从同一文件构建，必须能解密（密钥持久化）
        Assert.Equal("abc", ToolkitSecrets.FromKeyFile(keyFileFullPath).Decrypt(encrypted));
    }
}
