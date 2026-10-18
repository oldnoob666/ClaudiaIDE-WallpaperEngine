#if VS_LATEST
using System;
using System.IO;

namespace ClaudiaIDE.WallpaperEngine
{
    internal static class WallpaperDiagnostics
    {
        private static string lastError = "";
        public static string LastError => System.Threading.Volatile.Read(ref lastError);
        public static string LogDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClaudiaIDE");
        public static void Clear() => System.Threading.Volatile.Write(ref lastError, "");
        public static void Report(string message)
        {
            System.Threading.Volatile.Write(ref lastError, message ?? "");
            WallpaperBackground.Log(message);
        }
    }
}
#endif
