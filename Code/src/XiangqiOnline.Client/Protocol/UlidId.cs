using System.Security.Cryptography;

namespace UDM18.Client.Protocol;

public static class UlidId
{
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    public static string New()
    {
        Span<byte> bytes = stackalloc byte[16];
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        bytes[0] = (byte)(timestamp >> 40);
        bytes[1] = (byte)(timestamp >> 32);
        bytes[2] = (byte)(timestamp >> 24);
        bytes[3] = (byte)(timestamp >> 16);
        bytes[4] = (byte)(timestamp >> 8);
        bytes[5] = (byte)timestamp;
        RandomNumberGenerator.Fill(bytes[6..]);

        Span<char> output = stackalloc char[26];
        for (var group = 0; group < output.Length; group++)
        {
            var value = 0;
            for (var bitInGroup = 0; bitInGroup < 5; bitInGroup++)
            {
                var logicalBit = group * 5 + bitInGroup - 2;
                value <<= 1;
                if (logicalBit < 0) continue;
                var byteIndex = logicalBit / 8;
                var bitIndex = 7 - logicalBit % 8;
                value |= (bytes[byteIndex] >> bitIndex) & 1;
            }
            output[group] = Alphabet[value];
        }
        return new string(output);
    }
}
