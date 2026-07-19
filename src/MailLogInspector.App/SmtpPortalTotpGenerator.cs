using System.Buffers.Binary;
using System.Security.Cryptography;

namespace MailLogInspector.App;

public static class SmtpPortalTotpGenerator
{
    private const int TimeStepSeconds = 30;

    public static string Generate(string base32Secret, DateTimeOffset timestamp)
    {
        byte[] secret = DecodeBase32(base32Secret);
        long counter = timestamp.ToUnixTimeSeconds() / TimeStepSeconds;
        Span<byte> counterBytes = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64BigEndian(counterBytes, counter);

        byte[] hash = HMACSHA1.HashData(secret, counterBytes);
        int offset = hash[^1] & 0x0F;
        int binaryCode =
            ((hash[offset] & 0x7F) << 24) |
            ((hash[offset + 1] & 0xFF) << 16) |
            ((hash[offset + 2] & 0xFF) << 8) |
            (hash[offset + 3] & 0xFF);

        return (binaryCode % 1_000_000).ToString("D6");
    }

    public static IReadOnlyList<string> GenerateWindow(string base32Secret, DateTimeOffset timestamp)
    {
        return
        [
            Generate(base32Secret, timestamp),
            Generate(base32Secret, timestamp.AddSeconds(-TimeStepSeconds)),
            Generate(base32Secret, timestamp.AddSeconds(TimeStepSeconds))
        ];
    }

    private static byte[] DecodeBase32(string value)
    {
        string normalized = new string((value ?? string.Empty)
            .Where(character => !char.IsWhiteSpace(character) && character != '-')
            .Select(char.ToUpperInvariant)
            .ToArray())
            .TrimEnd('=');
        if (normalized.Length == 0)
        {
            throw new FormatException("Het MFA-secret ontbreekt.");
        }

        List<byte> bytes = new();
        int buffer = 0;
        int bitsInBuffer = 0;
        foreach (char character in normalized)
        {
            int valueIndex = character switch
            {
                >= 'A' and <= 'Z' => character - 'A',
                >= '2' and <= '7' => character - '2' + 26,
                _ => throw new FormatException("Het MFA-secret is geen geldige Base32-code.")
            };

            buffer = (buffer << 5) | valueIndex;
            bitsInBuffer += 5;
            if (bitsInBuffer < 8)
            {
                continue;
            }

            bitsInBuffer -= 8;
            bytes.Add((byte)(buffer >> bitsInBuffer));
            buffer &= (1 << bitsInBuffer) - 1;
        }

        return bytes.ToArray();
    }
}
