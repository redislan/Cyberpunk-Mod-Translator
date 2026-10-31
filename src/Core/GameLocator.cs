using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace CyberpunkModTranslator.Core;

/// <summary>Cherche le dossier d'installation de Cyberpunk 2077 (Steam, GOG, Epic).</summary>
public static class GameLocator
{
    private const string SteamAppId = "1091500";
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
            catch
            {
                // chemin invalide : on ignore
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
                    foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                        libs.Add(m.Groups[1].Value.Replace(@"\\", @"\"));
                }
                foreach (var lib in libs)
                    Add(Path.Combine(lib, "steamapps", "common", "Cyberpunk 2077"));
            }
        }
        catch
        {
            // pas de Steam
        }

        // GOG
        try
        {
            Add(Registry.GetValue($@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\GOG.com\Games\{GogGameId}", "path", null) as string);
        }
        catch
        {
            // pas de GOG
        }

        // Epic : manifestes JSON du launcher
        try
        {
            var manifests = @"C:\ProgramData\Epic\EpicGamesLauncher\Data\Manifests";
            if (Directory.Exists(manifests))
            {
                foreach (var item in Directory.EnumerateFiles(manifests, "*.item"))
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(item));
                    if (doc.RootElement.TryGetProperty("DisplayName", out var name) &&
                        (name.GetString() ?? "").Contains("Cyberpunk", StringComparison.OrdinalIgnoreCase) &&
                        doc.RootElement.TryGetProperty("InstallLocation", out var loc))
                        Add(loc.GetString());
                }
            }
        }
        catch
        {
            // pas d'Epic
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
                    using var k = key.OpenSubKey(sub);
                    if ((k?.GetValue("DisplayName") as string ?? "").Contains("Cyberpunk 2077", StringComparison.OrdinalIgnoreCase))
                        Add(k!.GetValue("InstallLocation") as string);
                }
            }
        }
        catch
        {
            // registre inaccessible
        }

        _ = SteamAppId;
        return found;
    }
}
