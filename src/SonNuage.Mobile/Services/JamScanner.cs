using SonNuage.UI.Services;
using ZXing.Net.Maui;
using ZXing.Net.Maui.Controls;

namespace SonNuage.Mobile.Services;

/// <summary>
/// Scan du QR code d'un Jam : une page caméra native (CameraX + ZXing.Net, via ZXing.Net.Maui) ouverte par-dessus
/// l'interface. Aucun service Google : marche aussi sur un téléphone sans Play Services. Demande la permission caméra
/// au premier usage ; refusée, le scan rend null et on colle le lien à la main.
/// </summary>
public sealed class JamScanner : IJamScanner
{
    public bool Available => true;

    public async Task<string?> ScanAsync(CancellationToken ct)
    {
        return await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            if (await Permissions.RequestAsync<Permissions.Camera>() != PermissionStatus.Granted) return null;
            if (Application.Current?.Windows.FirstOrDefault()?.Page is not { } root) return null;

            var page = new QrScanPage();
            await root.Navigation.PushModalAsync(page, animated: true);
            string? text;
            using (ct.Register(() => page.Cancel()))
                text = await page.Result;
            if (root.Navigation.ModalStack.Contains(page)) await root.Navigation.PopModalAsync(animated: true);
            return text;
        });
    }
}

/// <summary>Page plein écran : aperçu caméra, consigne, bouton Annuler. Rend le premier QR code lu, ou null.</summary>
internal sealed class QrScanPage : ContentPage
{
    private readonly TaskCompletionSource<string?> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CameraBarcodeReaderView _camera;

    public Task<string?> Result => _result.Task;

    public QrScanPage()
    {
        BackgroundColor = Colors.Black;
        _camera = new CameraBarcodeReaderView
        {
            Options = new BarcodeReaderOptions { Formats = BarcodeFormat.QrCode, AutoRotate = true, Multiple = false },
            IsDetecting = true,
        };
        _camera.BarcodesDetected += (_, e) =>
        {
            if (e.Results.FirstOrDefault()?.Value is not { Length: > 0 } value) return;
            _camera.IsDetecting = false;
            _result.TrySetResult(value);
        };

        var hint = new Label
        {
            Text = NativeText.Get("jam.scanHint", "Visez le QR code du Jam"),
            TextColor = Colors.White,
            FontSize = 16,
            HorizontalTextAlignment = TextAlignment.Center,
            Margin = new Thickness(24, 48, 24, 0),
        };
        var cancel = new Button
        {
            Text = NativeText.Get("common.cancel", "Annuler"),
            TextColor = Colors.White,
            BackgroundColor = Color.FromArgb("#33FFFFFF"),
            CornerRadius = 20,
            Padding = new Thickness(24, 8),
            HorizontalOptions = LayoutOptions.Center,
            Margin = new Thickness(0, 0, 0, 48),
        };
        cancel.Clicked += (_, _) => Cancel();

        var grid = new Grid { RowDefinitions = [new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto)] };
        grid.Add(_camera);
        Grid.SetRowSpan(_camera, 3);
        grid.Add(hint, 0, 0);
        grid.Add(cancel, 0, 2);
        Content = grid;
    }

    public void Cancel()
    {
        _camera.IsDetecting = false;
        _result.TrySetResult(null);
    }

    /// <summary>Retour système : même effet qu'Annuler.</summary>
    protected override bool OnBackButtonPressed()
    {
        Cancel();
        return true;
    }

    protected override void OnDisappearing()
    {
        // Fermée autrement (geste, autre navigation) : l'appelant ne doit pas attendre indéfiniment.
        _camera.IsDetecting = false;
        _result.TrySetResult(null);
        base.OnDisappearing();
    }
}
