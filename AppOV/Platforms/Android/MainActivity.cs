using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Graphics.Drawables;
using AndroidX.Core.Content;
using Microsoft.Maui;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using System;
using System.Linq;
using AppOV.Views;
using Android.Webkit;
using Android.Content.Res;

// Alias para evitar el conflicto con Resource (CS0104)
using AppResource = AppOV.Resource;

namespace AppOV
{
    // Activity original para Edelap
    [Activity(
        Name = "com.storey.AppOV.Edelap.MainActivity",
        Theme = "@style/Maui.SplashTheme",
        MainLauncher = true,
        LaunchMode = LaunchMode.SingleTask,
        Exported = true,
        ConfigurationChanges =
            ConfigChanges.ScreenSize |
            ConfigChanges.Orientation |
            ConfigChanges.UiMode |
            ConfigChanges.ScreenLayout |
            ConfigChanges.SmallestScreenSize |
            ConfigChanges.Density |
            ConfigChanges.KeyboardHidden |
            ConfigChanges.Keyboard |
            ConfigChanges.Navigation
        )]
    public class MainActivity : MauiAppCompatActivity
    {
        private const string LogTag = "MainActivity";
        protected override void OnCreate(Bundle? savedInstanceState)
        {
            AppLogger.Log(LogTag, $"OnCreate");
            base.OnCreate(savedInstanceState);
            Android.Webkit.WebView.SetWebContentsDebuggingEnabled(true);
            try
            {
                // Fondo blanco splash
                Window?.SetBackgroundDrawable(new ColorDrawable(Android.Graphics.Color.White));

                // Colores de sistema (status + nav)
                var brand = new Android.Graphics.Color(
                    ContextCompat.GetColor(this, AppResource.Color.brandPrimary));

                Window?.SetStatusBarColor(brand);
                Window?.SetNavigationBarColor(brand);

                var cm = CookieManager.Instance;
                cm?.SetAcceptCookie(true);
            }
            catch (Exception ex)
            {
                AppLogger.Log(LogTag, $"Init UI/Cookies failed: {ex}");
            }

#if DEBUG
            try
            {
                if (Build.VERSION.SdkInt >= BuildVersionCodes.Kitkat)
                {
                    global::Android.Webkit.WebView.SetWebContentsDebuggingEnabled(true);
                    AppLogger.Log(LogTag, $"WebView remote debugging ENABLED");
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log(LogTag, $"Enable WebView debugging failed: {ex}");
            }
#endif

            HandleIncomingUrl(Intent);
        }

        // ConfigChanges arriba, al rotar NO se destruye la Activity,
        // solo se llama a este método.
        public override void OnConfigurationChanged(Configuration newConfig)
        {
            base.OnConfigurationChanged(newConfig);
            AppLogger.Log(LogTag, $"OnConfigurationChanged -> Orientation={newConfig.Orientation}");
        }

        protected override void OnNewIntent(Intent? intent)
        {
            base.OnNewIntent(intent);
            AppLogger.Log(LogTag, $"OnNewIntent");
            HandleIncomingUrl(intent);
        }

        private void HandleIncomingUrl(Intent? intent)
        {
            try
            {
                var data = intent?.Data;
                AppLogger.Log(LogTag, $"HandleIncomingUrl raw: {data}");
                if (data == null) return;

                var scheme = (data.Scheme ?? string.Empty).ToLowerInvariant();
                var host = (data.Host ?? string.Empty).ToLowerInvariant();
                var path = data.Path ?? string.Empty;

                // Edelap + Cashpower + hosts locales / ngrok
                bool isKnownHost =
                    host == "edelap.ovqa.storey.com.ar" ||
                    host == "edelap.ovdev.storey.com.ar" ||
                    host == "portalderecargaqa.cashpower.com.ar" ||
                    host == "portalderecargadev.cashpower.com.ar" ||
                    host == "portalderecarga.cashpower.com.ar" ||
                    host == "hemikaryotic-sanford-unmetallically.ngrok-free.dev" ||
                    host == "localhost" || host == "10.0.2.2" || host == "192.168.0.102";

                bool isKnownPath =
                    path.StartsWith("/iniciar-sesion", StringComparison.OrdinalIgnoreCase) ||
                    path.StartsWith("/registrar", StringComparison.OrdinalIgnoreCase) ||
                    path.StartsWith("/iniciar-sesion/registro", StringComparison.OrdinalIgnoreCase);

                if (!((scheme == "https" || scheme == "http") && isKnownHost && isKnownPath))
                    return;

                var callbackUrl = data.ToString();
                AppLogger.Log(LogTag, $"[HIT] AppLink -> {callbackUrl}");
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    try
                    {
                        var wvp = ResolveCurrentWebViewPage();
                        if (wvp != null)
                        {
                            wvp.LoadCallbackUrl(callbackUrl);
                        }
                        else
                        {
                            var page = new WebViewPage(callbackUrl);
                            Microsoft.Maui.Controls.Application.Current!.MainPage = new NavigationPage(page)
                            {
                                BarBackgroundColor = Colors.Transparent,
                                BarTextColor = Colors.Transparent
                            };
                        }
                    }
                    catch (Exception exUi)
                    {
                        AppLogger.Log(LogTag, $"Forward AppLink to WebView failed: {exUi}");
                    }
                });
            }
            catch (Exception ex)
            {
                AppLogger.Log(LogTag, $"HandleIncomingUrl error: {ex}");
            }
        }

        private WebViewPage? ResolveCurrentWebViewPage()
        {
            try
            {
                var mp = Microsoft.Maui.Controls.Application.Current?.MainPage;

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
            catch (Exception ex)
            {
                AppLogger.Log(LogTag, $"ResolveCurrentWebViewPage failed: {ex}");
                return null;
            }
        }
    }

    // Activity para Cashpower, reutiliza lo de MainActivity
    [Activity(
        Name = "com.storey.AppOV.Cashpower.MainActivity",
        Theme = "@style/Maui.SplashTheme",
        MainLauncher = false,
        LaunchMode = LaunchMode.SingleTask,
        Exported = true,
        ConfigurationChanges =
            ConfigChanges.ScreenSize |
            ConfigChanges.Orientation |
            ConfigChanges.UiMode |
            ConfigChanges.ScreenLayout |
            ConfigChanges.SmallestScreenSize |
            ConfigChanges.Density |
            ConfigChanges.KeyboardHidden |
            ConfigChanges.Keyboard |
            ConfigChanges.Navigation)]
    public class CashpowerMainActivity : MainActivity
    {
        // Hereda OnCreate / OnConfigurationChanged / OnNewIntent / HandleIncomingUrl
    }
}
