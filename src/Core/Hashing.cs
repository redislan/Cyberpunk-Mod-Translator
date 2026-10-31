using System.Text;

namespace CyberpunkModTranslator.Core;

/// <summary>CRC-32 standard (IEEE, polynome 0xEDB88320) : sommes de controle des fichiers CR2W.</summary>
public static class Crc32
{
    private static readonly uint[] Table = BuildTable();

    private static uint[] BuildTable()
    {
        var t = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            var c = i;
            for (var k = 0; k < 8; k++)
                c = (c & 1) != 0 ? (c >> 1) ^ 0xEDB88320u : c >> 1;
            t[i] = c;
        }
        return t;
    }

    public static uint Compute(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
            crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return ~crc;
    }
}

/// <summary>CRC-64/XZ : somme de controle de l'index d'une archive.</summary>
public static class Crc64
{
    private static readonly ulong[] Table = BuildTable();

    private static ulong[] BuildTable()
    {
        var t = new ulong[256];
        for (ulong i = 0; i < 256; i++)
        {
            var c = i;
            for (var k = 0; k < 8; k++)
                c = (c & 1) != 0 ? (c >> 1) ^ 0xC96C5795D7870F42UL : c >> 1;
            t[i] = c;
        }
        return t;
    }

    public static ulong Compute(ReadOnlySpan<byte> data)
    {
        var crc = ulong.MaxValue;
        foreach (var b in data)
            crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return ~crc;
    }
}

/// <summary>Chemins de ressources du jeu et leur hash FNV-1a 64 bits (identifiant d'un fichier dans une archive).</summary>
public static class ResourcePath
{
    private static readonly char[] TrimChars = { '\'', '"', '/', '\\', ' ', '\n', '\r' };

    /// <summary>Minuscules, separateurs uniformises en '\', pas de separateur en double.</summary>
    public static string Sanitize(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var t = text.Trim(TrimChars);
        if (t.Length == 0) return "";
        var lower = t.ToLowerInvariant();
        var sb = new StringBuilder(lower.Length);
        sb.Append(lower[0]);
        for (var i = 1; i < lower.Length; i++)
        {
            var c = lower[i];
            if (c == '\\' || c == '/')
            {
                if (sb[sb.Length - 1] != '\\') sb.Append('\\');
                continue;
            }
            sb.Append(c);
        }
        return sb.ToString();
    }

    public static ulong Hash(string path)
    {
        var bytes = Encoding.ASCII.GetBytes(Sanitize(path));
        var h = 0xCBF29CE484222325UL;
        foreach (var b in bytes)
        {
            h ^= b;
            h *= 0x100000001B3UL;
        }
        return h;
    }
}
