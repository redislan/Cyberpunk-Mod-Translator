using System.Diagnostics;
using System.Text.Json;
using CyberpunkModTranslator.Core;

namespace CyberpunkModTranslator.App;

/// <summary>Dossiers de l'appli : %AppData%\CyberpunkModTranslator (reglages, projets, journal).</summary>
public static class AppPaths
{
    public static string Data { get; } = Path.Combine(BaseFolder(), "CyberpunkModTranslator");

    private static string BaseFolder()
    {
        foreach (var f in new[] { Environment.SpecialFolder.ApplicationData, Environment.SpecialFolder.LocalApplicationData })
        {
            var p = Environment.GetFolderPath(f, Environment.SpecialFolderOption.Create);
            if (!string.IsNullOrEmpty(p)) return p;
        }
        return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    public static string Projects => Path.Combine(Data, "projets");
    public static string SettingsFile => Path.Combine(Data, "reglages.json");
    public static string Logs => Path.Combine(Data, "logs");

    public static string Downloads => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
}

public sealed class Settings
{
    public string GameDir { get; set; } = "";
    public string Mo2ModsDir { get; set; } = "";
    public string OutputDir { get; set; } = Path.Combine(AppPaths.Downloads, "Traductions Cyberpunk");
    public string Lang { get; set; } = "fr-fr";
    /// <summary>Langue de l'interface : "" = automatique (langue de Windows), sinon en, fr, de...</summary>
    public string UiLang { get; set; } = "";
    public List<string> RecentMods { get; set; } = new();
}

public record PathRequest(string Path, string? Name = null);
public record TranslationUpdate(int Id, string? Fr);
public record LinkUpdate(int Id, bool Apply);
public record ClientLog(string? Level, string? Message);

/// <summary>Etat de l'appli : reglages, compresseur Oodle, projet de traduction ouvert.</summary>
public sealed class AppState : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly object _lock = new();
    private readonly string? _forcedOodle;
    private ICompressor? _compressor;
    private string? _compressorError;
    private TranslationProject? _project;
    private string? _projectPath;
    private int _restored;

    public Settings Settings { get; private set; }
    public DateTime LastPing { get; private set; } = DateTime.UtcNow;
    public bool EverPinged { get; private set; }
    public bool ByeReceived { get; private set; }
    public DateTime ByeAt { get; private set; }

    public AppState(string? forcedOodle, string? forcedGameDir)
    {
        _forcedOodle = forcedOodle;
        Directory.CreateDirectory(AppPaths.Projects);
        Settings = LoadSettings();
        if (!string.IsNullOrEmpty(forcedGameDir)) Settings.GameDir = forcedGameDir;
        if (string.IsNullOrEmpty(Settings.GameDir))
            Settings.GameDir = GameLocator.FindGameDirectories().FirstOrDefault() ?? "";
        UiText.Use(Settings.UiLang);
        Log.Info($"Reglages : jeu = {Show(Settings.GameDir)}, mods MO2 = {Show(Settings.Mo2ModsDir)}, sortie = {Show(Settings.OutputDir)}, " +
                 $"langue cible = {Settings.Lang}, interface = {UiText.Current} ({Show(Settings.UiLang, "auto")})");
    }

    private static string Show(string? value, string empty = "(vide)") => string.IsNullOrWhiteSpace(value) ? empty : value;

    // -----------------------------------------------------------------------------------------
    // Vie de l'appli
    // -----------------------------------------------------------------------------------------

    public void Ping()
    {
        LastPing = DateTime.UtcNow;
        EverPinged = true;
        ByeReceived = false;
    }

    public void Bye()
    {
        ByeReceived = true;
        ByeAt = DateTime.UtcNow;
    }

    private ICompressor GetCompressor()
    {
        if (_compressor != null) return _compressor;
        try
        {
            _compressor = Compressors.Load(_forcedOodle, Settings.GameDir);
            _compressorError = null;
            Log.Info("Compression : " + _compressor.Name);
            return _compressor;
        }
        catch (Exception ex)
        {
            // L'etat est redemande souvent : on ne journalise que quand l'erreur change
            if (_compressorError != ex.Message) Log.Warn("DLL de compression du jeu introuvable", ex);
            _compressorError = ex.Message;
            throw new InvalidOperationException(UiText.T("core.noDll"));
        }
    }

