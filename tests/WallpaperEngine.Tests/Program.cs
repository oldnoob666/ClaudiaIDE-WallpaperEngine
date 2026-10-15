using ClaudiaIDE.WallpaperEngine;
using Newtonsoft.Json.Linq;

var root = Path.Combine(Path.GetTempPath(), "claudia-wallpaper-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
int passed = 0;
void Check(bool result, string name) { if (!result) throw new Exception(name); Console.WriteLine("PASS " + name); passed++; }
try
{
    var project = Path.Combine(root, "中文 壁纸");
    Directory.CreateDirectory(project);
    File.WriteAllText(Path.Combine(project, "scene.pkg"), "fixture");
    File.WriteAllText(Path.Combine(project, "project.json"), "{\"type\":\"scene\",\"file\":\"scene.json\",\"title\":\"场景\"}");
    var config = new JObject {
        ["OtherUser"] = new JObject { ["general"] = new JObject { ["wallpaperconfig"] = new JObject { ["selectedwallpapers"] = new JObject { ["Monitor0"] = new JObject { ["file"] = "wrong.pkg" } } } } },
        ["TestUser"] = new JObject { ["general"] = new JObject {
            ["wallpaperconfig"] = new JObject { ["selectedwallpapers"] = new JObject { ["Monitor0"] = new JObject { ["file"] = Path.Combine(project, "scene.pkg") } } },
            ["wallpaperconfigrecent"] = new JArray(new JObject { ["file"] = "old.mp4" }) } }
    };
    File.WriteAllText(Path.Combine(root, "config.json"), config.ToString());
    var resolved = WallpaperProject.Resolve(root, "TestUser", "Monitor0");
    Check(resolved.Type == "scene" && resolved.Title == "场景", "select current user's wallpaper, not other profile/history");
    Check(resolved.EntryPath == Path.Combine(project, "scene.pkg"), "resolve packaged scene when project declares scene.json");
    Check(WallpaperProject.Resolve(root, "MissingUser", "Monitor0") == null, "missing profile never selects another user's wallpaper");
    Check(WallpaperProject.Resolve(root, "TestUser", "Monitor9") == null, "missing monitor never falls back silently");
    Check(WallpaperProject.SafeAssetPath(project, "scene.pkg") == Path.Combine(project, "scene.pkg"), "serve legitimate asset under Chinese path");
    using (var host = new WallpaperHost(project))
    using (var client = new HttpClient())
    {
        Check((await client.GetAsync(host.BaseUrl + "state")).StatusCode == System.Net.HttpStatusCode.Forbidden, "control-plane read requires trusted header");
        var response = await client.GetAsync(host.BaseUrl + "scene.pkg");
        Check(await response.Content.ReadAsStringAsync() == "fixture", "loopback server returns original file bytes");
        using var request = new HttpRequestMessage(HttpMethod.Get, host.BaseUrl + "scene.pkg");
        request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(1, 3);
        using var range = await client.SendAsync(request);
        Check(range.StatusCode == System.Net.HttpStatusCode.PartialContent && await range.Content.ReadAsStringAsync() == "ixt", "video HTTP byte-range returns correct slice");
        using var invalid = new HttpRequestMessage(HttpMethod.Get, host.BaseUrl + "scene.pkg");
        invalid.Headers.TryAddWithoutValidation("Range", "bytes=99999999999999999999999999999-");
        Check((int)(await client.SendAsync(invalid)).StatusCode >= 400, "oversized Range does not crash host");
        Check((await client.GetAsync(host.BaseUrl + "../config.json")).StatusCode == System.Net.HttpStatusCode.NotFound, "HTTP traversal cannot escape session");
    }
    foreach (var bad in new[] { "../config.json", "..\\config.json", "C:\\Windows\\win.ini", "/Windows/win.ini", "file.txt:secret", "%2e%2e/config.json" })
        Check(WallpaperProject.SafeAssetPath(project, bad) == null, "reject asset path " + bad);
    File.WriteAllText(Path.Combine(project, "project.json"), "{\"type\":\"application\",\"file\":\"app.exe\"}");
    Check(WallpaperProject.Resolve(root, "TestUser", "Monitor0") == null, "application wallpaper is not executable content");
    File.WriteAllText(Path.Combine(root, "config.json"), "partial json");
    Check(WallpaperProject.Resolve(root, "TestUser", "Monitor0") == null, "partial config read does not crash editor");
    var nested = Path.Combine(project, "videos"); Directory.CreateDirectory(nested);
    File.WriteAllText(Path.Combine(nested, "a b.mp4"), "nested video");
    File.WriteAllText(Path.Combine(project, "project.json"), "{\"type\":\"video\",\"file\":\"videos/a b.mp4\"}");
    Check(WallpaperProject.ResolveEntry(Path.Combine(nested, "a b.mp4"))?.EntryPath == Path.Combine(nested, "a b.mp4"), "live video entry finds project in parent directory");
    Check(WallpaperProject.ResolveEntry(Path.Combine(project, "lossy-cli-name.mp4"))?.EntryPath == Path.Combine(nested, "a b.mp4"), "project metadata preserves actual filename when WE CLI loses leaf-name encoding");
    config[Environment.UserName] = config["TestUser"].DeepClone();
    File.WriteAllText(Path.Combine(root, "config.json"), config.ToString());
    using (var host = new WallpaperHost(project))
    using (var client = new HttpClient())
    {
        host.Configure(root, "Monitor0");
        client.DefaultRequestHeaders.Add("X-ClaudiaIDE", "1");
        var selected = JObject.Parse(await client.GetStringAsync(host.BaseUrl + "state"));
        var entry = (string)selected["entry"];
        Check(entry == "videos/a b.mp4", "state retains nested project-relative video path");
        var encoded = string.Join("/", entry.Split('/').Select(Uri.EscapeDataString));
        Check(await client.GetStringAsync((string)selected["source"] + encoded) == "nested video", "nested video URL resolves original asset");
    }
    Console.WriteLine($"All {passed} checks passed.");
}
finally { Directory.Delete(root, true); }
