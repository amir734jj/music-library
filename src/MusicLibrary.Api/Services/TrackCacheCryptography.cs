using System.Security.Cryptography;

namespace MusicLibrary.Api.Services;

internal static class TrackCacheCryptography
{
    private static readonly byte[] Header = "MLTC1"u8.ToArray();

    public static bool TryGetKey(string value, out byte[] key)
    {
        try
        {
            key = Convert.FromBase64String(value.Trim());
            return key.Length == 32;
        }
        catch (FormatException)
        {
            key = [];
            return false;
        }
    }

    public static string GetFingerprint(byte[] key) => Convert.ToHexString(SHA256.HashData(key));

    public static byte[] Encrypt(byte[] plaintext, byte[] key)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var tag = new byte[16];
        var ciphertext = new byte[plaintext.Length];
        using var aes = new AesGcm(key, tag.Length);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);
        return [.. Header, .. nonce, .. tag, .. ciphertext];
    }

    public static byte[] Decrypt(byte[] encrypted, byte[] key)
    {
        const int nonceLength = 12;
        const int tagLength = 16;
        if (encrypted.Length <= Header.Length + nonceLength + tagLength
            || !encrypted.AsSpan(0, Header.Length).SequenceEqual(Header)) throw new CryptographicException("The cached track is invalid.");

        var nonce = encrypted.AsSpan(Header.Length, nonceLength);
        var tag = encrypted.AsSpan(Header.Length + nonceLength, tagLength);
        var ciphertext = encrypted.AsSpan(Header.Length + nonceLength + tagLength);
        var plaintext = new byte[ciphertext.Length];
        using var aes = new AesGcm(key, tagLength);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);
        return plaintext;
    }
}