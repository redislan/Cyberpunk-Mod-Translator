using System.Text;

namespace CyberpunkModTranslator.Core;

/// <summary>Nature probable d'une chaine trouvee dans un fichier en clair.</summary>
public enum StringKind
{
    /// <summary>Phrase ou texte affiche : a traduire.</summary>
    Text,

    /// <summary>Peut-etre du texte (un seul mot, chaine interpolee...) : a verifier par l'utilisateur.</summary>
    Maybe,

    /// <summary>Identifiant, chemin, cle, message de debug : a ne pas toucher.</summary>
    Code,
}

/// <summary>Une chaine litterale dans un fichier source, avec sa position exacte.</summary>
public sealed class StringLiteral
{
    public int Index { get; init; }
    public int Start { get; init; }
    public int Length { get; init; }
    public string Value { get; init; } = "";
    public int Line { get; init; }
    public string LineText { get; init; } = "";
    public StringKind Kind { get; set; }

    /// <summary>
    /// La chaine sert de valeur de comparaison dans le code (x == "...", Equals(x, "..."), case "...", cle de table).
    /// Si le meme texte est traduit ailleurs, il faut traduire ces comparaisons pareil, sinon le mod casse.
    /// </summary>
    public bool IsComparison { get; init; }

    /// <summary>Comment reecrire la chaine (guillemets, echappements) : propre a chaque langage.</summary>
    public Func<string, string> Encode { get; init; } = v => v;

    /// <summary>
    /// Valeur qu'on doit relire apres avoir ecrit un texte. C'est le texte lui-meme, sauf pour les chaines lues telles
    /// quelles (redscript s"...\(x)...") : un guillemet tape dans la traduction y est relu echappe (\").
    /// </summary>
    public Func<string, string> Normalize { get; init; } = v => v;
}

/// <summary>
/// Fichier texte d'un mod (.lua, .reds, .yaml, .json, .tweak) et ses chaines.
/// Le tri texte / code est dans <see cref="TextClassifier"/>, la lecture de chaque langage dans le dossier Scanners.
/// </summary>
public sealed class PlainTextFile
{
    public static readonly string[] SupportedExtensions = { ".lua", ".reds", ".yaml", ".yml", ".json", ".tweak" };

    public string RelativePath { get; }
    public string Content { get; }
    public Encoding Encoding { get; }
    public bool HasBom { get; }
    public List<StringLiteral> Literals { get; }

    private PlainTextFile(string relativePath, string content, Encoding encoding, bool bom, List<StringLiteral> literals)
    {
        RelativePath = relativePath;
        Content = content;
        Encoding = encoding;
        HasBom = bom;
        Literals = literals;
    }

    public static bool IsSupported(string relativePath)
    {
        var ext = Path.GetExtension(relativePath).ToLowerInvariant();
        if (!SupportedExtensions.Contains(ext)) return false;
        var norm = relativePath.Replace('\\', '/').ToLowerInvariant();
        // Dossiers d'outils de developpement (.vscode, .github, .git) : jamais des fichiers du mod
        if (norm.StartsWith('.') || norm.Contains("/.")) return false;
        if (ext == ".json" && (norm.StartsWith("archive/") || norm.Contains("/archive/"))) return false;
        return true;
    }

    public static PlainTextFile Load(string fullPath, string relativePath)
    {
        var bytes = SharedFile.ReadAllBytes(fullPath);
        var bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        var body = bom ? bytes[3..] : bytes;
        Encoding encoding;
        string content;
        try
        {
            encoding = new UTF8Encoding(false, true);
            content = encoding.GetString(body);
        }
        catch (DecoderFallbackException)
        {
            // Pas de l'UTF-8 valide : lu en Latin-1, et reecrit dans le meme encodage
            Log.Info($"{relativePath} : pas en UTF-8, lu en Latin-1");
            encoding = Encoding.Latin1;
            content = encoding.GetString(body);
        }
        return Parse(relativePath, content, encoding, bom);
    }

    /// <summary>Lecteur de chaque type de fichier (voir <see cref="SourceScanner"/>).</summary>
    private static SourceScanner? ScannerFor(string ext, string content) => ext switch
    {
        ".lua" => new LuaScanner(content),
        ".reds" or ".tweak" => new RedsScanner(content),
        ".yaml" or ".yml" => new YamlScanner(content),
        ".json" => new JsonScanner(content),
        _ => null,
    };

    public static PlainTextFile Parse(string relativePath, string content, Encoding? encoding = null, bool bom = false)
    {
        var ext = Path.GetExtension(relativePath).ToLowerInvariant();
        var literals = ScannerFor(ext, content)?.Scan() ?? new List<StringLiteral>();
        FileRules.Apply(relativePath, content, literals);
        return new PlainTextFile(relativePath, content, encoding ?? new UTF8Encoding(false), bom, literals);
    }

    /// <summary>Remplace les chaines indiquees (index -> nouveau texte). Le reste du fichier ne bouge pas d'un octet.</summary>
    public byte[] Apply(IReadOnlyDictionary<int, string> replacements) => Apply(replacements, out _);

    /// <param name="switchedToUtf8">
    /// Le fichier n'etait pas en UTF-8 et une traduction contient un caractere que son encodage ne sait pas ecrire
    /// (« œ », « ’ », « … ») : le fichier entier est alors ecrit en UTF-8 plutot que de remplacer ce caractere par « ? ».
    /// </param>
    public byte[] Apply(IReadOnlyDictionary<int, string> replacements, out bool switchedToUtf8)
    {
        var sb = new StringBuilder(Content.Length + 256);
        var pos = 0;
        foreach (var lit in Literals.OrderBy(l => l.Start))
        {
            if (!replacements.TryGetValue(lit.Index, out var value)) continue;
            sb.Append(Content, pos, lit.Start - pos);
            sb.Append(lit.Encode(value));
            pos = lit.Start + lit.Length;
        }
        sb.Append(Content, pos, Content.Length - pos);
        var text = sb.ToString();
        var encoding = Encoding;
        switchedToUtf8 = false;
        if (encoding is not UTF8Encoding && text.Any(c => c > 0xFF))
        {
            encoding = new UTF8Encoding(false);
            switchedToUtf8 = true;
        }
        var body = encoding.GetBytes(text);
        return HasBom ? Encoding.UTF8.GetPreamble().Concat(body).ToArray() : body;
    }
}

