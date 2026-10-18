using ClaudiaIDE.WallpaperEngine;
using Newtonsoft.Json.Linq;

internal static class SetupTests
{
    public static void Run(Action<bool, string> check, string root)
    {
        WallpaperSetupResult Inspect(string directory, string monitor = "Monitor0", string liveEntry = null) =>
            WallpaperSetup.Inspect(directory, "SetupUser", monitor, liveEntry);
        var install = Path.Combine(root, "首次 使用");
        Directory.CreateDirectory(install);
        check(Inspect("").Code == "engine-missing", "missing install offers directory selection");
        check(Inspect(install).Code == "config-missing", "missing config asks to apply a wallpaper first");
        File.WriteAllText(Path.Combine(install, "config.json"), "partial json");
        check(Inspect(install).Code == "config-invalid", "partial config produces a retryable explanation");
        var selections = new JObject();
        var config = new JObject { ["OtherUser"] = new JObject { ["general"] = new JObject { ["wallpaperconfig"] = new JObject { ["selectedwallpapers"] = selections } } } };
        void Save() => File.WriteAllText(Path.Combine(install, "config.json"), config.ToString());
        Save();
        check(Inspect(install).Code == "profile-missing", "setup never borrows another user's config");
        config["SetupUser"] = config["OtherUser"].DeepClone();
        selections = (JObject)config["SetupUser"]["general"]["wallpaperconfig"]["selectedwallpapers"];
        Save();
        check(Inspect(install).Code == "selection-missing", "empty selection explains how to apply a wallpaper");
        var project = Path.Combine(root, "示例 素材");
        Directory.CreateDirectory(project);
        var entry = Path.Combine(project, "clip.mp4");
        selections["Monitor2"] = new JObject { ["file"] = entry };
        selections["Monitor0"] = new JObject { ["file"] = entry };
        Save();
        check(Inspect(install, "Monitor9").Code == "monitor-missing", "invalid monitor is not silently replaced");
        check(string.Join(",", (string[])Inspect(install).Monitors) == "Monitor0,Monitor2", "only current user's monitors populate selector");
        check(Inspect(install).Code == "asset-missing", "missing project explains missing workshop content");
        File.WriteAllText(Path.Combine(project, "project.json"), "{\"type\":\"application\",\"file\":\"app.exe\"}");
        check(Inspect(install).Code == "type-unsupported", "application wallpaper has an actionable unsupported message");
        File.WriteAllText(Path.Combine(project, "project.json"), "{\"type\":\"video\",\"file\":\"clip.mp4\"}");
        check(Inspect(install).Code == "asset-missing", "missing video is not reported as ready");
        File.WriteAllText(entry, "fixture");
        check(Inspect(install).CanEnable && Inspect(install).Project.Title == "示例 素材", "valid Chinese path is ready to enable");
        check(Inspect(install, "Monitor0", Path.Combine(root, "missing-live.pkg")).Code == "asset-missing", "unsupported live selection cannot fall back to unrelated saved wallpaper");
        File.WriteAllText(Path.Combine(project, "project.json"), "{\"type\":\"scene\",\"file\":\"scene.json\"}");
        File.WriteAllText(Path.Combine(project, "scene.pkg"), "fixture");
        check(Inspect(install).CanEnable, "scene package fallback works in first-use check");
        File.WriteAllText(Path.Combine(project, "project.json"), "{\"type\":\"web\",\"file\":\"clip.mp4\"}");
        check(Inspect(install).Code == "type-unsupported", "non-HTML web entry is explained before enable");
        // Discovery must inspect alternate Steam libraries and tolerate stale/unreadable entries.
        var steam = Path.Combine(root, "Steam");
        var library = Path.Combine(root, "另一个 Steam 库");
        Directory.CreateDirectory(Path.Combine(steam, "steamapps"));
        var alternate = Path.Combine(library, "steamapps", "common", "wallpaper_engine");
        Directory.CreateDirectory(alternate);
        File.WriteAllText(Path.Combine(alternate, "config.json"), "{}");
        File.WriteAllText(Path.Combine(steam, "steamapps", "libraryfolders.vdf"), "\"libraryfolders\" { \"1\" { \"path\" \"" + library.Replace("\\", "\\\\") + "\" } }");
        var found = WallpaperSetup.FindInstall(new[] { Path.Combine(root, "stale"), steam });
        check(found == alternate, "auto discovery follows non-default Steam library with Chinese spaces");
    }
}
