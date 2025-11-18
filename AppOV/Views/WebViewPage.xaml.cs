using Microsoft.Maui.Controls;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Storage;
using Microsoft.Maui.Devices;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

#if ANDROID
using Android.Webkit;
#endif

#if IOS || MACCATALYST
using WebKit;
using Foundation;
#endif

namespace AppOV.Views
{
    public partial class WebViewPage : ContentPage
    {
        private bool _tokenHookInjected;
        private bool _providerHookInjected;
        private bool _iframeHookInjected;
        private bool _clearedForStart;

        public WebViewPage(string url)
        {
            InitializeComponent();
            NavigationPage.SetHasNavigationBar(this, false);

            MyWebView.Navigating += OnWebViewNavigating;
            MyWebView.Navigated += OnWebViewNavigated;

            _ = NavigateFreshAsync(url);
        }

        private async Task NavigateFreshAsync(string url)
        {
            try
            {
                if (IsLoginStartUrl(url) && !_clearedForStart)
                {
                    await ClearWebStateAsync(includeCookies: true, includeStorage: true, includeCache: true);
                    _clearedForStart = true;
                }
                MyWebView.Source = url;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WebViewPage] NavigateFreshAsync error: {ex}");
                MyWebView.Source = url;
            }
        }

        private static bool IsLoginStartUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            try
            {
                var u = new Uri(url, UriKind.Absolute);
                var path = (u.AbsolutePath ?? string.Empty).ToLowerInvariant();
                var query = (u.Query ?? string.Empty).ToLowerInvariant();

                bool looksCallback = query.Contains("code=") || query.Contains("id_token=") || query.Contains("access_token=");
                bool looksStart = path.StartsWith("/iniciar-sesion") || path.StartsWith("/registrar");
                return looksStart && !looksCallback;
            }
            catch
            {
                var lower = url.ToLowerInvariant();
                bool looksCallback = lower.Contains("code=") || lower.Contains("id_token=") || lower.Contains("access_token=");
                bool looksStart = lower.Contains("/iniciar-sesion") || lower.Contains("/registrar");
                return looksStart && !looksCallback;
            }
        }

        private async Task ClearWebStateAsync(bool includeCookies, bool includeStorage, bool includeCache)
        {
            try
            {
#if ANDROID
                if (includeCookies)
                {
                    try
                    {
                        var cm = CookieManager.Instance;
                        if (cm != null)
                        {
                            cm.RemoveAllCookies(null);
                            cm.Flush();
                        }
                    }
                    catch (Exception exCookies)
                    {
                        Android.Util.Log.Warn("WebViewPage", $"Clear cookies warning: {exCookies.Message}");
                    }
                }

                if (includeStorage)
                {
                    try { WebStorage.Instance?.DeleteAllData(); }
                    catch (Exception exStorage)
                    {
                        Android.Util.Log.Warn("WebViewPage", $"Clear WebStorage warning: {exStorage.Message}");
                    }
                }

                if (includeCache)
                {
                    try
                    {
                        var wv = MyWebView?.Handler?.PlatformView as Android.Webkit.WebView;
                        wv?.ClearCache(true);
                        wv?.ClearHistory();
                    }
                    catch (Exception exCache)
                    {
                        Android.Util.Log.Warn("WebViewPage", $"Clear cache/history warning: {exCache.Message}");
                    }
                }
#endif

#if IOS || MACCATALYST
                var types = WKWebsiteDataStore.AllWebsiteDataTypes;
                var since = NSDate.FromTimeIntervalSince1970(0);

                if (includeCookies || includeStorage || includeCache)
                {
                    try
                    {
                        var tcs = new TaskCompletionSource<bool>();
                        WKWebsiteDataStore.DefaultDataStore.RemoveDataOfTypes(types, since, () =>
                        {
                            try { tcs.TrySetResult(true); }
                            catch (Exception exCb)
                            {
                                Debug.WriteLine($"[WebViewPage] RemoveData callback error: {exCb}");
                                tcs.TrySetResult(false);
                            }
                        });
                        await tcs.Task.ConfigureAwait(false);
                    }
                    catch (Exception exIos)
                    {
                        Debug.WriteLine($"[WebViewPage] iOS/MacCatalyst clear failed: {exIos}");
                    }
                }
#endif
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WebViewPage] ClearWebStateAsync error: {ex}");
            }
        }

        public void LoadCallbackUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return;
            MainThread.BeginInvokeOnMainThread(() =>
            {
                try
                {
                    _tokenHookInjected = false;
                    _providerHookInjected = false;
                    _iframeHookInjected = false;
                    MyWebView.Source = url;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"LoadCallbackUrl error: {ex}");
                }
            });
        }

        private async void OnWebViewNavigating(object sender, WebNavigatingEventArgs e)
        {
            try
            {
                var next = e?.Url?.Trim() ?? string.Empty;
                if (IsLoginStartUrl(next) && !_clearedForStart)
                {
                    e.Cancel = true;
                    await ClearWebStateAsync(true, true, true);
                    _clearedForStart = true;
                    await Task.Delay(25);
                    MyWebView.Source = next;
                    return;
                }

                _tokenHookInjected = false;
                _providerHookInjected = false;
                _iframeHookInjected = false;

                if (string.IsNullOrEmpty(next)) return;

                if (next.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
                {
                    e.Cancel = true;
                    if (Uri.TryCreate(next, UriKind.Absolute, out var mailto))
                        await Launcher.Default.OpenAsync(mailto);
                    else
                        await DisplayAlert("Error", $"URL inválida: {next}", "OK");
                    return;
                }

                if (Uri.TryCreate(next, UriKind.Absolute, out var u))
                {
                    var host = (u.Host ?? string.Empty).ToLowerInvariant();
                    string? provider = null;
                    if (host.Contains("accounts.google.")) provider = "google";
                    else if (host.Contains("appleid.apple.") || host.Contains("identity.apple.com")) provider = "apple";
                    else if (host.Contains("login.microsoftonline.") || host.Contains("login.live.")) provider = "microsoft";

                    if (!string.IsNullOrEmpty(provider))
                    {
                        try
                        {
                            await MyWebView.EvaluateJavaScriptAsync($@"
                                try {{
                                  sessionStorage.setItem('provider','{provider}');
                                  localStorage.setItem('provider','{provider}');
                                }} catch(e) {{ }}");
                        }
                        catch (Exception exJs)
                        {
                            Debug.WriteLine($"[WebViewPage] set provider JS failed: {exJs}");
                        }

                        Preferences.Set("web_provider", provider);

                        e.Cancel = true;
                        await Browser.OpenAsync(u, BrowserLaunchMode.External);
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error en OnWebViewNavigating: {ex}");
            }
        }

        private async void OnWebViewNavigated(object sender, WebNavigatedEventArgs e)
        {
            async Task EvalJsAsync(string js, string tag)
            {
                try { await MyWebView.EvaluateJavaScriptAsync(js); }
                catch (Exception ex) { Debug.WriteLine($"[WebViewPage][{tag}] {ex}"); }
            }

            // probe
            const string probeJs = @"
                (function(){
                  try{
                    if (window.jsBridge && typeof window.jsBridge.Dbg === 'function'){
                      window.jsBridge.Dbg('probe: jsBridge OK - ' + (window.location ? window.location.href : 'no-location'));
                    }
                  }catch(e){}
                })();";
            await EvalJsAsync(probeJs, "probe");

            // Diag (fetch/xhr)
            const string diagJs = @"
                (function(){
                  if (window.__netDiagInstalled) return; window.__netDiagInstalled = true;
                  function dbg(m){ try{ window.jsBridge && window.jsBridge.Dbg && window.jsBridge.Dbg(m); }catch(_){ } }
                  window.addEventListener('error', function(ev){ try{ dbg('window.onerror: ' + (ev && ev.message ? ev.message : 'unknown')); }catch(_){} });
                  window.addEventListener('unhandledrejection', function(ev){
                    try{
                      var r = ev && (ev.reason && (ev.reason.message || ev.reason) || ev);
                      dbg('unhandledrejection: ' + r);
                    }catch(_){}
                  });
                })();";
            await EvalJsAsync(diagJs, "netDiag");

            // flags
            var platform =
                DeviceInfo.Platform == DevicePlatform.Android ? "android" :
                DeviceInfo.Platform == DevicePlatform.iOS ? "ios" :
                DeviceInfo.Platform == DevicePlatform.MacCatalyst ? "mac" :
                DeviceInfo.Platform == DevicePlatform.WinUI ? "windows" :
                DeviceInfo.Platform == DevicePlatform.Tizen ? "tizen" : "unknown";

            var nativeOpens = (DeviceInfo.Platform == DevicePlatform.iOS || DeviceInfo.Platform == DevicePlatform.MacCatalyst) ? "true" : "false";
            var flagsJs = $@"
                (function(){{
                  try{{
                    window.__nativePlatform = '{platform}';
                    window.__nativeOpensProviders = {nativeOpens};
                  }}catch(e){{}}
                }})();";
            await EvalJsAsync(flagsJs, "nativeFlags");

            // Re-inyecta token si existe
            var saved = Preferences.Get("web_jwt", null);
            if (!string.IsNullOrEmpty(saved))
            {
                var tok = EscapeForJs(saved);
                var jsSet = (@"try {
                  localStorage.setItem('access_token', '__TOKEN__');
                  sessionStorage.setItem('access_token', '__TOKEN__');
                  localStorage.setItem('token', '__TOKEN__');
                } catch(e) {}").Replace("__TOKEN__", tok);
                await EvalJsAsync(jsSet, "tokenReinject");
            }
        }

        private static string EscapeForJs(string s)
            => (s ?? string.Empty)
               .Replace(@"\", @"\\")
               .Replace("'", @"\'")
               .Replace("\r", "")
               .Replace("\n", "");
    }
}
