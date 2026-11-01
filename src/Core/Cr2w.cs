using System.Text;

namespace CyberpunkModTranslator.Core;

// ---------------------------------------------------------------------------------------------
// Valeurs des proprietes d'un fichier CR2W.
// On ne decode que ce qui sert a la traduction (textes, structures, tableaux de structures) ;
// tout le reste est garde tel quel, octet pour octet.
// ---------------------------------------------------------------------------------------------

public abstract class RedValue
{
    public abstract void Write(BinaryWriter w);
}

/// <summary>Valeur non decodee, recopiee a l'identique.</summary>
public sealed class RedRaw : RedValue
{
    public byte[] Bytes { get; }
    public RedRaw(byte[] bytes) => Bytes = bytes;
    public override void Write(BinaryWriter w) => w.Write(Bytes);
}

/// <summary>Chaine de caracteres (type "String").</summary>
public sealed class RedString : RedValue
{
    private readonly byte[] _original;
    private string _value;
    public bool Modified { get; private set; }

    public RedString(string value, byte[] originalBytes)
    {
        _value = value;
        _original = originalBytes;
    }

    public string Value
    {
        get => _value;
        set
        {
            if (value == _value) return;
            _value = value;
            Modified = true;
        }
    }

    public override void Write(BinaryWriter w)
    {
        if (!Modified)
        {
            w.Write(_original);
            return;
        }
        Encode(w, _value);
    }

    /// <summary>Encodage du jeu : longueur VLQ signee (negative = UTF-8) puis les octets UTF-8.</summary>
    public static void Encode(BinaryWriter w, string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            w.Write((byte)0);
            return;
        }
        var bytes = Encoding.UTF8.GetBytes(value);
        var length = (uint)bytes.Length;
        var first = (byte)((length & 0x3F) | 0x80);
        length >>= 6;
        if (length > 0) first |= 0x40;
        w.Write(first);
        while (length > 0)
        {
            var b = (byte)(length & 0x7F);
            length >>= 7;
            if (length > 0) b |= 0x80;
            w.Write(b);
        }
        w.Write(bytes);
    }
}

/// <summary>Une propriete : index du nom, index du type, valeur.</summary>
public sealed class RedProperty
{
    public ushort NameIndex { get; }
    public ushort TypeIndex { get; }
    public string Name { get; }
    public string Type { get; }
    public RedValue Value { get; }

    public RedProperty(ushort nameIndex, ushort typeIndex, string name, string type, RedValue value)
    {
        NameIndex = nameIndex;
        TypeIndex = typeIndex;
        Name = name;
        Type = type;
        Value = value;
    }

    public void Write(BinaryWriter w)
    {
        var body = new MemoryStream();
        using (var bw = new BinaryWriter(body, Encoding.UTF8, true))
            Value.Write(bw);
        w.Write(NameIndex);
        w.Write(TypeIndex);
        w.Write((uint)body.Length + 4);
        w.Write(body.ToArray());
    }
}

/// <summary>Structure (ou classe) : octet 0, proprietes, terminateur 0x0000.</summary>
public sealed class RedStruct : RedValue
{
    public string Type { get; }
    public List<RedProperty> Properties { get; } = new();

    public RedStruct(string type) => Type = type;

    public RedProperty? Get(string name) => Properties.FirstOrDefault(p => p.Name == name);

    public override void Write(BinaryWriter w)
    {
        w.Write((byte)0);
        foreach (var p in Properties) p.Write(w);
        w.Write((ushort)0);
    }
}

/// <summary>Tableau : nombre d'elements (int32) puis les elements.</summary>
public sealed class RedArray : RedValue
{
    public List<RedValue> Items { get; } = new();

    public override void Write(BinaryWriter w)
    {
        w.Write(Items.Count);
        foreach (var i in Items) i.Write(w);
    }
}

public readonly record struct Cr2wExport(ushort ClassName, ushort ObjectFlags, uint ParentId, uint DataSize,
    uint DataOffset, uint Template, uint Crc32);

/// <summary>Un "chunk" (objet) du fichier : sa classe, ses proprietes et d'eventuelles donnees en fin d'objet.</summary>
public sealed class Cr2wChunk
{
    public string ClassName { get; init; } = "";
    public RedStruct? Data { get; init; }
    public byte[] Tail { get; init; } = Array.Empty<byte>();
    public byte[]? RawData { get; init; }

