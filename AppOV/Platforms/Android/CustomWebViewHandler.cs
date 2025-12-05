#if ANDROID
using global::Android.Content;
using global::Android.OS;
using global::Android.Util;
using global::Android.Webkit;
using Java.Interop;
using Microsoft.Maui;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Storage;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace AppOV.Platforms.Android
{
    public class CustomWebViewHandler : WebViewHandler
    {
        public static new IPropertyMapper<Microsoft.Maui.Controls.WebView, CustomWebViewHandler> Mapper =
            new PropertyMapper<Microsoft.Maui.Controls.WebView, CustomWebViewHandler>(WebViewHandler.Mapper);

        static CustomWebViewHandler()
        {
            Mapper.AppendToMapping("EnsureClientsAfterMap", (handler, view) =>
            {
                handler?.EnsureClients();
            });
        }

        AuthAwareClient? _client;
        AuthChromeClient? _chrome;

        // --------- JS HOOKS TEMPRANOS (se inyectan en OnPageStarted y también en popups) ----------
        static readonly string EarlyHooksJs = @"
        (function(){
            if (window.__earlyHooksInstalled) return; window.__earlyHooksInstalled = true;

            function log(m){ try { window.jsBridge && window.jsBridge.Dbg && window.jsBridge.Dbg(m); } catch(_){ } }

            // === HOOKS PARA TOKEN EN localStorage/sessionStorage ===
            function __installTokenStorageHooks(){
            try{
                var keys = ['access_token','token','jwt','id_token'];

                function isTokenKey(k){
                if (!k) return false;
                k = String(k).toLowerCase();
                for (var i=0; i<keys.length; i++){
                    if (k === keys[i]) return true;
                }
                return false;
                }

                var ls = window.localStorage;
                if (ls && !ls.__nativeTokenPatched){
                var origSet = ls.setItem;
                ls.setItem = function(k, v){
                    try{
                    if (isTokenKey(k) && window.jsBridge && typeof window.jsBridge.OnToken === 'function'){
                        window.jsBridge.OnToken(v);
                        log('localStorage.setItem token len=' + (v ? String(v).length : 0));
                    }
                    }catch(e){ log('localStorage.setItem hook error: ' + e); }
                    return origSet.apply(this, arguments);
                };
                ls.__nativeTokenPatched = true;
                }

                var ss = window.sessionStorage;
                if (ss && !ss.__nativeTokenPatched){
                var origSetS = ss.setItem;
                ss.setItem = function(k, v){
                    try{
                    if (isTokenKey(k) && window.jsBridge && typeof window.jsBridge.OnToken === 'function'){
                        window.jsBridge.OnToken(v);
                        log('sessionStorage.setItem token len=' + (v ? String(v).length : 0));
                    }
                    }catch(e){ log('sessionStorage.setItem hook error: ' + e); }
                    return origSetS.apply(this, arguments);
                };
                ss.__nativeTokenPatched = true;
                }
            }catch(e){
                log('installTokenStorageHooks error: ' + e);
            }
            }

            function __syncExistingToken(){
            try{
                var keys = ['access_token','token','jwt','id_token'];
                var found = null;

                for (var i=0; i<keys.length; i++){
                var k = keys[i];
                var v = null;
                try{
                    if (window.localStorage)  v = v || window.localStorage.getItem(k);
                    if (window.sessionStorage) v = v || window.sessionStorage.getItem(k);
                }catch(_){}

                if (v){
                    found = v;
                    break;
                }
                }

                if (found && window.jsBridge && typeof window.jsBridge.OnToken === 'function'){
                window.jsBridge.OnToken(found);
                log('syncExistingToken -> token len=' + (found ? String(found).length : 0));
                }
            }catch(e){
                log('syncExistingToken error: ' + e);
            }
            }

            try{
            __installTokenStorageHooks();
            __syncExistingToken();
            }catch(e){
            log('token hooks init error: ' + e);
            }

            // Registro de blobs y prebuffer a dataURL
            window.__blobRegistry = window.__blobRegistry || {};
            document.addEventListener('click', function(ev){
            try {
                var t = ev.target;
                window.__lastClickText = (t && (t.innerText || t.textContent) || '').toLowerCase();
            } catch(e){}
            }, true);

            (function(){
            var _origCreate = URL.createObjectURL;
            var _origRevoke = URL.revokeObjectURL;

            URL.createObjectURL = function(obj){
                var url = _origCreate.call(this, obj);
                try {
                var entry = {
                    blob: obj, type: (obj && obj.type) || '', size: (obj && obj.size) || 0,
                    revoked: false, dataUrl: null
                };
                window.__blobRegistry[url] = entry;
                // prebuffer inmediato
                try {
                    var fr = new FileReader();
                    fr.onloadend = function(){ try{ entry.dataUrl = fr.result; }catch(_){ } };
                    fr.readAsDataURL(obj);
                } catch(_){}
                } catch(_){}
                return url;
            };

            URL.revokeObjectURL = function(u){
                try {
                if (window.__blobRegistry && window.__blobRegistry[u]) window.__blobRegistry[u].revoked = true;
                } catch(_){}
                // diferimos la revocación real para evitar ERR_FILE_NOT_FOUND
                try { var self=this; setTimeout(function(){ try{ _origRevoke.call(self, u); }catch(_){ } }, 5000); }
                catch(_){ try{ _origRevoke.call(this,u); }catch(e){} }
            };
            })();

            async function blobToDataUrl(blob){
            return await new Promise(function(res){
                var fr = new FileReader();
                fr.onloadend = function(){ res(fr.result); };
                fr.readAsDataURL(blob);
            });
            }

            async function blobUrlToDataUrl(blobUrl){
            try{
                var meta = (window.__blobRegistry && window.__blobRegistry[blobUrl]) || null;
                if (meta){
                if (meta.dataUrl) return meta.dataUrl;
                if (meta.blob) return await blobToDataUrl(meta.blob);
                }
                // último recurso (puede fallar si ya fue revocado, pero intentamos igual)
                var b = await (await fetch(blobUrl)).blob();
                return await blobToDataUrl(b);
            }catch(e){ log('blobUrlToDataUrl error: ' + e); return null; }
            }

            window.__nativeDownload = async function(input, suggestedName){
            try{
                var name = suggestedName || '';
                if (typeof Blob !== 'undefined' && input instanceof Blob){
                var du = await blobToDataUrl(input);
                return sendToNative(du, name || 'download.csv');
                }
                if (typeof input === 'string' && input.indexOf('blob:') === 0){
                var dataUrl = await blobUrlToDataUrl(input);
                if (dataUrl) return sendToNative(dataUrl, name || 'download.csv');
                return false;
                }
                if (typeof input === 'string' && input.indexOf('data:') === 0){
                return sendToNative(input, name || 'download.csv');
                }
            }catch(e){ log('__nativeDownload error: ' + e); }
            return false;
            };

            function sendToNative(dataUrl, fileName){
            if (!fileName) fileName = 'download.bin';
            try{
                if (window.jsBridge && typeof window.jsBridge.saveBase64 === 'function'){
                log('[DL] sendToNative -> ' + fileName + ' len=' + (dataUrl ? dataUrl.length : 0));
                window.jsBridge.saveBase64(dataUrl, fileName);
                return true;
                }
                if (window.webkit && window.webkit.messageHandlers && window.webkit.messageHandlers.jsBridge){
                window.webkit.messageHandlers.jsBridge.postMessage({ data: dataUrl, fileName: fileName });
                return true;
                }
            }catch(e){ log('sendToNative error: ' + e); }
            return false;
            }

            // Intercepta anchors y programático
            document.addEventListener('click', function(ev){
            try{
                var a = ev.target && ev.target.closest && ev.target.closest('a');
                if (!a) return;
                var href = a.getAttribute('href') || '';
                var dn = a.getAttribute('download') || '';
                if (!href) return;
                if (href.indexOf('blob:') === 0 || href.indexOf('data:') === 0){
                ev.preventDefault(); ev.stopPropagation();
                window.__nativeDownload(href, dn || guessNameFromMeta(href));
                }
            }catch(e){ log('click handler error: ' + e); }
            }, true);

            try{
            var origClick = HTMLAnchorElement.prototype.click;
            HTMLAnchorElement.prototype.click = function(){
                var href = this.getAttribute('href') || '';
                var dn = this.getAttribute('download') || '';
                if (href && (href.indexOf('blob:') === 0 || href.indexOf('data:') === 0)){
                return void window.__nativeDownload(href, dn || guessNameFromMeta(href));
                }
                return origClick.call(this);
            };
            }catch(e){}

            // Guard top-nav a blob:/data:
            (function(){
            function handle(url, dn){
                try{
                if (!url) return false;
                var u = String(url);
                if (u.indexOf('blob:') === 0 || u.indexOf('data:') === 0){
                    window.__nativeDownload(u, dn || guessNameFromMeta(u));
                    return true;
                }
                }catch(e){}
                return false;
            }
            try{
                var _open = window.open;
                window.open = function(u){ if (handle(u,'')) return null; return _open.apply(this, arguments); };
            }catch(_){}
            try{
                var _assign = window.location.assign.bind(window.location);
                window.location.assign = function(u){ if (handle(u,'')) return; return _assign(u); };
                var _replace = window.location.replace.bind(window.location);
                window.location.replace = function(u){ if (handle(u,'')) return; return _replace(u); };
                var desc = Object.getOwnPropertyDescriptor(Location.prototype, 'href');
                if (desc && desc.set && desc.configurable){
                Object.defineProperty(Location.prototype, 'href', {
                    get: desc.get,
                    set: function(u){ if (handle(u,'')) return; return desc.set.call(this, u); },
                    configurable: true,
                    enumerable: desc.enumerable
                });
                }
            }catch(_){}
            })();

            // Hook saveAs / msSaveOrOpenBlob
            (function(){
            function interceptSaveAs(){
                try{
                var original = window.saveAs;
                window.saveAs = function(blob, name){
                    try{
                    if (typeof window.__nativeDownload === 'function' && blob){
                        return window.__nativeDownload(blob, name || 'download.csv');
                    }
                    }catch(e){}
                    if (original) return original.apply(this, arguments);
                    return false;
                };
                }catch(_){}
            }
            function interceptMsSave(){
                try{
                if (window.navigator && typeof window.navigator.msSaveOrOpenBlob === 'function'){
                    var orig = window.navigator.msSaveOrOpenBlob;
                    window.navigator.msSaveOrOpenBlob = function(blob, name){
                    try{
                        if (typeof window.__nativeDownload === 'function' && blob){
                        return window.__nativeDownload(blob, name || 'download.csv');
                        }
                    }catch(e){}
                    return orig.call(window.navigator, blob, name);
                    };
                }
                }catch(_){}
            }
            interceptSaveAs();
            interceptMsSave();
            try{
                var _d = Object.getOwnPropertyDescriptor(window, 'saveAs');
                if (!_d || _d.configurable){
                var _val = window.saveAs;
                Object.defineProperty(window, 'saveAs', {
                    get: function(){ return _val; },
                    set: function(v){ _val = v; try{ interceptSaveAs(); }catch(_){} },
                    configurable: true,
                    enumerable: true
                });
                }
            }catch(_){}
            })();

            function guessNameFromMeta(href){
            try{
                var meta = (window.__blobRegistry && window.__blobRegistry[href]) || null;
                var last = (window.__lastClickText || '').toLowerCase();
                if (meta && meta.type && meta.type.indexOf('spreadsheetml')>=0) return 'export.xlsx';
                if (meta && meta.type && meta.type.indexOf('csv')>=0) return 'export.csv';
                if (last.includes('xlsx')) return 'export.xlsx';
                if (last.includes('csv')) return 'export.csv';
            }catch(_){}
            return 'download.csv';
            }
        })();";


        protected override global::Android.Webkit.WebView CreatePlatformView()
        {
            var v = base.CreatePlatformView();
            Log.Debug("CWH", "CreatePlatformView()");
            return v;
        }

        protected override void ConnectHandler(global::Android.Webkit.WebView platformView)
        {
            Log.Debug("CWH", "ConnectHandler(START)");
            base.ConnectHandler(platformView);

            try { global::Android.Webkit.WebView.SetWebContentsDebuggingEnabled(true); } catch { }
            Log.Debug("CWH", "ConnectHandler(END) debugging ON");

            var s = platformView.Settings;
            s.JavaScriptEnabled = true;
            s.JavaScriptCanOpenWindowsAutomatically = true;
            s.DomStorageEnabled = true;
            s.BuiltInZoomControls = false;
            s.DisplayZoomControls = false;
            s.SetSupportZoom(false);
            s.UseWideViewPort = false;
            s.LoadWithOverviewMode = false;
            s.MixedContentMode = MixedContentHandling.AlwaysAllow;
            s.SetSupportMultipleWindows(true);

            try
            {
                CookieManager.Instance.SetAcceptCookie(true);
                CookieManager.Instance.SetAcceptThirdPartyCookies(platformView, true);
            }
            catch { }

            platformView.SetDownloadListener(new DownloadListener(Context, platformView));
            platformView.AddJavascriptInterface(new JsBridge(Context), "jsBridge");

            EnsureClients();
            Log.Debug("CustomWebViewHandler", "ConnectHandler: clientes fijados y debugging ON");
        }

        protected override void DisconnectHandler(global::Android.Webkit.WebView platformView)
        {
            try
            {
                platformView.SetWebViewClient(null);
                platformView.SetWebChromeClient(null);
            }
            catch { }
            base.DisconnectHandler(platformView);
        }

        void EnsureClients()
        {
            var pv = PlatformView;
            if (pv == null) return;

            if (_client == null) _client = new AuthAwareClient();
            if (_chrome == null) _chrome = new AuthChromeClient(_client, Context);

            pv.SetWebViewClient(_client);
            pv.SetWebChromeClient(_chrome);
        }

        // ========= helpers JS generados desde C# =========

        static string JsFromBlobUrl(string blobUrl)
        {
            var safe = (blobUrl ?? string.Empty)
                .Replace("\\", "\\\\")
                .Replace("'", "\\'");

            const string script =
"(async function(){\n" +
"  try{\n" +
"    var blobUrl = '__BLOB_URL__';\n" +
"    var meta = (window.__blobRegistry && window.__blobRegistry[blobUrl]) || null;\n" +
"    var dataUrl = null;\n" +
"    if (meta && meta.dataUrl) dataUrl = meta.dataUrl;\n" +
"    else if (meta && meta.blob){\n" +
"      dataUrl = await new Promise(function(res){ var fr=new FileReader(); fr.onloadend=function(){res(fr.result);}; fr.readAsDataURL(meta.blob); });\n" +
"    } else {\n" +
"      try { var b = await (await fetch(blobUrl)).blob(); dataUrl = await new Promise(function(res){ var fr=new FileReader(); fr.onloadend=function(){res(fr.result);}; fr.readAsDataURL(b); }); }\n" +
"      catch(e){ try{ window.jsBridge && window.jsBridge.Dbg && window.jsBridge.Dbg('blob->native error: '+e); }catch(_){ } }\n" +
"    }\n" +
"    var name = 'download.csv';\n" +
"    try{ var last=(window.__lastClickText||'').toLowerCase(); if (meta && meta.type && meta.type.indexOf('spreadsheetml')>=0) name='export.xlsx'; else if (meta && meta.type && meta.type.indexOf('csv')>=0) name='export.csv'; else if (last.includes('xlsx')) name='export.xlsx'; else if (last.includes('csv')) name='export.csv'; }catch(_){ }\n" +
"    if (dataUrl){ if (window.jsBridge && window.jsBridge.saveBase64) window.jsBridge.saveBase64(dataUrl, name); else if(window.webkit && window.webkit.messageHandlers && window.webkit.messageHandlers.jsBridge){ window.webkit.messageHandlers.jsBridge.postMessage({ data: dataUrl, fileName: name }); } }\n" +
"  } catch(e) { try{ window.jsBridge && window.jsBridge.Dbg && window.jsBridge.Dbg('blob->native error: ' + e); }catch(_){ } }\n" +
"})();";

            return script.Replace("__BLOB_URL__", safe);
        }

        static string JsFromDataUrl(string dataUrl)
        {
            var safe = Uri.EscapeDataString(dataUrl ?? string.Empty);
            const string script =
"(function(){ try{ var s=decodeURIComponent('__DATA__'); var name=((window.__lastClickText||'').indexOf('csv')>=0)?'export.csv':'download.bin'; if(window.jsBridge && window.jsBridge.saveBase64) window.jsBridge.saveBase64(s,name); else if(window.webkit && window.webkit.messageHandlers && window.webkit.messageHandlers.jsBridge){ window.webkit.messageHandlers.jsBridge.postMessage({ data:s, fileName:name }); } }catch(e){ try{ window.jsBridge && window.jsBridge.Dbg && window.jsBridge.Dbg('data->native error: '+e);}catch(_){ } }})();";
            return script.Replace("__DATA__", safe);
        }

        // ------------------- WebViewClient -------------------
        public class AuthAwareClient : WebViewClient
        {
            internal static void Notify(string msg)
            {
                try
                {
                    Log.Debug("AuthAwareClient", msg);
                    var ctx = Platform.CurrentActivity ?? (global::Android.App.Application.Context as Context);
                    if (ctx != null)
                        global::Android.Widget.Toast.MakeText(ctx, msg, global::Android.Widget.ToastLength.Short)?.Show();
                }
                catch { }
            }

            internal static bool IsHttp(string? u) =>
                !string.IsNullOrEmpty(u) &&
                (u!.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                 u.StartsWith("https://", StringComparison.OrdinalIgnoreCase));

            internal static string HostOf(string url) { try { return new Uri(url).Host.ToLowerInvariant(); } catch { return ""; } }

            internal static bool IsOwnDomain(string url)
            {
                var h = HostOf(url);
                return h == "edelap.ovqa.storey.com.ar" ||
                       h == "edelap.ovdev.storey.com.ar" ||
                       h == "portalderecargaqa.cashpower.com.ar" ||
                       h == "portalderecarga.cashpower.com.ar" ||
                       h == "localhost" ||
                       h == "10.0.2.2" ||
                       h == "hemikaryotic-sanford-unmetallically.ngrok-free.dev";
            }

            internal static bool IsProviderHost(string url)
            {
                var h = HostOf(url);
                return h.Contains("accounts.google.com") ||
                       h.Contains("appleid.apple.com") ||
                       h.Contains("identity.apple.com") ||
                       h.Contains("login.microsoftonline.com") ||
                       h.Contains("login.live.com");
            }

            internal static bool LooksLikeAuthEndpoint(string url)
            {
                if (string.IsNullOrWhiteSpace(url)) return false;
                var lower = url.ToLowerInvariant();

                if (lower.Contains("client_id=") || lower.Contains("redirect_uri=") || lower.Contains("response_type="))
                    return true;

                if (lower.Contains("/oauth") || lower.Contains("/o/oauth2/") || lower.Contains("/authorize") ||
                    lower.Contains("/signin") || lower.Contains("/sign-in") || lower.Contains("/login"))
                    return true;

                return false;
            }

            internal static bool ShouldOpenOutside(string url, bool isMain)
            {
                if (!IsHttp(url)) return false;

                if (IsProviderHost(url))
                {
                    if (IsIdpBackchannel(url)) return false;
                    return isMain || LooksLikeAuthEndpoint(url);
                }

                if (LooksLikeAuthEndpoint(url))
                    return isMain;

                return false;
            }

            internal static bool HandleBlobOrDataNavigation(global::Android.Webkit.WebView view, string url)
            {
                if (string.IsNullOrEmpty(url)) return false;

                if (url.StartsWith("blob:", StringComparison.OrdinalIgnoreCase))
                {
                    try { view.StopLoading(); } catch { }
                    view.Post(() => { try { view.EvaluateJavascript(JsFromBlobUrl(url), null); } catch { } });
                    Log.Debug("JSDBG", "[blob] handled natively (cancel + JS save)");
                    AppLogger.Log("JSDBG", "[blob] handled natively (cancel + JS save)");
                    return true;
                }

                if (url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                {
                    try { view.StopLoading(); } catch { }
                    view.Post(() => { try { view.EvaluateJavascript(JsFromDataUrl(url), null); } catch { } });
                    Log.Debug("JSDBG", "[data] handled natively (cancel + JS save)");
                    AppLogger.Log("JSDBG", "[blob] handled natively (cancel + JS save)");
                    return true;
                }

                return false;
            }

            internal static void OpenExtern(string url)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    try
                    {
                        var ctx = Platform.CurrentActivity ?? (global::Android.App.Application.Context as Context);
                        if (ctx is null) { Log.Error("AuthAwareClient", "[OPEN-EXTERNAL] Context nulo"); return; }
                        try { global::Android.Widget.Toast.MakeText(ctx, "Abriendo en navegador…", global::Android.Widget.ToastLength.Short).Show(); } catch { }
                        Log.Debug("AuthAwareClient", $"[OPEN-EXTERNAL] {url}");
                        var uri = global::Android.Net.Uri.Parse(url);
                        var intent = new Intent(Intent.ActionView, uri);
                        intent.AddCategory(Intent.CategoryBrowsable);
                        intent.AddFlags(ActivityFlags.NewTask);
                        ctx.StartActivity(intent);
                    }
                    catch (Exception ex)
                    {
                        Log.Error("AuthAwareClient", $"[OPEN-EXTERNAL][FALLBACK] {ex}");
                        _ = Browser.OpenAsync(new Uri(url), BrowserLaunchMode.External);
                    }
                });
            }

            static bool TryHandleIntentScheme(string url)
            {
                if (string.IsNullOrWhiteSpace(url) || !url.StartsWith("intent://", StringComparison.OrdinalIgnoreCase)) return false;
                try
                {
                    var lower = url.ToLowerInvariant();
                    var key = "s.browser_fallback_url=";
                    var idx = lower.IndexOf(key);
                    if (idx >= 0)
                    {
                        var raw = url.Substring(idx + key.Length);
                        var end = raw.IndexOfAny(new[] { ';', '#' });
                        var val = end >= 0 ? raw.Substring(0, end) : raw;
                        var decoded = Uri.UnescapeDataString(val);
                        if (IsHttp(decoded)) { Notify("[INTENT->FALLBACK] " + decoded); OpenExtern(decoded); return true; }
                    }
                    Notify("[INTENT->OPEN] " + url);
                    OpenExtern(url);
                    return true;
                }
                catch { return false; }
            }

            public override void OnPageStarted(global::Android.Webkit.WebView view, string url, global::Android.Graphics.Bitmap? favicon)
            {
                Log.Debug("AuthAwareClient", "[OnPageStarted] " + url);

                // **Inyección TEMPRANA** en cada comienzo de navegación (incluye iframes principales)
                try { view.EvaluateJavascript(EarlyHooksJs, null); } catch { }

                if (HandleBlobOrDataNavigation(view, url)) return;
                if (TryHandleIntentScheme(url)) { try { view.StopLoading(); } catch { } return; }
                if (ShouldOpenOutside(url, true)) { try { view.StopLoading(); } catch { } OpenExtern(url); return; }
                base.OnPageStarted(view, url, favicon);
            }

            public override bool ShouldOverrideUrlLoading(global::Android.Webkit.WebView view, IWebResourceRequest request)
            {
                var url = request?.Url?.ToString() ?? string.Empty;
                var isMain = request?.IsForMainFrame ?? false;
                Log.Debug("AuthAwareClient", "[Override(req)] " + url + " main=" + isMain);

                if (HandleBlobOrDataNavigation(view, url)) return true;
                if (TryHandleIntentScheme(url)) return true;
                if (ShouldOpenOutside(url, isMain)) { OpenExtern(url); return true; }
                return base.ShouldOverrideUrlLoading(view, request);
            }

            public override bool ShouldOverrideUrlLoading(global::Android.Webkit.WebView view, string url)
            {
                Log.Debug("AuthAwareClient", "[Override(str)] " + url);

                if (HandleBlobOrDataNavigation(view, url)) return true;
                if (TryHandleIntentScheme(url)) return true;
                if (ShouldOpenOutside(url, true)) { OpenExtern(url); return true; }
                return base.ShouldOverrideUrlLoading(view, url);
            }

            public override WebResourceResponse? ShouldInterceptRequest(global::Android.Webkit.WebView view, IWebResourceRequest request)
            {
                var url = request?.Url?.ToString() ?? string.Empty;
                var isMain = request?.IsForMainFrame ?? false;
                Log.Debug("AuthAwareClient", "[Intercept] " + url + " main=" + isMain);

                if (string.IsNullOrEmpty(url)) return base.ShouldInterceptRequest(view, request);

                if (url.StartsWith("intent://", StringComparison.OrdinalIgnoreCase))
                    if (TryHandleIntentScheme(url)) return MakeEmpty204();

                if (isMain)
                {
                    if (ShouldOpenOutside(url, true))
                    {
                        MainThread.BeginInvokeOnMainThread(() => { try { view.StopLoading(); } catch { } });
                        Notify("[INTERCEPT->OUTSIDE:MAIN] " + url);
                        OpenIdpOutsideOnce(url);
                        return MakeEmpty204();
                    }
                }
                else
                {
                    if (IsProviderHost(url) && !IsIdpBackchannel(url) && LooksLikeAuthEndpoint(url))
                    {
                        Notify("[INTERCEPT->OUTSIDE:SUB] " + url);
                        OpenIdpOutsideOnce(url);
                        return MakeEmpty204();
                    }
                }

                return base.ShouldInterceptRequest(view, request);
            }

            static WebResourceResponse MakeEmpty204()
            {
                try
                {
                    return new WebResourceResponse(
                        "text/plain", "utf-8",
                        204, "No Content",
                        new Dictionary<string, string>(),
                        new MemoryStream(Array.Empty<byte>()));
                }
                catch
                {
                    return new WebResourceResponse("text/plain", "utf-8", new MemoryStream(Array.Empty<byte>()));
                }
            }

            static volatile bool _openingExternal;
            internal static void OpenIdpOutsideOnce(string url)
            {
                if (_openingExternal) return;
                _openingExternal = true;
                OpenExtern(url);
                _ = Task.Run(async () => { try { await Task.Delay(1500); } catch { } _openingExternal = false; });
            }

            internal static bool IsGoogleIframeOrGsi(string lower) =>
                lower.Contains("/gsi/") || lower.Contains("iframerpc") ||
                lower.Contains("listaccounts") || lower.Contains("checkconnection") ||
                lower.Contains("checksession");

            internal static bool IsIdpBackchannel(string url)
            {
                if (string.IsNullOrWhiteSpace(url)) return false;
                var lower = url.ToLowerInvariant();

                if (lower.Contains("/.well-known/openid-configuration") ||
                    lower.Contains("/discovery") ||
                    lower.Contains("/jwks") || lower.Contains("/certs") ||
                    lower.Contains("/token") || lower.Contains("/userinfo"))
                    return true;

                if (lower.Contains("appleid.apple.com/auth/keys") ||
                    lower.Contains("appleid.apple.com/auth/token") ||
                    lower.Contains("appleid.apple.com/auth/revoke"))
                    return true;

                if (IsGoogleIframeOrGsi(lower)) return true;

                return false;
            }
        }

        // ------------------- WebChromeClient -------------------
        public class AuthChromeClient : WebChromeClient
        {
            readonly AuthAwareClient _helper;
            readonly Context _ctx;
            public AuthChromeClient(AuthAwareClient helper, Context ctx) { _helper = helper; _ctx = ctx; }

            public override bool OnConsoleMessage(global::Android.Webkit.ConsoleMessage consoleMessage)
            {
                try
                {
                    var msg = consoleMessage?.Message();
                    var src = consoleMessage?.SourceId();
                    var line = consoleMessage?.LineNumber() ?? 0;

                    string levelStr = "LOG";
                    try
                    {
                        var t = consoleMessage?.GetType();
                        var m = t?.GetMethod("MessageLevel");
                        if (m != null)
                        {
                            var val = m.Invoke(consoleMessage, null);
                            levelStr = val?.ToString() ?? "LOG";
                        }
                        else
                        {
                            var p = t?.GetProperty("MessageLevel");
                            if (p != null)
                            {
                                var val = p.GetValue(consoleMessage);
                                levelStr = val?.ToString() ?? "LOG";
                            }
                        }
                    }
                    catch { }

                    var composed = $"[console][{levelStr}] {msg} (src={src}:{line})";
                    AppLogger.Log("JSDBG", composed);
                    if (levelStr.Equals("ERROR", StringComparison.OrdinalIgnoreCase))
                        Log.Error("JSDBG", composed);
                    else
                        Log.Debug("JSDBG", composed);
                }
                catch { }
                return base.OnConsoleMessage(consoleMessage);
            }

            public override bool OnCreateWindow(global::Android.Webkit.WebView view, bool isDialog, bool isUserGesture, Message resultMsg)
            {
                try
                {
                    var transport = (global::Android.Webkit.WebView.WebViewTransport)resultMsg.Obj;
                    // WebView temporal PARA POPUPS, con los mismos ganchos/bridge
                    var temp = new global::Android.Webkit.WebView(view.Context!);
                    var s = temp.Settings;
                    s.JavaScriptEnabled = true;
                    s.DomStorageEnabled = true;
                    s.JavaScriptCanOpenWindowsAutomatically = true;
                    s.SetSupportMultipleWindows(true);

                    temp.AddJavascriptInterface(new JsBridge(_ctx), "jsBridge");
                    temp.SetDownloadListener(new DownloadListener(_ctx, temp));
                    temp.SetWebChromeClient(this); // hereda console logs y createWindow anidados

                    // Cliente temp que inyecta hooks tempranos
                    temp.SetWebViewClient(new TempClient());

                    transport.WebView = temp;
                    resultMsg.SendToTarget();
                    return true;
                }
                catch (Exception ex)
                {
                    Log.Error("AuthChromeClient", $"OnCreateWindow error: {ex}");
                    return false;
                }
            }

            sealed class TempClient : WebViewClient
            {
                public override void OnPageStarted(global::Android.Webkit.WebView view, string url, global::Android.Graphics.Bitmap? favicon)
                {
                    try { view.EvaluateJavascript(EarlyHooksJs, null); } catch { }
                    if (AuthAwareClient.HandleBlobOrDataNavigation(view, url)) return;

                    var tryIntent = typeof(AuthAwareClient).GetMethod("TryHandleIntentScheme", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
                    if ((bool)tryIntent.Invoke(null, new object[] { url })!) { try { view.StopLoading(); } catch { } return; }

                    if (AuthAwareClient.IsHttp(url) && !AuthAwareClient.IsOwnDomain(url) &&
                        (AuthAwareClient.IsProviderHost(url) || AuthAwareClient.LooksLikeAuthEndpoint(url)))
                    {
                        try { view.StopLoading(); } catch { }
                        AuthAwareClient.OpenExtern(url);
                        return;
                    }
                    base.OnPageStarted(view, url, favicon);
                }

                public override bool ShouldOverrideUrlLoading(global::Android.Webkit.WebView view, string url)
                {
                    if (AuthAwareClient.HandleBlobOrDataNavigation(view, url)) return true;

                    var tryIntent = typeof(AuthAwareClient).GetMethod("TryHandleIntentScheme", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
                    if ((bool)tryIntent.Invoke(null, new object[] { url })!) return true;

                    if (AuthAwareClient.IsHttp(url) && !AuthAwareClient.IsOwnDomain(url) &&
                        (AuthAwareClient.IsProviderHost(url) || AuthAwareClient.LooksLikeAuthEndpoint(url)))
                    {
                        AuthAwareClient.OpenExtern(url);
                        return true;
                    }
                    return base.ShouldOverrideUrlLoading(view, url);
                }

                public override bool ShouldOverrideUrlLoading(global::Android.Webkit.WebView view, IWebResourceRequest request)
                {
                    var url = request?.Url?.ToString() ?? string.Empty;
                    if (AuthAwareClient.HandleBlobOrDataNavigation(view, url)) return true;

                    var tryIntent = typeof(AuthAwareClient).GetMethod("TryHandleIntentScheme", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
                    if ((bool)tryIntent.Invoke(null, new object[] { url })!) return true;

                    if (AuthAwareClient.IsHttp(url) && !AuthAwareClient.IsOwnDomain(url) &&
                        (AuthAwareClient.IsProviderHost(url) || AuthAwareClient.LooksLikeAuthEndpoint(url)))
                    {
                        AuthAwareClient.OpenExtern(url);
                        return true;
                    }
                    return base.ShouldOverrideUrlLoading(view, request);
                }
            }
        }

        // ----------------- DownloadListener (fallback HTTP/DATA) -----------------
        class DownloadListener : Java.Lang.Object, IDownloadListener
        {
            readonly Context _ctx;
            readonly global::Android.Webkit.WebView _webView;

            public DownloadListener(Context context, global::Android.Webkit.WebView webView)
            {
                _ctx = context;
                _webView = webView;
            }

            public void OnDownloadStart(string url, string userAgent, string contentDisposition, string mimeType, long contentLength)
            {
                if (string.IsNullOrWhiteSpace(url)) return;

                if (url.StartsWith("blob:", StringComparison.OrdinalIgnoreCase))
                {
                    Log.Debug("JSDBG", "[DL] Blob reached DownloadListener (late) -> handling anyway");
                    AppLogger.Log("JSDBG", "[DL] Blob reached DownloadListener (late) -> handling anyway");
                    _webView.Post(() => _webView.EvaluateJavascript(JsFromBlobUrl(url), null));
                    return;
                }
                if (url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                {
                    _webView.Post(() => _webView.EvaluateJavascript(JsFromDataUrl(url), null));
                    return;
                }

                _ = HandleHttpDownloadAsync(url, contentDisposition, mimeType);
            }

            async Task HandleHttpDownloadAsync(string url, string contentDisposition, string mimeType)
            {
                try
                {
                    using var client = new HttpClient();
                    var data = await client.GetByteArrayAsync(url);

                    var fileName = System.IO.Path.GetFileName(new Uri(url).LocalPath);
                    if (string.IsNullOrEmpty(fileName))
                        fileName = ParseFileName(contentDisposition) ?? "download";

                    if (!System.IO.Path.HasExtension(fileName))
                    {
                        var ext = ExtFromMime(mimeType);
                        if (!string.IsNullOrEmpty(ext)) fileName += ext;
                    }

                    var path = System.IO.Path.Combine(_ctx.CacheDir.AbsolutePath, fileName);
                    File.WriteAllBytes(path, data);

                    await ShowOpenOrShareAsync(path, fileName, mimeType);
                }
                catch (Exception ex) { Log.Error("Download", ex.ToString()); }
            }
        }

        // ===== JS Bridge =====
        public sealed class JsBridge : Java.Lang.Object
        {
            private readonly Context _ctx;
            public JsBridge(Context ctx) => _ctx = ctx;

            [JavascriptInterface, Export("saveBase64")]
            public void saveBase64(string dataUrl, string fileName)
            {
                try
                {
                    string mimeFromHeader = "application/octet-stream";
                    byte[] bytes;

                    if (dataUrl.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                    {
                        var comma = dataUrl.IndexOf(',');
                        if (comma < 0) return;

                        var header = dataUrl.Substring(5, comma - 5);
                        var payload = dataUrl.Substring(comma + 1);

                        mimeFromHeader = header.Split(';')[0].Trim().ToLowerInvariant();
                        bool isBase64 = header.IndexOf(";base64", StringComparison.OrdinalIgnoreCase) >= 0;

                        bytes = isBase64
                            ? Convert.FromBase64String(payload)
                            : Encoding.UTF8.GetBytes(Uri.UnescapeDataString(payload));
                    }
                    else
                    {
                        bytes = Convert.FromBase64String(dataUrl);
                    }

                    if (!System.IO.Path.HasExtension(fileName))
                    {
                        var ext = CustomWebViewHandler.ExtFromMime(mimeFromHeader);
                        if (!string.IsNullOrEmpty(ext)) fileName += ext;
                        else if (string.Equals(mimeFromHeader, "text/csv", StringComparison.OrdinalIgnoreCase)) fileName += ".csv";
                    }

                    var path = System.IO.Path.Combine(_ctx.CacheDir.AbsolutePath, fileName);
                    File.WriteAllBytes(path, bytes);

                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        _ = CustomWebViewHandler.ShowOpenOrShareAsync(path, fileName, mimeFromHeader);
                    });
                }
                catch (Exception ex)
                {
                    Log.Error("JsBridge", ex.ToString());
                    MainThread.BeginInvokeOnMainThread(async () =>
                    {
                        await Application.Current?.MainPage?.DisplayAlert("Error al descargar", "No se pudo procesar el archivo.", "OK");
                    });
                }
            }

            [JavascriptInterface, Export("dbg")]
            public void Dbg(string msg)
            {
                try {
                    AppLogger.Log("JSDBG", msg ?? "");
                    Log.Debug("JSDBG", msg ?? ""); } catch { }
            }

            [JavascriptInterface, Export("Dbg")]
            public void DbgPascal(string msg) => Dbg(msg);

            [JavascriptInterface, Export("OnToken")]
            public void OnToken(string tokenRawOrJson)
            {
                try
                {
                    var token = TryExtractToken(tokenRawOrJson);
                    if (!string.IsNullOrEmpty(token))
                        Preferences.Set("web_jwt", token);
                    AppLogger.Log("AuthAwareClient", $"JWT guardado en Preferences (len={token?.Length ?? 0})");
                }
                catch (Exception ex) { AppLogger.Log("JsBridge", $"OnToken error: {ex}"); }
            }

            [JavascriptInterface, Export("OpenExternal")]
            public void OpenExternal(string url)
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(url)) return;

                    var ctx = Platform.CurrentActivity ?? (global::Android.App.Application.Context as Context);
                    if (ctx != null)
                    {
                        var uri = global::Android.Net.Uri.Parse(url);
                        var intent = new Intent(Intent.ActionView, uri);
                        intent.AddFlags(ActivityFlags.NewTask);
                        intent.AddCategory(Intent.CategoryBrowsable);
                        ctx.StartActivity(intent);
                        return;
                    }

                    MainThread.BeginInvokeOnMainThread(async () =>
                    {
                        await Browser.OpenAsync(new Uri(url), BrowserLaunchMode.External);
                    });
                }
                catch (Exception ex)
                {
                    Log.Error("JsBridge", $"OpenExternal error: {ex}");
                }
            }

            static string? TryExtractToken(string? tokenRawOrJson)
            {
                if (string.IsNullOrWhiteSpace(tokenRawOrJson)) return null;
                var s = tokenRawOrJson.Trim();

                if ((s.StartsWith("{") && s.EndsWith("}")) || (s.StartsWith("[") && s.EndsWith("]")))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(s);
                        var root = doc.RootElement;
                        string? pick(params string[] names)
                        {
                            foreach (var n in names)
                                if (root.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String)
                                    return v.GetString();
                            return null;
                        }
                        return pick("jwt", "token", "id_token", "access_token") ?? s;
                    }
                    catch { }
                }
                return s;
            }
        }

        public static string? ParseFileName(string? contentDisposition)
        {
            if (string.IsNullOrWhiteSpace(contentDisposition)) return null;
            const string key = "filename=";
            var idx = contentDisposition.IndexOf(key, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return null;

            var filename = contentDisposition[(idx + key.Length)..].Trim();
            if (filename.Length > 1 && filename[0] == '"')
            {
                var end = filename.IndexOf('"', 1);
                if (end > 1) return filename.Substring(1, end - 1);
            }
            var semi = filename.IndexOf(';');
            if (semi > 0) return filename[..semi];
            return filename;
        }

        public static string? ExtFromMime(string? mime)
        {
            if (string.IsNullOrWhiteSpace(mime)) return null;
            mime = mime.ToLowerInvariant();
            return mime switch
            {
                "text/csv" => ".csv",
                "application/pdf" => ".pdf",
                "image/png" => ".png",
                "image/jpeg" or "image/jpg" => ".jpg",
                "application/vnd.ms-excel" => ".xls",
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" => ".xlsx",
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document" => ".docx",
                "application/msword" => ".doc",
                _ => null
            };
        }

        static string GuessMimeFromExtension(string fileName, string? fallback = null)
        {
            try
            {
                var ext = System.IO.Path.GetExtension(fileName)?.TrimStart('.').ToLowerInvariant();
                if (!string.IsNullOrEmpty(ext))
                {
                    var mt = MimeTypeMap.Singleton;
                    var mime = mt.GetMimeTypeFromExtension(ext);
                    if (!string.IsNullOrEmpty(mime)) return mime;
                }
            }
            catch { }
            return fallback ?? "*/*";
        }

        public static async Task ShowOpenOrShareAsync(string path, string fileName, string? contentType)
        {
            contentType ??= GuessMimeFromExtension(fileName, "*/*");

            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                var choice = await Application.Current!.MainPage!.DisplayActionSheet(
                    "Archivo descargado", "Cancelar", null, "Abrir", "Compartir");

                if (choice == "Abrir")
                {
                    await Launcher.Default.OpenAsync(new OpenFileRequest
                    {
                        Title = fileName,
                        File = new ReadOnlyFile(path, contentType)
                    });
                }
                else if (choice == "Compartir")
                {
                    await Share.RequestAsync(new ShareFileRequest
                    {
                        Title = fileName,
                        File = new ShareFile(path)
                    });
                }
            });
        }

        public void AttachClients()
        {
            EnsureClients();
        }
    }
}
#endif