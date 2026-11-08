using System.Text;

namespace CyberpunkModTranslator.Core;

/// <summary>Une ligne de la liste de mods de MO2 (panneau de gauche), dans l'ordre affiche par MO2.</summary>
public sealed class Mo2Entry
{
    public const string SeparatorSuffix = "_separator";

    /// <summary>Nom du dossier dans "mods" (avec "_separator" pour un separateur).</summary>
    public string Name { get; init; } = "";

    public bool Enabled { get; init; }
    public bool IsSeparator { get; init; }

    /// <summary>Absent du modlist.txt du profil (mod ajoute pendant que MO2 etait ferme) : MO2 le montrera desactive.</summary>
    public bool NotInProfile { get; init; }

    /// <summary>Couleur du separateur choisie dans MO2 (#rrggbb), ou null.</summary>
    public string? Color { get; init; }

    /// <summary>Nom affiche : sans le "_separator" final.</summary>
    public string DisplayName => IsSeparator ? Name[..^SeparatorSuffix.Length] : Name;
}

/// <summary>
/// Instance de Mod Organizer 2 retrouvee a partir de son dossier "mods" : dossier des profils, profil actif, liste des mods.
/// On ne fait que lire : rien n'est jamais ecrit dans les fichiers de MO2.
/// </summary>
public sealed class Mo2Instance
{
    public string ModsDir { get; }
    public string ProfilesDir { get; }

    /// <summary>ModOrganizer.ini de l'instance, s'il a ete trouve.</summary>
    public string? IniPath { get; }

    /// <summary>Profil selectionne dans MO2 (ModOrganizer.ini), sinon celui dont la liste a ete modifiee en dernier.</summary>
    public string? SelectedProfile { get; }

    /// <summary>Profils trouves (dossiers de "profiles" qui contiennent un modlist.txt), par ordre alphabetique.</summary>
    public List<string> Profiles { get; }

    private Mo2Instance(string modsDir, string profilesDir, string? iniPath, string? selected, List<string> profiles)
    {
        ModsDir = modsDir;
        ProfilesDir = profilesDir;
        IniPath = iniPath;
        SelectedProfile = selected;
        Profiles = profiles;
    }

    /// <summary>
    /// Retrouve l'instance. Le ModOrganizer.ini est cherche a cote du dossier "mods", dans les dossiers voisins
    /// (MO2 portable installe a part, avec un "base_directory" qui pointe ici) et dans %LocalAppData%\ModOrganizer.
    /// Sans ini, le dossier "profiles" voisin de "mods" suffit.
    /// </summary>
    public static Mo2Instance Find(string modsDir)
    {
        if (string.IsNullOrWhiteSpace(modsDir)) throw new ArgumentException(UiText.T("mo2.noModsDir"));
        // Sans separateur final : sinon le dossier parent serait "mods" lui-meme
        // Guillemets d'un chemin copie depuis l'Explorateur (« Copier en tant que chemin ») retires
        var mods = Path.TrimEndingDirectorySeparator(Path.GetFullPath(modsDir.Trim().Trim('"').Trim()));
        if (!Directory.Exists(mods)) throw new DirectoryNotFoundException(UiText.T("mo2.modsDirMissing", mods));

        string? iniPath = null, selected = null, profilesDir = null;
        foreach (var ini in IniCandidates(mods))
        {
            QtIni data;
            try
            {
                data = QtIni.Load(ini);
            }
            catch (Exception ex)
            {
                Log.Debug($"MO2 : {ini} illisible ({ex.Message})");
                continue;
            }
            var iniDir = Path.GetDirectoryName(ini)!;
            var baseDir = Resolve(data.Get("Settings", "base_directory"), iniDir, iniDir);
            var modDir = Resolve(data.Get("Settings", "mod_directory"), baseDir, Path.Combine(baseDir, "mods"));
            if (!SamePath(modDir, mods)) continue;
            iniPath = ini;
            profilesDir = Resolve(data.Get("Settings", "profiles_directory"), baseDir, Path.Combine(baseDir, "profiles"));
            selected = data.Get("General", "selected_profile");
            break;
        }

        profilesDir ??= Path.Combine(Path.GetDirectoryName(mods) ?? mods, "profiles");
        if (!Directory.Exists(profilesDir)) throw new DirectoryNotFoundException(UiText.T("mo2.profilesMissing", profilesDir));

        var profiles = new List<string>();
        try
        {
            profiles = Directory.EnumerateDirectories(profilesDir)
                .Where(d => File.Exists(Path.Combine(d, "modlist.txt")))
                .Select(d => Path.GetFileName(d))
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex)
        {
            Log.Warn($"MO2 : liste des profils illisible ({profilesDir})", ex);
        }
        if (profiles.Count == 0) throw new FileNotFoundException(UiText.T("mo2.noProfile", profilesDir));

        selected = profiles.FirstOrDefault(p => string.Equals(p, selected, StringComparison.OrdinalIgnoreCase));
        selected ??= profiles
            .OrderByDescending(p => File.GetLastWriteTimeUtc(Path.Combine(profilesDir, p, "modlist.txt")))
            .First();

        Log.Info($"MO2 : mods = {mods}, profils = {profilesDir} ({string.Join(", ", profiles)}), profil actif = {selected}" +
                 (iniPath != null ? $", ini = {iniPath}" : ", ini introuvable"));
        return new Mo2Instance(mods, profilesDir, iniPath, selected, profiles);
    }

