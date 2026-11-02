using System.Text.Json;
using Android.Runtime;
using SonNuage.Contracts;
using SonNuage.UI.Services;

namespace SonNuage.Mobile.Services;

/// <summary>
/// Filet de sécurité : toute exception non gérée (.NET, tâches, Java) devient un rapport (trace de pile, appareil,
/// dernières actions) écrit sur le téléphone avant que le processus ne meure. Au lancement suivant,
/// <see cref="CrashPage"/> le montre et propose de l'envoyer au serveur : un plantage n'est plus jamais muet.
/// </summary>
public static class CrashLog
{
    private const string Folder = "crashes";
    private const string LegacyFile = "crash.log";
    // On garde les derniers plantages, pas un historique sans fin.
    private const int MaxPending = 10;
    private static int _installed;

    /// <summary>Dernières actions, partagées avec l'interface (navigation), le lecteur et la journalisation.</summary>
    public static Breadcrumbs Breadcrumbs { get; } = new();

    public static void Install()
    {
        if (Interlocked.Exchange(ref _installed, 1) == 1) return;
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Write("AppDomain", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            // Tâche « fire and forget » en échec : on la note sans tuer l'appli.
            Write("TaskScheduler", e.Exception);
            e.SetObserved();
        };
        AndroidEnvironment.UnhandledExceptionRaiser += (_, e) => Write("Android", e.Exception);
        Breadcrumbs.Add("démarrage");
    }

    public static void Write(string source, Exception? exception)
    {
        try
        {
            var report = CrashReports.Create(DateTimeOffset.Now, source, exception ?? new Exception("(exception inconnue)"), Device(), Breadcrumbs.Snapshot());
            Android.Util.Log.Error("SonNuage", report.Report);
            if (DirectoryOrNull() is not { } dir) return;
            File.WriteAllText(System.IO.Path.Combine(dir, $"{report.Id}.json"), JsonSerializer.Serialize(report));
            foreach (var old in Directory.GetFiles(dir, "*.json").OrderByDescending(File.GetLastWriteTimeUtc).Skip(MaxPending)) File.Delete(old);
        }
        catch
        {
            // Écrire le rapport ne doit jamais aggraver le plantage.
        }
    }

    /// <summary>Rapports ni envoyés ni écartés, du plus ancien au plus récent.</summary>
    public static IReadOnlyList<CrashReportRequest> ReadPending()
    {
        var reports = new List<CrashReportRequest>();
        try
        {
            if (DirectoryOrNull() is not { } dir) return reports;
            ImportLegacy(dir);
            foreach (var file in Directory.GetFiles(dir, "*.json").OrderBy(File.GetLastWriteTimeUtc))
            {
                try
                {
                    if (JsonSerializer.Deserialize<CrashReportRequest>(File.ReadAllText(file)) is { } report) reports.Add(report);
                }
                catch (JsonException)
                {
                    File.Delete(file); // Écrit à moitié pendant le plantage : illisible, inutile de le garder.
                }
            }
        }
        catch
        {
        }
        return reports;
    }

    /// <summary>Rapport envoyé ou écarté : il ne sera plus proposé.</summary>
    public static void Remove(Guid id)
    {
        try
        {
            if (DirectoryOrNull() is { } dir) File.Delete(System.IO.Path.Combine(dir, $"{id}.json"));
        }
        catch
        {
        }
    }

    /// <summary>Texte lisible des rapports, pour l'écran et le presse-papiers.</summary>
    public static string Describe(IEnumerable<CrashReportRequest> reports) => string.Join("\n\n", reports.Select(r =>
        $"==== {r.OccurredAt.ToLocalTime():yyyy-MM-dd HH:mm:ss zzz} · {r.Source} ====\nSonNuage {r.AppVersion} · {r.Os} · {r.Device}\n{r.Report}"));

