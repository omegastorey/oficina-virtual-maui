using System;
using System.Runtime.CompilerServices;

namespace AppOV
{
    /// <summary>
    /// Evita que el linker quite tipos/métodos que se usan por reflexión/JNI/JS bridge.
    /// Se ejecuta una vez al cargar el assembly (ModuleInitializer) y crea referencias
    /// “vivas” a los miembros críticos para que el linker los conserve.
    /// </summary>
    internal static class LinkerPreserve
    {
        [ModuleInitializer]
        internal static void Init()
        {
            try
            {
                AppLogger.Log("LinkerPreserve", "Init() ejecutado");
            }
            catch { }
#if ANDROID
            KeepAndroid();
#endif
#if IOS
            KeepiOS();
#endif
        }

#if ANDROID
        [Android.Runtime.Preserve(AllMembers = true)]
        private static void KeepAndroid()
        {
            // Fuerza a conservar el handler y tipos anidados que se usan desde Java/JS
            _ = typeof(AppOV.Platforms.Android.CustomWebViewHandler);
            _ = typeof(global::Android.Webkit.WebView);

            // JS bridge: sus métodos se invocan desde JavaScript (reflection/JNI).
            // Creamos un delegate por cada método exportado para que el linker NO los recorte.
            try
            {
                var jb = new AppOV.Platforms.Android.CustomWebViewHandler.JsBridge(null!);

                // NO ejecutar — sólo mantener referencias a los métodos.
                System.Action<string, string> m1 = jb.saveBase64;
                System.Action<string> m2 = jb.Dbg;
                System.Action<string> m3 = jb.OnToken;
                System.Action<string> m4 = jb.OpenExternal;

                // Silenciar advertencias de variables no usadas
                _ = m1; _ = m2; _ = m3; _ = m4;
            }
            catch
            {
                // No hacer nada: esto no debería ejecutarse, es sólo para el linker.
            }

            // Asegura presencia del WebChromeClient/WebViewClient anidados.
            // (No podemos referenciar tipos anidados privados directamente, pero ya se
            //  referencian desde CreatePlatformView; este typeof adicional mantiene el contenedor).
            _ = typeof(AppOV.Platforms.Android.CustomWebViewHandler);
        }
#endif

#if IOS
        [Foundation.Preserve(AllMembers = true)]
        private static void KeepiOS()
        {
            // Conserva el handler de iOS y sus delegados/bridge usados por WKWebView
            _ = typeof(AppOV.Platforms.iOS.CustomWebViewHandler);
            _ = typeof(AppOV.Platforms.iOS.JsBridgeIos);
            _ = typeof(AppOV.Platforms.iOS.AuthUiDelegate);
            _ = typeof(AppOV.Platforms.iOS.DownloadNavigationDelegate);
        }
#endif
    }
}