    private static IEnumerable<string> IniCandidates(string mods)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        IEnumerable<string> Raw()
        {
            var parent = Path.GetDirectoryName(mods);
            if (parent == null) yield break;
            yield return Path.Combine(parent, "ModOrganizer.ini");

            // MO2 portable installe dans un dossier voisin (ex. "Mo2 - Main files" a cote de "Modpack Files")
            var grand = Path.GetDirectoryName(parent);
            if (grand != null)
            {
                IEnumerable<string> siblings;
                try
                {
                    siblings = Directory.EnumerateDirectories(grand).Take(500).ToList();
                }
                catch (Exception ex)
                {
                    Log.Debug($"MO2 : dossiers voisins illisibles ({grand} : {ex.Message})");
                    siblings = Array.Empty<string>();
                }
                foreach (var d in siblings) yield return Path.Combine(d, "ModOrganizer.ini");
            }

            // Instances globales
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrEmpty(local))
            {
                var root = Path.Combine(local, "ModOrganizer");
                IEnumerable<string> instances;
                try
                {
                    instances = Directory.Exists(root) ? Directory.EnumerateDirectories(root).Take(500).ToList() : Array.Empty<string>();
                }
                catch (Exception ex)
                {
                    Log.Debug($"MO2 : instances globales illisibles ({ex.Message})");
                    instances = Array.Empty<string>();
                }
                foreach (var d in instances) yield return Path.Combine(d, "ModOrganizer.ini");
            }
        }
        foreach (var p in Raw())
            if (seen.Add(p) && File.Exists(p))
                yield return p;
    }

    /// <summary>Chemin d'un reglage de MO2 : %BASE_DIR% remplace, relatif au dossier de base, valeur par defaut si vide.</summary>
    private static string Resolve(string? value, string baseDir, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return Path.GetFullPath(fallback);
        var v = value.Trim().Replace("%BASE_DIR%", baseDir, StringComparison.OrdinalIgnoreCase);
        try
        {
            return Path.GetFullPath(Path.IsPathRooted(v) ? v : Path.Combine(baseDir, v));
        }
        catch (Exception ex)
        {
            Log.Debug($"MO2 : chemin illisible \"{value}\" ({ex.Message})");
            return Path.GetFullPath(fallback);
        }
    }

    private static bool SamePath(string a, string b) =>
        string.Equals(Path.TrimEndingDirectorySeparator(a.Replace('/', Path.DirectorySeparatorChar)),
            Path.TrimEndingDirectorySeparator(b.Replace('/', Path.DirectorySeparatorChar)),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    /// <summary>
    /// Liste d'un profil dans l'ordre affiche par MO2 (de haut en bas). modlist.txt est ecrit a l'envers :
    /// la premiere ligne est le mod le plus prioritaire, tout en bas de la liste de MO2.
    /// Les lignes "*" (DLC, fichiers hors MO2) sont ignorees, comme les mods dont le dossier n'existe plus.
    /// </summary>
    public List<Mo2Entry> ReadModList(string profile)
    {
        var file = Path.Combine(ProfilesDir, profile, "modlist.txt");
        if (!File.Exists(file)) throw new FileNotFoundException(UiText.T("mo2.noModlist", file));

        var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var d in Directory.EnumerateDirectories(ModsDir)) folders.Add(Path.GetFileName(d));
        }
        catch (Exception ex)
        {
            Log.Warn($"MO2 : dossier des mods illisible ({ModsDir})", ex);
        }

        var lines = new List<(string Name, bool Enabled)>();
        foreach (var raw in SharedFile.ReadAllLines(file))
        {
            var line = raw.TrimEnd('\r', '\n');
            if (line.Length < 2 || line[0] == '#') continue;
            var prefix = line[0];
            if (prefix != '+' && prefix != '-') continue; // '*' : DLC ou fichiers geres hors MO2
            lines.Add((line[1..], prefix == '+'));
        }
        lines.Reverse();

        var result = new List<Mo2Entry>();
        var listed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, enabled) in lines)
        {
            if (!listed.Add(name)) continue;
            if (!folders.Contains(name))
            {
                Log.Debug($"MO2 : \"{name}\" est dans modlist.txt mais son dossier n'existe pas, ignore");
                continue;
            }
            result.Add(MakeEntry(name, enabled, false));
        }

        // Mods installes pendant que MO2 etait ferme : pas encore dans modlist.txt, MO2 les ajoutera desactives en bas
        foreach (var name in folders.Where(f => !listed.Contains(f)).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            result.Add(MakeEntry(name, false, true));

        Log.Info($"MO2 : profil {profile}, {result.Count(e => !e.IsSeparator)} mods et {result.Count(e => e.IsSeparator)} separateurs");
        return result;
    }

    private Mo2Entry MakeEntry(string name, bool enabled, bool notInProfile)
    {
        var isSeparator = name.EndsWith(Mo2Entry.SeparatorSuffix, StringComparison.Ordinal) && name.Length > Mo2Entry.SeparatorSuffix.Length;
        string? color = null;
        if (isSeparator)
        {
            var meta = Path.Combine(ModsDir, name, "meta.ini");
            try
            {
                if (File.Exists(meta)) color = ParseColor(QtIni.Load(meta).Get("General", "color", raw: true));
            }
            catch (Exception ex)
            {
                Log.Debug($"MO2 : couleur du separateur {name} illisible ({ex.Message})");
            }
        }
        return new Mo2Entry { Name = name, Enabled = enabled, IsSeparator = isSeparator, NotInProfile = notInProfile, Color = color };
    }

    /// <summary>
    /// Couleur d'un separateur : "@Variant(...)" (QColor serialise par Qt, flux Qt 4.0 :
    /// type 67 sur 4 octets, puis spec, alpha, rouge, vert, bleu sur 16 bits chacun), ou "#rrggbb".
    /// Une couleur "invalide" (spec 0, ce que MO2 ecrit quand aucune couleur n'est choisie) donne null.
    /// </summary>
    public static string? ParseColor(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var value = QtIni.Unescape(raw.Trim());
        if (value.StartsWith('#'))
        {
            var hex = value[1..];
            if (hex.Length == 8) hex = hex[2..]; // #aarrggbb
            return hex.Length == 6 && hex.All(Uri.IsHexDigit) ? "#" + hex.ToLowerInvariant() : null;
        }
        const string prefix = "@Variant(";
        if (!value.StartsWith(prefix, StringComparison.Ordinal) || !value.EndsWith(')')) return null;
        var bytes = value[prefix.Length..^1].Select(c => (byte)(c & 0xFF)).ToArray();
        if (bytes.Length < 15) return null;
        var type = (bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3];
        if (type != 67) return null;
        var spec = bytes[4];
        if (spec != 1) return null; // 0 = couleur invalide (aucune couleur), autres modeles : non geres
        var alpha = bytes[5];
        if (alpha == 0) return null;
        return $"#{bytes[7]:x2}{bytes[9]:x2}{bytes[11]:x2}";
    }
}