    // Ancien format (un seul fichier texte) : converti pour pouvoir l'envoyer lui aussi.
    private static void ImportLegacy(string dir)
    {
        var legacy = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(dir)!, LegacyFile);
        if (!File.Exists(legacy)) return;
        var text = File.ReadAllText(legacy).Trim();
        if (text.Length > 0)
        {
            var d = Device();
            var report = new CrashReportRequest(Guid.NewGuid(), File.GetLastWriteTimeUtc(legacy), d.App, d.AppVersion, d.Device, d.Os, LegacyFile,
                text.Length > CrashReportRequest.MaxReportLength ? text[^CrashReportRequest.MaxReportLength..] : text);
            File.WriteAllText(System.IO.Path.Combine(dir, $"{report.Id}.json"), JsonSerializer.Serialize(report));
        }
        File.Delete(legacy);
    }

    private static CrashDevice Device()
    {
        var details = new List<(string, string)>();
        void Add(string name, Func<string> value)
        {
            try
            {
                details.Add((name, value()));
            }
            catch
            {
                // Une information manquante ne doit pas empêcher le rapport.
            }
        }
        Add("ABI", () => string.Join(", ", Android.OS.Build.SupportedAbis ?? []));
        Add("Ouverte depuis", () => TimeSpan.FromMilliseconds(Android.OS.SystemClock.ElapsedRealtime() - Android.OS.Process.StartElapsedRealtime).ToString(@"d\.hh\:mm\:ss"));
        Add("Thread", () => $"{Environment.CurrentManagedThreadId} {Thread.CurrentThread.Name}".Trim());
        Add("Mémoire .NET", () => $"{GC.GetTotalMemory(false) / 1048576} Mo");
        Add("Mémoire Java", () =>
        {
            var rt = Java.Lang.Runtime.GetRuntime()!;
            return $"{(rt.TotalMemory() - rt.FreeMemory()) / 1048576} Mo / {rt.MaxMemory() / 1048576} Mo";
        });
        Add("Mémoire appareil", () =>
        {
            var info = new Android.App.ActivityManager.MemoryInfo();
            ((Android.App.ActivityManager)Android.App.Application.Context.GetSystemService(Android.Content.Context.ActivityService)!).GetMemoryInfo(info);
            return $"{info.AvailMem / 1048576} Mo libres / {info.TotalMem / 1048576} Mo{(info.LowMemory ? " (mémoire basse)" : "")}";
        });
        Add("Langue", () => Java.Util.Locale.Default.ToLanguageTag());
        return new CrashDevice("android",
            $"{AppInfo.Current.VersionString} ({AppInfo.Current.BuildString})",
            $"{Android.OS.Build.Manufacturer} {Android.OS.Build.Model}",
            $"Android {Android.OS.Build.VERSION.Release} (API {(int)Android.OS.Build.VERSION.SdkInt})",
            details);
    }

    private static string? DirectoryOrNull() =>
        Android.App.Application.Context?.FilesDir?.AbsolutePath is { } files ? Directory.CreateDirectory(System.IO.Path.Combine(files, Folder)).FullName : null;
}

/// <summary>
/// Écran d'erreur natif (sans Blazor ni WebView) : rapport sélectionnable, Envoyer au serveur, Copier, puis Continuer.
/// <paramref name="send"/> absent (l'appli n'a pas démarré) : seulement Copier.
/// </summary>
public sealed class CrashPage : ContentPage
{
    public CrashPage(IReadOnlyList<CrashReportRequest> reports, Func<Page>? next, Func<CrashReportRequest, Task>? send = null)
    {
        BackgroundColor = Color.FromArgb("#121212");
        var text = CrashLog.Describe(reports);
        var buttons = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };
        var status = new Label { TextColor = Color.FromArgb("#ff8a80"), IsVisible = false };

        if (send is not null)
        {
            var sendButton = MakeButton(NativeText.Get("crash.send", "Envoyer le rapport"), primary: true);
            sendButton.Clicked += async (_, _) =>
            {
                sendButton.IsEnabled = false;
                sendButton.Text = NativeText.Get("crash.sending", "Envoi…");
                status.IsVisible = false;
                try
                {
                    foreach (var report in reports)
                    {
                        await send(report);
                        CrashLog.Remove(report.Id);
                    }
                    sendButton.Text = NativeText.Get("crash.sent", "Envoyé ✓");
                }
                catch (Exception ex)
                {
                    // Le rapport reste sur le téléphone : il sera reproposé, rien n'est perdu.
                    sendButton.IsEnabled = true;
                    sendButton.Text = NativeText.Get("crash.send", "Envoyer le rapport");
                    status.Text = $"{NativeText.Get("crash.sendFailed", "Envoi impossible :")} {ex.Message}";
                    status.IsVisible = true;
                }
            };
            buttons.Children.Add(sendButton);
        }

