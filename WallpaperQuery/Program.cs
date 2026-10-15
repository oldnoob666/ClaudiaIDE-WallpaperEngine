using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

internal static class Program
{
    [StructLayout(LayoutKind.Sequential)] private struct Coord { public short X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public short Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct ConsoleInfo { public Coord Size, Cursor; public short Attributes; public Rect Window; public Coord MaximumSize; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CreateFile(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll")] private static extern bool GetConsoleScreenBufferInfo(IntPtr handle, out ConsoleInfo info);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool ReadConsoleOutputCharacter(IntPtr handle, [Out] char[] text, uint length, Coord position, out uint read);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll")] private static extern IntPtr GetStdHandle(int which);
    [DllImport("kernel32.dll")] private static extern bool SetStdHandle(int which, IntPtr handle);
    [DllImport("kernel32.dll")] private static extern bool FreeConsole();
    [DllImport("kernel32.dll")] private static extern bool AllocConsole();
    [DllImport("kernel32.dll")] private static extern IntPtr GetConsoleWindow();
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("kernel32.dll")] private static extern bool WriteFile(IntPtr file, byte[] data, uint length, out uint written, IntPtr overlapped);

    private static int Main(string[] args)
    {
        // WE writes through the console API, bypassing redirected stdout. This
        // helper owns an isolated hidden console and reads its output buffer.
        if (args.Length != 2 || !File.Exists(args[0]) || !int.TryParse(args[1], out var monitor) || monitor < 0) return 2;
        var stdout = GetStdHandle(-11);
        FreeConsole();
        if (!AllocConsole()) return 3;
        ShowWindow(GetConsoleWindow(), 0);
        var console = CreateFile("CONOUT$", 0xc0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
        if (console == new IntPtr(-1)) return 3;
        SetStdHandle(-11, console);
        SetStdHandle(-12, console);
        try
        {
            using (var process = Process.Start(new ProcessStartInfo(args[0], "-control getWallpaper -monitor " + monitor) { UseShellExecute = false }))
            {
                if (!process.WaitForExit(2500)) { process.Kill(); return 4; }
            }
            if (!GetConsoleScreenBufferInfo(console, out var info)) return 5;
            var size = Math.Min(65536, info.Size.X * (info.Cursor.Y + 2));
            var output = new char[size];
            if (!ReadConsoleOutputCharacter(console, output, (uint)size, new Coord(), out var read)) return 6;
            var text = new string(output, 0, (int)read);
            // Console buffers wrap lines at fixed width. Keep spaces inside
            // paths; the selected file starts with a drive/UNC prefix.
            text = text.Trim().Trim('"');
            var match = System.Text.RegularExpressions.Regex.Match(text, @"(?:[A-Za-z]:[\\/]|\\\\).+\.(?:pkg|json|mp4|webm|avi|mov|mkv|m4v|wmv|html?)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (!match.Success) return 7;
            var bytes = new UTF8Encoding(false).GetBytes(match.Value.Trim());
            if (!WriteFile(stdout, bytes, (uint)bytes.Length, out var written, IntPtr.Zero)) return 9;
            return 0;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception || e is InvalidOperationException || e is IOException) { return 8; }
        finally { CloseHandle(console); }
    }
}
