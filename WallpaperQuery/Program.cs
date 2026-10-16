using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

internal static class Program
{
    [DllImport("kernel32.dll")] private static extern IntPtr GetStdHandle(int which);
    [DllImport("kernel32.dll")] private static extern bool WriteFile(IntPtr file, byte[] data, uint length, out uint written, IntPtr overlapped);

    private static int Main(string[] args)
    {
        // WE attaches to its parent's console. This client is itself launched
        // inside the broker's headless ConPTY, so that attachment stays headless.
        if (args.Length == 3 && args[0] == "--console-client")
        {
            if (!File.Exists(args[1]) || !int.TryParse(args[2], out var index) || index < 0) return 2;
            using (var process = Process.Start(new ProcessStartInfo(args[1], "-control getWallpaper -monitor " + index) { UseShellExecute = false }))
            {
                if (!process.WaitForExit(2000)) { process.Kill(); return 4; }
                return process.ExitCode;
            }
        }
        if (args.Length != 2 || !File.Exists(args[0]) || !int.TryParse(args[1], out var monitor) || monitor < 0) return 2;
        try
        {
            var text = PseudoConsole.Read(args[0], monitor);
            // ConPTY emits terminal control sequences around the console output.
            text = Regex.Replace(text, @"\x1B(?:\][^\x07]*(?:\x07|\x1B\\)|\[[0-?]*[ -/]*[@-~])", "");
            var match = Regex.Match(text, @"(?:[A-Za-z]:[\\/]|\\\\)[^\r\n\0]+\.(?:pkg|json|mp4|webm|avi|mov|mkv|m4v|wmv|html?)", RegexOptions.IgnoreCase);
            if (!match.Success) return 7;
            var bytes = new UTF8Encoding(false).GetBytes(match.Value.Trim());
            return WriteFile(GetStdHandle(-11), bytes, (uint)bytes.Length, out _, IntPtr.Zero) ? 0 : 9;
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception || error is IOException || error is TimeoutException || error is ExternalException || error is EntryPointNotFoundException) { return 8; }
    }
}
