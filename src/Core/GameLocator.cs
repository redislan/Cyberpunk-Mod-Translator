using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace CyberpunkModTranslator.Core;

/// <summary>Cherche le dossier d'installation de Cyberpunk 2077 (Steam, GOG, Epic).</summary>
public static class GameLocator
{
    private const string GogGameId = "1423049311";
    private const string ExeRelative = @"bin\x64\Cyberpunk2077.exe";

    public static IEnumerable<string> FindGameDirectories()
    {
        var found = new List<string>();
        if (!OperatingSystem.IsWindows()) return found;

        void Add(string? dir)
        {
            if (string.IsNullOrWhiteSpace(dir)) return;
            try
            {
                var full = Path.GetFullPath(dir.Trim());
                if (File.Exists(Path.Combine(full, ExeRelative)) &&
                    !found.Contains(full, StringComparer.OrdinalIgnoreCase))
                    found.Add(full);
            }
            catch (Exception ex)
            {
                Log.Debug($"Chemin du jeu ignore ({dir}) : {ex.Message}");
            }
        }

        // Steam : dossier Steam + toutes les bibliotheques declarees
        try
        {
            var steam = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string
                        ?? Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null) as string;
            if (steam != null)
            {
                var libs = new List<string> { steam };
                var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
                if (File.Exists(vdf))
                {
                    try
                    {
                        foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                            libs.Add(m.Groups[1].Value.Replace(@"\\", @"\"));
                    }
                    catch (Exception ex)
                    {
                        Log.Warn($"Bibliotheques Steam illisibles ({vdf})", ex);
                    }
                }
                foreach (var lib in libs)
                    Add(Path.Combine(lib, "steamapps", "common", "Cyberpunk 2077"));
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Recherche du jeu dans Steam impossible", ex);
        }

        // GOG
        try
        {
            Add(Registry.GetValue($@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\GOG.com\Games\{GogGameId}", "path", null) as string);
        }
        catch (Exception ex)
        {
            Log.Warn("Recherche du jeu dans GOG impossible", ex);
        }

        // Epic : manifestes JSON du launcher
        try
        {
            var manifests = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Epic", "EpicGamesLauncher", "Data", "Manifests");
            if (Directory.Exists(manifests))
            {
                foreach (var item in Directory.EnumerateFiles(manifests, "*.item"))
                {
                    // Un manifeste abime ne doit pas empecher de lire les autres
                    try
                    {
                        using var doc = JsonDocument.Parse(File.ReadAllText(item));
                        if (doc.RootElement.TryGetProperty("DisplayName", out var name) &&
                            (name.GetString() ?? "").Contains("Cyberpunk", StringComparison.OrdinalIgnoreCase) &&
                            doc.RootElement.TryGetProperty("InstallLocation", out var loc))
                            Add(loc.GetString());
                    }
                    catch (Exception ex)
                    {
                        Log.Warn($"Manifeste Epic illisible : {item}", ex);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Recherche du jeu dans Epic impossible", ex);
        }

        // Cles de desinstallation Windows
        try
        {
            foreach (var root in new[] { @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                                         @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall" })
            {
                using var key = Registry.LocalMachine.OpenSubKey(root);
                if (key == null) continue;
                foreach (var sub in key.GetSubKeyNames())
                {
                    // Une cle protegee ou abimee ne doit pas arreter la recherche dans les autres.
                    // Add() ne garde un dossier que s'il contient bin\x64\Cyberpunk2077.exe.
                    try
                    {
                        using var k = key.OpenSubKey(sub);
                        if ((k?.GetValue("DisplayName") as string ?? "").Contains("Cyberpunk 2077", StringComparison.OrdinalIgnoreCase))
                            Add(k!.GetValue("InstallLocation") as string);
                    }
                    catch (Exception ex)
                    {
                        Log.Debug($"Cle de desinstallation ignoree ({sub}) : {ex.Message}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Lecture des cles de desinstallation impossible", ex);
        }

        Log.Debug(found.Count == 0 ? "Dossier du jeu : aucun trouve automatiquement" : "Dossier du jeu trouve : " + string.Join(" ; ", found));
        return found;
    }
}
