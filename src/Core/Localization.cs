namespace CyberpunkModTranslator.Core;

public enum LocKind
{
    OnScreens,
    Subtitles,
}

/// <summary>Une ligne de texte d'un fichier de localisation.</summary>
public sealed class LocEntry
{
    /// <summary>Cle utilisee pour faire correspondre les lignes : cle numerique, sinon cle texte.</summary>
    public string Key { get; init; } = "";
    public ulong PrimaryKey { get; init; }
    public string? SecondaryKey { get; init; }
    public RedString? Female { get; init; }
    public RedString? Male { get; init; }

    public string? Get(string field) => field == "M" ? Male?.Value : Female?.Value;

    public bool Set(string field, string value)
    {
        var target = field == "M" ? Male : Female;
        if (target == null) return false;
        target.Value = value;
        return true;
    }
}

/// <summary>
/// Fichier de textes du jeu (onscreens = interface, subtitles = sous-titres) lu depuis un CR2W.
/// </summary>
public sealed class LocalizationFile
{
    public const string OnScreensClass = "localizationPersistenceOnScreenEntries";
    public const string SubtitlesClass = "localizationPersistenceSubtitleEntries";

    public Cr2wFile File { get; }
    public LocKind Kind { get; }
    public List<LocEntry> Entries { get; } = new();
    public Dictionary<string, LocEntry> ByKey { get; } = new();

    private LocalizationFile(Cr2wFile file, LocKind kind)
    {
        File = file;
        Kind = kind;
    }

    public static LocalizationFile? TryLoad(byte[] data)
    {
        if (!Cr2wFile.LooksLikeCr2w(data)) return null;
        Cr2wFile cr2w;
        try
        {
            cr2w = Cr2wFile.Parse(data);
        }
        catch
        {
            return null;
        }

        foreach (var chunk in cr2w.Chunks)
        {
            LocKind kind;
            if (chunk.ClassName == OnScreensClass) kind = LocKind.OnScreens;
            else if (chunk.ClassName == SubtitlesClass) kind = LocKind.Subtitles;
            else continue;

            var loc = new LocalizationFile(cr2w, kind);
            if (chunk.Data?.Get("entries")?.Value is RedArray array)
            {
                foreach (var item in array.Items.OfType<RedStruct>())
                {
                    var pk = ReadU64(item, kind == LocKind.Subtitles ? "stringId" : "primaryKey");
                    var sk = (item.Get("secondaryKey")?.Value as RedString)?.Value;
                    var key = pk != 0 ? pk.ToString() : sk ?? "";
                    var entry = new LocEntry
                    {
                        Key = key,
                        PrimaryKey = pk,
                        SecondaryKey = sk,
                        Female = item.Get("femaleVariant")?.Value as RedString,
                        Male = item.Get("maleVariant")?.Value as RedString,
                    };
                    loc.Entries.Add(entry);
                    if (key.Length > 0) loc.ByKey.TryAdd(key, entry);
                }
            }
            return loc;
        }
        return null;
    }

    private static ulong ReadU64(RedStruct s, string name)
    {
        if (s.Get(name)?.Value is RedRaw raw && raw.Bytes.Length == 8) return BitConverter.ToUInt64(raw.Bytes, 0);
        if (s.Get(name)?.Value is RedStruct inner && inner.Properties.FirstOrDefault()?.Value is RedRaw r2 && r2.Bytes.Length == 8)
            return BitConverter.ToUInt64(r2.Bytes, 0);
        return 0;
    }

    public byte[] Save() => File.Write();

    /// <summary>
    /// Fichier "carte des sous-titres" (localizationPersistenceSubtitleMap) : renvoie les fichiers de sous-titres
    /// qu'il reference, ou null si ce n'est pas une carte.
    /// </summary>
    public static List<string>? TryReadSubtitleMap(byte[] data)
    {
        if (!Cr2wFile.LooksLikeCr2w(data)) return null;
        Cr2wFile cr2w;
        try
        {
            cr2w = Cr2wFile.Parse(data);
        }
        catch
        {
            return null;
        }
        var chunk = cr2w.Chunks.FirstOrDefault(c => c.ClassName == "localizationPersistenceSubtitleMap");
        if (chunk?.Data?.Get("entries")?.Value is not RedArray array) return null;
        var files = new List<string>();
        foreach (var item in array.Items.OfType<RedStruct>())
        {
            if (item.Get("subtitleFile")?.Value is RedRaw raw && raw.Bytes.Length == 2)
            {
                var index = BitConverter.ToUInt16(raw.Bytes, 0);
                if (index > 0 && index <= cr2w.Imports.Count) files.Add(cr2w.Imports[index - 1]);
            }
        }
        return files;
    }
}
