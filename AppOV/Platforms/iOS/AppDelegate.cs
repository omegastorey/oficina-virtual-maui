using Foundation;
using UIKit;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using System.Linq;

namespace AppOV
{
    [Register("AppDelegate")]
    public class AppDelegate : MauiUIApplicationDelegate
    {
        protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

        // Universal Links QA/DEV
        public override bool ContinueUserActivity(UIApplication application, NSUserActivity userActivity, UIApplicationRestorationHandler completionHandler)
        {
            try
            {
                if (userActivity?.ActivityType == NSUserActivityType.BrowsingWeb)
                {
                    var url = userActivity.WebPageUrl;
                    var abs = url?.AbsoluteString ?? string.Empty;
                    if (!string.IsNullOrEmpty(abs))
                    {
                        var host = url!.Host?.ToLowerInvariant();
                        var path = url.Path ?? string.Empty;

                        bool match =
                            (host == "edelap.ovqa.storey.com.ar" || host == "edelap.ovdev.storey.com.ar" || host == "portalderecarga.cashpower.com.ar" || host == "portalderecargaqa.cashpower.com.ar") &&
                            (path.StartsWith("/iniciar-sesion") || path.StartsWith("/registrar")(path.StartsWith("/iniciar-sesion/registro") ||);

                        if (match)
                        {
                            ForwardToWebView(abs);
                            return true;
                        }
                    }
                }
            }
            catch { }

            return base.ContinueUserActivity(application, userActivity, completionHandler);
        }

        // (Opcional) esquema custom appov://
        public override bool OpenUrl(UIApplication application, NSUrl url, NSDictionary options)
        {
            try
            {
                if ((url.Scheme ?? "").ToLowerInvariant() == "appov")
                {
                    ForwardToWebView(url.AbsoluteString);
                    return true;
                }
            }
            catch { }

            return base.OpenUrl(application, url, options);
        }

        private static void ForwardToWebView(string url)
        {
            Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(() =>
            {
                var mp = Microsoft.Maui.Controls.Application.Current?.MainPage;

                if (mp is Shell shell)
                {
                    if (shell.CurrentPage is Views.WebViewPage w1) { w1.LoadCallbackUrl(url); return; }
                    foreach (var p in shell.Navigation?.NavigationStack?.Reverse() ?? Enumerable.Empty<Page>())
                        if (p is Views.WebViewPage w) { w.LoadCallbackUrl(url); return; }
                }

                if (mp is NavigationPage nav)
                {
                    if (nav.CurrentPage is Views.WebViewPage w2) { w2.LoadCallbackUrl(url); return; }
                    foreach (var p in nav.Navigation.NavigationStack.Reverse())
                        if (p is Views.WebViewPage w) { w.LoadCallbackUrl(url); return; }
                }

                var page = new Views.WebViewPage(url);
                Microsoft.Maui.Controls.Application.Current!.MainPage = new NavigationPage(page)
                {
                    BarBackgroundColor = Colors.Transparent,
                    BarTextColor = Colors.Transparent
                };
            });
        }
    }
}
