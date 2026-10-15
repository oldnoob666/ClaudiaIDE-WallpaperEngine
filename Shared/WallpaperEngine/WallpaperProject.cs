using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace ClaudiaIDE.WallpaperEngine
{
    internal sealed class WallpaperProject
    {
        public string DirectoryPath { get; private set; }
        public string EntryPath { get; private set; }
        public string Title { get; private set; }
        public string Type { get; private set; }

        public static WallpaperProject Resolve(string installDirectory, string userName, string monitorName)
        {
            try
            {
                var config = JObject.Parse(File.ReadAllText(Path.Combine(installDirectory, "config.json")));
                var selected = (string)config[userName]?["general"]?["wallpaperconfig"]?["selectedwallpapers"]?[monitorName]?["file"];
                return ResolveEntry(selected);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is Newtonsoft.Json.JsonException || e is ArgumentException || e is NotSupportedException)
            {
                return null; // WE may be replacing config.json while it is read.
            }
        }

        public static WallpaperProject ResolveEntry(string selected)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(selected) || !Path.IsPathRooted(selected)) return null;
                var directory = Path.GetDirectoryName(Path.GetFullPath(selected));
                string projectFile = null;
                // Video entries may live below the project root.
                for (var depth = 0; directory != null && depth < 8; depth++, directory = Path.GetDirectoryName(directory))
                {
                    projectFile = SafeAssetPath(directory, "project.json");
                    if (projectFile != null) break;
                }
                if (projectFile == null) return null;
                var project = JObject.Parse(File.ReadAllText(projectFile));
                var type = ((string)project["type"] ?? "").ToLowerInvariant();
                if (type != "scene" && type != "video" && type != "web") return null;
                var entry = SafeAssetPath(directory, (string)project["file"] ?? "");
                if (entry == null && type == "scene") entry = SafeAssetPath(directory, "scene.pkg");
                if (entry == null) return null;
                return new WallpaperProject {
                    DirectoryPath = directory, EntryPath = entry, Type = type,
                    Title = (string)project["title"] ?? Path.GetFileName(directory)
                };
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is Newtonsoft.Json.JsonException || e is ArgumentException || e is NotSupportedException)
            {
                return null;
            }
        }

        public static string SafeAssetPath(string root, string relative)
        {
            try
            {
                relative = Uri.UnescapeDataString(relative ?? "").Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
                if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains(":")) return null;
                var segments = relative.Split(Path.DirectorySeparatorChar);
                if (segments.Any(s => s == ".." || s == ".")) return null;
                root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
                var path = Path.GetFullPath(Path.Combine(root, relative));
                if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(path)) return null;
                // A junction/symlink can escape a lexical directory fence.
                for (var item = path; item != null; item = Path.GetDirectoryName(item))
                    if ((File.GetAttributes(item) & FileAttributes.ReparsePoint) != 0) return null;
                return path;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException || e is NotSupportedException)
            {
                return null;
            }
        }
    }
}
