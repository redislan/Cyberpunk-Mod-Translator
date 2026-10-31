using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using CyberpunkModTranslator.App;
using CyberpunkModTranslator.Core;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

// -------------------------------------------------------------------------------------------------
// Cyberpunk Mod Translator : petit serveur local (127.0.0.1 uniquement) + interface affichee
// dans une fenetre d'application Edge. Fermer la fenetre arrete le programme.
// Options (pour les tests) : --port N  --sans-fenetre  --oodle <dll>  --jeu <dossier du jeu>
// -------------------------------------------------------------------------------------------------

var port = 0;
var noWindow = false;
string? oodle = null, gameDir = null;
for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--port" when i + 1 < args.Length: port = int.Parse(args[++i]); break;
        case "--sans-fenetre": noWindow = true; break;
        case "--oodle" when i + 1 < args.Length: oodle = args[++i]; break;
        case "--jeu" when i + 1 < args.Length: gameDir = args[++i]; break;
    }
}

var state = new AppState(oodle, gameDir);
var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = Array.Empty<string>(),
    ContentRootPath = AppContext.BaseDirectory,
});
builder.Logging.ClearProviders();
builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase);

var app = builder.Build();

// Securite : l'API n'accepte que les requetes de notre propre page (jeton aleatoire a chaque lancement)
app.Use(async (ctx, next) =>
{
    if (ctx.Request.Path.StartsWithSegments("/api"))
    {
        var given = ctx.Request.Headers["X-Token"].FirstOrDefault() ?? ctx.Request.Query["t"].FirstOrDefault();
        if (given != token)
        {
            ctx.Response.StatusCode = 403;
            return;
        }
    }
    ctx.Response.Headers.CacheControl = "no-store";
    try
    {
        await next();
    }
    catch (Exception ex)
    {
        state.Log(ex);
        if (!ctx.Response.HasStarted)
        {
            ctx.Response.StatusCode = 400;
            await ctx.Response.WriteAsJsonAsync(new { error = ex.Message });
        }
    }
});

// Interface integree dans l'exe
app.MapGet("/", () => Asset("index.html"));
app.MapGet("/assets/{name}", (string name) => Asset(name));

var api = app.MapGroup("/api");
api.MapGet("/state", () => state.GetState());
api.MapGet("/lang", () => state.GetLanguage());
api.MapPost("/ping", () =>
{
    state.Ping();
    return Results.Ok();
});
api.MapPost("/bye", () =>
{
    state.Bye();
    return Results.Ok();
});
api.MapGet("/browse", (string? path, string? ext) => state.Browse(path, ext));
api.MapPost("/dialog", (DialogRequest r) => state.PickNative(r));
api.MapPost("/open", (PathRequest r) => state.Open(r.Path));
api.MapPost("/close", () => state.Close());
api.MapGet("/units", () => state.GetUnits());
api.MapPost("/translations", (TranslationUpdate[] items) => state.UpdateTranslations(items));
api.MapPost("/links", (LinkUpdate r) => state.UpdateLink(r));
api.MapPost("/save", () => state.Save());
api.MapPost("/import-translation", (PathRequest r) => state.ImportTranslation(r.Path));
api.MapPost("/import-tsv", (PathRequest r) => state.ImportTsv(r.Path));
api.MapPost("/export-tsv", (PathRequest r) => state.ExportTsv(r.Path));
api.MapPost("/build", (PathRequest r) => state.Build(r.Path, r.Name));
api.MapPost("/settings", (Settings s) => state.UpdateSettings(s));
api.MapPost("/reveal", (PathRequest r) =>
{
    state.Reveal(r.Path);
    return Results.Ok();
});
api.MapPost("/reveal-lang", () =>
{
    Directory.CreateDirectory(UiText.Folder);
    state.Reveal(UiText.Folder);
    return Results.Ok();
});
api.MapPost("/quit", (IHostApplicationLifetime life) =>
{
    _ = Task.Delay(300).ContinueWith(_ => life.StopApplication());
    return Results.Ok();
});

