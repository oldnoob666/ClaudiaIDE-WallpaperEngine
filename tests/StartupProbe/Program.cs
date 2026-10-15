using System;
using System.Collections.Generic;
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
        return 0;
    }
}
