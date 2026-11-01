using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using CyberpunkModTranslator.Core;

namespace CyberpunkModTranslator.App;

/// <summary>
/// Demande de la page : mode "folder" (dossier) ou "file" (fichier), extensions acceptees (".zip", ".tsv"...).
/// Start : dossier ouvert a coup sur. Fallback : dossier ouvert la premiere fois seulement (ensuite Windows
/// rouvre le dernier dossier utilise pour ce Key).
/// </summary>
public record DialogRequest(string Mode, string? Title, string? Start, string? Ext, string? OkLabel, string? Key, string? Fallback = null);

/// <summary>
/// Vraie fenetre « Ouvrir » de Windows (celle de l'Explorateur), appelee directement par COM
/// (IFileOpenDialog), sans WinForms ni dependance en plus. Elle s'affiche par-dessus la fenetre
/// de l'appli et la bloque tant qu'elle est ouverte, comme dans n'importe quel logiciel.
/// </summary>
public static class NativeDialog
{
    public static bool Supported => OperatingSystem.IsWindows();

    private static readonly SemaphoreSlim OneAtATime = new(1, 1);

    /// <summary>Resultat : chemin choisi, ou null si l'utilisateur a annule. Leve une exception si la fenetre n'a pas pu s'ouvrir.</summary>
    public static (bool Busy, string? Path) Pick(DialogRequest request, IEnumerable<string> places)
    {
        if (!Supported) throw new PlatformNotSupportedException(UiText.T("dialog.unavailable"));
        if (!OneAtATime.Wait(0)) return (true, null);
        try
        {
            var owner = FindAppWindow();
            string? result = null;
            Exception? error = null;
            uint dialogThreadId = 0;
            var placeList = places.Where(p => !string.IsNullOrWhiteSpace(p) && Directory.Exists(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            var thread = new Thread(() =>
            {
                dialogThreadId = GetCurrentThreadId();
                try
                {
                    result = Show(request, owner, placeList);
                }
                catch (Exception e)
                {
                    error = e;
                }
            });
            thread.SetApartmentState(ApartmentState.STA); // obligatoire pour les fenetres du shell
            thread.IsBackground = true;
            thread.Start();

            // La fenetre appartient a notre programme, pas au navigateur : on l'amene au premier plan
            BringToFrontWhenShown(thread, () => dialogThreadId);

            thread.Join();
            if (error != null) throw error;
            return (false, result);
        }
        finally
        {
            OneAtATime.Release();
        }
    }

    private static string? Show(DialogRequest request, IntPtr owner, List<string> places)
    {
        var folderMode = request.Mode == "folder";
        var dialog = (IFileDialog)Activator.CreateInstance(Type.GetTypeFromCLSID(ClsidFileOpenDialog, true)!)!;
        try
        {
            dialog.GetOptions(out var options);
            options |= FOS_FORCEFILESYSTEM | FOS_NOCHANGEDIR | FOS_PATHMUSTEXIST;
            options |= folderMode ? FOS_PICKFOLDERS : FOS_FILEMUSTEXIST;
            dialog.SetOptions(options);

            // Windows retient le dernier dossier utilise pour chaque usage (mod, tsv, dossier de sortie...)
            var key = "CyberpunkModTranslator/" + (string.IsNullOrWhiteSpace(request.Key) ? request.Mode : request.Key);
            var guid = new Guid(MD5.HashData(Encoding.UTF8.GetBytes(key)));
            dialog.SetClientGuid(ref guid);

            if (!string.IsNullOrWhiteSpace(request.Title)) dialog.SetTitle(request.Title);
            if (!string.IsNullOrWhiteSpace(request.OkLabel)) dialog.SetOkButtonLabel(request.OkLabel);

            if (!folderMode)
            {
                var exts = (request.Ext ?? "").Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(e => e.Trim().TrimStart('*', '.').ToLowerInvariant()).Where(e => e.Length > 0).ToList();
                var filters = new List<COMDLG_FILTERSPEC>();
                if (exts.Count > 0)
                {
                    var spec = string.Join(";", exts.Select(e => "*." + e));
                    filters.Add(new COMDLG_FILTERSPEC { pszName = $"{string.Join(", ", exts.Select(e => "." + e))} ({spec})", pszSpec = spec });
                }
                filters.Add(new COMDLG_FILTERSPEC { pszName = UiText.T("dialog.allFiles"), pszSpec = "*.*" });
                dialog.SetFileTypes((uint)filters.Count, filters.ToArray());
                dialog.SetFileTypeIndex(1);
            }

            // Raccourcis dans le volet de gauche (dossier mods de MO2, jeu, sortie...)
            foreach (var place in places)
            {
                var item = TryItem(place);
                if (item == null) continue;
                try { dialog.AddPlace(item, FDAP_TOP); } catch (Exception ex) { Log.Debug($"Raccourci non ajoute a la fenetre Ouvrir ({place}) : {ex.Message}"); }
                finally { Marshal.ReleaseComObject(item); }
            }

            // Dossier de depart : celui demande s'il existe (le dossier parent pour un fichier)
            var start = request.Start?.Trim() ?? "";
            if (File.Exists(start)) start = Path.GetDirectoryName(start) ?? "";
            var startItem = Directory.Exists(start) ? TryItem(start) : null;
            if (startItem != null)
            {
                try { dialog.SetFolder(startItem); } catch (Exception ex) { Log.Debug($"Dossier de depart refuse ({start}) : {ex.Message}"); }
                finally { Marshal.ReleaseComObject(startItem); }
            }
            else
            {
                var fallback = request.Fallback?.Trim() ?? "";
                var first = TryItem(Directory.Exists(fallback) ? fallback : AppPaths.Downloads);
                if (first != null)
                {
                    try { dialog.SetDefaultFolder(first); } catch (Exception ex) { Log.Debug($"Dossier par defaut refuse : {ex.Message}"); }
                    finally { Marshal.ReleaseComObject(first); }
                }
            }

            var hr = dialog.Show(owner);
            if (hr == HRESULT_CANCELLED) return null;
            if (hr != 0) Marshal.ThrowExceptionForHR(hr);

            dialog.GetResult(out var chosen);
            try
            {
                chosen.GetDisplayName(SIGDN_FILESYSPATH, out var path);
                return path;
            }
            finally
            {
                Marshal.ReleaseComObject(chosen);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(dialog);
        }
    }

    private static IShellItem? TryItem(string path)
    {
        try
        {
            var iid = IidShellItem;
            SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out var item);
            return item;
        }
        catch (Exception ex)
        {
            Log.Debug($"Chemin refuse par le shell ({path}) : {ex.Message}");
            return null;
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Fenetre de l'appli (proprietaire de la boite de dialogue) et premier plan
    // ---------------------------------------------------------------------------------------------

    private const string AppTitle = "Cyberpunk Mod Translator";

    /// <summary>La fenetre Edge de l'appli : celle au premier plan (on vient de cliquer dedans), sinon on la cherche par son titre.</summary>
    private static IntPtr FindAppWindow()
    {
        var fg = GetForegroundWindow();
        if (fg != IntPtr.Zero && GetTitle(fg).Contains(AppTitle, StringComparison.OrdinalIgnoreCase)) return fg;
        var found = IntPtr.Zero;
        EnumWindows((hwnd, _) =>
        {
            if (IsWindowVisible(hwnd) && GetTitle(hwnd).Contains(AppTitle, StringComparison.OrdinalIgnoreCase))
            {
                found = hwnd;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    private static string GetTitle(IntPtr hwnd)
    {
        var sb = new StringBuilder(256);
        GetWindowText(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    /// <summary>
    /// Attend que la boite de dialogue apparaisse (jusqu'a 3 s) puis la passe au premier plan.
    /// Windows refuse normalement qu'un programme en arriere-plan prenne la main : on s'accroche
    /// un instant a la file d'entree de la fenetre active (celle de l'appli), le temps de basculer.
    /// </summary>
    private static void BringToFrontWhenShown(Thread dialogThread, Func<uint> dialogThreadId)
    {
        try
        {
            for (var i = 0; i < 60 && dialogThread.IsAlive; i++)
            {
                Thread.Sleep(50);
                var tid = dialogThreadId();
                if (tid == 0) continue;
                var dialog = IntPtr.Zero;
                EnumThreadWindows(tid, (hwnd, _) =>
                {
                    if (IsWindowVisible(hwnd))
                    {
                        dialog = hwnd;
                        return false;
                    }
                    return true;
                }, IntPtr.Zero);
                if (dialog == IntPtr.Zero) continue;

                var fg = GetForegroundWindow();
                if (fg == dialog) return;
                var fgThread = fg == IntPtr.Zero ? 0 : GetWindowThreadProcessId(fg, out _);
                var me = GetCurrentThreadId();
                var attached = fgThread != 0 && fgThread != me && AttachThreadInput(me, fgThread, true);
                try
                {
                    BringWindowToTop(dialog);
                    SetForegroundWindow(dialog);
                }
                finally
                {
                    if (attached) AttachThreadInput(me, fgThread, false);
                }
                return;
            }
        }
        catch (Exception ex)
        {
            // La fenetre reste ouverte, simplement sans le focus
            Log.Debug($"Fenetre Ouvrir non amenee au premier plan : {ex.Message}");
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Interop Windows (shobjidl_core.h, user32)
    // ---------------------------------------------------------------------------------------------

    private static readonly Guid ClsidFileOpenDialog = new("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7");
    private static readonly Guid IidShellItem = new("43826D1E-E718-42EE-BC55-A1E261C37BFE");

    private const uint FOS_NOCHANGEDIR = 0x8;
    private const uint FOS_PICKFOLDERS = 0x20;
    private const uint FOS_FORCEFILESYSTEM = 0x40;
    private const uint FOS_PATHMUSTEXIST = 0x800;
    private const uint FOS_FILEMUSTEXIST = 0x1000;
    private const int FDAP_TOP = 1;
    private const uint SIGDN_FILESYSPATH = 0x80058000;
    private const int HRESULT_CANCELLED = unchecked((int)0x800704C7);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct COMDLG_FILTERSPEC
    {
        [MarshalAs(UnmanagedType.LPWStr)] public string pszName;
        [MarshalAs(UnmanagedType.LPWStr)] public string pszSpec;
    }

    /// <summary>IFileDialog (herite de IModalWindow). L'ordre des methodes doit suivre exactement la table de Windows.</summary>
    [ComImport, Guid("42F85136-DB7E-439C-85F1-E4075D135FC8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileDialog
    {
        [PreserveSig] int Show(IntPtr hwndOwner);
        void SetFileTypes(uint cFileTypes, [MarshalAs(UnmanagedType.LPArray)] COMDLG_FILTERSPEC[] rgFilterSpec);
        void SetFileTypeIndex(uint iFileType);
        void GetFileTypeIndex(out uint piFileType);
        void Advise(IntPtr pfde, out uint pdwCookie);
        void Unadvise(uint dwCookie);
        void SetOptions(uint fos);
        void GetOptions(out uint pfos);
        void SetDefaultFolder(IShellItem psi);
        void SetFolder(IShellItem psi);
        void GetFolder(out IShellItem ppsi);
        void GetCurrentSelection(out IShellItem ppsi);
        void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string pszName);
        void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
        void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);
        void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
        void GetResult(out IShellItem ppsi);
        void AddPlace(IShellItem psi, int fdap);
        void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);
        void Close(int hr);
        void SetClientGuid(ref Guid guid);
        void ClearClientData();
        void SetFilter(IntPtr pFilter);
    }

    [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
        void GetParent(out IShellItem ppsi);
        void GetDisplayName(uint sigdnName, [MarshalAs(UnmanagedType.LPWStr)] out string ppszName);
        void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
        void Compare(IShellItem psi, uint hint, out int piOrder);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(
        [MarshalAs(UnmanagedType.LPWStr)] string pszPath, IntPtr pbc, ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItem ppv);

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool EnumThreadWindows(uint dwThreadId, EnumWindowsProc lpfn, IntPtr lParam);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
}
