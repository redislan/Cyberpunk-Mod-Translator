using System.Runtime.InteropServices;

namespace CyberpunkModTranslator.Core;

/// <summary>Compression Kraken (Oodle) utilisee par les archives du jeu.</summary>
public interface ICompressor : IDisposable
{
    string Name { get; }

    /// <summary>Decompresse un flux Kraken brut (sans l'en-tete KARK).</summary>
    byte[] Decompress(byte[] source, int rawSize);

    /// <summary>Compresse en flux Kraken brut. Renvoie null en cas d'echec.</summary>
    byte[]? Compress(byte[] raw);
}

/// <summary>
/// La DLL Oodle livree avec le jeu (bin\x64\oo2ext_7_win64.dll). C'est elle qu'on utilise sous Windows :
/// aucune dependance a installer, et c'est exactement le compresseur du jeu.
/// </summary>
public sealed class OodleDll : ICompressor
{
    public const string DllName = "oo2ext_7_win64.dll";
    private const int CompressorKraken = 8;
    private const int LevelNormal = 4;

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate long DecompressFn(byte[] comp, long compLen, byte[] raw, long rawLen,
        int fuzzSafe, int checkCrc, int verbosity, IntPtr decBufBase, long decBufSize,
        IntPtr callback, IntPtr callbackData, IntPtr decoderMemory, long decoderMemorySize, int threadPhase);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate long CompressFn(int compressor, byte[] raw, long rawLen, byte[] comp, int level,
        IntPtr options, IntPtr dictionaryBase, IntPtr lrm, IntPtr scratchMem, long scratchSize);

    private IntPtr _lib;
    private readonly DecompressFn _decompress;
    private readonly CompressFn _compress;

    public string Name { get; }

    public OodleDll(string dllPath)
    {
        _lib = NativeLibrary.Load(dllPath);
        try
        {
            _decompress = Marshal.GetDelegateForFunctionPointer<DecompressFn>(NativeLibrary.GetExport(_lib, "OodleLZ_Decompress"));
            _compress = Marshal.GetDelegateForFunctionPointer<CompressFn>(NativeLibrary.GetExport(_lib, "OodleLZ_Compress"));
        }
        catch
        {
            // Pas la bonne DLL : on la relache avant de signaler l'erreur
            NativeLibrary.Free(_lib);
            _lib = IntPtr.Zero;
            throw;
        }
        Name = $"Oodle ({dllPath})";
    }

    public byte[] Decompress(byte[] source, int rawSize)
    {
        ObjectDisposedException.ThrowIf(_lib == IntPtr.Zero, this);
        Compressors.CheckSize(rawSize);
        var output = new byte[rawSize];
        var r = _decompress(source, source.Length, output, rawSize, 1, 0, 0,
            IntPtr.Zero, 0, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, 3);
        if (r != rawSize)
            throw new InvalidDataException(UiText.T("core.oodleFailed", r, rawSize));
        return output;
    }

    public byte[]? Compress(byte[] raw)
    {
        ObjectDisposedException.ThrowIf(_lib == IntPtr.Zero, this);
        var bound = raw.Length + 274 * (raw.Length / 0x40000 + 2) + 4096;
        var output = new byte[bound];
        var r = _compress(CompressorKraken, raw, raw.Length, output, LevelNormal,
            IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0);
        return r <= 0 ? null : output[..(int)r];
    }

    /// <summary>Relache la DLL (une seule fois, meme si Dispose est appele deux fois).</summary>
    public void Dispose()
    {
        var lib = Interlocked.Exchange(ref _lib, IntPtr.Zero);
        if (lib != IntPtr.Zero) NativeLibrary.Free(lib);
    }
}

/// <summary>
/// Decodeur Kraken open source (libkraken / ooz). Sert uniquement aux tests hors Windows.
/// </summary>
public sealed class KrakenLib : ICompressor
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int DecompressFn(byte[] src, nuint srcLen, byte[] dst, nuint dstLen);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int CompressFn(byte[] src, nuint srcLen, byte[] dst, int level);

    private IntPtr _lib;
    private readonly DecompressFn _decompress;
    private readonly CompressFn _compress;

    public string Name { get; }

    public KrakenLib(string path)
    {
        _lib = NativeLibrary.Load(path);
        try
        {
            _decompress = Marshal.GetDelegateForFunctionPointer<DecompressFn>(NativeLibrary.GetExport(_lib, "Kraken_Decompress"));
            _compress = Marshal.GetDelegateForFunctionPointer<CompressFn>(NativeLibrary.GetExport(_lib, "Kraken_Compress"));
        }
        catch
        {
            NativeLibrary.Free(_lib);
            _lib = IntPtr.Zero;
            throw;
        }
        Name = $"Kraken ({path})";
    }

    public byte[] Decompress(byte[] source, int rawSize)
    {
        Compressors.CheckSize(rawSize);
        var output = new byte[rawSize + 64];
        var r = _decompress(source, (nuint)source.Length, output, (nuint)rawSize);
        if (r != rawSize)
            throw new InvalidDataException(UiText.T("core.krakenFailed", r, rawSize));
        return output[..rawSize];
    }

    public byte[]? Compress(byte[] raw)
    {
        var output = new byte[raw.Length + raw.Length / 2 + 65536];
        var r = _compress(raw, (nuint)raw.Length, output, 4);
        return r <= 0 ? null : output[..r];
    }

    /// <summary>Relache la DLL (une seule fois, meme si Dispose est appele deux fois).</summary>
    public void Dispose()
    {
        var lib = Interlocked.Exchange(ref _lib, IntPtr.Zero);
        if (lib != IntPtr.Zero) NativeLibrary.Free(lib);
    }
}

public static class Compressors
{
    /// <summary>Refuse une taille decompressee negative ou demesuree (lue dans un fichier abime ou piege).</summary>
    internal static void CheckSize(int rawSize)
    {
        if (rawSize < 0 || rawSize > RedArchive.MaxFileSize)
            throw new InvalidDataException(UiText.T("core.badSize", rawSize));
    }

    /// <summary>
    /// Charge le compresseur : chemin explicite (DLL Oodle ou libkraken), sinon la DLL Oodle du jeu trouvee automatiquement.
    /// </summary>
    public static ICompressor Load(string? explicitPath, string? gameDir)
    {
        if (!string.IsNullOrEmpty(explicitPath))
        {
            if (explicitPath.EndsWith(".so", StringComparison.OrdinalIgnoreCase) ||
                explicitPath.EndsWith(".dylib", StringComparison.OrdinalIgnoreCase) ||
                Path.GetFileName(explicitPath).StartsWith("kraken", StringComparison.OrdinalIgnoreCase))
                return new KrakenLib(explicitPath);
            return new OodleDll(explicitPath);
        }

        var dirs = new List<string>();
        if (!string.IsNullOrEmpty(gameDir)) dirs.Add(gameDir);
        dirs.AddRange(GameLocator.FindGameDirectories());
        foreach (var dir in dirs)
        {
            var dll = Path.Combine(dir, "bin", "x64", OodleDll.DllName);
            if (File.Exists(dll)) return new OodleDll(dll);
        }
        throw new FileNotFoundException(UiText.T("core.oodleNotFound", OodleDll.DllName));
    }
}
