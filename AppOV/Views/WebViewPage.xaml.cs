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
        private const string LogTag = "WebViewPage";

        private const string PrefGlobalClearedKey = "web_global_cleared_once";
        private const string PrefLastUrlKey = "web_last_url";

        // Limpieza global: solo una vez por ciclo de vida del proceso
        private static bool _globalClearedOnce = Preferences.Get(PrefGlobalClearedKey, false);

        private bool _tokenHookInjected;
        private bool _providerHookInjected;
        private bool _iframeHookInjected;
        private bool _clearedForStart;

        // Para saber que ya hubo al menos una conexión de handler
        //private bool _handlerAttachedOnce;

        public WebViewPage(string url)
        {
            InitializeComponent();
            NavigationPage.SetHasNavigationBar(this, false);

            var hasToken = HasSavedToken();
            var lastUrl = Preferences.Get(PrefLastUrlKey, null);
            var effectiveUrl = url;

            if (hasToken)
            {
                if (!string.IsNullOrWhiteSpace(lastUrl) && !LooksLoginPath(lastUrl))
                {
                    effectiveUrl = lastUrl;
                    AppLogger.Log(LogTag,
                        $"ctor – hay token y lastUrl={lastUrl}; uso esa URL.");
                }
                else if (IsLoginStartUrl(url) || LooksLoginPath(url))
                {
                    var root = GetRootUrl(url);
                    effectiveUrl = root;
                    AppLogger.Log(LogTag,
                        $"ctor – hay token pero url inicial es login; cambio a root: {effectiveUrl}");
                }
            }

            AppLogger.Log(LogTag, $"ctor – url solicitada: {url}");
            AppLogger.Log(LogTag, $"ctor – url efectiva: {effectiveUrl}");

            MyWebView.Navigating += OnWebViewNavigating;
            MyWebView.Navigated += OnWebViewNavigated;

            _ = NavigateFreshAsync(effectiveUrl);
        }

        protected override void OnHandlerChanged()
        {
            base.OnHandlerChanged();

            if (Handler == null)
                return;

            try
            {
                var lastUrl = Preferences.Get(PrefLastUrlKey, null);
                var hasToken = HasSavedToken();

                var currentUrl = (MyWebView.Source as UrlWebViewSource)?.Url ?? string.Empty;

                AppLogger.Log(LogTag,
                    $"OnHandlerChanged(hasToken={hasToken}) lastUrl={lastUrl}, current={currentUrl}");

                // 1) Si tengo lastUrl válida, hago lo que ya tenías
                if (!string.IsNullOrWhiteSpace(lastUrl))
                {
                    if (LooksLoginPath(lastUrl) && !hasToken)
                    {
                        AppLogger.Log(LogTag,
                            "OnHandlerChanged -> lastUrl es login sin token; dejo Source actual.");
                        return;
                    }

                    if (string.Equals(currentUrl, lastUrl, StringComparison.OrdinalIgnoreCase))
                    {
                        AppLogger.Log(LogTag,
                            "OnHandlerChanged -> MyWebView ya está en lastUrl, no renavego.");
                        return;
                    }

                    AppLogger.Log(LogTag,
                        $"OnHandlerChanged -> re-navegando a última URL: {lastUrl}");
                    MyWebView.Source = lastUrl;
                    return;
                }

                // 2) NO hay lastUrl, pero SÍ hay token y estamos en login -> ir a root del sitio
                if (hasToken && LooksLoginPath(currentUrl))
                {
                    var root = GetRootUrl(currentUrl);
                    AppLogger.Log(LogTag,
                        $"OnHandlerChanged -> hay token pero no lastUrl y estamos en login; voy a root: {root}");
                    MyWebView.Source = root;
                }
                else
                {
                    AppLogger.Log(LogTag,
                        "OnHandlerChanged -> no hay lastUrl; dejo Source actual.");
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log(LogTag, $"OnHandlerChanged error: {ex}");
            }
        }

        private static string GetRootUrl(string url)
        {
            try
            {
                var u = new Uri(url, UriKind.Absolute);
                var root = $"{u.Scheme}://{u.Host}{(u.IsDefaultPort ? "" : ":" + u.Port)}/";
                return root;
            }
            catch
            {
                return url;
            }
        }

        private async Task NavigateFreshAsync(string url)
        {
            try
            {
                AppLogger.Log(LogTag, $"NavigateFreshAsync – url: {url}");
                if (IsLoginStartUrl(url) && !_globalClearedOnce)
                {
                    AppLogger.Log(LogTag, "LoginStart detectado, limpiando estado web (cookies+storage+cache).");
                    await ClearWebStateAsync(includeCookies: true, includeStorage: true, includeCache: true);
                    _globalClearedOnce = true;
                    Preferences.Set(PrefGlobalClearedKey, true); // *** CAMBIO: uso constante
                    _clearedForStart = true;
                }

                MyWebView.Source = url;
            }
            catch (Exception ex)
            {
                AppLogger.Log(LogTag, $"NavigateFreshAsync error: {ex}");
                MyWebView.Source = url;
            }
        }

        private static bool LooksLoginPath(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;

            try
            {
                var u = new Uri(url, UriKind.Absolute);
                var path = (u.AbsolutePath ?? string.Empty).ToLowerInvariant();
                return path.StartsWith("/iniciar-sesion") || path.StartsWith("/registrar");
            }
            catch
            {
                var lower = url.ToLowerInvariant();
                return lower.Contains("/iniciar-sesion") || lower.Contains("/registrar");
            }
        }

        private static bool HasSavedToken()
        {
            var saved = Preferences.Get("web_jwt", null);
            var has = !string.IsNullOrEmpty(saved);
            AppLogger.Log("WebViewPage", $"HasSavedToken -> {has} (len={saved?.Length ?? 0})");
            return has;
        }

        private static bool IsLoginStartUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;

            var hasToken = HasSavedToken();

            try
            {
                var u = new Uri(url, UriKind.Absolute);
                var path = (u.AbsolutePath ?? string.Empty).ToLowerInvariant();
                var query = (u.Query ?? string.Empty).ToLowerInvariant();

                bool looksCallback = query.Contains("code=") ||
                                     query.Contains("id_token=") ||
                                     query.Contains("access_token=");

                bool looksStartPath = path.StartsWith("/iniciar-sesion") ||
                                      path.StartsWith("/registrar");

                // Inicio de login SOLO si:
                //  - es path de login/registro
                //  - no es callback con code/tokens
                //  - y aún NO tenemos token guardado
                return looksStartPath && !looksCallback && !hasToken;
            }
            catch
            {
                var lower = url.ToLowerInvariant();

                bool looksCallback = lower.Contains("code=") ||
                                     lower.Contains("id_token=") ||
                                     lower.Contains("access_token=");

                bool looksStartPath = lower.Contains("/iniciar-sesion") ||
                                      lower.Contains("/registrar");

                return looksStartPath && !looksCallback && !hasToken;
            }
        }

        private async Task ClearWebStateAsync(bool includeCookies, bool includeStorage, bool includeCache)
        {
            try
            {
                AppLogger.Log(LogTag, $"ClearWebStateAsync(cookies={includeCookies}, storage={includeStorage}, cache={includeCache})");
                // limpiamos también preferencias de token/proveedor
                Preferences.Remove("web_jwt");
                Preferences.Remove("web_provider");
                Preferences.Remove(PrefLastUrlKey);
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
                        AppLogger.Log(LogTag, $"Clear cookies warning: {exCookies.Message}");
                    }
                }

                if (includeStorage)
                {
                    try { WebStorage.Instance?.DeleteAllData(); }
                    catch (Exception exStorage)
                    {
                        AppLogger.Log(LogTag, $"Clear WebStorage warning: {exStorage.Message}");
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
                        AppLogger.Log(LogTag, $"Clear cache/history warning: {exCache.Message}");
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
                                AppLogger.Log(LogTag, $"RemoveData callback error: {exCb}");
                                tcs.TrySetResult(false);
                            }
                        });
                        await tcs.Task.ConfigureAwait(false);
                    }
                    catch (Exception exIos)
                    {
                        AppLogger.Log(LogTag, $"iOS/MacCatalyst clear failed: {exIos}");
                    }
                }
