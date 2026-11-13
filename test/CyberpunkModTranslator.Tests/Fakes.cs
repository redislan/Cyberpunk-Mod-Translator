using System.Text;
using CyberpunkModTranslator.Core;

namespace CyberpunkModTranslator.Tests;

/// <summary>Remplace la DLL du jeu : les archives des tests ne sont pas compressees, elle n'est jamais appelee.</summary>
internal sealed class NoCompressor : ICompressor
{
    public string Name => "tests";
    public byte[] Decompress(byte[] source, int rawSize) => throw new InvalidOperationException("pas de decompression dans les tests");
    public byte[]? Compress(byte[] raw) => null;
    public void Dispose() { }
}

/// <summary>Fabrique un petit fichier de textes du jeu (CR2W, type onscreens) lisible par le moteur.</summary>
internal static class FakeLoc
{
    public static byte[] OnScreens(params (ulong Key, string TextKey, string Text)[] entries)
    {
        var names = new List<string>
        {
            "", "localizationPersistenceOnScreenEntries", "entries", "array:localizationPersistenceOnScreenEntry",
            "primaryKey", "Uint64", "secondaryKey", "String", "femaleVariant", "maleVariant",
        };
        ushort N(string s) => (ushort)names.IndexOf(s);
        static byte[] Str(string s)
        {
            var ms = new MemoryStream();
            RedString.Encode(new BinaryWriter(ms), s);
            return ms.ToArray();
        }
        void Prop(BinaryWriter w, string name, string type, byte[] value)
        {
            w.Write(N(name));
            w.Write(N(type));
            w.Write((uint)value.Length + 4);
            w.Write(value);
        }

        var array = new MemoryStream();
        var aw = new BinaryWriter(array);
        aw.Write(entries.Length);
        foreach (var e in entries)
        {
            aw.Write((byte)0);
            Prop(aw, "primaryKey", "Uint64", BitConverter.GetBytes(e.Key));
            Prop(aw, "secondaryKey", "String", Str(e.TextKey));
            Prop(aw, "femaleVariant", "String", Str(e.Text));
            Prop(aw, "maleVariant", "String", Str(""));
            aw.Write((ushort)0);
        }
        var chunk = new MemoryStream();
        var cw = new BinaryWriter(chunk);
        cw.Write((byte)0);
        Prop(cw, "entries", "array:localizationPersistenceOnScreenEntry", array.ToArray());
        cw.Write((ushort)0);
        var data = chunk.ToArray();

        var strings = new MemoryStream();
        var offsets = new List<uint>();
        foreach (var n in names)
        {
            offsets.Add((uint)strings.Length);
            strings.Write(Encoding.UTF8.GetBytes(n));
            strings.WriteByte(0);
        }
        var stringBytes = strings.ToArray();

        const int header = 160;
        var namesAt = header + stringBytes.Length;
        var exportsAt = namesAt + names.Count * 8;
        var dataAt = exportsAt + 24;
        var file = new MemoryStream();
        var fw = new BinaryWriter(file);
        fw.Write(Cr2wFile.Magic);
        fw.Write(195u);
        fw.Write(0u);
        fw.Write(0UL);
        fw.Write(0u);
        fw.Write((uint)(dataAt + data.Length));
        fw.Write((uint)(dataAt + data.Length));
        fw.Write(0u);
        fw.Write(6u);
        var tables = new (uint Offset, uint Count)[10];
        tables[0] = (header, (uint)stringBytes.Length);
        tables[1] = ((uint)namesAt, (uint)names.Count);
        tables[4] = ((uint)exportsAt, 1);
        foreach (var (o, c) in tables)
        {
            fw.Write(o);
            fw.Write(c);
            fw.Write(0u);
        }
        fw.Write(stringBytes);
        foreach (var o in offsets)
        {
            fw.Write(o);
            fw.Write(0u);
        }
        fw.Write((ushort)1);
        fw.Write((ushort)0);
        fw.Write(0u);
        fw.Write((uint)data.Length);
        fw.Write((uint)dataAt);
        fw.Write(0u);
        fw.Write(0u);
        fw.Write(data);
        return file.ToArray();
    }
}
