using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CyberpunkModTranslator.Core;

/// <summary>
/// Textes de l'interface et des messages, dans la langue choisie.
/// <para>
/// Langues disponibles : en.json et fr.json integres dans le programme, plus les fichiers
/// <c>lang\&lt;code&gt;.json</c> poses a cote de l'exe. Un fichier du dossier remplace la version integree
/// du meme code (on peut corriger fr.json sans recompiler). Pour ajouter une langue, on copie en.json,
/// on le renomme (de.json, pt-br.json...) et on traduit les valeurs.
/// </para>
/// <para>
/// Une cle absente de la langue choisie s'affiche en anglais ; absente de l'anglais, c'est la cle elle-meme
/// qui s'affiche (ca se voit, donc ca se corrige). Les {0}, {1}... sont remplaces par les valeurs.
/// Les autres accolades ({int_0}...) ne sont pas touchees.
/// </para>
/// </summary>
public static class UiText
{
    public const string Reference = "en";

    /// <summary>Une langue telle qu'affichee dans les Parametres.</summary>
    /// <param name="Source">"builtin" (integree au programme) ou "file" (dossier lang).</param>
    /// <param name="Percent">Part des textes anglais traduits dans ce fichier.</param>
    /// <param name="Error">null, "badname" (nom de fichier qui n'est pas un code de langue) ou le message d'erreur JSON.</param>
    public sealed record LanguageInfo(string Code, string Name, string Translator, string Source, string File, int Percent, string? Error);

    private sealed record Pack(string Code, string Name, string Translator, string Source, string File,
        Dictionary<string, string> Strings, string? Error);

    private static readonly JsonDocumentOptions ReadOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly Regex Placeholder = new(@"\{(\d+)\}", RegexOptions.Compiled);
    private static readonly Regex CodePattern = new(@"^[a-z]{2,3}(-[a-z0-9]{2,8})?$", RegexOptions.Compiled);
    private static readonly object Gate = new();

    private static Dictionary<string, string> _reference = new();
    private static Dictionary<string, string> _current = new();
    private static volatile bool _ready;

    /// <summary>Code de la langue utilisee (en, fr, de...).</summary>
    public static string Current { get; private set; } = Reference;

    /// <summary>Dossier des fichiers de langue, a cote de l'exe.</summary>
    public static string Folder => Path.Combine(AppContext.BaseDirectory, "lang");

    /// <summary>Texte traduit de la cle, avec les {0}, {1}... remplaces.</summary>
    public static string T(string key, params object?[] args)
    {
        EnsureReady();
        if (!_current.TryGetValue(key, out var text) && !_reference.TryGetValue(key, out text)) text = key;
        return args.Length == 0 ? text : Format(text, args);
    }

    public static string Format(string text, params object?[] args) =>
        Placeholder.Replace(text, m =>
            int.TryParse(m.Groups[1].Value, out var i) && i < args.Length
                ? Convert.ToString(args[i], CultureInfo.InvariantCulture) ?? ""
                : m.Value);

    /// <summary>
    /// Choisit la langue. "" ou "auto" : langue de Windows si elle est disponible, sinon l'anglais.
    /// Une langue demandee mais introuvable (fichier supprime...) se rabat aussi sur le choix automatique.
    /// Renvoie le code reellement utilise.
    /// </summary>
    public static string Use(string? wanted)
    {
        lock (Gate)
        {
            var packs = LoadAll().Where(p => p.Error == null).ToList();
            var reference = packs.FirstOrDefault(p => p.Code == Reference);
            var code = Resolve(wanted, packs.Select(p => p.Code).ToList());
            var chosen = packs.FirstOrDefault(p => p.Code == code) ?? reference;
            _reference = reference?.Strings ?? new Dictionary<string, string>();
            _current = chosen?.Strings ?? _reference;
            Current = chosen?.Code ?? Reference;
            _ready = true;
            return Current;
        }
    }

    /// <summary>Tous les textes de la langue utilisee, completes par l'anglais (pour la page).</summary>
    public static Dictionary<string, string> Strings()
    {
        EnsureReady();
        var all = new Dictionary<string, string>(_reference);
        foreach (var (k, v) in _current) all[k] = v;
        return all;
    }

