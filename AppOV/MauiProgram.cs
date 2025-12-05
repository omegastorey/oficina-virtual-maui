using Microsoft.Extensions.Logging;
using Microsoft.Maui.Handlers;
using CommunityToolkit.Maui;

#if ANDROID
using Android.Webkit;
using Android.Graphics;
using AppOV.Platforms.Android;
#endif

#if IOS
using UIKit;
using AppOV.Platforms.iOS;
#endif

namespace AppOV
{
    public static class MauiProgram
    {
        private const string LogTag = "MauiProgram";
        public static MauiApp CreateMauiApp()
        {

            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .UseMauiCommunityToolkit()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                });

#if DEBUG
            builder.Logging.AddDebug();
#endif

#if ANDROID
            builder.ConfigureMauiHandlers(handlers =>
            {
                // Handler propio (descargas, auth, etc.)
                handlers.AddHandler<Microsoft.Maui.Controls.WebView, CustomWebViewHandler>();

                // Fondo blanco para evitar parpadeos en dark mode
                WebViewHandler.Mapper.AppendToMapping("WhiteBG", (h, v) =>
                {
                    try
                    {
                        h.PlatformView?.SetBackgroundColor(Android.Graphics.Color.White);
                    }
                    catch (System.Exception ex)
                    {
                        AppLogger.Log(LogTag, $"WhiteBG mapping failed: {ex}");
                    }
                });

                // 🔧 Ajustes de WebView Android: JS + Storage + Cookies + NoCache + MixedContent
                WebViewHandler.Mapper.AppendToMapping("AndroidTweaks", (h, v) =>
                {
                    try
                    {
                        var wv = h.PlatformView;
                        if (wv == null) return;

                        var s = wv.Settings;
                        if (s == null) return;

                        // JS + almacenamiento web
                        s.JavaScriptEnabled = true;
                        s.DomStorageEnabled = true;    
                        s.DatabaseEnabled = true;

                        // Cache off (evita ERR_CACHE_MISS en primeras cargas con redirects/PWA)
                        s.CacheMode = CacheModes.NoCache;
                        wv.ClearCache(true);
                        wv.ClearHistory();

                        // Permitir (si hace falta) contenido mixto bajo https (SDK 21+)
                        s.MixedContentMode = MixedContentHandling.CompatibilityMode;

                        // Cookies
                        var cm = CookieManager.Instance;
                        cm?.SetAcceptCookie(true);
                        try
                        {
                            // third-party cookies (necesario para algunos IdP embebidos)
                            if (cm != null)
                                cm.SetAcceptThirdPartyCookies(wv, true);
                        }
                        catch (System.Exception ex2)
                        {
                            AppLogger.Log(LogTag, $"AcceptThirdPartyCookies warning: {ex2.Message}");
                        }
                    }
                    catch (System.Exception ex)
                    {
                        AppLogger.Log(LogTag, $"AndroidTweaks mapping failed: {ex}");
                    }
                });

                // Asegura clientes (WebViewClient/WebChromeClient) de tu handler
                WebViewHandler.Mapper.AppendToMapping("AuthClients", (h, v) =>
                {
                    try
                    {
                        if (h is CustomWebViewHandler cw)
                        {
                            cw.AttachClients();
                        }
                        else if (h?.PlatformView != null)
                        {
                            var c = new CustomWebViewHandler.AuthAwareClient();
                            h.PlatformView.SetWebViewClient(c);

                            var ctx = h.PlatformView.Context ?? Android.App.Application.Context;
                            h.PlatformView.SetWebChromeClient(new CustomWebViewHandler.AuthChromeClient(c, ctx));
                        }
                    }
                    catch (System.Exception ex)
                    {
                        AppLogger.Log(LogTag, $"AuthClients mapping failed: {ex}");
                    }
                });

            });
#endif

#if IOS
            builder.ConfigureMauiHandlers(handlers =>
            {
                // Handler propio iOS
                handlers.AddHandler<Microsoft.Maui.Controls.WebView, CustomWebViewHandler>();

                // WKWebView opaco con fondo blanco
                WebViewHandler.Mapper.AppendToMapping("WhiteBG", (h, v) =>
                {
                    try
                    {
                        if (h.PlatformView is not null)
                        {
                            h.PlatformView.Opaque = true;
                            h.PlatformView.BackgroundColor = UIColor.White;
                        }
                    }
                    catch (System.Exception ex)
                    {
                        AppLogger.Log(LogTag, $"[iOS] WhiteBG mapping failed: {ex}");
                    }
                });
            });
#endif

            return builder.Build();
        }
    }
}
