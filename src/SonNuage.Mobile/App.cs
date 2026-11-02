using SonNuage.Mobile.Services;
using SonNuage.UI.Services;
using Microsoft.AspNetCore.Components.WebView.Maui;

namespace SonNuage.Mobile;

public sealed class App(ApiClient api) : Application
{
    protected override Window CreateWindow(IActivationState? activationState)
    {
        // Plantage lors de la session précédente : on le montre, et propose de l'envoyer, avant de relancer l'interface.
        var pending = CrashLog.ReadPending();
        Page page = pending.Count > 0 ? new CrashPage(pending, () => new MainPage(), api.SendCrashReportAsync) : new MainPage();
        var window = new Window(page) { Title = "SonNuage" };
        window.Resumed += (_, _) => CrashLog.Breadcrumbs.Add("appli au premier plan");
        window.Stopped += (_, _) => CrashLog.Breadcrumbs.Add("appli en arrière-plan");
        window.Destroying += (_, _) => CrashLog.Breadcrumbs.Add("fenêtre fermée");
        return window;
    }
}

public sealed class MainPage : ContentPage
{
    public MainPage()
    {
        BackgroundColor = Color.FromArgb("#141a33");
        var view = new BlazorWebView { HostPage = "wwwroot/index.html" };
        view.RootComponents.Add(new RootComponent { Selector = "#app", ComponentType = typeof(SonNuage.UI.App) });
        Content = view;
    }
}
