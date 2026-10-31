using System.Security.Cryptography;
using System.Text;

namespace CyberpunkModTranslator.Core;

/// <summary>Un fichier contenu dans une archive.</summary>
public sealed class ArchiveEntry
{
    public ulong Hash { get; init; }
    public long Timestamp { get; init; }
    public uint NumInlineBufferSegments { get; init; }
    public uint SegmentsStart { get; init; }
    public uint SegmentsEnd { get; init; }
    public uint DependenciesStart { get; init; }
    public uint DependenciesEnd { get; init; }
    public byte[] Sha1 { get; init; } = Array.Empty<byte>();

    /// <summary>Chemin dans le jeu, s'il est connu (liste LXRS ecrite par WolvenKit).</summary>
    public string? Path { get; set; }

    /// <summary>Taille decompressee du fichier principal (sans ses buffers).</summary>
    public uint Size { get; set; }

    public string Display => Path ?? Hash.ToString();
}

public readonly record struct ArchiveSegment(ulong Offset, uint ZSize, uint Size);

/// <summary>Lecteur d'archive .archive (format RDAR de Cyberpunk 2077).</summary>
public sealed class RedArchive : IDisposable
{
    public const uint Magic = 0x52414452; // "RDAR"
    public const uint LxrsMagic = 0x4C585253; // "LXRS"
    public const int ExtendedHeaderSize = 0xAC;
    private static readonly byte[] Kark = "KARK"u8.ToArray();

    private readonly FileStream _stream;
    private readonly ICompressor _compressor;

    public string FilePath { get; }
    public uint Version { get; private set; }
    public List<string> CustomPaths { get; } = new();
    public List<ArchiveEntry> Files { get; } = new();
    public Dictionary<ulong, ArchiveEntry> ByHash { get; } = new();
    public List<ArchiveSegment> Segments { get; } = new();
    public List<ulong> Dependencies { get; } = new();

    private RedArchive(string path, ICompressor compressor)
    {
        FilePath = path;
        _compressor = compressor;
        _stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
    }

    public static RedArchive Open(string path, ICompressor compressor)
    {
        var ar = new RedArchive(path, compressor);
        try
        {
            ar.ReadStructure();
        }
        catch
        {
            ar.Dispose();
            throw;
        }
        return ar;
    }

    private void ReadStructure()
    {
        var br = new BinaryReader(_stream, Encoding.UTF8, true);
        _stream.Position = 0;
        if (br.ReadUInt32() != Magic) throw new InvalidDataException(UiText.T("core.notArchive", FilePath));
        Version = br.ReadUInt32();
        var indexPosition = br.ReadUInt64();
        br.ReadUInt32(); // taille de l'index
        br.ReadUInt64(); // debug
        br.ReadUInt32();
        br.ReadUInt64(); // taille du fichier
        var customLength = br.ReadUInt32();

        if (customLength > 0)
            ReadLxrs(br);

        _stream.Position = (long)indexPosition;
        br.ReadUInt32(); // offset de la table
        br.ReadUInt32(); // taille de la table
        br.ReadUInt64(); // crc64
        var fileCount = br.ReadUInt32();
        var segmentCount = br.ReadUInt32();
        var dependencyCount = br.ReadUInt32();

        for (var i = 0; i < fileCount; i++)
        {
            var e = new ArchiveEntry
            {
                Hash = br.ReadUInt64(),
                Timestamp = br.ReadInt64(),
                NumInlineBufferSegments = br.ReadUInt32(),
                SegmentsStart = br.ReadUInt32(),
                SegmentsEnd = br.ReadUInt32(),
                DependenciesStart = br.ReadUInt32(),
                DependenciesEnd = br.ReadUInt32(),
                Sha1 = br.ReadBytes(20),
            };
            Files.Add(e);
            ByHash.TryAdd(e.Hash, e);
        }
        for (var i = 0; i < segmentCount; i++)
            Segments.Add(new ArchiveSegment(br.ReadUInt64(), br.ReadUInt32(), br.ReadUInt32()));
        for (var i = 0; i < dependencyCount; i++)
            Dependencies.Add(br.ReadUInt64());

        foreach (var e in Files)
            if (e.SegmentsStart < Segments.Count)
                e.Size = Segments[(int)e.SegmentsStart].Size;

        foreach (var p in CustomPaths)
            if (ByHash.TryGetValue(ResourcePath.Hash(p), out var e))
                e.Path = p;
    }

    private void ReadLxrs(BinaryReader br)
    {
        _stream.Position = ExtendedHeaderSize;
        if (br.ReadUInt32() != LxrsMagic) return;
        br.ReadUInt32(); // version
        var size = br.ReadInt32();
        var zsize = br.ReadInt32();
        var count = br.ReadInt32();
        var data = br.ReadBytes(zsize);
        byte[] buffer = data;
        if (size != zsize)
        {
            try
            {
                buffer = _compressor.Decompress(data, size);
            }
            catch
            {
                return; // liste de noms illisible : les fichiers resteront identifies par leur hash
            }
        }
        var latin1 = Encoding.Latin1;
        var start = 0;
        for (var i = 0; i < buffer.Length && CustomPaths.Count < count; i++)
        {
            if (buffer[i] != 0) continue;
            CustomPaths.Add(latin1.GetString(buffer, start, i - start));
            start = i + 1;
        }
    }

    public byte[] ReadSegment(int index)
    {
        var seg = Segments[index];
        var raw = new byte[seg.ZSize];
        lock (_stream)
        {
            _stream.Position = (long)seg.Offset;
            _stream.ReadExactly(raw);
        }
        if (seg.ZSize == seg.Size) return raw;
        if (raw.Length >= 8 && raw.AsSpan(0, 4).SequenceEqual(Kark))
        {
            var realSize = BitConverter.ToInt32(raw, 4);
            return _compressor.Decompress(raw[8..], realSize);
        }
        return raw;
    }

    /// <summary>Le fichier principal (pour un CR2W : le fichier sans ses buffers externes).</summary>
    public byte[] ReadMain(ArchiveEntry entry) => ReadSegment((int)entry.SegmentsStart);

    public static string ShortSha1(byte[] data) => Convert.ToHexString(SHA1.HashData(data))[..12].ToLowerInvariant();

    public void Dispose() => _stream.Dispose();
}
