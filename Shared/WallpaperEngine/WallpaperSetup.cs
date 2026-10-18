using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ClaudiaIDE.WallpaperEngine
{
    // Read-only checks shared by first-use UI and the background state endpoint.
    internal sealed class WallpaperSetupResult
    {
        public string Code { get; set; }
        public string Message { get; set; }
        public string Directory { get; set; }
        public string Monitor { get; set; }
        public string[] Monitors { get; set; } = new string[0];
        public WallpaperProject Project { get; set; }
        public bool CanEnable => Project != null && Code == "ready";
    }

    internal static class WallpaperSetup
    {
        public static string FindInstall(IEnumerable<string> steamRoots)
        {
            var roots = new List<string>(steamRoots.Where(r => !string.IsNullOrWhiteSpace(r)));
            foreach (var root in roots.ToArray())
            {
                try
                {
                    var vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
                    if (!File.Exists(vdf)) continue;
                    foreach (Match match in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s*\"([^\"]+)\""))
                        roots.Add(match.Groups[1].Value.Replace(@"\\", @"\"));
                }
                catch (Exception e) when (IsReadError(e)) { }
            }
            foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var directory = Path.Combine(root, "steamapps", "common", "wallpaper_engine");
                    if (File.Exists(Path.Combine(directory, "config.json"))) return directory;
                }
                catch (Exception e) when (IsReadError(e)) { }
            }
            return "";
        }

        public static WallpaperSetupResult Inspect(string directory, string userName, string monitor, string liveEntry = null)
        {
            var result = new WallpaperSetupResult { Directory = (directory ?? "").Trim().Trim('"'), Monitor = monitor };
            WallpaperSetupResult Fail(string code, string message) { result.Code = code; result.Message = message; return result; }
            if (result.Directory == "") return Fail("engine-missing", "未找到 Wallpaper Engine。请先通过 Steam 安装并运行它，或点击「选择目录」找到 wallpaper_engine 文件夹。");
            try
            {
                result.Directory = Path.GetFullPath(result.Directory);
                var configFile = Path.Combine(result.Directory, "config.json");
                if (!File.Exists(configFile)) return Fail("config-missing", "此目录没有 config.json。请选择 wallpaper_engine 安装目录；首次使用请先运行 Wallpaper Engine 并应用一张壁纸。");
                JObject config;
                try { config = JObject.Parse(File.ReadAllText(configFile)); }
                catch (JsonException) { return Fail("config-invalid", "Wallpaper Engine 配置正在写入或格式损坏。请在 Wallpaper Engine 中重新应用壁纸，再点击「重新检测」。"); }
                var profile = config[userName];
                if (profile == null) return Fail("profile-missing", "配置中没有当前 Windows 用户的记录。请用当前用户运行 Wallpaper Engine 并应用一张壁纸，再重新检测。");
                var selected = profile["general"]?["wallpaperconfig"]?["selectedwallpapers"] as JObject;
                result.Monitors = selected?.Properties().Select(p => p.Name).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray() ?? new string[0];
                if (result.Monitors.Length == 0) return Fail("selection-missing", "尚未找到当前用户的显示器壁纸。请在 Wallpaper Engine 中应用一张视频或场景壁纸，再重新检测。");
                if (!result.Monitors.Contains(monitor)) return Fail("monitor-missing", "当前配置没有显示器「" + monitor + "」。请从显示器下拉列表中选择要跟随的显示器。");
                var entry = liveEntry ?? (string)selected[monitor]?["file"];
                if (string.IsNullOrWhiteSpace(entry)) return Fail("selection-missing", "此显示器尚未选择壁纸。请在 Wallpaper Engine 中应用一张视频或场景壁纸。");
                if (!Path.IsPathRooted(entry)) return Fail("asset-missing", "当前壁纸路径无效。请在 Wallpaper Engine 中重新下载或应用该壁纸。");
                var projectDirectory = Path.GetDirectoryName(Path.GetFullPath(entry));
                string projectFile = null;
                for (var depth = 0; projectDirectory != null && depth < 8; depth++, projectDirectory = Path.GetDirectoryName(projectDirectory))
                {
                    projectFile = WallpaperProject.SafeAssetPath(projectDirectory, "project.json");
                    if (projectFile != null) break;
                }
                if (projectFile == null) return Fail("asset-missing", "找不到当前壁纸的项目文件。请确认素材仍在本机，并在 Steam 创意工坊重新下载后应用。");
                var metadata = JObject.Parse(File.ReadAllText(projectFile));
                var type = ((string)metadata["type"] ?? "").ToLowerInvariant();
                if (type != "video" && type != "scene" && type != "web")
                    return Fail("type-unsupported", "当前壁纸类型「" + type + "」不受支持。请选择视频或场景壁纸；应用程序类壁纸不会执行。");
                var project = WallpaperProject.ResolveEntry(entry);
                if (project == null) return Fail("asset-missing", "壁纸素材缺失或无法读取。请在 Wallpaper Engine 中确认它能正常播放，必要时重新下载。");
                if (type == "web" && !new[] { ".html", ".htm" }.Contains(Path.GetExtension(project.EntryPath).ToLowerInvariant()))
                    return Fail("type-unsupported", "此网页壁纸没有 HTML 入口，无法播放。请更换视频、场景或 HTML 网页壁纸。");
                result.Project = project;
                result.Code = "ready";
                result.Message = "已找到：" + project.Title + "（" + project.Type + "）。可以启用当前壁纸。";
                return result;
            }
            catch (Exception e) when (IsReadError(e) || e is JsonException || e is InvalidOperationException)
            {
                return Fail("read-failed", "无法读取壁纸配置或素材。请检查目录权限，在 Wallpaper Engine 中重新应用壁纸后重试；详细排查见使用教程。");
            }
        }

        private static bool IsReadError(Exception e) => e is IOException || e is UnauthorizedAccessException || e is ArgumentException || e is NotSupportedException || e is System.Security.SecurityException;
    }
}
