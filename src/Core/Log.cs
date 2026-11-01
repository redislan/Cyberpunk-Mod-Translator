using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace CyberpunkModTranslator.Core;

public enum LogSeverity
{
    Debug,
    Info,
    Warning,
    Error,
}

/// <summary>
/// Journal de session : un fichier par lancement dans %AppData%\CyberpunkModTranslator\logs, ecrit ligne par
/// ligne (il reste lisible meme si le programme plante). Les derniers journaux sont gardes, les plus vieux effaces.
/// <para>
/// Chaque ligne part aussi dans Debug.WriteLine : en lancant depuis Visual Studio, elle s'affiche dans la fenetre Sortie.
/// Le niveau Debug n'est ecrit que dans une compilation Debug, avec --debug ou avec la variable CMT_DEBUG=1.
/// </para>
/// <para>
/// Tout le programme passe par cette classe : pour utiliser une autre bibliotheque (Serilog...), seul ce fichier change.
/// </para>
/// </summary>
public static class Log
{
    private const int KeepFiles = 10;
    private static readonly object Gate = new();
    private static readonly HashSet<string> Once = new();
    private static StreamWriter? _writer;

    /// <summary>Dossier des journaux par defaut (le meme pour l'appli et la ligne de commande).</summary>
    public static string DefaultFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.Create),
        "CyberpunkModTranslator", "logs");

    /// <summary>Le journal de la session en cours (null s'il n'a pas pu etre cree).</summary>
    public static string? FilePath { get; private set; }

    public static string Folder { get; private set; } = DefaultFolder;

    public static bool DebugEnabled { get; set; } =
#if DEBUG
        true;
#else
        Environment.GetEnvironmentVariable("CMT_DEBUG") == "1";
#endif

    /// <summary>Ouvre le journal de la session : en-tete avec la version, Windows, .NET et la langue du systeme.</summary>
    public static void Start(string program, string? folder = null)
    {
        lock (Gate)
        {
            Folder = string.IsNullOrWhiteSpace(folder) ? DefaultFolder : folder;
            try
            {
                Directory.CreateDirectory(Folder);
                CleanOldFiles(program);
                var stamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture);
                var path = Path.Combine(Folder, $"{program}_{stamp}.log");
                for (var n = 2; File.Exists(path); n++)
                    path = Path.Combine(Folder, $"{program}_{stamp}_{n}.log");
                var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                _writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
                FilePath = path;
            }
            catch (Exception ex)
            {
                // Pas de fichier possible (disque plein, droits...) : il reste la sortie de debug
                _writer = null;
                FilePath = null;
                System.Diagnostics.Debug.WriteLine($"[CMT] Journal impossible a creer dans {Folder} : {ex}");
            }
        }

        var version = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "?";
        Info($"{program} {version}");
        Info($"Windows : {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})");
        Info($".NET : {RuntimeInformation.FrameworkDescription}");
        Info($"Langue du systeme : {CultureInfo.CurrentUICulture.Name}");
    }

    public static void Debug(string message) => Write(LogSeverity.Debug, message, null);
    public static void Info(string message) => Write(LogSeverity.Info, message, null);
    public static void Warn(string message, Exception? ex = null) => Write(LogSeverity.Warning, message, ex);
    public static void Error(string message, Exception? ex = null) => Write(LogSeverity.Error, message, ex);

    /// <summary>Comme Warn, mais une seule fois par session pour un meme message (evite de remplir le journal).</summary>
    public static void WarnOnce(string message, Exception? ex = null)
    {
        lock (Gate)
        {
            if (!Once.Add(message + "|" + ex?.Message)) return;
        }
        Write(LogSeverity.Warning, message, ex);
    }

    public static void Write(LogSeverity level, string message, Exception? ex)
    {
        if (level == LogSeverity.Debug && !DebugEnabled) return;
        var tag = level switch
        {
            LogSeverity.Debug => "DEBUG",
            LogSeverity.Info => "INFO ",
            LogSeverity.Warning => "WARN ",
            _ => "ERROR",
        };
        var sb = new StringBuilder();
        sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture))
          .Append(" [").Append(tag).Append("] ").Append(message);
        if (ex != null)
        {
            // Detail complet (type, message, pile d'appels, exceptions internes), decale pour rester lisible
            foreach (var line in ex.ToString().Split('\n'))
                sb.Append("\n        ").Append(line.TrimEnd('\r'));
        }
        var text = sb.ToString();

        System.Diagnostics.Debug.WriteLine("[CMT] " + text);
        lock (Gate)
        {
            if (_writer == null) return;
            try
            {
                _writer.WriteLine(text);
            }
            catch (Exception writeError)
            {
                // Le journal lui-meme ne peut plus ecrire : on s'arrete d'y ecrire et on le signale en debug
                System.Diagnostics.Debug.WriteLine($"[CMT] Ecriture du journal impossible, journal coupe : {writeError}");
                _writer = null;
            }
        }
    }

    /// <summary>Derniere ligne et fermeture du fichier.</summary>
    public static void End()
    {
        Info("Fin de session");
        lock (Gate)
        {
            try
            {
                _writer?.Dispose();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CMT] Fermeture du journal impossible : {ex}");
            }
            _writer = null;
        }
    }

    /// <summary>Garde les derniers journaux de ce programme (KeepFiles - 1, plus celui qui s'ouvre).</summary>
    private static void CleanOldFiles(string program)
    {
        var old = new DirectoryInfo(Folder).GetFiles(program + "_*.log")
            .OrderByDescending(f => f.Name, StringComparer.Ordinal)
            .Skip(KeepFiles - 1);
        foreach (var f in old)
        {
            try
            {
                f.Delete();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CMT] Ancien journal non supprime ({f.Name}) : {ex.Message}");
            }
        }
    }
}