    public byte[] Serialize()
    {
        if (Data == null) return RawData ?? Array.Empty<byte>();
        var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, Encoding.UTF8, true))
        {
            Data.Write(w);
            w.Write(Tail);
        }
        return ms.ToArray();
    }
}

/// <summary>
/// Fichier CR2W (format des ressources du jeu). Lecture complete de l'en-tete et des tables,
/// decodage partiel des objets, et reecriture avec tailles, offsets et sommes de controle recalcules.
/// </summary>
public sealed class Cr2wFile
{
    public const uint Magic = 0x57325243; // "CR2W"
    private const int HeaderSize = 160;

    private readonly byte[] _data;

    public uint Version { get; private set; }
    public uint Flags { get; private set; }
    public ulong TimeStamp { get; private set; }
    public uint BuildVersion { get; private set; }
    public uint ObjectsEnd { get; private set; }
    public uint BuffersEnd { get; private set; }
    public uint NumChunks { get; private set; }
    public (uint Offset, uint Count, uint Crc)[] Tables { get; } = new (uint, uint, uint)[10];
    public List<string> Names { get; } = new();

    /// <summary>Chemins des ressources referencees (rRef / raRef pointent ici, index a partir de 1).</summary>
    public List<string> Imports { get; } = new();

    public List<Cr2wExport> Exports { get; } = new();
    public List<Cr2wChunk> Chunks { get; } = new();

    public string RootClass => Chunks.Count > 0 ? Chunks[0].ClassName : "";

    private Cr2wFile(byte[] data) => _data = data;

    public static bool LooksLikeCr2w(byte[] data) => data.Length > HeaderSize && BitConverter.ToUInt32(data, 0) == Magic;

    public static Cr2wFile Parse(byte[] data)
    {
        if (!LooksLikeCr2w(data)) throw new InvalidDataException(UiText.T("core.notCr2w"));
        var f = new Cr2wFile(data);
        f.ParseInternal();
        return f;
    }

    private void ParseInternal()
    {
        var r = new BinaryReader(new MemoryStream(_data));
        r.ReadUInt32();
        Version = r.ReadUInt32();
        Flags = r.ReadUInt32();
        TimeStamp = r.ReadUInt64();
        BuildVersion = r.ReadUInt32();
        ObjectsEnd = r.ReadUInt32();
        BuffersEnd = r.ReadUInt32();
        r.ReadUInt32(); // crc de l'en-tete
        NumChunks = r.ReadUInt32();
        for (var i = 0; i < 10; i++)
            Tables[i] = (r.ReadUInt32(), r.ReadUInt32(), r.ReadUInt32());

        // Table des chaines (noms) : suite de chaines terminees par 0, reperees par leur offset
        var strings = new Dictionary<uint, string>();
        var (so, ssize, _) = Tables[0];
        var pos = (int)so;
        while (pos < so + ssize)
        {
            var end = Array.IndexOf(_data, (byte)0, pos);
            if (end < 0) break;
            strings[(uint)(pos - so)] = Encoding.UTF8.GetString(_data, pos, end - pos);
            pos = end + 1;
        }

        var (no, ncount, _) = Tables[1];
        for (var i = 0; i < ncount; i++)
        {
            var offset = BitConverter.ToUInt32(_data, (int)no + i * 8);
            Names.Add(strings.TryGetValue(offset, out var s) ? s : "");
        }

        var (io, icount, _) = Tables[2];
        for (var i = 0; i < icount; i++)
        {
            var offset = BitConverter.ToUInt32(_data, (int)io + i * 8);
            Imports.Add(strings.TryGetValue(offset, out var s) ? s : "");
        }

        var (eo, ecount, _) = Tables[4];
        for (var i = 0; i < ecount; i++)
        {
            var o = (int)eo + i * 24;
            Exports.Add(new Cr2wExport(
                BitConverter.ToUInt16(_data, o), BitConverter.ToUInt16(_data, o + 2),
                BitConverter.ToUInt32(_data, o + 4), BitConverter.ToUInt32(_data, o + 8),
                BitConverter.ToUInt32(_data, o + 12), BitConverter.ToUInt32(_data, o + 16),
                BitConverter.ToUInt32(_data, o + 20)));
        }

        foreach (var e in Exports)
        {
            var className = Name(e.ClassName);
            var start = (int)e.DataOffset;
            var end = start + (int)e.DataSize;
            try
            {
                var (cls, consumed) = ReadStruct(className, start, end);
                Chunks.Add(new Cr2wChunk { ClassName = className, Data = cls, Tail = _data[consumed..end] });
            }
            catch (Exception ex)
            {
                // Bloc non decodable : il est garde tel quel (et reecrit a l'identique)
                Log.Debug($"CR2W : bloc {className} garde en brut ({ex.Message})");
                Chunks.Add(new Cr2wChunk { ClassName = className, RawData = _data[start..end] });
            }
        }
    }

