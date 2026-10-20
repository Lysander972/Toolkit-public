using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace Toolkit.Core.Infra;

/// <summary>
/// 配置密钥加解密层（enc:v1）。规范唯一权威：docs/Common_ToolkitConfig_Spec.md §4。
/// 格式：enc:v1: + base64(nonce(12B) ‖ tag(16B) ‖ ciphertext)，明文 UTF-8。
/// 空串 = 未配置，不加密不加前缀——空值判定是调用方职责，本层不特判。
/// </summary>
public sealed class ToolkitSecrets
{
    public const string Prefix = "enc:v1:";
    private const int KeyLength = 32;
    private const int NonceLength = 12;
    private const int TagLength = 16;

    private readonly byte[] _key;
    private readonly byte[]? _fixedNonce;

    /// <param name="key">32 字节 AES-256 密钥</param>
    /// <param name="fixedNonce">测试缝：固定 nonce；生产必须为 null（每次加密随机）</param>
    public ToolkitSecrets(byte[] key, byte[]? fixedNonce = null)
    {
        if (key.Length != KeyLength)
            throw new ArgumentException($"密钥必须为 {KeyLength} 字节", nameof(key));
        if (fixedNonce != null && fixedNonce.Length != NonceLength)
            throw new ArgumentException($"nonce 必须为 {NonceLength} 字节", nameof(fixedNonce));
        _key = key;
        _fixedNonce = fixedNonce;
    }

    /// <summary>
    /// 从密钥文件构建；文件缺失时生成随机密钥并落盘（写前再查，容忍并发首建）。
    /// 密钥文件为裸 32 字节，Windows 下限当前用户 ACL。
    /// </summary>
    public static ToolkitSecrets FromKeyFile(string keyFileFullPath)
    {
        if (!SystemIOWrapper.FileExists(keyFileFullPath))
        {
            SystemIOWrapper.CreateDirectory(SystemIOWrapper.GetParentDirectory(keyFileFullPath));
            if (!SystemIOWrapper.FileExists(keyFileFullPath))
            {
                byte[] newKey = RandomNumberGenerator.GetBytes(KeyLength);
                SystemIOWrapper.WriteAllBytes(keyFileFullPath, newKey);
                RestrictToCurrentUser(keyFileFullPath);
            }
        }
        return new ToolkitSecrets(SystemIOWrapper.ReadAllBytes(keyFileFullPath));
    }

    public string Encrypt(string plain)
    {
        byte[] nonce = _fixedNonce != null ? _fixedNonce : RandomNumberGenerator.GetBytes(NonceLength);
        byte[] plainBytes = Encoding.UTF8.GetBytes(plain);
        byte[] ciphertext = new byte[plainBytes.Length];
        byte[] tag = new byte[TagLength];
        using (AesGcm aes = new AesGcm(_key, TagLength))
        {
            aes.Encrypt(nonce, plainBytes, ciphertext, tag);
        }
        // .NET AesGcm 的 tag 与密文分离输出，按规范字节序组装：nonce ‖ tag ‖ ciphertext
        byte[] payload = new byte[NonceLength + TagLength + ciphertext.Length];
        nonce.CopyTo(payload, 0);
        tag.CopyTo(payload, NonceLength);
        ciphertext.CopyTo(payload, NonceLength + TagLength);
        return Prefix + Convert.ToBase64String(payload);
    }

    public string Decrypt(string encoded)
    {
        if (!encoded.StartsWith(Prefix, StringComparison.Ordinal))
            throw new FormatException("非 enc:v1 密文");
        byte[] payload = Convert.FromBase64String(encoded[Prefix.Length..]);
        byte[] nonce = payload[..NonceLength];
        byte[] tag = payload[NonceLength..(NonceLength + TagLength)];
        byte[] ciphertext = payload[(NonceLength + TagLength)..];
        byte[] plainBytes = new byte[ciphertext.Length];
        using (AesGcm aes = new AesGcm(_key, TagLength))
        {
            aes.Decrypt(nonce, ciphertext, tag, plainBytes);
        }
        return Encoding.UTF8.GetString(plainBytes);
    }

    private static void RestrictToCurrentUser(string fileFullPath)
    {
        if (!OperatingSystem.IsWindows())
            return;
        FileSecurity security = new FileSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(
            WindowsIdentity.GetCurrent().Name,
            FileSystemRights.FullControl,
            AccessControlType.Allow));
        new FileInfo(fileFullPath).SetAccessControl(security);
    }
}
