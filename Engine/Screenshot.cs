using System.IO.Compression;
using Silk.NET.OpenGL;

namespace AlienInvasion.Engine;

/// <summary>Reads the back buffer and writes a PNG (used for automated visual checks).</summary>
public static unsafe class Screenshot
{
    public static void Save(GL gl, int w, int h, string path)
    {
        var px = new byte[w * h * 4];
        gl.PixelStore(PixelStoreParameter.PackAlignment, 1);
        fixed (byte* p = px) gl.ReadPixels(0, 0, (uint)w, (uint)h, PixelFormat.Rgba, PixelType.UnsignedByte, p);
        int step = w > 1600 ? 2 : 1;
        int ow = w / step, oh = h / step;
        var raw = new MemoryStream();
        for (int y = 0; y < oh; y++)
        {
            raw.WriteByte(0);
            int sy = h - 1 - y * step;
            for (int x = 0; x < ow; x++)
            {
                int i = (sy * w + x * step) * 4;
                raw.WriteByte(px[i]); raw.WriteByte(px[i + 1]); raw.WriteByte(px[i + 2]);
            }
        }
        var comp = new MemoryStream();
        using (var z = new ZLibStream(comp, CompressionLevel.Fastest, true)) { raw.Position = 0; raw.CopyTo(z); }
        using var fs = File.Create(path);
        fs.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var ihdr = new byte[13];
        BE(ihdr, 0, ow); BE(ihdr, 4, oh);
        ihdr[8] = 8; ihdr[9] = 2; ihdr[10] = 0; ihdr[11] = 0; ihdr[12] = 0;
        Chunk(fs, "IHDR", ihdr);
        Chunk(fs, "IDAT", comp.ToArray());
        Chunk(fs, "IEND", Array.Empty<byte>());
    }

    static void BE(byte[] b, int o, int v) { b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v; }

    static void Chunk(Stream s, string type, byte[] data)
    {
        var len = new byte[4]; BE(len, 0, data.Length); s.Write(len);
        var t = System.Text.Encoding.ASCII.GetBytes(type);
        s.Write(t); s.Write(data);
        uint crc = Crc(t, 0xFFFFFFFF);
        crc = Crc(data, crc) ^ 0xFFFFFFFF;
        var c = new byte[4]; BE(c, 0, (int)crc); s.Write(c);
    }

    static uint[] table;
    static uint Crc(byte[] d, uint crc)
    {
        if (table == null)
        {
            table = new uint[256];
            for (uint n = 0; n < 256; n++) { uint c = n; for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1; table[n] = c; }
        }
        foreach (var b in d) crc = table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc;
    }
}