    public string Name(int index) => index >= 0 && index < Names.Count ? Names[index] : $"#{index}";

    // -----------------------------------------------------------------------------------------
    // Decodage des valeurs
    // -----------------------------------------------------------------------------------------

    private (RedStruct, int) ReadStruct(string type, int o, int limit)
    {
        if (o >= limit || _data[o] != 0) throw new InvalidDataException(UiText.T("core.cr2w.noZero"));
        o++;
        var s = new RedStruct(type);
        while (true)
        {
            if (o + 2 > limit) throw new InvalidDataException(UiText.T("core.cr2w.truncated"));
            var nameIndex = BitConverter.ToUInt16(_data, o);
            o += 2;
            if (nameIndex == 0) break;
            if (o + 6 > limit) throw new InvalidDataException(UiText.T("core.cr2w.propTruncated"));
            var typeIndex = BitConverter.ToUInt16(_data, o);
            var size = BitConverter.ToUInt32(_data, o + 2);
            o += 6;
            if (nameIndex >= Names.Count || typeIndex >= Names.Count || size < 4 || o + size - 4 > limit)
                throw new InvalidDataException(UiText.T("core.cr2w.propInvalid"));
            var valueEnd = o + (int)size - 4;
            var propType = Names[typeIndex];
            var value = ReadValue(propType, o, valueEnd);
            s.Properties.Add(new RedProperty(nameIndex, typeIndex, Names[nameIndex], propType, value));
            o = valueEnd;
        }
        return (s, o);
    }

    private static readonly HashSet<string> Opaque = new()
    {
        "Bool", "Int8", "Uint8", "Int16", "Uint16", "Int32", "Uint32", "Int64", "Uint64", "Float", "Double",
        "CName", "TweakDBID", "CRUID", "LocalizationString", "NodeRef", "DataBuffer", "SharedDataBuffer",
        "serializationDeferredDataBuffer", "gamedataLocKeyWrapper", "EditorObjectID", "CDateTime", "CGUID",
        "CVariant", "MessageResourcePath",
    };

    private RedValue ReadValue(string type, int o, int end)
    {
        try
        {
            if (type == "String")
            {
                var (s, next) = ReadString(o);
                if (next == end) return new RedString(s, _data[o..end]);
            }
            else if (type.StartsWith("array:"))
            {
                var inner = type[6..];
                var arr = TryReadArray(inner, o, end);
                if (arr != null) return arr;
            }
            else if (!Opaque.Contains(type) && !type.Contains(':') && end - o >= 3 && _data[o] == 0)
            {
                var (s, next) = ReadStruct(type, o, end);
                if (next == end) return s;
            }
        }
        catch (Exception ex)
        {
            // Valeur non decodable : on garde les octets bruts (reecrits a l'identique)
            Log.Debug($"CR2W : valeur {type} gardee en brut ({ex.Message})");
        }
        return new RedRaw(_data[o..end]);
    }

    private RedArray? TryReadArray(string inner, int o, int end)
    {
        if (end - o < 4) return null;
        var count = BitConverter.ToInt32(_data, o);
        o += 4;
        if (count < 0 || count > end - o) return null;
        var arr = new RedArray();
        if (inner == "String")
        {
            for (var i = 0; i < count; i++)
            {
                var (s, next) = ReadString(o);
                arr.Items.Add(new RedString(s, _data[o..next]));
                o = next;
            }
            return o == end ? arr : null;
        }
        if (Opaque.Contains(inner) || inner.Contains(':')) return null;
        for (var i = 0; i < count; i++)
        {
            if (o >= end || _data[o] != 0) return null;
            var (s, next) = ReadStruct(inner, o, end);
            arr.Items.Add(s);
            o = next;
        }
        return o == end ? arr : null;
    }

