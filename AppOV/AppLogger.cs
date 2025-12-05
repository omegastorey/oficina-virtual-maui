using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Maui.Storage;

namespace AppOV
{
    public static class AppLogger
    {
        private static readonly object _lock = new();

        private static string LogFilePath =>
            Path.Combine(FileSystem.AppDataDirectory, "appov.log");

        public static void Log(string tag, string message)
        {
            var line = $"{DateTime.Now:O} [{tag}] {message}";

            // 1) System.Diagnostics.Trace (funciona en Release si la constante TRACE está definida)
            Trace.WriteLine(line);

#if ANDROID
            // 2) Logcat de Android
            try
            {
                Android.Util.Log.Debug(tag, message);
            }
            catch { /* no romper la app si falla el log */ }
#endif

            // 3) Guardar en archivo local
            try
            {
                lock (_lock)
                {
                    File.AppendAllText(LogFilePath, line + Environment.NewLine);
                }
            }
            catch
            {
                // En producción jamás tiramos excepción por log
            }
        }

        public static Task LogAsync(string tag, string message)
        {
            // Fire-and-forget liviano para no bloquear el hilo UI
            return Task.Run(() => Log(tag, message));
        }
    }
}
