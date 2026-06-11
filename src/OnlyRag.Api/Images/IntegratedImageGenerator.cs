using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;

namespace OnlyRag.Api.Images;

internal static class IntegratedImageGenerator
{
    public static ImageGenerationBinary GeneratePng(
        string prompt,
        string? negativePrompt,
        int width,
        int height,
        long? seed)
    {
        byte[] hash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"{prompt}\n{negativePrompt}\n{seed}"));
        byte[] raw = new byte[(width * 3 + 1) * height];
        int index = 0;
        for (int y = 0; y < height; y++)
        {
            raw[index++] = 0;
            for (int x = 0; x < width; x++)
            {
                raw[index++] = (byte)((x + hash[0] + (y * hash[1])) % 256);
                raw[index++] = (byte)((y + hash[2] + (x * hash[3])) % 256);
                raw[index++] = (byte)(((x / 2) + (y / 2) + hash[4]) % 256);
            }
        }

        using MemoryStream png = new();
        png.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        WriteChunk(png, "IHDR", BuildHeader(width, height));
        WriteChunk(png, "IDAT", Compress(raw));
        WriteChunk(png, "IEND", []);
        return new ImageGenerationBinary(png.ToArray(), "image/png", ".png");
    }

    private static byte[] BuildHeader(int width, int height)
    {
        byte[] header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0, 4), width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4, 4), height);
        header[8] = 8;
        header[9] = 2;
        header[10] = 0;
        header[11] = 0;
        header[12] = 0;
        return header;
    }

    private static byte[] Compress(byte[] raw)
    {
        using MemoryStream compressed = new();
        using (ZLibStream zlib = new(compressed, CompressionLevel.Fastest, leaveOpen: true))
        {
            zlib.Write(raw);
        }

        return compressed.ToArray();
    }

    private static void WriteChunk(Stream stream, string type, byte[] data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        stream.Write(length);
        byte[] typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        stream.Write(typeBytes);
        stream.Write(data);
        uint crc = Crc32(typeBytes, data);
        Span<byte> crcBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
        stream.Write(crcBytes);
    }

    private static uint Crc32(byte[] typeBytes, byte[] data)
    {
        uint crc = 0xffffffff;
        foreach (byte value in typeBytes)
        {
            crc = UpdateCrc(crc, value);
        }

        foreach (byte value in data)
        {
            crc = UpdateCrc(crc, value);
        }

        return crc ^ 0xffffffff;
    }

    private static uint UpdateCrc(uint crc, byte value)
    {
        crc ^= value;
        for (int bit = 0; bit < 8; bit++)
        {
            crc = (crc & 1) == 1 ? 0xedb88320u ^ (crc >> 1) : crc >> 1;
        }

        return crc;
    }
}