    // -----------------------------------------------------------------------------------------
    // Reglages
    // -----------------------------------------------------------------------------------------

    private static Settings LoadSettings()
    {
        try
        {
            if (File.Exists(AppPaths.SettingsFile))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(AppPaths.SettingsFile)) ?? new Settings();
        }
        catch (Exception ex)
        {
            // Reglages illisibles : on repart des valeurs par defaut, mais on garde une copie du fichier abime
            Log.Error($"Reglages illisibles ({AppPaths.SettingsFile}) : valeurs par defaut utilisees", ex);
            try
            {
                File.Copy(AppPaths.SettingsFile, Path.Combine(AppPaths.Data, "reglages.illisible.json"), true);
                Log.Info("Copie du fichier abime : reglages.illisible.json");
            }
            catch (Exception copyError)
            {
                Log.Warn("Copie des reglages abimes impossible", copyError);
            }
        }
        return new Settings();
    }

    private void SaveSettings()
    {
        Directory.CreateDirectory(AppPaths.Data);
        File.WriteAllText(AppPaths.SettingsFile, JsonSerializer.Serialize(Settings, JsonOptions));
    }

    public object UpdateSettings(Settings s)
    {
        lock (_lock)
        {
            var gameChanged = !string.Equals(s.GameDir?.Trim(), Settings.GameDir, StringComparison.OrdinalIgnoreCase);
            Settings.GameDir = s.GameDir?.Trim() ?? "";
            Settings.Mo2ModsDir = s.Mo2ModsDir?.Trim() ?? "";
            Settings.OutputDir = string.IsNullOrWhiteSpace(s.OutputDir) ? new Settings().OutputDir : s.OutputDir.Trim();
            Settings.Lang = string.IsNullOrWhiteSpace(s.Lang) ? "fr-fr" : s.Lang.Trim();
            Settings.UiLang = (s.UiLang ?? "").Trim().ToLowerInvariant();
            UiText.Use(Settings.UiLang);
            Log.Info($"Reglages modifies : jeu = {Show(Settings.GameDir)}, mods MO2 = {Show(Settings.Mo2ModsDir)}, sortie = {Show(Settings.OutputDir)}, " +
                     $"langue cible = {Settings.Lang}, interface = {UiText.Current} ({Show(Settings.UiLang, "auto")})");
            if (gameChanged && _forcedOodle == null)
            {
                _compressor?.Dispose();
                _compressor = null;
            }
            SaveSettings();
            return GetState();
        }
    }

    // -----------------------------------------------------------------------------------------
    // Etat general
    // -----------------------------------------------------------------------------------------

    public object GetState()
    {
        lock (_lock)
        {
            string compressor;
            try
            {
                compressor = GetCompressor().Name;
            }
            catch
            {
                // deja ecrit dans le journal par GetCompressor ; l'interface affiche le bandeau
                compressor = "";
            }
            return new
            {
                version = typeof(AppState).Assembly.GetName().Version?.ToString(3),
                compressor,
                compressorError = string.IsNullOrEmpty(compressor) ? _compressorError : null,
                settings = Settings,
                project = ProjectSummary(),
                dataDir = AppPaths.Data,
                nativeDialogs = NativeDialog.Supported,
                uiLang = UiText.Current,
            };
        }
    }

    private object? ProjectSummary()
    {
        if (_project == null) return null;
        var files = _project.Units
            .GroupBy(u => (u.SourceName, u.FilePath))
            .Select(g => new
            {
                src = g.Key.SourceName,
                file = g.Key.FilePath,
                kind = g.First().Loc != null ? (g.First().Loc!.Kind == LocKind.Subtitles ? "subtitles" : "texts") : "file",
                reason = g.First().Loc?.Reason.ToString() ?? UiText.T("reason.plainFile"),
                total = g.Count(),
            })
            .ToList();
        return new
        {
            name = _project.Mod.Name,
            path = _projectPath,
            lang = _project.TargetLang,
            suffix = TranslationProject.LangSuffix(_project.TargetLang),
            files,
            notes = _project.Notes.Select(n => n.ToString()).ToList(),
            archivesWithoutText = _project.ArchivesWithoutText,
            restored = _restored,
            saveFile = ProjectFile(),
        };
    }

    private string ProjectFile() =>
        Path.Combine(AppPaths.Projects, TranslationProject.SafeName(_project!.Mod.Name) + ".tsv");

    /// <summary>Langue de l'interface : textes pour la page et langues disponibles pour les Parametres.</summary>
    public object GetLanguage()
    {
        lock (_lock)
        {
            // le dossier lang est relu a chaque fois : un fichier ajoute apparait sans relancer le programme
            UiText.Use(Settings.UiLang);
            var auto = UiText.AutoLanguage();
            return new
            {
                code = UiText.Current,
                setting = Settings.UiLang,
                auto,
                strings = UiText.Strings(),
                languages = UiText.Available(),
                folder = UiText.Folder,
            };
        }
    }

    // -----------------------------------------------------------------------------------------
    // Projet
    // -----------------------------------------------------------------------------------------

    public object Open(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException(UiText.T("core.noModChosen"));
        lock (_lock)
        {
            var compressor = GetCompressor();
            var project = TranslationProject.Scan(path.Trim(), compressor, Settings.Lang);
            if (project.Units.Count == 0)
            {
                var notes = string.Join(" ", project.Notes);
                project.Dispose();
                throw new InvalidOperationException(UiText.T("core.noText", notes).Trim());
            }
            _project?.Dispose();
            _project = project;
            _projectPath = path.Trim();
            _restored = 0;

            // Reprise automatique du travail deja fait sur ce mod
            var saved = ProjectFile();
            if (File.Exists(saved))
            {
                _project.ImportTsv(saved);
                _restored = _project.Units.Count(u => u.HasChange);
            }

            Log.Info($"Mod ouvert : {_project.Mod.Name} ({_project.Units.Count} chaines, {_restored} traduction(s) reprises de la sauvegarde)");
            Settings.RecentMods.RemoveAll(p => string.Equals(p, _projectPath, StringComparison.OrdinalIgnoreCase));
            Settings.RecentMods.Insert(0, _projectPath);
            if (Settings.RecentMods.Count > 10) Settings.RecentMods.RemoveRange(10, Settings.RecentMods.Count - 10);
            SaveSettings();
            return GetState();
        }
    }

    public object Close()
    {
        lock (_lock)
        {
            if (_project != null) SaveProject();
            _project?.Dispose();
            _project = null;
            _projectPath = null;
            return GetState();
        }
    }

    private TranslationProject RequireProject() =>
        _project ?? throw new InvalidOperationException(UiText.T("core.noModOpen"));

    public object GetUnits()
    {
        lock (_lock)
        {
            var p = RequireProject();
            return p.Units.Select((u, i) => new
            {
                id = i,
                src = u.SourceName,
                file = u.FilePath,
                key = u.Key,
                field = u.Field,
                status = u.Status switch
                {
                    UnitStatus.AlreadyTranslated => "done",
                    UnitStatus.ToCheck => "check",
                    UnitStatus.Code => "code",
                    _ => "todo",
                },
                context = u.Context,
                en = u.Source,
                current = u.Current,
                fr = u.Translation ?? (u.Status == UnitStatus.AlreadyTranslated ? u.Current : ""),
                links = u.LinkedCode.Count == 0 ? null : u.LinkedCode.Select(TranslationProject.Where).ToList(),
                applyLinks = u.ApplyToLinkedCode,
                linkedTo = u.LinkedTexts.Count == 0 ? null : u.LinkedTexts.Select(t => p.Units.IndexOf(t) + 1).ToList(),
                same = u.LinkedCode.Count == 0 ? null : u.LinkedCode.SelectMany(c => c.LinkedTexts).Distinct()
                    .Where(t => t != u).Select(t => new { line = p.Units.IndexOf(t) + 1, where = TranslationProject.ShortWhere(t) }).ToList(),
            }).ToList();
        }
    }

    public object UpdateTranslations(TranslationUpdate[] items)
    {
        lock (_lock)
        {
            var p = RequireProject();
            foreach (var item in items)
            {
                if (item.Id < 0 || item.Id >= p.Units.Count) continue;
                var u = p.Units[item.Id];
                var text = item.Fr ?? "";
                u.Translation = text.Length == 0 ? null : text;
            }
            SaveProject();
            return new { saved = items.Length };
        }
    }

    public object UpdateLink(LinkUpdate item)
    {
        lock (_lock)
        {
            var p = RequireProject();
            if (item.Id < 0 || item.Id >= p.Units.Count) throw new ArgumentException(UiText.T("core.unknownLine"));
            p.Units[item.Id].ApplyToLinkedCode = item.Apply;
            SaveProject();
            return new { id = item.Id, apply = item.Apply };
        }
    }

    public object Save()
    {
        lock (_lock)
        {
            RequireProject();
            SaveProject();
            return new { file = ProjectFile() };
        }
    }

    /// <summary>Sauvegarde du travail en cours (TSV complet, y compris les chaines de code traduites).</summary>
    private void SaveProject()
    {
        if (_project == null) return;
        Directory.CreateDirectory(AppPaths.Projects);
        var tmp = ProjectFile() + ".tmp";
        _project.ExportTsv(tmp, includeCode: true);
        File.Move(tmp, ProjectFile(), true);
    }

    public object ImportTranslation(string path)
    {
        lock (_lock)
        {
            var p = RequireProject();
            var count = p.ImportFromTranslationMod(path.Trim());
            SaveProject();
            return new { count };
        }
    }

    public object ImportTsv(string path)
    {
        lock (_lock)
        {
            var p = RequireProject();
            var (applied, warnings) = p.ImportTsv(path.Trim());
            SaveProject();
            return new { count = applied, warnings = warnings.Take(50) };
        }
    }

    public object ExportTsv(string folder)
    {
        lock (_lock)
        {
            var p = RequireProject();
            var dir = string.IsNullOrWhiteSpace(folder) ? Settings.OutputDir : folder.Trim();
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, TranslationProject.SafeName(p.Mod.Name) + ".tsv");
            p.ExportTsv(file);
            Log.Info("TSV exporte : " + file);
            return new { file };
        }
    }

    public object Build(string folder, string? name)
    {
        lock (_lock)
        {
            var p = RequireProject();
            SaveProject();
            var dir = string.IsNullOrWhiteSpace(folder) ? Settings.OutputDir : folder.Trim();
            Directory.CreateDirectory(dir);
            var result = p.Build(dir, GetCompressor(), true, string.IsNullOrWhiteSpace(name) ? null : name.Trim());
            if (!string.Equals(dir, Settings.OutputDir, StringComparison.OrdinalIgnoreCase))
            {
                Settings.OutputDir = dir;
                SaveSettings();
            }
            return new
            {
                zip = result.Zip,
                archives = result.Archives,
                plainFiles = result.PlainFiles,
                warnings = result.Warnings,
                infos = result.Infos,
                changed = result.ChangedLines,
            };
        }
    }

    // -----------------------------------------------------------------------------------------
    // Explorateur de fichiers
    // -----------------------------------------------------------------------------------------

    /// <summary>Vraie fenetre « Ouvrir » de Windows. Le verrou n'est pas garde pendant qu'elle est ouverte.</summary>
    public object PickNative(DialogRequest request)
    {
        if (!NativeDialog.Supported) return new { unsupported = true };
        var places = new List<string>();
        lock (_lock)
        {
            places.Add(Settings.Mo2ModsDir);
            places.Add(Settings.GameDir);
            places.Add(Settings.OutputDir);
        }
        try
        {
            if (string.IsNullOrWhiteSpace(places[1])) places.AddRange(GameLocator.FindGameDirectories().Take(1));
        }
        catch (Exception ex)
        {
            // Simple raccourci en moins dans la fenetre Ouvrir
            Log.Warn("Dossier du jeu non ajoute aux raccourcis", ex);
        }
        try
        {
            var (busy, path) = NativeDialog.Pick(request, places);
            return new { busy, path, cancelled = !busy && path == null };
        }
        catch (Exception e)
        {
            Log.Error("Fenetre Ouvrir de Windows impossible : explorateur integre a la place", e);
            return new { unsupported = true, error = e.Message };
        }
    }

    public object Browse(string? path, string? ext)
    {
        var extensions = (ext ?? ".zip").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(e => e.StartsWith('.') ? e.ToLowerInvariant() : "." + e.ToLowerInvariant())
            .ToHashSet();

        var places = new List<object>();
        void Place(string name, string? p)
        {
            if (!string.IsNullOrWhiteSpace(p) && Directory.Exists(p)) places.Add(new { name, path = p });
        }
        Place(UiText.T("places.downloads"), AppPaths.Downloads);
        Place(UiText.T("places.desktop"), Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
        Place(UiText.T("places.documents"), Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        Place(UiText.T("places.mo2"), Settings.Mo2ModsDir);
        Place(UiText.T("places.output"), Settings.OutputDir);
        Place(UiText.T("places.game"), Settings.GameDir);

        var entries = new List<object>();
        string current;
        string? parent;

        if (string.IsNullOrWhiteSpace(path))
        {
            if (OperatingSystem.IsWindows())
            {
                foreach (var d in DriveInfo.GetDrives().Where(d => d.IsReady))
                {
                    var label = string.IsNullOrEmpty(d.VolumeLabel) ? d.Name : $"{d.Name} ({d.VolumeLabel})";
                    entries.Add(new { name = label, path = d.RootDirectory.FullName, type = "drive", isMod = false });
                }
                return new { path = "", parent = (string?)null, entries, places };
            }
            path = "/";
        }

        var dir = new DirectoryInfo(path.Trim());
        if (!dir.Exists) throw new DirectoryNotFoundException(UiText.T("core.folderNotFound", path));
        current = dir.FullName;
        parent = dir.Parent?.FullName ?? (OperatingSystem.IsWindows() ? "" : null);

        try
        {
            foreach (var d in dir.EnumerateDirectories().OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase))
            {
                if ((d.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
                entries.Add(new { name = d.Name, path = d.FullName, type = "dir", isMod = LooksLikeMod(d) });
            }
            foreach (var f in dir.EnumerateFiles().OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
            {
                if (!extensions.Contains(f.Extension.ToLowerInvariant())) continue;
                entries.Add(new { name = f.Name, path = f.FullName, type = "file", isMod = false });
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            // Dossier protege : on affiche ce qu'on a pu lire
            Log.Debug($"Explorateur integre : {current} en partie illisible ({ex.Message})");
        }
        return new { path = current, parent, entries, places };
    }

    private static bool LooksLikeMod(DirectoryInfo d)
    {
        try
        {
            return Directory.Exists(Path.Combine(d.FullName, "archive")) ||
                   Directory.Exists(Path.Combine(d.FullName, "r6")) ||
                   Directory.Exists(Path.Combine(d.FullName, "bin")) ||
                   Directory.Exists(Path.Combine(d.FullName, "red4ext"));
        }
        catch (Exception ex)
        {
            Log.Debug($"Explorateur integre : {d.FullName} illisible ({ex.Message})");
            return false;
        }
    }

    public void Reveal(string path)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(path)) return;
        if (File.Exists(path))
            Process.Start("explorer.exe", $"/select,\"{path}\"");
        else if (Directory.Exists(path))
            Process.Start("explorer.exe", $"\"{path}\"");
    }

    public void Dispose()
    {
        lock (_lock)
        {
            try
            {
                if (_project != null) SaveProject();
            }
            catch (Exception ex)
            {
                Log.Error("Derniere sauvegarde du travail impossible a la fermeture", ex);
            }
            _project?.Dispose();
            _compressor?.Dispose();
        }
    }
}
