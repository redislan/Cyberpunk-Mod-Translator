using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CyberpunkModTranslator.Core;

namespace CyberpunkModTranslator.App;

/// <summary>Choix de l'onglet Modlist, gardes dans reglages.json.</summary>
public sealed class ModlistPrefs
{
    /// <summary>Profil MO2 choisi ("" = le profil actif de MO2).</summary>
    public string Profile { get; set; } = "";

    /// <summary>Separateurs caches (nom du dossier, avec "_separator").</summary>
    public List<string> Hidden { get; set; } = new();

    /// <summary>Separateurs replies.</summary>
    public List<string> Collapsed { get; set; } = new();

    public bool HideDisabled { get; set; }

    /// <summary>Statuts decoches dans les filtres (todo, translated, none, trad, error, pending).</summary>
    public List<string> HiddenStatuses { get; set; } = new();
}

public record ModlistRequest(string? Profile, bool Force = false);

/// <summary>Ce dont l'analyse a besoin, copie au moment du lancement (les reglages peuvent changer pendant ce temps).</summary>
public sealed record ModlistContext(string ModsDir, string Lang, string OutputDir, string GameDir, string? Profile);

/// <summary>
/// Onglet Modlist : la liste des mods de MO2 (meme ordre, memes separateurs) avec, pour chaque mod, s'il est traduit
/// (un mod « Nom - FR » est dans MO2), s'il a du texte a traduire, ou rien du tout.
/// <para>
/// Chaque mod est analyse comme a son ouverture, en arriere-plan. Le resultat est garde sur le disque
/// (modlist-cache.json) avec une empreinte des fichiers qui comptent (archives, .xl, fichiers en clair, sauvegarde de la
/// trad) : un mod qui n'a pas change n'est jamais reanalyse.
/// </para>
/// </summary>
public sealed class ModlistService : IDisposable
{
    /// <summary>A augmenter si la facon de compter change : tout le cache est alors refait.</summary>
    private const int CacheFormat = 1;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };
    private static readonly string AppVersion =
        typeof(ModlistService).Assembly.GetName().Version?.ToString() ?? "0";

    private readonly object _lock = new();
    private readonly string? _forcedOodle;
    private readonly Dictionary<string, CachedScan> _cache;
    private bool _cacheDirty;

    private int _generation;
    private CancellationTokenSource? _cts;
    private Task _task = Task.CompletedTask;

    // Etat affiche (protege par _lock)
    private List<Row> _rows = new();
    private Mo2Instance? _instance;
    private string? _profile;
    private string? _error;
    private string _suffix = "FR";
    private bool _noDll;
    private bool _scanning;
    private int _done, _total;
    private string? _current;
    private int _revision;
    private bool _loaded;

    private sealed class Row
    {
        public string Name = "";
        public string Display = "";
        public bool Enabled;
        public bool IsSeparator;
        public bool NotInProfile;
        public string? Color;
        public int Group = -1;
        /// <summary>"sep", "mod" ou "trad" (un mod « Nom - FR »).</summary>
        public string Kind = "mod";
        public string? Original;
        public bool OriginalFound;
        public string? Translation;
        public bool TranslationEnabled;
        public string? Built;
        public ScanResult? Result;
    }

    public sealed class ScanResult
    {
        public int Todo { get; set; }
        public int Check { get; set; }
        public int Done { get; set; }
        /// <summary>Lignes a traduire deja traduites dans la sauvegarde de l'appli.</summary>
        public int Saved { get; set; }
        public string? Error { get; set; }
    }

    private sealed class CachedScan
    {
        public string Signature { get; set; } = "";
        public ScanResult Result { get; set; } = new();
    }

    private sealed class CacheFile
    {
        public int Format { get; set; }
        public Dictionary<string, CachedScan> Mods { get; set; } = new();
    }

    private static string CachePath => Path.Combine(AppPaths.Data, "modlist-cache.json");

    public ModlistService(string? forcedOodle)
    {
        _forcedOodle = forcedOodle;
        _cache = LoadCache();
    }

    // -----------------------------------------------------------------------------------------
    // Cache
    // -----------------------------------------------------------------------------------------

    private static StringComparer PathComparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static Dictionary<string, CachedScan> LoadCache()
    {
        var empty = new Dictionary<string, CachedScan>(PathComparer);
        try
        {
            if (!File.Exists(CachePath)) return empty;
            var file = JsonSerializer.Deserialize<CacheFile>(File.ReadAllText(CachePath));
            if (file == null || file.Format != CacheFormat) return empty;
            // Pas de verification des dossiers ici (un lecteur reseau absent ralentirait le demarrage) :
            // les mods desinstalles sont retires a la lecture de la liste de MO2.
            var cache = new Dictionary<string, CachedScan>(PathComparer);
            foreach (var (path, scan) in file.Mods)
                if (scan?.Result != null)
                    cache[path] = scan;
            Log.Info($"Modlist : {cache.Count} analyse(s) en cache");
            return cache;
        }
        catch (Exception ex)
        {
            // Le cache n'est qu'un gain de temps : on repart de zero
            Log.Warn($"Modlist : cache illisible ({CachePath}), tout sera reanalyse", ex);
            return empty;
        }
    }

    private void SaveCache()
    {
        string json;
        lock (_lock)
        {
            if (!_cacheDirty) return;
            json = JsonSerializer.Serialize(new CacheFile { Format = CacheFormat, Mods = new Dictionary<string, CachedScan>(_cache) }, JsonOptions);
            _cacheDirty = false;
        }
        try
        {
            Directory.CreateDirectory(AppPaths.Data);
            var tmp = CachePath + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, CachePath, true);
        }
        catch (Exception ex)
        {
            Log.Warn("Modlist : cache non enregistre", ex);
        }
    }

    // -----------------------------------------------------------------------------------------
    // Lecture de la liste de MO2
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// Relit la liste de MO2 et lance l'analyse des mods qui ont change. force : oublie le cache de ces mods.
    /// Une analyse en cours est abandonnee (ses resultats deja obtenus restent dans le cache).
    /// </summary>
    public object Load(ModlistContext ctx, bool force)
    {
        lock (_lock)
        {
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            var generation = ++_generation;
            _loaded = true;
            _error = null;
            _noDll = false;
            _rows = new List<Row>();
            _instance = null;
            _profile = null;
            _scanning = false;
            _done = _total = 0;
            _current = null;
            _suffix = TranslationProject.LangSuffix(ctx.Lang);
            _revision++;

            try
            {
                _instance = Mo2Instance.Find(ctx.ModsDir);
                _profile = _instance.Profiles.FirstOrDefault(p => string.Equals(p, ctx.Profile, StringComparison.OrdinalIgnoreCase))
                           ?? _instance.SelectedProfile;
                _rows = BuildRows(_instance.ReadModList(_profile!), ctx);
            }
            catch (Exception ex)
            {
                _error = ex.Message;
                Log.Warn("Modlist : liste de MO2 illisible", ex);
                return SnapshotLocked();
            }

            // Resultats gardes pour des mods qui ne sont plus dans le dossier "mods" : oublies
            var present = new HashSet<string>(_rows.Where(r => r.Kind != "sep").Select(ModPath), PathComparer);
            var prefix = _instance.ModsDir + Path.DirectorySeparatorChar;
            foreach (var gone in _cache.Keys.Where(k => k.StartsWith(prefix, PathComparer == StringComparer.Ordinal
                         ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase) && !present.Contains(k)).ToList())
            {
                _cache.Remove(gone);
                _cacheDirty = true;
            }

            var mods = _rows.Where(r => r.Kind == "mod").ToList();
            foreach (var r in mods)
            {
                if (force)
                {
                    if (_cache.Remove(ModPath(r))) _cacheDirty = true;
                }
                else if (_cache.TryGetValue(ModPath(r), out var known))
                {
                    // Dernier resultat connu affiche tout de suite (pas de liste « en cours d'analyse » a chaque
                    // passage sur l'onglet) ; l'analyse en arriere-plan le verifie et le remplace s'il a change.
                    r.Result = known.Result;
                }
            }

            _scanning = mods.Count > 0;
            _total = mods.Count;
            var token = _cts.Token;
            var previous = _task;
            _task = Task.Run(async () =>
            {
                // L'analyse precedente doit d'abord s'arreter (elle verifie l'annulation entre deux mods)
                try
                {
                    await previous;
                }
                catch (Exception ex)
                {
                    Log.Debug($"Modlist : fin de l'analyse precedente ({ex.Message})");
                }
                ScanAll(generation, mods, ctx, token);
            });
            return SnapshotLocked();
        }
    }

    private string ModPath(Row r) => Path.Combine(_instance!.ModsDir, r.Name);

    private List<Row> BuildRows(List<Mo2Entry> entries, ModlistContext ctx)
    {
        var suffix = " - " + TranslationProject.LangSuffix(ctx.Lang);
        var byName = new Dictionary<string, Mo2Entry>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in entries.Where(e => !e.IsSeparator)) byName.TryAdd(e.Name, e);

        var rows = new List<Row>();
        var group = -1;
        foreach (var e in entries)
        {
            var row = new Row
            {
                Name = e.Name,
                Display = e.DisplayName,
                Enabled = e.Enabled,
                IsSeparator = e.IsSeparator,
                NotInProfile = e.NotInProfile,
                Color = e.Color,
            };
            if (e.IsSeparator)
            {
                row.Kind = "sep";
                group = rows.Count;
                rows.Add(row);
                continue;
            }
            row.Group = group;
            if (e.Name.Length > suffix.Length && e.Name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                // Mod de traduction (fabrique par l'outil ou telecharge) : rien a analyser
                row.Kind = "trad";
                row.Original = e.Name[..^suffix.Length].TrimEnd();
                row.OriginalFound = byName.ContainsKey(row.Original);
            }
            else
            {
                if (byName.TryGetValue(e.Name + suffix, out var translation))
                {
                    row.Translation = translation.Name;
                    row.TranslationEnabled = translation.Enabled;
                }
                else
                {
                    // Trad deja fabriquee mais pas encore installee dans MO2
                    var built = Path.Combine(ctx.OutputDir, TranslationProject.SafeName(e.Name + suffix));
                    if (File.Exists(built + ".zip")) row.Built = built + ".zip";
                    else if (Directory.Exists(built)) row.Built = built;
                }
            }
            rows.Add(row);
        }
        return rows;
    }

    // -----------------------------------------------------------------------------------------
    // Analyse en arriere-plan
    // -----------------------------------------------------------------------------------------

    private void ScanAll(int generation, List<Row> mods, ModlistContext ctx, CancellationToken token)
    {
        var clock = Stopwatch.StartNew();
        int fromCache = 0, scanned = 0, failed = 0;
        ICompressor? compressor = null;
        try
        {
            lock (_lock)
                if (generation != _generation || token.IsCancellationRequested)
                    return;
            try
            {
                compressor = Compressors.Load(_forcedOodle, ctx.GameDir);
            }
            catch (Exception ex)
            {
                // Sans la DLL, on ne peut pas lire les archives : on montre quand meme la liste et les trads installees
                Log.Warn("Modlist : DLL de compression du jeu introuvable, les mods non analyses restent sans statut", ex);
                lock (_lock)
                    if (generation == _generation)
                        _noDll = true;
            }

            string modsDir;
            lock (_lock)
            {
                if (generation != _generation) return;
                modsDir = _instance!.ModsDir;
            }

            foreach (var row in mods)
            {
                if (token.IsCancellationRequested) break;
                var path = Path.Combine(modsDir, row.Name);
                lock (_lock)
                {
                    if (generation != _generation) break;
                    _current = row.Display;
                    _revision++;
                }

                ScanResult result;
                string? signature = null;
                var cacheIt = true;
                var saveSoon = false;
                try
                {
                    signature = Signature(path, row.Name, ctx.Lang, out var relevant, out var archives);
                    CachedScan? hit;
                    lock (_lock) _cache.TryGetValue(path, out hit);
                    if (hit != null && hit.Signature == signature)
                    {
                        result = hit.Result;
                        cacheIt = false;
                        fromCache++;
                    }
                    else if (relevant == 0)
                    {
                        result = new ScanResult();
                    }
                    else if (compressor == null && archives > 0)
                    {
                        // Archives illisibles sans la DLL : pas de resultat (ni de cache) tant qu'elle manque
                        result = new ScanResult { Error = UiText.T("core.noDll") };
                        cacheIt = false;
                    }
                    else
                    {
                        // Sans archive, la DLL ne sert pas : les fichiers en clair s'analysent quand meme
                        result = ScanMod(path, compressor ?? MissingCompressor.Instance, ctx.Lang);
                        scanned++;
                        saveSoon = scanned % 10 == 0;
                    }
                }
                catch (Exception ex)
                {
                    // Mod illisible (archive abimee, droits...) : signale, l'analyse continue avec les autres.
                    // Mis en cache comme le reste : il ne sera reessaye que si ses fichiers changent (ou « Tout reanalyser »).
                    Log.Warn($"Modlist : analyse de {row.Name} impossible", ex);
                    // Chemins raccourcis dans le message affiche : relatifs au dossier du mod
                    result = new ScanResult { Error = ex.Message.Replace(path + Path.DirectorySeparatorChar, "") };
                    failed++;
                    // Fichier verrouille (MO2 qui installe, antivirus, WolvenKit...) : on reessaiera a la prochaine actualisation
                    if (IsTransient(ex)) cacheIt = false;
                }
                if (cacheIt && signature != null)
                {
                    lock (_lock)
                    {
                        // Une analyse abandonnee (Tout reanalyser, autre profil) ne remet rien dans le cache
                        if (generation != _generation) break;
                        _cache[path] = new CachedScan { Signature = signature, Result = result };
                        _cacheDirty = true;
                    }
                }

                lock (_lock)
                {
                    if (generation != _generation) break;
                    row.Result = result;
                    _done++;
                    _revision++;
                }
                if (saveSoon) SaveCache();
            }
        }
        catch (Exception ex)
        {
            Log.Error("Modlist : analyse interrompue", ex);
        }
        finally
        {
            compressor?.Dispose();
            lock (_lock)
            {
                if (generation == _generation)
                {
                    _scanning = false;
                    _current = null;
                    _revision++;
                }
            }
            SaveCache();
            Log.Info($"Modlist : {mods.Count} mods, {fromCache} deja en cache, {scanned} analyse(s), {failed} erreur(s), " +
                     $"{clock.Elapsed.TotalSeconds:0.0} s" + (token.IsCancellationRequested ? " (interrompue)" : ""));
        }
    }

    /// <summary>
    /// Empreinte de ce qui compte pour l'analyse : archives, .xl, fichiers en clair lus par l'outil, sauvegarde de la trad,
    /// langue cible et version du programme. relevant : nombre de fichiers que l'analyse lirait.
    /// </summary>
    private static string Signature(string modDir, string name, string lang, out int relevant, out int archives)
    {
        archives = 0;
        var sb = new StringBuilder();
        sb.Append(CacheFormat).Append('|').Append(AppVersion).Append('|').Append(lang).Append('\n');
        relevant = 0;
        var files = Directory.EnumerateFiles(modDir, "*", SearchOption.AllDirectories)
            .Select(f => (Full: f, Rel: Path.GetRelativePath(modDir, f)))
            .Where(f => f.Rel.EndsWith(".archive", StringComparison.OrdinalIgnoreCase) ||
                        f.Rel.EndsWith(".xl", StringComparison.OrdinalIgnoreCase) ||
                        PlainTextFile.IsSupported(f.Rel))
            .OrderBy(f => f.Rel, StringComparer.Ordinal);
        foreach (var (full, rel) in files)
        {
            var info = new FileInfo(full);
            sb.Append(rel).Append('|').Append(info.Length).Append('|').Append(info.LastWriteTimeUtc.Ticks).Append('\n');
            relevant++;
            if (rel.EndsWith(".archive", StringComparison.OrdinalIgnoreCase)) archives++;
        }
        var save = SaveFile(name);
        if (File.Exists(save))
        {
            var info = new FileInfo(save);
            sb.Append("save|").Append(info.Length).Append('|').Append(info.LastWriteTimeUtc.Ticks).Append('\n');
        }
        return Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }

    /// <summary>Erreur qui ne dit rien du mod lui-meme : fichier verrouille ou acces refuse pour l'instant.</summary>
    private static bool IsTransient(Exception ex)
    {
        for (var e = ex; e != null; e = e.InnerException)
        {
            if (e is UnauthorizedAccessException) return true;
            // 0x20 : ERROR_SHARING_VIOLATION, 0x21 : ERROR_LOCK_VIOLATION
            if (e is IOException && (e.HResult & 0xFFFF) is 0x20 or 0x21) return true;
        }
        return false;
    }

    /// <summary>Sauvegarde automatique de la trad d'un mod (meme nom que dans AppState).</summary>
    private static string SaveFile(string modName) =>
        Path.Combine(AppPaths.Projects, TranslationProject.SafeName(modName) + ".tsv");

    private static ScanResult ScanMod(string path, ICompressor compressor, string lang)
    {
        using var project = TranslationProject.Scan(path, compressor, lang, quiet: true);
        var u = project.Units;
        var result = new ScanResult
        {
            Todo = u.Count(x => x.Status == UnitStatus.ToTranslate),
            Check = u.Count(x => x.Status == UnitStatus.ToCheck),
            Done = u.Count(x => x.Status == UnitStatus.AlreadyTranslated),
        };
        // Archive illisible : le mod reste signale en erreur (ses autres fichiers ont quand meme ete comptes)
        if (project.FailedArchives.Count > 0)
            result.Error = string.Join(" ; ", project.Notes.Select(n => n.ToString()).Take(3)).Replace(path + Path.DirectorySeparatorChar, "");
        var save = SaveFile(project.Mod.Name);
        if (File.Exists(save))
        {
            try
            {
                result.Saved = project.CountTranslatedIn(save);
            }
            catch (Exception ex)
            {
                // Simple indication en moins dans la liste
                Log.Warn($"Modlist : sauvegarde {save} illisible", ex);
            }
        }
        return result;
    }

    // -----------------------------------------------------------------------------------------
    // Ce que voit la page
    // -----------------------------------------------------------------------------------------

    /// <summary>Etat actuel. La premiere fois, lit la liste de MO2.</summary>
    public object Get(Func<ModlistContext> context)
    {
        lock (_lock)
        {
            if (_loaded) return SnapshotLocked();
        }
        return Load(context(), false);
    }

    private object SnapshotLocked()
    {
        return new
        {
            revision = _revision,
            error = _error,
            noDll = _noDll,
            modsDir = _instance?.ModsDir,
            profilesDir = _instance?.ProfilesDir,
            profile = _profile,
            activeProfile = _instance?.SelectedProfile,
            profiles = _instance?.Profiles ?? new List<string>(),
            suffix = _suffix,
            scanning = new { active = _scanning, done = _done, total = _total, current = _current },
            rows = _rows.Select(r => new
            {
                name = r.Name,
                display = r.Display,
                kind = r.Kind,
                enabled = r.Enabled,
                notInProfile = r.NotInProfile,
                color = r.Color,
                group = r.Group,
                status = Status(r),
                path = r.Kind == "sep" || _instance == null ? null : Path.Combine(_instance.ModsDir, r.Name),
                original = r.Original,
                originalFound = r.OriginalFound,
                translation = r.Translation,
                translationEnabled = r.TranslationEnabled,
                built = r.Built,
                todo = r.Result?.Todo ?? 0,
                check = r.Result?.Check ?? 0,
                done = r.Result?.Done ?? 0,
                saved = r.Result?.Saved ?? 0,
                error = r.Result?.Error,
            }).ToList(),
        };
    }

    /// <summary>
    /// Vert (translated) : un mod « Nom - FR » est dans MO2. Orange (todo) : des lignes a traduire ou a verifier.
    /// Sans couleur (none) : rien a traduire.
    /// </summary>
    private static string Status(Row r)
    {
        if (r.Kind != "mod") return r.Kind;
        if (r.Translation != null) return "translated";
        if (r.Result == null) return "pending";
        if (r.Result.Error != null) return "error";
        return r.Result.Todo + r.Result.Check > 0 ? "todo" : "none";
    }

    /// <summary>Remplace la DLL du jeu pour un mod sans archive : n'est jamais appele.</summary>
    private sealed class MissingCompressor : ICompressor
    {
        public static readonly MissingCompressor Instance = new();
        public string Name => "-";
        public byte[] Decompress(byte[] source, int rawSize) => throw new InvalidOperationException(UiText.T("core.noDll"));
        public byte[]? Compress(byte[] raw) => throw new InvalidOperationException(UiText.T("core.noDll"));
        public void Dispose() { }
    }

    /// <summary>
    /// Reglages changes (dossier MO2, langue cible, dossier de sortie, dossier du jeu) : l'analyse en cours s'arrete et la
    /// liste sera relue au prochain affichage de l'onglet.
    /// </summary>
    public void Invalidate()
    {
        lock (_lock)
        {
            _cts?.Cancel();
            _generation++;
            _loaded = false;
            _scanning = false;
            _current = null;
            _revision++;
        }
    }

    public void Dispose()
    {
        Task task;
        lock (_lock)
        {
            _cts?.Cancel();
            _generation++;
            task = _task;
        }
        try
        {
            // Le mod en cours d'analyse se termine (quelques secondes au plus)
            task.Wait(TimeSpan.FromSeconds(5));
        }
        catch (Exception ex)
        {
            Log.Debug($"Modlist : arret de l'analyse ({ex.Message})");
        }
        SaveCache();
    }
}