await app.StartAsync();
var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
var url = $"{address}/?t={token}";
try
{
    Directory.CreateDirectory(AppPaths.Data);
    File.WriteAllText(Path.Combine(AppPaths.Data, "adresse.txt"), url);
}
catch
{
    // pas grave
}

if (!noWindow)
{
    var window = WindowLauncher.Open(url);
    var started = DateTime.UtcNow;

    // Arret : fenetre fermee, page quittee sans revenir, ou plus de nouvelles de la page depuis longtemps
    _ = Task.Run(async () =>
    {
        while (true)
        {
            await Task.Delay(2000);
            // Si le navigateur a rendu la main tout de suite (fenetre confiee a une instance deja ouverte),
            // on ne peut pas suivre le processus : on se fie alors aux signaux de la page.
            if (window != null && window.HasExited && window.ExitTime.ToUniversalTime() - started < TimeSpan.FromSeconds(5))
                window = null;
            var windowClosed = window != null && window.HasExited;
            var leftPage = state.ByeReceived && DateTime.UtcNow - state.ByeAt > TimeSpan.FromSeconds(6);
            var silent = state.EverPinged && DateTime.UtcNow - state.LastPing > TimeSpan.FromMinutes(3);
            if (windowClosed || leftPage || silent)
            {
                app.Lifetime.StopApplication();
                break;
            }
        }
    });
}
else
{
    Console.WriteLine(url);
}

await app.WaitForShutdownAsync();
state.Dispose();

static IResult Asset(string name)
{
    var stream = typeof(AppState).Assembly.GetManifestResourceStream("wwwroot/" + name);
    if (stream == null) return Results.NotFound();
    var type = Path.GetExtension(name) switch
    {
        ".js" => "text/javascript; charset=utf-8",
        ".css" => "text/css; charset=utf-8",
        ".svg" => "image/svg+xml",
        ".png" => "image/png",
        ".ico" => "image/x-icon",
        _ => "text/html; charset=utf-8",
    };
    return Results.Stream(stream, type);
}

/// <summary>Ouvre l'interface dans une fenetre d'application (Edge, sinon Chrome/Brave, sinon le navigateur par defaut).</summary>
internal static class WindowLauncher
{
    public static Process? Open(string url)
    {
        if (OperatingSystem.IsWindows())
        {
            var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var candidates = new[]
            {
                Path.Combine(pf86, @"Microsoft\Edge\Application\msedge.exe"),
                Path.Combine(pf, @"Microsoft\Edge\Application\msedge.exe"),
                Path.Combine(local, @"Microsoft\Edge\Application\msedge.exe"),
                Path.Combine(pf, @"Google\Chrome\Application\chrome.exe"),
                Path.Combine(pf86, @"Google\Chrome\Application\chrome.exe"),
                Path.Combine(local, @"Google\Chrome\Application\chrome.exe"),
                Path.Combine(pf, @"BraveSoftware\Brave-Browser\Application\brave.exe"),
                Path.Combine(local, @"BraveSoftware\Brave-Browser\Application\brave.exe"),
            };
            foreach (var exe in candidates.Where(File.Exists))
            {
                try
                {
                    var psi = new ProcessStartInfo(exe) { UseShellExecute = false };
                    psi.ArgumentList.Add($"--app={url}");
                    psi.ArgumentList.Add($"--user-data-dir={Path.Combine(AppPaths.Data, "fenetre")}");
                    psi.ArgumentList.Add("--window-size=1500,920");
                    psi.ArgumentList.Add("--no-first-run");
                    psi.ArgumentList.Add("--no-default-browser-check");
                    psi.ArgumentList.Add("--disable-features=Translate");
                    return Process.Start(psi);
                }
                catch
                {
                    // on essaie le suivant
                }
            }
        }
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // aucun navigateur : l'adresse est dans adresse.txt
        }
        return null;
    }
}
