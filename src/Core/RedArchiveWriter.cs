using System.Security.Cryptography;
using System.Text;

namespace CyberpunkModTranslator.Core;

/// <summary>
/// Ecrit une archive .archive avec la meme disposition que WolvenKit :
/// en-tete, liste des chemins (LXRS), fichiers tries par hash, index aligne sur 4096 octets.
/// Ne gere que des fichiers sans buffers externes (c'est le cas des fichiers de texte).
/// </summary>
public static class RedArchiveWriter
{
    private const int Page = 4096;

    public static void Write(string outputPath, IReadOnlyList<(string Path, byte[] Data)> files, ICompressor? compressor)
    {
        var entries = files
            .Select(f => (Hash: ResourcePath.Hash(f.Path), f.Path, f.Data))
            .OrderBy(f => f.Hash)
            .ToList();
        if (entries.Select(e => e.Hash).Distinct().Count() != entries.Count)
            throw new InvalidOperationException(UiText.T("core.duplicatePath"));

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms, Encoding.UTF8, true);

        // En-tete provisoire (40 octets) + longueur des donnees perso + bourrage jusqu'a 0xAC
        bw.Write(new byte[RedArchive.ExtendedHeaderSize]);

        // Liste des chemins (LXRS)
        var blob = new MemoryStream();
        foreach (var e in entries)
        {
            blob.Write(Encoding.Latin1.GetBytes(e.Path));
            blob.WriteByte(0);
        }
        var names = blob.ToArray();
        var packedNames = TryCompress(names, compressor, minimumSize: 0);
        bw.Write(RedArchive.LxrsMagic);
        bw.Write(1u);
        bw.Write(names.Length);
        bw.Write(packedNames.Length);
        bw.Write(entries.Count);
        bw.Write(packedNames);
        var customLength = (uint)(ms.Position - RedArchive.ExtendedHeaderSize);

        // Donnees des fichiers
        var segments = new List<ArchiveSegment>();
        var table = new List<(ulong Hash, int Segment, byte[] Sha1)>();
        foreach (var e in entries)
        {
            var offset = (ulong)ms.Position;
            byte[] stored;
            if (e.Data.Length < 256)
            {
                stored = e.Data;
            }
            else
            {
                var packed = TryCompress(e.Data, compressor, minimumSize: 256);
                if (ReferenceEquals(packed, e.Data))
                {
                    stored = e.Data;
                }
                else
                {
                    stored = new byte[packed.Length + 8];
                    "KARK"u8.CopyTo(stored);
                    BitConverter.GetBytes(e.Data.Length).CopyTo(stored, 4);
                    packed.CopyTo(stored, 8);
                }
            }
            bw.Write(stored);
            segments.Add(new ArchiveSegment(offset, (uint)stored.Length, (uint)e.Data.Length));
            table.Add((e.Hash, segments.Count - 1, SHA1.HashData(e.Data)));
        }

        PadToPage(bw);

        // Index
        var index = new MemoryStream();
        var iw = new BinaryWriter(index);
        iw.Write((uint)table.Count);
        iw.Write((uint)segments.Count);
        iw.Write(0u); // dependances
        var now = DateTime.Now.ToFileTime();
        foreach (var t in table)
        {
            iw.Write(t.Hash);
            iw.Write(now);
            iw.Write(0u); // segments de buffers
            iw.Write((uint)t.Segment);
            iw.Write((uint)t.Segment + 1);
            iw.Write(0u);
            iw.Write(0u);
            iw.Write(t.Sha1);
        }
        foreach (var s in segments)
        {
            iw.Write(s.Offset);
            iw.Write(s.ZSize);
            iw.Write(s.Size);
        }
        iw.Flush();
        var indexBytes = index.ToArray();

        var indexPosition = (ulong)ms.Position;
        bw.Write(8u);
        bw.Write((uint)indexBytes.Length + 8);
        bw.Write(Crc64.Compute(indexBytes));
        bw.Write(indexBytes);
        var indexSize = (uint)((ulong)ms.Position - indexPosition);
        PadToPage(bw);
        var fileSize = (ulong)ms.Position;

        // En-tete definitif
        ms.Position = 0;
        bw.Write(RedArchive.Magic);
        bw.Write(12u);
        bw.Write(indexPosition);
        bw.Write(indexSize);
        bw.Write(0UL);
        bw.Write(0u);
        bw.Write(fileSize);
        bw.Write(customLength);
        bw.Flush();

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        File.WriteAllBytes(outputPath, ms.ToArray());
    }

    /// <summary>Meme regle que WolvenKit : on ne garde la version compressee que si elle fait gagner plus de 8 octets.</summary>
    private static byte[] TryCompress(byte[] data, ICompressor? compressor, int minimumSize)
    {
        if (compressor == null || data.Length < minimumSize || data.Length == 0) return data;
        byte[]? packed;
        try
        {
            packed = compressor.Compress(data);
        }
        catch (Exception ex)
        {
            // Le fichier sera simplement stocke sans compression (le jeu le lit aussi)
            Log.Warn($"Compression impossible ({data.Length} octets) : donnees stockees sans compression", ex);
            packed = null;
        }
        if (packed == null || packed.Length == 0 || data.Length <= packed.Length + 8) return data;
        return packed;
    }

    private static void PadToPage(BinaryWriter bw)
    {
        var pad = (int)((Page - bw.BaseStream.Position % Page) % Page);
        if (pad > 0) bw.Write(new byte[pad]);
    }
}
