using System.Text.RegularExpressions;
using CyberpunkModTranslator.Core;

namespace CyberpunkModTranslator.App;

/// <summary>
/// Themes de l'interface : fichiers .css du dossier themes, a cote de l'exe.
/// <para>
/// Un theme est charge APRES app.css : il redefinit les variables de couleur (--bg, --accent...) et peut
/// changer n'importe quelle autre regle. Le programme ne touche jamais a son contenu : il le sert tel quel.
/// Le dossier est relu a chaque demande, donc un theme ajoute ou modifie apparait sans recompiler ni relancer.
/// </para>
/// <para>
/// Infos facultatives dans le premier commentaire du fichier : <c>@name</c>, <c>@author</c>, <c>@description</c>,
/// et des variantes par langue (<c>@name.fr</c>, <c>@description.de</c>...). Sans <c>@name</c>, c'est le nom du fichier.
/// Le theme peut utiliser des images ou des polices rangees dans le dossier themes (chemins relatifs a son .css).
/// </para>
/// </summary>
public static class Themes
{
    /// <param name="Id">Nom du fichier sans .css : c'est ce qui est enregistre dans les reglages.</param>
    public sealed record ThemeInfo(string Id, string Name, string Author, string Description, string File);

    public static string Folder => Path.Combine(AppContext.BaseDirectory, "themes");

    /// <summary>Fichiers que le dossier peut servir a la page : le theme et ce qu'il utilise (images, polices).</summary>
    private static readonly Dictionary<string, string> Types = new(StringComparer.OrdinalIgnoreCase)
    {
        [".css"] = "text/css; charset=utf-8",
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".webp"] = "image/webp",
        [".gif"] = "image/gif",
        [".svg"] = "image/svg+xml",
        [".avif"] = "image/avif",
        [".woff"] = "font/woff",
        [".woff2"] = "font/woff2",
        [".ttf"] = "font/ttf",
        [".otf"] = "font/otf",
    };

    /// <summary>On ne lit que le debut du fichier pour trouver les infos (le commentaire d'en-tete).</summary>
    private const int HeaderBytes = 8192;

    // Espaces et tabulations seulement ([ \t], pas \s qui avale aussi les retours a la ligne) : un en-tete rempli
    // de lignes vides bloquait l'ouverture des Parametres pendant de longues secondes.
    private static readonly Regex MetaLine = new(@"^[ \t]*\*?[ \t]*@([a-z]+(?:\.[a-z]{2,3}(?:-[a-z0-9]{2,8})?)?)[ \t]*:[ \t]*([^\r\n]*?)[ \t]*\r?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Multiline);

    /// <summary>Themes trouves dans le dossier, tries par nom. Les infos sont prises dans la langue de l'interface si le theme la prevoit.</summary>
    public static List<ThemeInfo> Available(string uiLang)
    {
        var list = new List<ThemeInfo>();
        try
        {
            if (!Directory.Exists(Folder)) return list;
            foreach (var file in Directory.EnumerateFiles(Folder, "*.css"))
            {
                var meta = ReadHeader(file);
                string Pick(string key)
                {
                    var lang = (uiLang ?? "").ToLowerInvariant();
                    foreach (var k in new[] { $"{key}.{lang}", $"{key}.{lang.Split('-')[0]}", key })
                        if (meta.TryGetValue(k, out var v) && v.Length > 0) return v;
                    return "";
                }
                var id = Path.GetFileNameWithoutExtension(file);
                var name = Pick("name");
                list.Add(new ThemeInfo(id, name.Length > 0 ? name : id, Pick("author"), Pick("description"), Path.GetFileName(file)));
            }
        }
        catch (Exception ex)
        {
            // Dossier illisible : on garde le theme d'origine
            Log.WarnOnce($"Dossier des themes illisible : {Folder}", ex);
        }
        return list.OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>
    /// Fichier .css du theme (sans tenir compte des majuscules), ou null : theme vide, introuvable, ou dossier absent.
    /// Le nom est compare a ceux du dossier : un nom bizarre (..\, C:\...) ne peut pas sortir du dossier.
    /// </summary>
    public static string? Find(string? id)
    {
        id = (id ?? "").Trim();
        if (id.Length == 0) return null;
        try
        {
            if (!Directory.Exists(Folder)) return null;
            return Directory.EnumerateFiles(Folder, "*.css")
                .FirstOrDefault(f => string.Equals(Path.GetFileNameWithoutExtension(f), id, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            Log.WarnOnce($"Dossier des themes illisible : {Folder}", ex);
            return null;
        }
    }

    /// <summary>
    /// Fichier du dossier themes demande par la page (le .css, ou une image / police qu'il utilise), ou null.
    /// Refuse tout ce qui sort du dossier et les types de fichiers qui ne servent pas a un theme.
    /// </summary>
    public static string? Resolve(string? relative, out string contentType)
    {
        contentType = "";
        if (string.IsNullOrWhiteSpace(relative)) return null;
        try
        {
            var root = Path.GetFullPath(Folder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return null;
            if (!Types.TryGetValue(Path.GetExtension(full), out var type) || !File.Exists(full)) return null;
            contentType = type;
            return full;
        }
        catch
        {
            // chemin invalide (caracteres interdits...) : simplement introuvable
            return null;
        }
    }

    /// <summary>Lignes « @cle: valeur » du premier commentaire du fichier.</summary>
    private static Dictionary<string, string> ReadHeader(string file)
    {
        var meta = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            string head;
            using (var stream = File.OpenRead(file))
            using (var reader = new StreamReader(stream))
            {
                var buffer = new char[HeaderBytes];
                head = new string(buffer, 0, reader.ReadBlock(buffer, 0, buffer.Length));
            }
            var start = head.IndexOf("/*", StringComparison.Ordinal);
            // le commentaire doit ouvrir le fichier (espaces mis a part)
            if (start < 0 || head[..start].Trim().Length > 0) return meta;
            var end = head.IndexOf("*/", start + 2, StringComparison.Ordinal);
            var comment = end < 0 ? head[(start + 2)..] : head[(start + 2)..end];
            foreach (Match m in MetaLine.Matches(comment))
                meta.TryAdd(m.Groups[1].Value.ToLowerInvariant(), m.Groups[2].Value);
        }
        catch (Exception ex)
        {
            // Fichier verrouille ou illisible : le theme garde le nom de son fichier
            Log.WarnOnce($"Theme illisible : {file}", ex);
        }
        return meta;
    }
}
