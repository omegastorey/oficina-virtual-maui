#if IOS
using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using CoreGraphics;
using Foundation;
using WebKit;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Storage;

namespace AppOV.Platforms.iOS
{
    public class CustomWebViewHandler : WebViewHandler
    {
// Hooks tempranos también en iOS (DocumentStart)
static readonly string EarlyHooksJs = @"
        (function(){ if (window.__earlyHooksInstalled) return; window.__earlyHooksInstalled = true;
        function log(m){ try{ window.webkit && window.webkit.messageHandlers && window.webkit.messageHandlers.jsBridge && window.webkit.messageHandlers.jsBridge.postMessage('DBG:'+m);}catch(_){ } }

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
                    if (isTokenKey(k) && window.webkit && window.webkit.messageHandlers && window.webkit.messageHandlers.jsBridge){
                    window.webkit.messageHandlers.jsBridge.postMessage({ token: v });
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
                    if (isTokenKey(k) && window.webkit && window.webkit.messageHandlers && window.webkit.messageHandlers.jsBridge){
                    window.webkit.messageHandlers.jsBridge.postMessage({ token: v });
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

            if (found && window.webkit && window.webkit.messageHandlers && window.webkit.messageHandlers.jsBridge){
                window.webkit.messageHandlers.jsBridge.postMessage({ token: found });
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

        window.__blobRegistry = window.__blobRegistry || {};
        document.addEventListener('click', function(ev){ try{ var t=ev.target; window.__lastClickText=(t&&(t.innerText||t.textContent)||'').toLowerCase(); }catch(e){} }, true);
        (function(){
            var _origCreate=URL.createObjectURL, _origRevoke=URL.revokeObjectURL;
            URL.createObjectURL=function(obj){ var url=_origCreate.call(this,obj); try{ var e={blob:obj,type:(obj&&obj.type)||'',size:(obj&&obj.size)||0,revoked:false,dataUrl:null}; window.__blobRegistry[url]=e; try{ var fr=new FileReader(); fr.onloadend=function(){ try{ e.dataUrl=fr.result; }catch(_){ } }; fr.readAsDataURL(obj);}catch(_){}}catch(_){ } return url; };
            URL.revokeObjectURL=function(u){ try{ if(window.__blobRegistry&&window.__blobRegistry[u]) window.__blobRegistry[u].revoked=true; }catch(_){ }
            try{ var self=this; setTimeout(function(){ try{ _origRevoke.call(self,u); }catch(_){ } }, 5000);}catch(_){ try{ _origRevoke.call(this,u); }catch(e){} } };
        })();
        async function blobToDataUrl(blob){ return await new Promise(function(res){ var fr=new FileReader(); fr.onloadend=function(){res(fr.result);}; fr.readAsDataURL(blob); }); }
        async function blobUrlToDataUrl(u){ try{ var m=(window.__blobRegistry&&window.__blobRegistry[u])||null; if(m){ if(m.dataUrl) return m.dataUrl; if(m.blob) return await blobToDataUrl(m.blob); } var b=await (await fetch(u)).blob(); return await blobToDataUrl(b); }catch(e){ return null; } }
        window.__nativeDownload = async function(input,name){ try{ if(typeof Blob!=='undefined' && input instanceof Blob){ var du=await blobToDataUrl(input); return sendToNative(du,name||'download.csv'); } if(typeof input==='string' && input.indexOf('blob:')===0){ var du=await blobUrlToDataUrl(input); if(du) return sendToNative(du,name||'download.csv'); return false; } if(typeof input==='string' && input.indexOf('data:')===0){ return sendToNative(input,name||'download.csv'); } }catch(e){} return false; };
        function sendToNative(du,fn){ try{ window.webkit.messageHandlers.jsBridge.postMessage({ data: du, fileName: fn||'download.bin' }); return true; }catch(e){ return false; } }
        document.addEventListener('click', function(ev){ try{ var a=ev.target&&ev.target.closest&&ev.target.closest('a'); if(!a) return; var href=a.getAttribute('href')||''; var dn=a.getAttribute('download')||''; if(!href) return; if(href.indexOf('blob:')===0||href.indexOf('data:')===0){ ev.preventDefault(); ev.stopPropagation(); window.__nativeDownload(href,dn||'download.csv'); } }catch(_){ } }, true);
        try{ var oc=HTMLAnchorElement.prototype.click; HTMLAnchorElement.prototype.click=function(){ var href=this.getAttribute('href')||''; var dn=this.getAttribute('download')||''; if(href&&(href.indexOf('blob:')===0||href.indexOf('data:')===0)) return void window.__nativeDownload(href,dn||'download.csv'); return oc.call(this); }; }catch(_){}
        (function(){ function handle(u){ if(!u) return false; u=String(u); if(u.indexOf('blob:')===0||u.indexOf('data:')===0){ window.__nativeDownload(u,'download.csv'); return true; } return false; }
            try{ var _open=window.open; window.open=function(u){ if(handle(u)) return null; return _open.apply(this,arguments); }; }catch(_){}
            try{ var _assign=window.location.assign.bind(window.location); window.location.assign=function(u){ if(handle(u)) return; return _assign(u); }; var _replace=window.location.replace.bind(window.location); window.location.replace=function(u){ if(handle(u)) return; return _replace(u); };
            var d=Object.getOwnPropertyDescriptor(Location.prototype,'href'); if(d&&d.set&&d.configurable){ Object.defineProperty(Location.prototype,'href',{ get:d.get, set:function(u){ if(handle(u)) return; return d.set.call(this,u); }, configurable:true, enumerable:d.enumerable }); } }catch(_){}
        })();
        (function(){ try{ var orig=window.saveAs; window.saveAs=function(b,n){ try{ if(typeof window.__nativeDownload==='function' && b) return window.__nativeDownload(b,n||'download.csv'); }catch(_){ } if(orig) return orig.apply(this,arguments); return false; }; }catch(_){ }
            try{ if(window.navigator && typeof window.navigator.msSaveOrOpenBlob==='function'){ var o=window.navigator.msSaveOrOpenBlob; window.navigator.msSaveOrOpenBlob=function(b,n){ try{ if(typeof window.__nativeDownload==='function' && b) return window.__nativeDownload(b,n||'download.csv'); }catch(_){ } return o.call(window.navigator,b,n); }; } }catch(_){ }
        })();
        })();";


        protected override WKWebView CreatePlatformView()
        {
            var cfg = new WKWebViewConfiguration();

            // Bridge JS <-> Nativo
            cfg.UserContentController.AddScriptMessageHandler(new JsBridgeIos(), "jsBridge");

            // Hooks tempranos al inicio del documento
            cfg.UserContentController.AddUserScript(new WKUserScript(
                new NSString(EarlyHooksJs),
                WKUserScriptInjectionTime.AtDocumentStart,
                true));

            // Evitar zoom
            const string disableZoomJs = @"(function(){
              var meta = document.querySelector('meta[name=viewport]');
              if (!meta) { meta = document.createElement('meta'); meta.name='viewport'; document.head.appendChild(meta); }
              meta.setAttribute('content','width=device-width, initial-scale=1.0, maximum-scale=1.0, user-scalable=no');
            })();";
            cfg.UserContentController.AddUserScript(new WKUserScript(
                new NSString(disableZoomJs),
                WKUserScriptInjectionTime.AtDocumentEnd,
                true));

            // Señal para login externo
            const string nativeOpenerJs = "window.__nativeOpensProviders = true;";
            cfg.UserContentController.AddUserScript(new WKUserScript(
                new NSString(nativeOpenerJs),
                WKUserScriptInjectionTime.AtDocumentStart,
                true));

            var web = new WKWebView(CGRect.Empty, cfg);

            // Bloquear zoom
            if (web.ScrollView.PinchGestureRecognizer is not null)
                web.ScrollView.PinchGestureRecognizer.Enabled = false;
            web.ScrollView.MinimumZoomScale = 1f;
            web.ScrollView.MaximumZoomScale = 1f;
            web.ScrollView.ZoomScale = 1f;

            return web;
        }

        protected override void ConnectHandler(WKWebView platformView)
        {
            base.ConnectHandler(platformView);
            platformView.NavigationDelegate = new DownloadNavigationDelegate(platformView);
            platformView.UIDelegate = new AuthUiDelegate(platformView);
        }

        static string GuessMimeFromExtension(string fileName, string? fallback = null)
        {
            var ext = Path.GetExtension(fileName)?.ToLowerInvariant();
            return ext switch
            {
                ".csv" => "text/csv",
                ".pdf" => "application/pdf",
                ".png" => "image/png",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".xls" => "application/vnd.ms-excel",
                ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                ".doc" => "application/msword",
                ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                _ => fallback ?? "application/octet-stream"
            };
        }

        public static async Task ShowOpenOrShareAsync(string path, string fileName, string? contentType)
        {
            contentType ??= GuessMimeFromExtension(fileName, "application/octet-stream");

            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                var choice = await Application.Current!.MainPage!.DisplayActionSheet(
                    "Archivo descargado", "Cancelar", null, "Abrir", "Compartir");

                if (choice == "Abrir")
                {
                    await Launcher.Default.OpenAsync(new OpenFileRequest { Title = fileName, File = new ReadOnlyFile(path, contentType) });
                }
                else if (choice == "Compartir")
                {
                    await Share.RequestAsync(new ShareFileRequest { Title = fileName, File = new ShareFile(path) });
                }
            });
        }
    }

    class DownloadNavigationDelegate : WKNavigationDelegate
    {
        readonly WKWebView _webView;
        public DownloadNavigationDelegate(WKWebView webView) => _webView = webView;

        static string HostOf(string url) { try { return new Uri(url).Host.ToLowerInvariant(); } catch { return ""; } }

        static bool IsOwnDomain(string url)
        {
            var h = HostOf(url);
            return h == "edelap.ovqa.storey.com.ar" ||
                   h == "edelap.ovdev.storey.com.ar" ||
                   h == "localhost" || h == "10.0.2.2";
        }

        static bool IsProviderHost(string url)
        {
            var h = HostOf(url);
            return h.Contains("accounts.google.com") ||
                   h.Contains("appleid.apple.com") ||
                   h.Contains("identity.apple.com") ||
                   h.Contains("login.microsoftonline.com") ||
                   h.Contains("login.live.com");
        }

        static bool IsTopLevelAuthorize(string url)
        {
            var lower = url.ToLowerInvariant();
            bool hasClient = lower.Contains("client_id=");
            bool hasRedirect = lower.Contains("redirect_uri=");
            bool hasRespType = lower.Contains("response_type=");
            if (!(hasClient && hasRedirect && hasRespType)) return false;

            var host = HostOf(url);
            if (host.Contains("accounts.google.com"))
            {
                if (lower.Contains("/gsi/") || lower.Contains("iframerpc") || lower.Contains("listaccounts") || lower.Contains("checkconnection"))
                    return false;
                return lower.Contains("/o/oauth2/") || lower.Contains("/oauth2/");
            }
            if (host.Contains("login.microsoftonline.com"))
                return lower.Contains("/oauth2/") || lower.Contains("/oauth2/v2.0/");
            if (host.Contains("appleid.apple.com"))
                return lower.Contains("/auth/authorize");

            return false;
        }

        static void OpenExtern(string url)
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                try { await Browser.OpenAsync(new Uri(url), BrowserLaunchMode.External); } catch { }
            });
        }

        public override void DecidePolicy(WKWebView webView, WKNavigationAction action, Action<WKNavigationActionPolicy> decisionHandler)
        {
            var url = action.Request.Url?.AbsoluteString ?? string.Empty;

            if (url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
            {
                decisionHandler(WKNavigationActionPolicy.Cancel);
                _ = Launcher.Default.OpenAsync(new Uri(url));
                return;
            }

            if (!IsOwnDomain(url) && IsProviderHost(url))
            {
                decisionHandler(WKNavigationActionPolicy.Cancel);
                OpenExtern(url);
                return;
            }

            if (url.StartsWith("blob:", StringComparison.OrdinalIgnoreCase))
            {
                decisionHandler(WKNavigationActionPolicy.Cancel);
                var js = $@"(async function(){{
                  var u='{url}'; var m=(window.__blobRegistry&&window.__blobRegistry[u])||null; var d=null;
                  try {{ if(m&&m.dataUrl) d=m.dataUrl; else if(m&&m.blob) d=await (new Promise(function(res){{var fr=new FileReader(); fr.onloadend=function(){{res(fr.result);}}; fr.readAsDataURL(m.blob);}})); else {{ var b=await (await fetch(u)).blob(); d=await (new Promise(function(res){{var fr=new FileReader(); fr.onloadend=function(){{res(fr.result);}}; fr.readAsDataURL(b);}})); }} }} catch(e) {{}}
                  var name='download.csv'; try{{ var last=(window.__lastClickText||'').toLowerCase(); if(m&&m.type&&m.type.indexOf('spreadsheetml')>=0) name='export.xlsx'; else if(m&&m.type&&m.type.indexOf('csv')>=0) name='export.csv'; else if(last.includes('xlsx')) name='export.xlsx'; else if(last.includes('csv')) name='export.csv'; }}catch(_){ }
                  if(d) window.webkit.messageHandlers.jsBridge.postMessage({{ data:d, fileName:name }});
                }})();";
                webView.EvaluateJavaScript(js, null);
                return;
            }

            if (url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                decisionHandler(WKNavigationActionPolicy.Cancel);
                var safe = Uri.EscapeDataString(url);
                var js = $@"(function(){{
                  var s = decodeURIComponent('{safe}');
                  var name = (window.__lastClickText||'').includes('csv') ? 'export.csv' : 'download.bin';
                  window.webkit.messageHandlers.jsBridge.postMessage({{ data: s, fileName: name }});
                }})();";
                webView.EvaluateJavaScript(js, null);
                return;
            }

            decisionHandler(WKNavigationActionPolicy.Allow);
        }

        public override void DecidePolicy(WKWebView webView, WKNavigationResponse response, Action<WKNavigationResponsePolicy> decisionHandler)
        {
            if (response.Response is NSHttpUrlResponse resp &&
                resp.AllHeaderFields.ContainsKey(new NSString("Content-Disposition")))
            {
                decisionHandler(WKNavigationResponsePolicy.Cancel);
                _ = DownloadAndShareAsync(resp.Url.AbsoluteString);
                return;
            }
            decisionHandler(WKNavigationResponsePolicy.Allow);
        }

        async Task DownloadAndShareAsync(string url)
        {
            try
            {
                using var client = new HttpClient();
                var data = await client.GetByteArrayAsync(url);

                var fileName = Path.GetFileName(new Uri(url).LocalPath);
                if (string.IsNullOrEmpty(fileName)) fileName = "download";

                var docs = NSSearchPath.GetDirectories(NSSearchPathDirectory.DocumentDirectory, NSSearchPathDomain.User, true)[0];
                var path = Path.Combine(docs, fileName);
                File.WriteAllBytes(path, data);

                await CustomWebViewHandler.ShowOpenOrShareAsync(path, fileName, null);
            }
            catch (Exception ex) { 
            Console.WriteLine($"Error descargando {url}: {ex}");
            AppLogger.Log("JsBridgeIos", $"Error descargando {url}: {ex}");
            }
        }
    }

    class AuthUiDelegate : WKUIDelegate
    {
        readonly WKWebView _root;
        public AuthUiDelegate(WKWebView root) => _root = root;

        static bool IsProviderHost(string url)
        {
            try
            {
                var h = new Uri(url).Host.ToLowerInvariant();
                return h.Contains("accounts.google.com") ||
                       h.Contains("appleid.apple.com") ||
                       h.Contains("identity.apple.com") ||
                       h.Contains("login.microsoftonline.com") ||
                       h.Contains("login.live.com");
            }
            catch { return false; }
        }

        static bool IsTopLevelAuthorize(string url)
        {
            var lower = url.ToLowerInvariant();
            bool hasClient = lower.Contains("client_id=");
            bool hasRedirect = lower.Contains("redirect_uri=");
            bool hasRespType = lower.Contains("response_type=");
            if (!(hasClient && hasRedirect && hasRespType)) return false;

            var host = (new Uri(url)).Host.ToLowerInvariant();
            if (host.Contains("accounts.google.com"))
            {
                if (lower.Contains("/gsi/") || lower.Contains("iframerpc") || lower.Contains("listaccounts") || lower.Contains("checkconnection"))
                    return false;
                return lower.Contains("/o/oauth2/") || lower.Contains("/oauth2/");
            }
            if (host.Contains("login.microsoftonline.com"))
                return lower.Contains("/oauth2/") || lower.Contains("/oauth2/v2.0/");
            if (host.Contains("appleid.apple.com"))
                return lower.Contains("/auth/authorize");
            return false;
        }

        public override WKWebView CreateWebView(WKWebView webView, WKWebViewConfiguration configuration, WKNavigationAction navigationAction, WKWindowFeatures windowFeatures)
        {
            var url = navigationAction?.Request?.Url?.AbsoluteString ?? string.Empty;

            if (!string.IsNullOrEmpty(url) && (IsProviderHost(url) || IsTopLevelAuthorize(url)))
            {
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    try { await Browser.OpenAsync(new Uri(url), BrowserLaunchMode.External); } catch { }
                });
                return null!;
            }

            if (!string.IsNullOrEmpty(url))
                MainThread.BeginInvokeOnMainThread(() => _root.LoadRequest(navigationAction.Request));

            return null!;
        }
    }

    class JsBridgeIos : NSObject, IWKScriptMessageHandler
    {
        [Export("userContentController:didReceiveScriptMessage:")]
        public async void DidReceiveScriptMessage(WKUserContentController _, WKScriptMessage message)
        {
            try
            {
                if (message.Body is NSDictionary dict)
                {
                    if (dict["token"] is NSString tokenStr && !string.IsNullOrWhiteSpace(tokenStr.ToString()))
                    {
                        var token = TryExtractToken(tokenStr.ToString());
                        if (!string.IsNullOrEmpty(token))
                            Preferences.Set("web_jwt", token);
                        return;
                    }

                    if (dict["openExternal"] is NSString ext && !string.IsNullOrWhiteSpace(ext.ToString()))
                    {
                        await MainThread.InvokeOnMainThreadAsync(async () =>
                        {
                            await Browser.OpenAsync(new Uri(ext.ToString()), BrowserLaunchMode.External);
                        });
                        return;
                    }

                    string? dataStr = null;
                    string? fileName = null;
                    if (dict["data"] is NSString d) dataStr = d.ToString();
                    if (dict["fileName"] is NSString n) fileName = n.ToString();
                    if (!string.IsNullOrEmpty(dataStr))
                    {
                        await HandleDataOrBase64Async(dataStr, fileName);
                    }
                    return;
                }

                if (message.Body is NSString s)
                {
                    var body = s.ToString();
                    Console.WriteLine("[JS] " + body);
                    AppLogger.Log("JsBridgeIos", body);
                    var maybeToken = TryExtractToken(body);
                    if (!string.IsNullOrEmpty(maybeToken))
                    {
                        Preferences.Set("web_jwt", maybeToken);
                        return;
                    }

                    await HandleDataOrBase64Async(body, null);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log("JsBridgeIos", $"[JsBridgeIos] Error: {ex}");
                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    await Application.Current?.MainPage?.DisplayAlert("Error", "No pudimos procesar el mensaje desde la web.", "OK");
                });
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

        static async Task HandleDataOrBase64Async(string dataStr, string? fileName)
        {
            if (string.IsNullOrEmpty(dataStr)) return;

            string mimeFromHeader = "application/octet-stream";
            byte[] bytes;

            if (dataStr.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                var comma = dataStr.IndexOf(',');
                if (comma < 0) return;

                var header = dataStr.Substring(5, comma - 5);
                var payload = dataStr.Substring(comma + 1);

                mimeFromHeader = header.Split(';')[0].Trim().ToLowerInvariant();
                bool isBase64 = header.IndexOf(";base64", StringComparison.OrdinalIgnoreCase) >= 0;

                bytes = isBase64
                    ? Convert.FromBase64String(payload)
                    : System.Text.Encoding.UTF8.GetBytes(Uri.UnescapeDataString(payload));
            }
            else
            {
                try { bytes = Convert.FromBase64String(dataStr); } catch { return; }
            }

            fileName = SanitizeFileName(fileName);
            if (string.IsNullOrEmpty(fileName))
                fileName = "download" + (ExtFromMime(mimeFromHeader) ?? ".bin");
            else if (!Path.HasExtension(fileName))
            {
                var ext = ExtFromMime(mimeFromHeader);
                if (!string.IsNullOrEmpty(ext)) fileName += ext;
            }

            var docs = NSSearchPath.GetDirectories(NSSearchPathDirectory.DocumentDirectory, NSSearchPathDomain.User, true)[0];
            var path = MakeUnique(Path.Combine(docs, fileName));
            File.WriteAllBytes(path, bytes);

            await CustomWebViewHandler.ShowOpenOrShareAsync(path, Path.GetFileName(path), null);
        }

        static string SanitizeFileName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return string.Empty;
            foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c.ToString(), "");
            name = name.Replace("/", "").Replace("\\", "");
            return name;
        }

        static string? ExtFromMime(string? mime)
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

        static string MakeUnique(string path)
        {
            if (!File.Exists(path)) return path;
            var dir = Path.GetDirectoryName(path)!;
            var name = Path.GetFileNameWithoutExtension(path);
            var ext = Path.GetExtension(path);
            var i = 1;
            string candidate;
            do { candidate = Path.Combine(dir, $"{name} ({i++}){ext}"); } while (File.Exists(candidate));
            return candidate;
        }
    }
}
#endif