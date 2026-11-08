using System.Text;

namespace CyberpunkModTranslator.Core;

/// <summary>Fichier TSV (colonnes separees par des tabulations), lisible dans Excel ou un editeur de texte.</summary>
public static class Tsv
{
    // Seuls les retours a la ligne et les tabulations sont ecrits sous forme \n et \t :
    // les chemins du jeu (base\localization\...) restent lisibles tels quels.
    public static string Escape(string? s) =>
        (s ?? "").Replace("\r\n", "\n").Replace("\t", "\\t").Replace("\r", "\\r").Replace("\n", "\\n");

    public static string Unescape(string s) =>
        s.Replace("\\n", "\n").Replace("\\t", "\t").Replace("\\r", "\r");

    public static void Write(string path, IEnumerable<string[]> rows)
    {
        using var w = new StreamWriter(path, false, new UTF8Encoding(true));
        foreach (var row in rows)
            w.WriteLine(string.Join('\t', row.Select(Escape)));
    }

    /// <summary>
    /// Lit les lignes brutes. Les colonnes de texte doivent etre passees a <see cref="Unescape"/> par l'appelant
    /// (pas les chemins : "fr-fr\nci_..." contient un "\n" qui n'est pas un retour a la ligne).
    /// Accepte les fichiers reenregistres par Excel : UTF-8, "Texte Unicode" (UTF-16) ou ANSI (Windows-1252),
    /// et les cellules entourees de guillemets.
    /// </summary>
    public static IEnumerable<string[]> Read(string path)
    {
        foreach (var raw in ReadText(path).Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            // ligne d'en-tete seulement : un nom d'archive peut lui-meme commencer par '#'
            if (line.Length == 0 || line.StartsWith("#source\t") || line.StartsWith("#archive\t")) continue;
            yield return line.Split('\t').Select(Unquote).ToArray();
        }
    }

    private static string ReadText(string path)
    {
        var bytes = SharedFile.ReadAllBytes(path);
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        try
        {
            return new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            Log.Info($"{path} : pas en UTF-8, lu en Windows-1252 (TSV reenregistre par Excel ?)");
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(1252).GetString(bytes);
        }
    }

    /// <summary>Excel entoure de guillemets les cellules qui en contiennent, en les doublant.</summary>
    private static string Unquote(string field)
    {
        if (field.Length >= 2 && field[0] == '"' && field[^1] == '"' && field.Contains("\"\""))
            return field[1..^1].Replace("\"\"", "\"");
        return field;
    }
}