        var copy = MakeButton(NativeText.Get("crash.copy", "Copier le rapport"), primary: send is null);
        copy.Clicked += async (_, _) =>
        {
            await Clipboard.Default.SetTextAsync(text);
            copy.Text = NativeText.Get("crash.copied", "Copié ✓");
        };
        buttons.Children.Add(copy);

        if (next is not null)
        {
            var go = MakeButton(NativeText.Get("crash.continue", "Continuer"), primary: false);
            go.Clicked += (_, _) =>
            {
                foreach (var report in reports) CrashLog.Remove(report.Id);
                if (Window is { } window) window.Page = next();
            };
            buttons.Children.Add(go);
        }

        var intro = send is null
            ? NativeText.Get("crash.intro", "Voici le rapport d'erreur. Copiez-le pour le transmettre.")
            : NativeText.Get("crash.introSend", "Envoyez ce rapport au serveur pour que le problème puisse être corrigé.");
        Content = new Grid
        {
            Padding = new Thickness(16, 32, 16, 16),
            RowSpacing = 12,
            RowDefinitions = [new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto), new(GridLength.Auto)],
            Children =
            {
                new Label { Text = next is null ? NativeText.Get("crash.startFailed", "SonNuage n'a pas pu démarrer") : NativeText.Get("crash.stoppedLastTime", "SonNuage s'est arrêté la dernière fois"), FontSize = 22, FontAttributes = FontAttributes.Bold, TextColor = Colors.White },
                Row(1, new Label { Text = intro, TextColor = Color.FromArgb("#b3b3b3") }),
                Row(2, new ScrollView { Content = new Editor { Text = text, IsReadOnly = true, FontSize = 11, FontFamily = "monospace", TextColor = Color.FromArgb("#e0e0e0"), BackgroundColor = Color.FromArgb("#1e1e1e"), AutoSize = EditorAutoSizeOption.TextChanges } }),
                Row(3, status),
                Row(4, buttons),
            },
        };
    }

    private static Button MakeButton(string text, bool primary) => new()
    {
        Text = text,
        BackgroundColor = Color.FromArgb(primary ? "#ffd166" : "#333333"),
        TextColor = primary ? Color.FromArgb("#141a33") : Colors.White,
        Margin = new Thickness(0, 0, 12, 8),
    };

    private static View Row(int row, View view)
    {
        Grid.SetRow(view, row);
        return view;
    }
}

/// <summary>Application de secours quand <see cref="MauiProgram.CreateMauiApp"/> lui-même échoue.</summary>
public sealed class CrashApp : Application
{
    protected override Window CreateWindow(IActivationState? activationState) => new(new CrashPage(CrashLog.ReadPending(), null)) { Title = "SonNuage" };
}

/// <summary>
/// Les exceptions non gérées d'un composant Blazor ne tuent pas le processus : l'interface s'arrête et n'affiche que
/// « Une erreur est survenue ». Elles passent par la journalisation, d'où on les recopie dans le journal de plantage.
/// Les avertissements et erreurs de toute l'appli rejoignent les dernières actions.
/// </summary>
public sealed class CrashLoggerProvider : Microsoft.Extensions.Logging.ILoggerProvider
{
    public Microsoft.Extensions.Logging.ILogger CreateLogger(string categoryName) => new Logger(categoryName);

    public void Dispose()
    {
    }

    private sealed class Logger(string category) : Microsoft.Extensions.Logging.ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => logLevel >= Microsoft.Extensions.Logging.LogLevel.Warning;

        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            var message = formatter(state, exception);
            CrashLog.Breadcrumbs.Add($"{logLevel} {category} : {(message.Length > 300 ? message[..300] + "…" : message)}");
            if (logLevel >= Microsoft.Extensions.Logging.LogLevel.Error && exception is not null && category.StartsWith("Microsoft.AspNetCore.Components", StringComparison.Ordinal))
                CrashLog.Write($"Interface ({category})", exception);
        }
    }
}
