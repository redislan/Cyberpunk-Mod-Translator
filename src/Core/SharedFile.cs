using System.Text;

namespace CyberpunkModTranslator.Core;

/// <summary>
/// Lecture de fichiers sans gener les autres programmes : MO2 doit pouvoir remplacer son modlist.txt, renommer ou
/// supprimer un mod pendant que l'onglet Modlist l'analyse, et l'appli doit pouvoir remplacer sa sauvegarde TSV
/// pendant qu'elle est relue. Les fichiers sont donc ouverts en partageant lecture, ecriture et suppression,
/// et refermes tout de suite.
/// </summary>
public static class SharedFile
{
    public const FileShare Share = FileShare.ReadWrite | FileShare.Delete;

    public static FileStream OpenRead(string path) =>
        new(path, FileMode.Open, FileAccess.Read, Share);

    public static byte[] ReadAllBytes(string path)
    {
        using var fs = OpenRead(path);
        using var ms = new MemoryStream(fs.CanSeek && fs.Length < int.MaxValue ? (int)fs.Length : 0);
        fs.CopyTo(ms);
        return ms.ToArray();
    }

    /// <summary>Lignes d'un fichier texte (UTF-8 par defaut, BOM reconnu comme File.ReadAllLines).</summary>
    public static List<string> ReadAllLines(string path)
    {
        using var reader = new StreamReader(OpenRead(path), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var lines = new List<string>();
        while (reader.ReadLine() is { } line) lines.Add(line);
        return lines;
    }
}