/// <summary>Lecture tolerante d'un fichier .ini ecrit par Qt (QSettings) : sections, cles, valeurs echappees.</summary>
public sealed class QtIni
{
    private readonly Dictionary<string, Dictionary<string, string>> _sections = new(StringComparer.OrdinalIgnoreCase);

    public static QtIni Load(string path)
    {
        var ini = new QtIni();
        var section = "General";
        foreach (var raw in SharedFile.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == ';' || line[0] == '#') continue;
            if (line[0] == '[' && line[^1] == ']')
            {
                section = line[1..^1].Trim();
                continue;
            }
            var eq = line.IndexOf('=');
            if (eq <= 0) continue;
            var key = line[..eq].Trim();
            if (!ini._sections.TryGetValue(section, out var values))
                ini._sections[section] = values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            values[key] = line[(eq + 1)..].Trim();
        }
        return ini;
    }

    /// <summary>Valeur d'une cle. raw : telle qu'ecrite dans le fichier (sinon dechiffree, "@ByteArray(...)" retire).</summary>
    public string? Get(string section, string key, bool raw = false)
    {
        if (!_sections.TryGetValue(section, out var values) || !values.TryGetValue(key, out var value)) return null;
        if (raw) return value;
        var text = Unescape(value);
        if (text.StartsWith("@ByteArray(", StringComparison.Ordinal) && text.EndsWith(')'))
        {
            // Octets en UTF-8 (noms de profil ou chemins avec accents)
            var bytes = text[11..^1].Select(c => (byte)(c & 0xFF)).ToArray();
            text = Encoding.UTF8.GetString(bytes);
        }
        return text;
    }

    /// <summary>
    /// Dechiffre une valeur Qt : guillemets retires, \\ \" \n \t..., \xHHHH (hexadecimal) et \0NNN (octal).
    /// </summary>
    public static string Unescape(string value)
    {
        var sb = new StringBuilder(value.Length);
        var i = 0;
        while (i < value.Length)
        {
            var c = value[i++];
            if (c == '"') continue;
            if (c != '\\' || i >= value.Length)
            {
                sb.Append(c);
                continue;
            }
            var e = value[i++];
            switch (e)
            {
                case 'a': sb.Append('\a'); break;
                case 'b': sb.Append('\b'); break;
                case 'f': sb.Append('\f'); break;
                case 'n': sb.Append('\n'); break;
                case 'r': sb.Append('\r'); break;
                case 't': sb.Append('\t'); break;
                case 'v': sb.Append('\v'); break;
                case 'x':
                {
                    var code = 0;
                    var digits = 0;
                    while (i < value.Length && Uri.IsHexDigit(value[i]))
                    {
                        code = (code << 4) | Convert.ToInt32(value[i].ToString(), 16);
                        code &= 0xFFFF;
                        i++;
                        digits++;
                    }
                    if (digits == 0) sb.Append('x');
                    else sb.Append((char)code);
                    break;
                }
                case >= '0' and <= '7':
                {
                    var code = e - '0';
                    while (i < value.Length && value[i] >= '0' && value[i] <= '7')
                    {
                        code = ((code << 3) | (value[i] - '0')) & 0xFFFF;
                        i++;
                    }
                    sb.Append((char)code);
                    break;
                }
                default:
                    sb.Append(e); // \\ \" \' \? et tout le reste
                    break;
            }
        }
        return sb.ToString();
    }
}
