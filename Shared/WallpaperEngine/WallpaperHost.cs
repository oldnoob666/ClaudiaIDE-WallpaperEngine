using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using Newtonsoft.Json;

namespace ClaudiaIDE.WallpaperEngine
{
    // Shared by all editor views. Only the read-only getWallpaper command is used.
    internal sealed class WallpaperHost : IDisposable
    {
        private static readonly Lazy<WallpaperHost> Shared = new Lazy<WallpaperHost>(() => new WallpaperHost());
        public static WallpaperHost Instance => Shared.Value;
        private readonly HttpListener listener = new HttpListener();
        private readonly string session = Guid.NewGuid().ToString("N");
        private readonly Dictionary<string, WallpaperProject> projects = new Dictionary<string, WallpaperProject>();
        private readonly object gate = new object();
        private readonly string rendererDirectory;
        private string installDirectory = "";
        private string configuredDirectory;
        private string monitor = "Monitor0";
        private object state;
        private DateTime checkedAt;
        private bool disposed;
        public string BaseUrl { get; }
        public string PlayerUrl => BaseUrl + "player.html";

        internal WallpaperHost(string rendererDirectory = null)
        {
            this.rendererDirectory = rendererDirectory ?? Path.Combine(Path.GetDirectoryName(typeof(WallpaperHost).Assembly.Location), "WallpaperEngine");
            // A dynamic loopback port avoids firewall rules and URL reservations.
            for (var attempt = 0; ; attempt++)
            {
                var probe = new TcpListener(IPAddress.Loopback, 0);
                probe.Start();
                var port = ((IPEndPoint)probe.LocalEndpoint).Port;
                probe.Stop();
                listener.Prefixes.Clear();
                listener.Prefixes.Add($"http://127.0.0.1:{port}/");
                try { listener.Start(); BaseUrl = $"http://127.0.0.1:{port}/{session}/"; break; }
                catch (HttpListenerException) when (attempt < 2) { }
            }
            _ = Task.Run(AcceptAsync);
            AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
        }

        public void Configure(string directory, string monitorName)
        {
            lock (gate)
            {
                directory = (directory ?? "").Trim();
                monitorName = string.IsNullOrWhiteSpace(monitorName) ? "Monitor0" : monitorName.Trim();
                if (configuredDirectory == directory && monitor == monitorName) return;
                configuredDirectory = directory;
                installDirectory = directory == "" ? DiscoverInstall() : directory; monitor = monitorName;
                state = null;
                checkedAt = DateTime.MinValue;
            }
        }

        internal static string DiscoverInstall()
        {
            var roots = new List<string>();
            using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
            {
                var steam = key?.GetValue("SteamPath") as string;
                if (!string.IsNullOrWhiteSpace(steam)) roots.Add(steam);
            }
            roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"));
            foreach (var root in roots.ToArray())
            {
                var vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
                if (!File.Exists(vdf)) continue;
                foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s*\"([^\"]+)\""))
                    roots.Add(match.Groups[1].Value.Replace(@"\\", @"\"));
            }
            return roots.Select(r => Path.Combine(r, "steamapps", "common", "wallpaper_engine"))
                .FirstOrDefault(r => File.Exists(Path.Combine(r, "config.json"))) ?? "";
        }

