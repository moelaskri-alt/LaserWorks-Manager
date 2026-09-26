using System.IO.Compression;

namespace LaserWorks.Tests.Support;

/// <summary>Minimal PNG writer (8-bit RGBA) for test screenshots.</summary>
public static class Png
{
    public static void Write(string path, int width, int height, int stride, byte[] pixels, bool rgba)
    {
        var raw = new byte[(width * 4 + 1) * height];
        for (var y = 0; y < height; y++)
        {
            var o = y * (width * 4 + 1);
            raw[o] = 0; // filter: none
            for (var x = 0; x < width; x++)
            {
                var i = y * stride + x * 4;
                var d = o + 1 + x * 4;
                raw[d] = rgba ? pixels[i] : pixels[i + 2];
                raw[d + 1] = pixels[i + 1];
                raw[d + 2] = rgba ? pixels[i + 2] : pixels[i];
                raw[d + 3] = pixels[i + 3];
            }
        }
        using var file = File.Create(path);
        file.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var ihdr = new byte[13];
        WriteInt(ihdr, 0, width);
        WriteInt(ihdr, 4, height);
        ihdr[8] = 8; ihdr[9] = 6; // 8-bit RGBA
        Chunk(file, "IHDR", ihdr);
        using (var ms = new MemoryStream())
        {
            using (var z = new ZLibStream(ms, CompressionLevel.Fastest, leaveOpen: true)) z.Write(raw);
            Chunk(file, "IDAT", ms.ToArray());
        }
        Chunk(file, "IEND", Array.Empty<byte>());
    }

    private static void WriteInt(byte[] b, int o, int v)
    {
        b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v;
    }

    private static void Chunk(Stream s, string type, byte[] data)
    {
        var len = new byte[4];
        WriteInt(len, 0, data.Length);
        s.Write(len);
        var t = System.Text.Encoding.ASCII.GetBytes(type);
        s.Write(t);
        s.Write(data);
        var crc = new byte[4];
        WriteInt(crc, 0, (int)Crc(t, data));
        s.Write(crc);
    }

    private static readonly uint[] Table = Enumerable.Range(0, 256).Select(n =>
    {
        var c = (uint)n;
        for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
        return c;
    }).ToArray();

    private static uint Crc(byte[] type, byte[] data)
    {
        var c = 0xFFFFFFFFu;
        foreach (var b in type) c = Table[(c ^ b) & 0xFF] ^ (c >> 8);
        foreach (var b in data) c = Table[(c ^ b) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFFu;
    }
}