#endif
            }
            catch (Exception ex)
            {
                AppLogger.Log(LogTag, $"ClearWebStateAsync error: {ex}");
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
                    AppLogger.Log(LogTag, $"LoadCallbackUrl error: {ex}");
                }
            });
        }

        private async void OnWebViewNavigating(object sender, WebNavigatingEventArgs e)
        {
            try
            {
                AppLogger.Log(LogTag, $" Navigating -> {e?.Url}");
                var next = e?.Url?.Trim() ?? string.Empty;

                if (!string.IsNullOrEmpty(next))
                {
                    var looksLogin = LooksLoginPath(next);
                    var hasToken = HasSavedToken();
                    var lastUrl = Preferences.Get(PrefLastUrlKey, null);

                    // Considero que hubo sesión real si alguna vez navegué a una URL NO login
                    var hadSession = !string.IsNullOrWhiteSpace(lastUrl) && !LooksLoginPath(lastUrl);

                    // caso rotación / reanudación con sesión abierta
    
                    if (looksLogin && hasToken && string.IsNullOrWhiteSpace(lastUrl))
                    {
                        var root = GetRootUrl(next);
                        AppLogger.Log(LogTag,
                            $"[NAV] Login+token pero SIN lastUrl previa -> asumo reanudación de sesión, redirijo a root: {root}");
                        e.Cancel = true;
                        MyWebView.Source = root;
                        return;
                    }

                    // si el servidor nos manda a login teniendo token guardado,
                    // asumimos que ese token ya no sirve y limpiamos estado local
                    // SOLO si sabemos que antes estuvimos dentro (hadSession = true)
                    if (looksLogin && hasToken)
                    {
                        if (hadSession)
                        {
                            AppLogger.Log(LogTag,
                                $"[NAV] Detectado login teniendo JWT guardado y lastUrl interna={lastUrl} -> reseteo prefs (token/provider/lastUrl) para permitir login limpio.");
                            Preferences.Remove("web_jwt");
                            Preferences.Remove("web_provider");
                            Preferences.Remove(PrefLastUrlKey);
                            hasToken = false;
                        }
                        else
                        {
                            AppLogger.Log(LogTag,
                                $"[NAV] Login con token pero SIN lastUrl interna (lastUrl={lastUrl ?? "<null>"}). No limpio prefs para no romper sesión inicial/SPA.");
                        }
                    }

                    // filtro también proveedores externos ---
                    var isProviderUrl = false;
                    if (Uri.TryCreate(next, UriKind.Absolute, out var parsed))
                    {
                        var host = (parsed.Host ?? string.Empty).ToLowerInvariant();
                        isProviderUrl =
                            host.Contains("accounts.google.") ||
                            host.Contains("appleid.apple.") ||
                            host.Contains("identity.apple.com") ||
                            host.Contains("login.microsoftonline.") ||
                            host.Contains("login.live.");
                    }

                    // Solo guardo lastUrl si:
                    //   - NO es path de login, o
                    //   - SÍ es path de login pero ya hay token (SPA que sigue en /iniciar-sesion)
                    //   - Y además NO es navegación a proveedor externo
                    if ((!looksLogin || hasToken) && !isProviderUrl)
                    {
                        Preferences.Set(PrefLastUrlKey, next);
                        AppLogger.Log(LogTag,
                            $"[NAV] Guardando lastUrl = {next} (looksLogin={looksLogin}, hasToken={hasToken}, isProviderUrl={isProviderUrl})");
                    }
                    else
                    {
                        AppLogger.Log(LogTag,
                            $"[NAV] NO guardo lastUrl (looksLogin={looksLogin}, hasToken={hasToken}, isProviderUrl={isProviderUrl}) url={next}");
                    }
                }

                if (IsLoginStartUrl(next) && !_globalClearedOnce)
                {
                    e.Cancel = true;
                    await ClearWebStateAsync(true, true, true);
                    _globalClearedOnce = true;
                    Preferences.Set(PrefGlobalClearedKey, true);
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
                            AppLogger.Log(LogTag, $" set provider JS failed: {exJs}");
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
                AppLogger.Log(LogTag, $"Error en OnWebViewNavigating: {ex}");
            }
        }

        private async void OnWebViewNavigated(object sender, WebNavigatedEventArgs e)
        {
            AppLogger.Log(LogTag, $"Navigated -> {e?.Url}, Result={e.Result}");

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

            var currentUrl = e?.Url ?? string.Empty;
            var hasToken = HasSavedToken();
            AppLogger.Log(LogTag, $"OnWebViewNavigated currentUrl={currentUrl}, hasToken={hasToken}");
            // Tratar de leer token desde storage del sitio y pasarlo a jsBridge.OnToken
            const string syncTokenJs = @"
                (function(){
                    try{
                    var keys = ['access_token','token','jwt','id_token'];
                    var found = null;

                    for (var i = 0; i < keys.length; i++) {
                        var k = keys[i];
                        var v = null;
                        try {
                        if (window.localStorage)  v = v || window.localStorage.getItem(k);
                        if (window.sessionStorage) v = v || window.sessionStorage.getItem(k);
                        } catch(_){}
                        if (v) { found = v; break; }
                    }

                    if (found && window.jsBridge && typeof window.jsBridge.OnToken === 'function') {
                        window.jsBridge.OnToken(found);
                    }
                    } catch(e) {
                    try {
                        if (window.jsBridge && typeof window.jsBridge.Dbg === 'function') {
                        window.jsBridge.Dbg('syncToken error: ' + e);
                        }
                    } catch(_){}
                    }
                })();";
            await EvalJsAsync(syncTokenJs, "syncToken");

            if (hasToken)
            {
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
                    AppLogger.Log(LogTag, "OnWebViewNavigated -> token reinyectado en la WebView.");
                }
                else
                {
                    AppLogger.Log(LogTag, "OnWebViewNavigated -> hasToken=true pero saved JWT vacío.");
                }
            }
            else
            {
                AppLogger.Log(LogTag, "OnWebViewNavigated -> no hay JWT guardado, no reinyecto.");
            }
        }

        async Task EvalJsAsync(string js, string tag)
        {
            try { await MyWebView.EvaluateJavaScriptAsync(js); }
            catch (Exception ex) { AppLogger.Log(LogTag, $"[{tag}] {ex}"); }
        }

        private static string EscapeForJs(string s)
            => (s ?? string.Empty)
               .Replace(@"\", @"\\")
               .Replace("'", @"\'")
               .Replace("\r", "")
               .Replace("\n", "");
    }
}