        private string GetState()
        {
            lock (gate)
            {
                if (state != null && DateTime.UtcNow - checkedAt < TimeSpan.FromSeconds(2)) return JsonConvert.SerializeObject(state);
                checkedAt = DateTime.UtcNow;
                var live = IsEngineRunning();
                var selected = live ? QueryLiveWallpaper() : null;
                var project = live ? WallpaperProject.ResolveEntry(selected)
                    : WallpaperProject.Resolve(installDirectory, Environment.UserName, monitor);
                // A transient CLI timeout should keep the last successfully queried
                // wallpaper, rather than jump to stale saved configuration.
                if (live && selected == null && state != null) return JsonConvert.SerializeObject(state);
                if (project == null)
                    state = new { id = "", error = "未找到当前壁纸。请检查 Wallpaper Engine 安装目录、显示器名称和当前壁纸类型。" };
                else
                {
                    string id;
                    using (var hash = SHA256.Create())
                        id = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(project.EntryPath))).Replace("-", "").ToLowerInvariant();
                    if (!projects.ContainsKey(id))
                    {
                        if (projects.Count >= 8) projects.Remove(projects.Keys.First());
                        projects[id] = project;
                    }
                    state = new { id, type = project.Type, title = project.Title, source = BaseUrl + "asset/" + id + "/", entry = project.EntryPath.Substring(project.DirectoryPath.Length + 1).Replace('\\', '/') };
                }
                return JsonConvert.SerializeObject(state);
            }
        }

        private bool IsEngineRunning()
        {
            foreach (var name in new[] { "wallpaper64", "wallpaper32" })
                foreach (var process in Process.GetProcessesByName(name))
                    using (process)
                    {
                        try
                        {
                            if (string.Equals(Path.GetDirectoryName(process.MainModule.FileName), installDirectory.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase)) return true;
                        }
                        catch (Exception e) when (e is System.ComponentModel.Win32Exception || e is InvalidOperationException) { }
                    }
            return false;
        }

        // Called only by the loopback HTTP worker, never by the VS UI thread.
