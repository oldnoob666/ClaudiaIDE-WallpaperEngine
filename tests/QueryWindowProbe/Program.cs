using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

internal static class Program
{
    private const string Expected = "D:/SteamLibrary/steamapps/workshop/content/431960/12345/中文 壁纸.pkg";
    private delegate void WinEvent(IntPtr hook, uint kind, IntPtr window, int objectId, int childId, uint thread, uint time);
    [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEvent callback, uint process, uint thread, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder name, int count);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("kernel32.dll")] private static extern IntPtr GetStdHandle(int type);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateFile(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool WriteConsole(IntPtr console, string text, uint length, out uint written, IntPtr reserved);

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "-control")
        {
            // Like WE, write directly to the console rather than redirected stdout.
            var console = CreateFile("CONOUT$", 0x40000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
            WriteConsole(console, Expected, (uint)Expected.Length, out _, IntPtr.Zero);
            CloseHandle(console);
            Thread.Sleep(150);
            return 0;
        }
        Console.OutputEncoding = new UTF8Encoding(false);
        var visible = new List<string>();
        WinEvent callback = (hook, kind, window, objectId, childId, thread, time) => {
            if (objectId != 0 || childId != 0) return;
            var name = new StringBuilder(256); GetClassName(window, name, name.Capacity);
            if (name.ToString() == "ConsoleWindowClass" || name.ToString().StartsWith("CASCADIA_")) { GetWindowThreadProcessId(window, out var owner); visible.Add(name + " pid=" + owner); }
        };
        var eventHook = SetWinEventHook(0x8002, 0x8002, IntPtr.Zero, callback, 0, 0, 0); // EVENT_OBJECT_SHOW
        if (eventHook == IntPtr.Zero) throw new Exception("Unable to observe window show events");
        try
        {
            var watching = args.Length == 2 && args[0] == "--watch";
            if (watching)
            {
                var timer = Stopwatch.StartNew();
                while (timer.ElapsedMilliseconds < int.Parse(args[1]) * 1000) { Application.DoEvents(); Thread.Sleep(1); }
            }
            for (var i = 0; !watching && i < 3; i++)
            {
                var fixture = args.Length > 1 ? args[1] : typeof(Program).Assembly.Location;
                using (var process = Process.Start(new ProcessStartInfo(args[0], "\"" + fixture + "\" 0") {
                    UseShellExecute = false, RedirectStandardOutput = true, StandardOutputEncoding = new UTF8Encoding(false),
                    CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden
                }))
                {
                    var output = process.StandardOutput.ReadToEndAsync();
                    var timer = Stopwatch.StartNew();
                    while (!process.HasExited && timer.ElapsedMilliseconds < 5000) { Application.DoEvents(); Thread.Sleep(1); }
                    Application.DoEvents();
                    if (!process.HasExited) { process.Kill(); throw new Exception("Query timed out"); }
                    if (process.ExitCode != 0 || (args.Length == 1 ? output.Result.Trim() != Expected : !output.Result.Contains("431960/"))) throw new Exception("Console query failed: exit=" + process.ExitCode + " output=" + output.Result);
                    Console.WriteLine("Query " + timer.ElapsedMilliseconds + "ms: " + output.Result.Trim());
                }
            }
            Application.DoEvents();
            Console.WriteLine("Window show events: " + string.Join(", ", visible));
            if (visible.Count != 0) { Console.Error.WriteLine("FAIL query displayed a console/terminal window " + visible.Count + " times"); return 1; }
            Console.WriteLine(watching ? "PASS no console/terminal window appeared during " + args[1] + " seconds" : "PASS direct-console query returns path without showing console/terminal windows (3 launches)");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error.GetType().FullName + ": " + error.Message);
            return 2;
        }
        finally { UnhookWinEvent(eventHook); GC.KeepAlive(callback); }
    }
}
