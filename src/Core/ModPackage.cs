using System.IO.Compression;

namespace CyberpunkModTranslator.Core;

/// <summary>
/// Un mod a traduire : un dossier (par exemple le dossier du mod dans MO2) ou un .zip telecharge sur Nexus.
/// <para>
/// D'un .zip, seuls les fichiers que l'outil lit sont sortis dans le dossier temporaire (archives, .xl, fichiers en
/// clair) : un mod de plusieurs Go de textures ne remplit plus le disque pour quelques lignes de texte.
/// </para>
/// </summary>
public sealed class ModPackage : IDisposable
{
    /// <summary>Dossiers du jeu qu'on trouve a la racine d'un mod bien range.</summary>
    private static readonly string[] GameFolders = { "archive", "bin", "r6", "red4ext", "engine", "mods" };

    private readonly string? _tempDir;
    private List<string>? _files;
    private string? _contentPrefix;

    public string Root { get; }
    public string Name { get; }

    private ModPackage(string root, string name, string? tempDir)
    {
        Root = root;
        Name = name;
        _tempDir = tempDir;
    }

    private static string TempRoot => Path.Combine(Path.GetTempPath(), "CyberpunkModTranslator");

    public static ModPackage Open(string path)
    {
        if (Directory.Exists(path))
            return new ModPackage(Path.GetFullPath(path), new DirectoryInfo(path).Name, null);

        if (File.Exists(path) && path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            var temp = Path.Combine(TempRoot, Guid.NewGuid().ToString("N"));
            try
            {
                ExtractUseful(path, temp);
            }
            catch
            {
                // Zip abime ou disque plein : on ne laisse pas un dossier a moitie rempli derriere nous
                TryDelete(temp);
                throw;
            }
            return new ModPackage(temp, Path.GetFileNameWithoutExtension(path), temp);
        }

        if (File.Exists(path))
            throw new NotSupportedException(UiText.T("core.unsupportedFormat", Path.GetExtension(path)));
        throw new FileNotFoundException(UiText.T("core.notFound", path));
    }

    /// <summary>Fichiers qui servent a la traduction : archives, .xl d'ArchiveXL et fichiers en clair.</summary>
    public static bool IsUseful(string relativePath) =>
        relativePath.EndsWith(".archive", StringComparison.OrdinalIgnoreCase) ||
        relativePath.EndsWith(".xl", StringComparison.OrdinalIgnoreCase) ||
        PlainTextFile.IsSupported(relativePath);

    private static void ExtractUseful(string zipPath, string destination)
    {
        Directory.CreateDirectory(destination);
        var root = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        using var zip = ZipFile.OpenRead(zipPath);
        foreach (var entry in zip.Entries)
        {
            if (entry.Name.Length == 0) continue; // dossier
            var relative = entry.FullName.Replace('\\', '/').TrimStart('/');
            if (!IsUseful(relative)) continue;
            var target = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
            // Un nom piege ("..\..\fichier") ne doit pas ecrire hors du dossier temporaire
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                Log.Warn($"{zipPath} : entree ignoree, elle sort du dossier ({entry.FullName})");
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, true);
        }
    }

    /// <summary>
    /// Vide les dossiers temporaires laisses par un lancement qui s'est mal termine (plantage, PC eteint).
    /// Seuls ceux de plus d'un jour sont supprimes : une autre fenetre de l'outil peut etre en train d'en utiliser un.
    /// </summary>
    public static void CleanLeftovers()
    {
        try
        {
            if (!Directory.Exists(TempRoot)) return;
            foreach (var dir in Directory.EnumerateDirectories(TempRoot))
            {
                if (DateTime.UtcNow - Directory.GetLastWriteTimeUtc(dir) < TimeSpan.FromDays(1)) continue;
                TryDelete(dir);
            }
        }
        catch (Exception ex)
        {
            Log.Debug($"Nettoyage du dossier temporaire impossible ({ex.Message})");
        }
    }

    private static void TryDelete(string dir)
    {
        try
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
        catch (Exception ex)
        {
            // Pas bloquant : Windows videra le dossier temporaire plus tard
            Log.Warn($"Dossier temporaire non supprime : {dir}", ex);
        }
    }

    /// <summary>Tous les fichiers du mod (pour un .zip : ceux que l'outil lit), en chemins relatifs.</summary>
    public IReadOnlyList<string> Files => _files ??=
        Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(Root, f))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public IEnumerable<string> Archives =>
        Files.Where(f => f.EndsWith(".archive", StringComparison.OrdinalIgnoreCase));

    public string FullPath(string relative) => Path.Combine(Root, relative);

    /// <summary>
    /// Dossier englobant d'un .zip mal range ("MonMod v1.2/archive/...", "MonMod v1.2/bin/...") : "MonMod v1.2/",
    /// sinon "". La traduction fabriquee doit commencer aux dossiers du jeu, pas a ce dossier.
    /// </summary>
    public string ContentPrefix => _contentPrefix ??= FindContentPrefix();

    private string FindContentPrefix()
    {
        var useful = Files.Where(IsUseful).Select(f => f.Replace('\\', '/').Split('/')).ToList();
        if (useful.Count == 0) return "";
        bool IsGame(string s) => GameFolders.Contains(s, StringComparer.OrdinalIgnoreCase);
        // Deja bien range, ou fichiers a la racine : rien a retirer
        if (useful.Any(p => p.Length < 2 || IsGame(p[0]))) return "";
        var first = useful[0][0];
        if (useful.Any(p => !string.Equals(p[0], first, StringComparison.OrdinalIgnoreCase))) return ""; // plusieurs dossiers (options)
        return useful.Any(p => p.Length > 2 && IsGame(p[1])) ? first + "/" : "";
    }

    /// <summary>Chemin d'un fichier dans la traduction fabriquee : le meme, sans le dossier englobant.</summary>
    public string OutputPath(string relative)
    {
        var norm = relative.Replace('\\', '/');
        var prefix = ContentPrefix;
        if (prefix.Length > 0 && norm.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) norm = norm[prefix.Length..];
        return norm;
    }

    public void Dispose()
    {
        if (_tempDir != null) TryDelete(_tempDir);
    }
}