#pragma warning disable VSTHRD002
        private string QueryLiveWallpaper()
        {
            var match = System.Text.RegularExpressions.Regex.Match(monitor, @"^(?:Monitor)?(\d+)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (!match.Success) return null;
            var engine = Path.Combine(installDirectory, "wallpaper64.exe");
            if (!File.Exists(engine)) engine = Path.Combine(installDirectory, "wallpaper32.exe");
            var helper = Path.Combine(rendererDirectory, "WallpaperQuery.exe");
            if (!File.Exists(engine) || !File.Exists(helper)) return null;
            try
            {
                using (var process = Process.Start(new ProcessStartInfo(helper, "\"" + engine + "\" " + match.Groups[1].Value) {
                    UseShellExecute = false, RedirectStandardOutput = true,
                    StandardOutputEncoding = new UTF8Encoding(false), WindowStyle = ProcessWindowStyle.Hidden
                }))
                {
                    var output = process.StandardOutput.ReadToEndAsync();
                    if (!process.WaitForExit(3500)) { process.Kill(); return null; }
                    return process.ExitCode == 0 && output.Wait(500) ? output.Result.Trim() : null;
                }
            }
            catch (Exception e) when (e is IOException || e is System.ComponentModel.Win32Exception || e is InvalidOperationException) { return null; }
        }
#pragma warning restore VSTHRD002

        private async Task AcceptAsync()
        {
            while (!disposed)
            {
                HttpListenerContext context;
                try { context = await listener.GetContextAsync().ConfigureAwait(false); }
                catch (Exception e) when (e is HttpListenerException || e is ObjectDisposedException) { break; }
                _ = Task.Run(() => RespondAsync(context));
            }
        }

        private async Task RespondAsync(HttpListenerContext context)
        {
            var response = context.Response;
            try
            {
                response.Headers["X-Content-Type-Options"] = "nosniff";
                response.Headers["Referrer-Policy"] = "no-referrer";
                var request = context.Request;
                if (request.HttpMethod != "GET" && request.HttpMethod != "HEAD") { response.StatusCode = 405; return; }
                var path = request.Url.AbsolutePath;
                var prefix = "/" + session + "/";
                if (!path.StartsWith(prefix, StringComparison.Ordinal)) { response.StatusCode = 404; return; }
                path = path.Substring(prefix.Length);
                if (path == "state")
                {
                    // Opaque wallpaper frames cannot read the control plane.
                    if (request.Headers["X-ClaudiaIDE"] != "1") { response.StatusCode = 403; return; }
                    response.ContentType = "application/json; charset=utf-8";
                    response.Headers["Cache-Control"] = "no-store";
                    var data = Encoding.UTF8.GetBytes(GetState());
                    response.ContentLength64 = data.Length;
                    if (request.HttpMethod == "GET") await response.OutputStream.WriteAsync(data, 0, data.Length).ConfigureAwait(false);
                    return;
                }
                string file;
                if (path.StartsWith("asset/", StringComparison.Ordinal))
                {
                    var parts = path.Split(new[] { '/' }, 3);
                    WallpaperProject project;
                    lock (gate) projects.TryGetValue(parts.Length > 1 ? parts[1] : "", out project);
                    file = project == null || parts.Length < 3 ? null : WallpaperProject.SafeAssetPath(project.DirectoryPath, parts[2]);
                    response.Headers["Access-Control-Allow-Origin"] = "*"; // strict sandbox frames request assets with Origin:null.
                }
                else file = WallpaperProject.SafeAssetPath(rendererDirectory, path);
                if (file == null) { response.StatusCode = 404; return; }
                response.ContentType = ContentType(file);
                response.Headers["Cache-Control"] = "no-cache";
                using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    long start = 0, end = stream.Length - 1;
                    var range = request.Headers["Range"];
                    if (!string.IsNullOrEmpty(range))
                    {
                        var match = System.Text.RegularExpressions.Regex.Match(range, @"^bytes=(\d*)-(\d*)$");
                        if (!match.Success || (match.Groups[1].Value == "" && match.Groups[2].Value == "")) { response.StatusCode = 416; return; }
                        if (match.Groups[1].Value == "") start = Math.Max(0, stream.Length - long.Parse(match.Groups[2].Value));
                        else start = long.Parse(match.Groups[1].Value);
                        if (match.Groups[1].Value != "" && match.Groups[2].Value != "") end = Math.Min(end, long.Parse(match.Groups[2].Value));
                        if (start > end || start >= stream.Length) { response.StatusCode = 416; response.Headers["Content-Range"] = "bytes */" + stream.Length; return; }
                        response.StatusCode = 206;
                        response.Headers["Content-Range"] = $"bytes {start}-{end}/{stream.Length}";
                    }
                    response.Headers["Accept-Ranges"] = "bytes";
                    long remaining = Math.Max(0, end - start + 1);
                    response.ContentLength64 = remaining;
                    if (request.HttpMethod == "HEAD") return;
                    stream.Position = start;
                    var buffer = new byte[65536];
                    while (remaining > 0)
                    {
                        var read = await stream.ReadAsync(buffer, 0, (int)Math.Min(buffer.Length, remaining)).ConfigureAwait(false);
                        if (read == 0) break;
                        await response.OutputStream.WriteAsync(buffer, 0, read).ConfigureAwait(false);
                        remaining -= read;
                    }
                }
            }
            catch (Exception e) when (e is IOException || e is HttpListenerException || e is ObjectDisposedException || e is UnauthorizedAccessException || e is FormatException || e is OverflowException)
            {
                try { response.StatusCode = 500; } catch { }
            }
            finally { try { response.Close(); } catch { } }
        }

        private static string ContentType(string file)
        {
            switch (Path.GetExtension(file).ToLowerInvariant())
            {
                case ".html": return "text/html; charset=utf-8";
                case ".js": case ".mjs": return "text/javascript; charset=utf-8";
                case ".json": return "application/json; charset=utf-8";
                case ".css": return "text/css";
                case ".png": return "image/png";
                case ".jpg": case ".jpeg": return "image/jpeg";
                case ".gif": return "image/gif";
                case ".mp4": return "video/mp4";
                case ".webm": return "video/webm";
                case ".mp3": return "audio/mpeg";
                case ".ogg": return "audio/ogg";
                case ".svg": return "image/svg+xml";
                case ".woff2": return "font/woff2";
                default: return "application/octet-stream";
            }
        }

        private void OnProcessExit(object sender, EventArgs e) => Dispose();
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            listener.Close();
            AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
        }
    }
}