    private (string, int) ReadString(int o)
    {
        var b = _data[o++];
        var utf8 = (b & 0x80) != 0;
        var length = b & 0x3F;
        if ((b & 0x40) != 0)
        {
            var shift = 6;
            while (true)
            {
                b = _data[o++];
                length |= (b & 0x7F) << shift;
                shift += 7;
                if ((b & 0x80) == 0) break;
            }
        }
        var byteCount = utf8 ? length : length * 2;
        var s = utf8 ? Encoding.UTF8.GetString(_data, o, byteCount) : Encoding.Unicode.GetString(_data, o, byteCount);
        return (s, o + byteCount);
    }

    // -----------------------------------------------------------------------------------------
    // Reecriture
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// Reecrit le fichier : l'en-tete et toutes les tables sont recopies, les objets sont reserialises,
    /// puis offsets, tailles et sommes de controle (CRC-32) sont recalcules.
    /// </summary>
    public byte[] Write()
    {
        if (Tables[5].Count > 0 || Tables[6].Count > 0)
            throw new NotSupportedException(UiText.T("core.cr2w.embedded"));
        if (Exports.Count == 0) return (byte[])_data.Clone();

        var dataStart = (int)Exports[0].DataOffset;
        var output = new MemoryStream();
        output.Write(_data, 0, dataStart);

        var newExports = new List<Cr2wExport>();
        foreach (var (export, chunk) in Exports.Zip(Chunks))
        {
            var bytes = chunk.Serialize();
            newExports.Add(export with { DataOffset = (uint)output.Position, DataSize = (uint)bytes.Length });
            output.Write(bytes);
        }
        var result = output.ToArray();
        var objectsEnd = (uint)result.Length;

        // Table des objets + sa somme de controle
        var (eo, ecount, _) = Tables[4];
        for (var i = 0; i < newExports.Count; i++)
        {
            var o = (int)eo + i * 24;
            var e = newExports[i];
            BitConverter.GetBytes(e.ClassName).CopyTo(result, o);
            BitConverter.GetBytes(e.ObjectFlags).CopyTo(result, o + 2);
            BitConverter.GetBytes(e.ParentId).CopyTo(result, o + 4);
            BitConverter.GetBytes(e.DataSize).CopyTo(result, o + 8);
            BitConverter.GetBytes(e.DataOffset).CopyTo(result, o + 12);
            BitConverter.GetBytes(e.Template).CopyTo(result, o + 16);
            BitConverter.GetBytes(e.Crc32).CopyTo(result, o + 20);
        }
        var tables = ((uint Offset, uint Count, uint Crc)[])Tables.Clone();
        tables[4] = (eo, ecount, Crc32.Compute(result.AsSpan((int)eo, (int)ecount * 24)));

        // En-tete (magic 0, version 4, flags 8, horodatage 12, build 20, objectsEnd 24, buffersEnd 28, crc 32, chunks 36)
        BitConverter.GetBytes(objectsEnd).CopyTo(result, 24);
        BitConverter.GetBytes(objectsEnd).CopyTo(result, 28);
        for (var i = 0; i < 10; i++)
        {
            BitConverter.GetBytes(tables[i].Offset).CopyTo(result, 40 + i * 12);
            BitConverter.GetBytes(tables[i].Count).CopyTo(result, 44 + i * 12);
            BitConverter.GetBytes(tables[i].Crc).CopyTo(result, 48 + i * 12);
        }
        var crc = new MemoryStream();
        using (var w = new BinaryWriter(crc))
        {
            w.Write(Magic);
            w.Write(Version);
            w.Write(Flags);
            w.Write(TimeStamp);
            w.Write(BuildVersion);
            w.Write(objectsEnd);
            w.Write(objectsEnd);
            w.Write(0xDEADBEEFu);
            w.Write(NumChunks);
            foreach (var t in tables)
            {
                w.Write(t.Offset);
                w.Write(t.Count);
                w.Write(t.Crc);
            }
        }
        BitConverter.GetBytes(Crc32.Compute(crc.ToArray())).CopyTo(result, 32);
        return result;
    }
}
