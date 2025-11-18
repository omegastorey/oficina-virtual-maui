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
namespace AppOV
{
    [Activity(
        Name = "com.storey.AppOV.Edelap.MainActivity",
        Theme = "@style/Maui.SplashTheme",
        MainLauncher = true,
        LaunchMode = LaunchMode.SingleTask,
        Exported = true,
        ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation |
                               ConfigChanges.UiMode | ConfigChanges.ScreenLayout |
                               ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
    public class MainActivity : MauiAppCompatActivity
    {
        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            try
            {
                // Fondo blanco splash
                Window?.SetBackgroundDrawable(new ColorDrawable(Android.Graphics.Color.White));

                // Colores de sistema (status + nav)
                var brand = new Android.Graphics.Color(ContextCompat.GetColor(this, Resource.Color.brandPrimary));
                Window?.SetStatusBarColor(brand);
                Window?.SetNavigationBarColor(brand);

                var cm = CookieManager.Instance;
                cm?.SetAcceptCookie(true);
            }
            catch (Exception ex)
            {
                Android.Util.Log.Error("MainActivity", $"Init UI/Cookies failed: {ex}");
            }

#if DEBUG
            try
            {
                if (Build.VERSION.SdkInt >= BuildVersionCodes.Kitkat)
                {
                    global::Android.Webkit.WebView.SetWebContentsDebuggingEnabled(true);
                    Android.Util.Log.Debug("MainActivity", "WebView remote debugging ENABLED");
                }
            }
            catch (Exception ex)
            {
                Android.Util.Log.Error("MainActivity", $"Enable WebView debugging failed: {ex}");
            }
#endif

            Android.Util.Log.Debug("MainActivity", "OnCreate");
            HandleIncomingUrl(Intent);
        }

        protected override void OnNewIntent(Intent? intent)
        {
            base.OnNewIntent(intent);
            Android.Util.Log.Debug("MainActivity", "OnNewIntent");
            HandleIncomingUrl(intent);
        }

        private void HandleIncomingUrl(Intent? intent)
        {
            try
            {
                var data = intent?.Data;
                Android.Util.Log.Debug("MainActivity", $"HandleIncomingUrl raw: {data}");
                if (data == null) return;

                var scheme = (data.Scheme ?? string.Empty).ToLowerInvariant();
                var host = (data.Host ?? string.Empty).ToLowerInvariant();
                var path = data.Path ?? string.Empty;

                bool isKnownHost =
                    host == "edelap.ovqa.storey.com.ar" ||
                    host == "edelap.ovdev.storey.com.ar" ||
                    host == "hemikaryotic-sanford-unmetallically.ngrok-free.dev" ||
                    host == "localhost" || host == "10.0.2.2" || host == "192.168.0.102";

                bool isKnownPath =
                    path.StartsWith("/iniciar-sesion", StringComparison.OrdinalIgnoreCase) ||
                    path.StartsWith("/registrar", StringComparison.OrdinalIgnoreCase);

                if (!((scheme == "https" || scheme == "http") && isKnownHost && isKnownPath))
                    return;

                var callbackUrl = data.ToString();
                Android.Util.Log.Debug("MainActivity", $"[HIT] AppLink -> {callbackUrl}");

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
                        Android.Util.Log.Error("MainActivity", $"Forward AppLink to WebView failed: {exUi}");
                    }
                });
            }
            catch (Exception ex)
            {
                Android.Util.Log.Error("MainActivity", $"HandleIncomingUrl error: {ex}");
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
                Android.Util.Log.Error("MainActivity", $"ResolveCurrentWebViewPage failed: {ex}");
                return null;
            }
        }
    }
}
