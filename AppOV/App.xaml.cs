using CommunityToolkit.Maui.Core;
using CommunityToolkit.Maui.Core.Platform;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Platform;          
using AppOV.Views;
using System;
using System.Linq;

namespace AppOV
{
    public partial class App : Application
    {
        // === CONFIG DEV ===
#if DEBUG
    //   adb reverse tcp:52953 tcp:52953
        private const bool UseAdbReverse = true;
        private const string DevPcLanIp = "192.168.0.102";
        private const int DevPort = 52953;

        // Activa/desactiva ngrok en DEBUG:
        private const bool UseNgrok = false;
        // Valor por defecto (puede ser sobreescrito por la env var OV_NGROK_HOST)
        private const string NgrokHostDefault = "hemikaryotic-sanford-unmetallically.ngrok-free.dev";
#endif
        private static bool _windowEventsWired;
        public App()
        {
            InitializeComponent();

            // 1) Inicializamos branding (valor por defecto)
            Branding.Initialize();

            // 2) Navegación de arranque
            var startUrl = ResolveStartUrl();
            var start = new WebViewPage(startUrl);
            var brandColor = (Color)Current.Resources["BrandPrimaryColor"];
            var nav = new NavigationPage(start)
            {
                BarBackgroundColor = brandColor,
                BarTextColor = Colors.White
            };
            MainPage = nav;

            // 3) Si cambia el color (p.ej. lo define la web), re-aplicamos
            Branding.Changed += (_, __) =>
            {
                var newColor = Color.FromArgb(Branding.CurrentHex);

                // status/nav bar nativo
                Branding.ApplySystemBars();

                // barra de navegación MAUI (iOS la usa para pintar detrás del status bar)
                if (Current?.MainPage is NavigationPage np)
                {
                    np.BarBackgroundColor = newColor;
                    np.BarTextColor = Colors.White;
                }
            };
        }

        private static string ResolveStartUrl()
        {
#if DEBUG
            // Permite cambiar el host sin recompilar:
            var envNgrok = Environment.GetEnvironmentVariable("OV_NGROK_HOST");
            var ngrokHost = !string.IsNullOrWhiteSpace(envNgrok) ? envNgrok.Trim() : NgrokHostDefault;

#if ANDROID
            if (UseNgrok && !string.IsNullOrWhiteSpace(ngrokHost))
                return $"https://{ngrokHost}/iniciar-sesion";

            if (UseAdbReverse)
                return $"http://localhost:{DevPort}/iniciar-sesion";
            return $"http://{DevPcLanIp}:{DevPort}/iniciar-sesion";

#elif IOS
            if (UseNgrok && !string.IsNullOrWhiteSpace(ngrokHost))
                return $"https://{ngrokHost}/iniciar-sesion";
            return $"http://{DevPcLanIp}:{DevPort}/iniciar-sesion";

#elif WINDOWS
            if (UseNgrok && !string.IsNullOrWhiteSpace(ngrokHost))
                return $"https://{ngrokHost}/iniciar-sesion";
            return $"http://localhost:{DevPort}/iniciar-sesion";

#else
            if (UseNgrok && !string.IsNullOrWhiteSpace(ngrokHost))
                return $"https://{ngrokHost}/iniciar-sesion";
            return $"http://{DevPcLanIp}:{DevPort}/iniciar-sesion";
#endif

#else
            // === PRODUCCIÓN ===
            return "https://edelap.ovqa.storey.com.ar/iniciar-sesion";
#endif
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            var window = base.CreateWindow(activationState);
            void Apply() => Branding.ApplySystemBars();
            void ApplyOnUi() => MainThread.BeginInvokeOnMainThread(Apply);
            window.Created += (_, __) => ApplyOnUi();
            window.Activated += (_, __) => ApplyOnUi();
            Application.Current!.RequestedThemeChanged += (_, __) => ApplyOnUi();
            if (Shell.Current is not null)
                Shell.Current.Navigated += (_, __) => ApplyOnUi();

            return window;
        }

        // === Manejo de App Links y reenvío al WebView ===
        protected override void OnAppLinkRequestReceived(Uri uri)
        {
            try
            {
                var callbackUrl = uri?.ToString();
                if (string.IsNullOrWhiteSpace(callbackUrl))
                {
                    base.OnAppLinkRequestReceived(uri);
                    return;
                }

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    var wvp = ResolveCurrentWebViewPage();
                    if (wvp != null)
                    {
                        wvp.LoadCallbackUrl(callbackUrl);
                    }
                    else
                    {
                        var page = new WebViewPage(callbackUrl);
                        Application.Current!.MainPage = new NavigationPage(page)
                        {
                            BarBackgroundColor = Colors.Transparent,
                            BarTextColor = Colors.Transparent
                        };
                    }
                });
            }
            finally
            {
                base.OnAppLinkRequestReceived(uri);
            }
        }

        private WebViewPage? ResolveCurrentWebViewPage()
        {
            var mp = Application.Current?.MainPage;

            if (mp is Shell shell)
            {
                if (shell.CurrentPage is WebViewPage w1) return w1;
                foreach (var p in shell.Navigation?.NavigationStack?.Reverse() ?? Enumerable.Empty<Page>())
                    if (p is WebViewPage w) return w;
            }

            if (mp is NavigationPage nav)
            {
                if (nav.CurrentPage is WebViewPage w2) return w2;
                foreach (var p in nav.Navigation.NavigationStack.Reverse())
                    if (p is WebViewPage w) return w;
            }

            return mp as WebViewPage;
        }
    }
}
