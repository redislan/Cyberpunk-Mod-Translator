using System.IO.Compression;

namespace CyberpunkModTranslator.Core;

/// <summary>
/// Un mod a traduire : un dossier (par exemple le dossier du mod dans MO2) ou un .zip telecharge sur Nexus.
/// </summary>
public sealed class ModPackage : IDisposable
{
    private readonly string? _tempDir;

    public string Root { get; }
    public string Name { get; }

    private ModPackage(string root, string name, string? tempDir)
    {
        Root = root;
        Name = name;
        _tempDir = tempDir;
    }

    public static ModPackage Open(string path)
    {
        if (Directory.Exists(path))
            return new ModPackage(Path.GetFullPath(path), new DirectoryInfo(path).Name, null);

        if (File.Exists(path) && path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            var temp = Path.Combine(Path.GetTempPath(), "CyberpunkModTranslator", Guid.NewGuid().ToString("N"));
            ZipFile.ExtractToDirectory(path, temp);
            return new ModPackage(temp, Path.GetFileNameWithoutExtension(path), temp);
        }

        if (File.Exists(path))
            throw new NotSupportedException(UiText.T("core.unsupportedFormat", Path.GetExtension(path)));
        throw new FileNotFoundException(UiText.T("core.notFound", path));
    }

    /// <summary>Tous les fichiers du mod, en chemins relatifs.</summary>
    public IEnumerable<string> Files =>
        Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(Root, f))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);

    public IEnumerable<string> Archives =>
        Files.Where(f => f.EndsWith(".archive", StringComparison.OrdinalIgnoreCase));

    public string FullPath(string relative) => Path.Combine(Root, relative);

    public void Dispose()
    {
        if (_tempDir == null) return;
        try
        {
            Directory.Delete(_tempDir, true);
        }
        catch (Exception ex)
        {
            // Pas bloquant : Windows videra le dossier temporaire plus tard
            Log.Warn($"Dossier temporaire non supprime : {_tempDir}", ex);
        }
    }
}
