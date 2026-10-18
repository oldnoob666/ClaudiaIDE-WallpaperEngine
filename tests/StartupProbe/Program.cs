using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using ClaudiaIDE;
using ClaudiaIDE.ImageProviders;
using ClaudiaIDE.Settings;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        var setting = new Setting { ImageBackgroundType = ImageBackgroundType.WallpaperEngine };
        var single = new SingleImageProvider(setting);
        ProvidersHolder.Initialize(setting, new List<ImageProvider> { single });
        if (!single.IsActive) { Console.Error.WriteLine("FAIL saved dynamic mode has no active compatibility provider"); return 1; }
        Console.WriteLine("PASS saved dynamic mode initializes a compatibility provider");
        var directory = Path.Combine(Path.GetTempPath(), "claudia-session-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var file = Path.Combine(directory, "test.claudiaide");
            new Setting { ImageBackgroundType = ImageBackgroundType.Single, WallpaperEngineDirectory = "old", WallpaperEngineMonitor = "Monitor2" }.Serialize(file);
            var original = File.ReadAllText(file);
            var selected = new Setting { SolutionConfigFilePath = file };
            var enable = typeof(Setting).GetMethod("EnableWallpaperEngineForSession", BindingFlags.Instance | BindingFlags.NonPublic);
            if (enable == null) throw new Exception("Session enable operation is missing");
            enable.Invoke(selected, new object[] { "new", "Monitor0" });
            selected.Load(file);
            if (selected.ImageBackgroundType != ImageBackgroundType.WallpaperEngine || selected.WallpaperEngineDirectory != "new" || selected.WallpaperEngineMonitor != "Monitor0")
                throw new Exception("FAIL opening a new view must retain the explicitly enabled session wallpaper");
            Console.WriteLine("PASS reopening solution settings retains current session wallpaper");
            if (File.ReadAllText(file) != original) throw new Exception("FAIL session enable rewrites solution config");
            Console.WriteLine("PASS session enable leaves solution config bytes untouched");
            selected.IsHidden = true;
            selected.Load(file);
            if (!selected.IsHidden) throw new Exception("FAIL new view resets the user's hide command");
            Console.WriteLine("PASS session reload preserves the hide command");
            var clear = typeof(Setting).GetMethod("ClearWallpaperEngineSession", BindingFlags.Instance | BindingFlags.NonPublic);
            clear.Invoke(selected, null);
            selected.Load(file);
            if (selected.ImageBackgroundType != ImageBackgroundType.Single) throw new Exception("FAIL explicit ordinary settings cannot clear the session override");
            Console.WriteLine("PASS clearing session restores ordinary solution settings");
            var transparent = typeof(Setting).GetProperty("TransparentToolWindows");
            transparent.SetValue(selected, true);
            selected.Serialize(file);
            var restored = new Setting(); restored.Load(file);
            if (!(bool)transparent.GetValue(restored)) throw new Exception("FAIL solution settings lose tool window transparency");
            Console.WriteLine("PASS solution settings round-trip tool window transparency");
        }
        catch (Exception error) { Console.Error.WriteLine(error.GetType().FullName + ": " + error.Message); return 1; }
        finally { Directory.Delete(directory, true); }
        return 0;
    }
}