    /// <summary>Langues trouvees (integrees et dossier lang), y compris les fichiers illisibles, pour les Parametres.</summary>
    public static List<LanguageInfo> Available()
    {
        var packs = LoadAll();
        var reference = packs.FirstOrDefault(p => p.Code == Reference && p.Error == null)?.Strings ?? new Dictionary<string, string>();
        return packs
            .Select(p => new LanguageInfo(p.Code, p.Name, p.Translator, p.Source, p.File, Percent(p, reference), p.Error))
            .OrderBy(l => l.Error != null)
            .ThenBy(l => l.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>Langue que choisirait le mode automatique (langue de Windows si disponible, sinon anglais).</summary>
    public static string AutoLanguage()
    {
        var codes = LoadAll().Where(p => p.Error == null).Select(p => p.Code).ToList();
        return Resolve(null, codes);
    }

    // -----------------------------------------------------------------------------------------

    private static void EnsureReady()
    {
        if (!_ready) Use(null);
    }

    private static string Resolve(string? wanted, List<string> available)
    {
        var w = (wanted ?? "").Trim().ToLowerInvariant();
        if (w.Length > 0 && w != "auto" && available.Contains(w)) return w;
        var ui = CultureInfo.CurrentUICulture;
        foreach (var c in new[] { ui.Name.ToLowerInvariant(), ui.TwoLetterISOLanguageName.ToLowerInvariant() })
            if (c.Length > 0 && available.Contains(c)) return c;
        return Reference;
    }

    private static int Percent(Pack p, Dictionary<string, string> reference)
    {
        if (p.Error != null || reference.Count == 0) return 0;
        var done = reference.Keys.Count(k => p.Strings.TryGetValue(k, out var v) && v.Length > 0);
        return (int)Math.Floor(done * 100.0 / reference.Count);
    }

    /// <summary>Langues integrees, puis fichiers du dossier lang (qui remplacent une langue integree du meme code).</summary>
    private static List<Pack> LoadAll()
    {
        var packs = new Dictionary<string, Pack>();
        var asm = typeof(UiText).Assembly;
        foreach (var res in asm.GetManifestResourceNames().Where(n => n.StartsWith("lang/") && n.EndsWith(".json")))
        {
            using var stream = asm.GetManifestResourceStream(res);
            if (stream == null) continue;
            using var reader = new StreamReader(stream);
            var name = Path.GetFileName(res);
            var pack = Parse(Path.GetFileNameWithoutExtension(res).ToLowerInvariant(), reader.ReadToEnd(), "builtin", name);
            if (pack.Error == null) packs[pack.Code] = pack;
        }

        try
        {
            if (Directory.Exists(Folder))
            {
                foreach (var file in Directory.EnumerateFiles(Folder, "*.json").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                {
                    var name = Path.GetFileName(file);
                    var code = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
                    Pack pack;
                    if (!CodePattern.IsMatch(code))
                    {
                        pack = new Pack(code, name, "", "file", name, new Dictionary<string, string>(), "badname");
                        packs["?" + name] = pack;
                        continue;
                    }
                    try
                    {
                        pack = Parse(code, File.ReadAllText(file), "file", name);
                    }
                    catch (Exception ex)
                    {
                        pack = new Pack(code, name, "", "file", name, new Dictionary<string, string>(), ex.Message);
                    }
                    // un fichier casse ne remplace pas la version integree qui marche
                    if (pack.Error != null) packs["?" + name] = pack;
                    else packs[code] = pack;
                }
            }
        }
        catch
        {
            // dossier illisible : on garde les langues integrees
        }
        return packs.Values.ToList();
    }

    private static Pack Parse(string code, string json, string source, string file)
    {
        try
        {
            using var doc = JsonDocument.Parse(json, ReadOptions);
            var root = doc.RootElement;
            var strings = new Dictionary<string, string>();
            if (root.TryGetProperty("strings", out var s) && s.ValueKind == JsonValueKind.Object)
            {
                foreach (var p in s.EnumerateObject())
                    if (p.Value.ValueKind == JsonValueKind.String) strings[p.Name] = p.Value.GetString() ?? "";
            }
            var name = root.TryGetProperty("language_name", out var n) && n.ValueKind == JsonValueKind.String && n.GetString()!.Trim().Length > 0
                ? n.GetString()!.Trim()
                : code;
            var translator = root.TryGetProperty("translator", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString()!.Trim() : "";
            return new Pack(code, name, translator, source, file, strings, strings.Count == 0 ? "\"strings\" ?" : null);
        }
        catch (JsonException ex)
        {
            return new Pack(code, file, "", source, file, new Dictionary<string, string>(), ex.Message);
        }
    }
}
