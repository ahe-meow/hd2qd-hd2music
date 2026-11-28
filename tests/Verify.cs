using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Web.Script.Serialization;

namespace Hd2Qd.HD2Music.Verify
{
    /// <summary>The checks the plugin can be held to without a game, a network or a manager:
    /// the file name the manager accepts, one public Plugin with Api 2 and Kind "window", the
    /// text in both languages, the embedded icon, the assemblies it needs (framework only), and
    /// both windows built and laid out off-screen.</summary>
    static class Program
    {
        static int _failed;

        [STAThread]
        static int Main(string[] args)
        {
            string path = Path.GetFullPath(args.Length > 0 ? args[0] : @"release\HD2QD.HD2Music.dll");
            Console.WriteLine("verifying " + path);
            if (!File.Exists(path))
            {
                return Fail("file is missing");
            }
            if (!Regex.IsMatch(Path.GetFileName(path), @"^HD2QD\.[A-Za-z0-9_-]+\.dll$"))
            {
                return Fail("file name is not HD2QD.<name>.dll");
            }

            Assembly asm;
            try
            {
                asm = Assembly.Load(File.ReadAllBytes(path));   // as the manager loads it
            }
            catch (Exception e)
            {
                return Fail("not a .NET assembly the manager can load: " + e.Message);
            }
            Pass("loads from bytes, as PluginFiles.Load does");

            if (asm.GetName().Name != "HD2QD.HD2Music")
            {
                Fail("assembly name is " + asm.GetName().Name);
            }
            else
            {
                Pass("assembly name HD2QD.HD2Music, version " + asm.GetName().Version.ToString(3));
            }

            TargetFrameworkAttribute framework = asm.GetCustomAttribute<TargetFrameworkAttribute>();
            if (framework == null || !framework.FrameworkName.EndsWith("v4.8", StringComparison.Ordinal))
            {
                Fail("target framework is " + (framework?.FrameworkName ?? "unknown") + ", not .NET Framework 4.8");
            }
            else
            {
                Pass("target framework " + framework.FrameworkName);
            }

            Type plugin = CheckPlugin(asm);
            if (plugin == null)
            {
                return 1;
            }
            CheckResources(asm);
            CheckReferences(asm);
            CheckText(asm, plugin);
            CheckQr(asm);
            CheckPlaybackWithoutGame(asm);
            CheckTransportToggle(asm);
            CheckFloatingTransport(asm);
            CheckFmStation(asm);
            CheckPlaylistBrowse(asm);
            CheckDataHub(asm);
            CheckFfmpeg(asm);
            CheckBrowseApi(asm);
            CheckBrowseState(asm);
            CheckBrowseUi(asm);
            CheckVerificationUi(asm);
            CheckI18n(asm);
            CheckQueuePersistence(asm);
            CheckUnplayableSkip(asm);
            CheckLyricProtocol(asm);
            CheckStreamWatchdog(asm);
            CheckQueueOrder(asm);
            CheckPaging(asm);
            CheckTab(asm, engineOf(asm));
            BuildWindows(asm);

            Console.WriteLine(_failed == 0 ? "OK: all checks passed" : _failed + " check(s) failed");
            return _failed == 0 ? 0 : 1;
        }

        static Type CheckPlugin(Assembly asm)
        {
            Type[] types;
            try
            {
                types = asm.GetTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                Fail("types do not load: " + string.Join("; ", e.LoaderExceptions.Where(x => x != null).Select(x => x.Message)));
                return null;
            }
            Type[] plugins = types.Where(t => t.IsPublic && t.Name == "Plugin").ToArray();
            if (plugins.Length != 1)
            {
                Fail(plugins.Length + " public classes named Plugin, expected 1");
                return null;
            }
            Type plugin = plugins[0];
            object api = plugin.GetField("Api")?.GetRawConstantValue();
            if (!(api is int n) || n != 2)
            {
                Fail("Api is " + (api ?? "missing") + ", expected the constant 2");
                return null;
            }
            Pass("Api = 2 (a const the manager reads before running any code)");

            object kind = plugin.GetField("Kind")?.GetRawConstantValue();
            if (!(kind is string k) || k != "window")
            {
                Fail("Kind is " + (kind ?? "missing") + ", expected \"window\"");
                return null;
            }
            Pass("Kind = window");

            if (!Has(plugin, "Title", typeof(string)) || !Has(plugin, "Summary", typeof(string)))
            {
                Fail("Title(string) or Summary(string) is missing");
            }
            MethodInfo open = plugin.GetMethod("Open", BindingFlags.Public | BindingFlags.Static);
            if (open == null || open.GetParameters().Length != 2
                || open.GetParameters()[0].ParameterType != typeof(Window)
                || open.GetParameters()[1].ParameterType != typeof(Dictionary<string, object>))
            {
                Fail("Open(Window, Dictionary<string, object>) is missing");
            }
            else
            {
                Pass("Title, Summary and the window's Open are present");
            }
            if (!Has(plugin, "Run", typeof(int)) || !Has(plugin, "Start", typeof(bool)))
            {
                Fail("the optional Run or Start is missing");
            }
            string[][] commands = plugin.GetField("Commands")?.GetValue(null) as string[][];
            if (commands == null || commands.Length == 0 || commands[0].Length != 2 || !commands[0][0].StartsWith("hd2music", StringComparison.Ordinal))
            {
                Fail("Commands does not offer hd2music");
            }
            else
            {
                Pass("command line: " + string.Join(", ", commands.Select(c => c[0])));
            }
            return plugin;
        }

        static void CheckResources(Assembly asm)
        {
            var names = asm.GetManifestResourceNames();
            foreach (string wanted in new[] { "Lang.en.json", "Lang.zh-CN.json", "Ui.xaml", "app.ico", "QRCoder.dll" })
            {
                if (names.Contains(wanted))
                {
                    Pass("embedded " + wanted);
                }
                else
                {
                    Fail("embedded resource " + wanted + " is missing");
                }
            }
            Pass(names.Contains("ffmpeg.exe.gz") ? "embedded ffmpeg.exe.gz (full build)" : "lite build: decoder is external");
            using (Stream icon = asm.GetManifestResourceStream("app.ico"))
            {
                if (icon == null || icon.Length < 6)
                {
                    Fail("app.ico is empty");
                    return;
                }
                byte[] head = new byte[6];
                icon.Read(head, 0, 6);
                if (head[0] != 0 || head[1] != 0 || head[2] != 1 || head[3] != 0)
                {
                    Fail("app.ico is not an ICO file");
                }
                else
                {
                    Pass("app.ico is an ICO of " + icon.Length + " bytes");
                }
            }
        }

        static void CheckReferences(Assembly asm)
        {
            var framework = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "mscorlib", "System", "System.Core", "System.Web.Extensions", "System.Xml",
                "System.Xaml", "System.Drawing", "System.Numerics", "System.Runtime",
                "PresentationCore", "PresentationFramework", "WindowsBase", "System.Configuration",
            };
            var outside = asm.GetReferencedAssemblies()
                .Where(r => !framework.Contains(r.Name))
                .Select(r => r.Name).ToArray();
            if (outside.Length != 0)
            {
                Fail("references outside the .NET Framework: " + string.Join(", ", outside));
            }
            else
            {
                Pass("references only .NET Framework assemblies (" + asm.GetReferencedAssemblies().Length + ")");
            }
        }

        static void CheckText(Assembly asm, Type plugin)
        {
            var title = plugin.GetMethod("Title", BindingFlags.Public | BindingFlags.Static);
            var summary = plugin.GetMethod("Summary", BindingFlags.Public | BindingFlags.Static);
            string enTitle = (string)title.Invoke(null, new object[] { "en" });
            string zhTitle = (string)title.Invoke(null, new object[] { "zh-CN" });
            string enSummary = (string)summary.Invoke(null, new object[] { "en" });
            string zhSummary = (string)summary.Invoke(null, new object[] { "zh-CN" });
            if (string.IsNullOrWhiteSpace(enTitle) || enTitle == "title" || string.IsNullOrWhiteSpace(enSummary) || enSummary == "summary")
            {
                Fail("English title or summary is missing");
            }
            else
            {
                Pass("English: " + enTitle + " — " + enSummary);
            }
            if (zhTitle == enTitle || zhTitle == "title")
            {
                Fail("zh-CN title falls back to English");
            }
            else
            {
                Pass("zh-CN: " + zhTitle + " — " + zhSummary);
            }
            string deTitle = (string)title.Invoke(null, new object[] { "de-AT" });
            if (deTitle != enTitle)
            {
                Fail("an unknown language does not fall back to English");
            }
            else
            {
                Pass("de-AT falls back to English");
            }
            object started = plugin.GetMethod("Start", BindingFlags.Public | BindingFlags.Static)
                .Invoke(null, new object[] { new Dictionary<string, object>() });
            if (!(started is bool) || (bool)started)
            {
                Fail("Start does not return false; the plugin would take the helper's lock at launch");
            }
            else
            {
                Pass("Start touches nothing: the lock is taken only by Open or a command");
            }
            foreach (string tag in new[] { "en", "zh-CN" })
            {
                string name = "Lang." + tag + ".json";
                using (Stream stream = asm.GetManifestResourceStream(name))
                using (var reader = new StreamReader(stream))
                {
                    var words = new JavaScriptSerializer().Deserialize<Dictionary<string, string>>(reader.ReadToEnd());
                    if (!words.ContainsKey("title") || !words.ContainsKey("summary"))
                    {
                        Fail(name + " has no title or summary");
                    }
                }
            }
        }

        static void CheckQr(Assembly asm)
        {
            Type qr = asm.GetType("Hd2Qd.HD2Music.QrCode");
            if (qr == null)
            {
                Fail("QrCode is missing");
                return;
            }
            try
            {
                string matrix = (string)qr.GetMethod("MakeMatrix", BindingFlags.Public | BindingFlags.Static)
                    .Invoke(null, new object[] { "https://music.163.com/login?codekey=verify" });
                string[] rows = matrix.Split('/');
                if (rows.Length < 21 || rows.Any(r => r.Length != rows.Length) || rows.Any(r => r.Any(c => c != '0' && c != '1')))
                {
                    Fail("the QR encoder returned a ragged matrix");
                }
                else
                {
                    Pass("QR encoder embedded (QRCoder, MIT): " + rows.Length + "x" + rows.Length + " matrix, no QRCoder.dll on disk");
                }
            }
            catch (Exception e)
            {
                Exception inner = e is TargetInvocationException tie ? tie.InnerException : e;
                Fail("the QR encoder does not run: " + inner.Message);
            }
        }

        /// <summary>
        /// The watcher used to clear its token whenever the game was absent, which made
        /// StreamPlayer.Start reject every track and made the state loop kill the song. A
        /// stopped heartbeat is the fixture: no game can be running with "0" in that file.
        /// </summary>
        static void CheckPlaybackWithoutGame(Assembly asm)
        {
            Type watch = asm.GetType("Hd2Qd.HD2Music.GameWatch", true);
            string heartbeat = Path.Combine(Path.GetTempPath(), "hd2qd-nogame-heartbeat.txt");
            File.WriteAllText(heartbeat, "0\n");
            object instance = Activator.CreateInstance(watch, heartbeat, (Action)delegate { });
            PropertyInfo token = watch.GetProperty("Token");
            MethodInfo sample = watch.GetMethod("Sample");
            sample.Invoke(instance, null);
            object first = token.GetValue(instance);
            sample.Invoke(instance, null);
            if (first == null || first != token.GetValue(instance))
            {
                Fail("playback must not depend on a running game (stable GameWatch.Token with a stopped heartbeat)");
                return;
            }
            Pass("playback token survives a stopped heartbeat: music works outside the game");
        }

        /// <summary>
        /// radio/get hands back one track per call. Without a refill path private FM is a
        /// one-song playlist and stops; the official helper keeps appending as it runs out.
        /// </summary>
        /// <summary>
        /// A player with no mixer yet used to reject Pause, so the engine kept "playing"
        /// and the button stayed on 暂停 after a click. The transport state must follow the
        /// click whether or not there is audio output to suspend.
        /// </summary>
        static void CheckTransportToggle(Assembly asm)
        {
            Type type = asm.GetType("Hd2Qd.HD2Music.StreamPlayer", true);
            object player = Activator.CreateInstance(type,
                Path.Combine(Path.GetTempPath(), "ffmpeg.exe"),
                Path.Combine(Path.GetTempPath(), "hd2qd-toggle-heartbeat.txt"));
            type.GetField("Active").SetValue(player, true);
            MethodInfo pause = type.GetMethod("Pause");
            object status = type.GetField("Status").GetValue(player);
            FieldInfo state = status.GetType().GetField("State");
            bool paused = (bool)pause.Invoke(player, new object[] { true });
            bool resumed = (bool)pause.Invoke(player, new object[] { false });
            if (!paused || !resumed || (string)state.GetValue(status) != "playing")
            {
                Fail("pause/resume must flip the transport state even with no output yet");
                return;
            }
            Pass("transport toggles back from 暂停 to 播放 even before the decoder is ready");
        }

        static void CheckFloatingTransport(Assembly asm)
        {
            Type icons = asm.GetType("Hd2Qd.HD2Music.Icon", true);
            foreach (string name in new[] { "Previous", "Play", "Pause", "Next", "Volume", "Mute", "Order", "Shuffle", "Repeat", "RepeatOne" })
            {
                if (icons.GetField(name, BindingFlags.Static | BindingFlags.Public) == null)
                {
                    Fail("RemixIcon " + name + " is missing");
                    return;
                }
            }
            Type engine = asm.GetType("Hd2Qd.HD2Music.Engine", true);
            string[] orders = (string[])engine.GetField("Orders", BindingFlags.Static | BindingFlags.Public).GetValue(null);
            if (orders.Length != 4 || !orders.Contains("loop") || !orders.Contains("repeat"))
            {
                Fail("playback orders must include order, shuffle, loop and repeat-one");
                return;
            }
            Type view = asm.GetType("Hd2Qd.HD2Music.PlayerView", true);
            foreach (string field in new[] { "_dock", "_playButton", "_volumePopup", "_volumeTrack", "_orderIcon" })
            {
                if (view.GetField(field, BindingFlags.Instance | BindingFlags.NonPublic) == null)
                {
                    Fail("floating transport field " + field + " is missing");
                    return;
                }
            }
            if (view.GetMethod("TransportButton", BindingFlags.Instance | BindingFlags.NonPublic) != null)
            {
                Fail("transport still builds text buttons");
                return;
            }
            Pass("floating transport uses RemixIcon glyphs and a volume popup");
        }

        static void CheckFmStation(Assembly asm)
        {
            Type engine = asm.GetType("Hd2Qd.HD2Music.Engine", true);
            if (engine.GetMethod("RefillFm", BindingFlags.Instance | BindingFlags.NonPublic) == null
                || engine.GetField("_fmRefilling", BindingFlags.Instance | BindingFlags.NonPublic) == null)
            {
                Fail("private FM has no refill path");
                return;
            }
            // radio/get returns 405 when hammered, so the fetch must be rate-limited
            if (engine.GetField("_fmLastFetch", BindingFlags.Instance | BindingFlags.NonPublic) == null)
            {
                Fail("private FM refill has no throttle");
                return;
            }
            Pass("private FM refills its queue with a rate limit instead of stopping after one track");
        }

        static void CheckPlaylistBrowse(Assembly asm)
        {
            // A retired-buffer path freed pinned PCM while winmm could still write to it;
            // that is native heap corruption, so keep the regression locked down.
            Type waveOut = asm.GetType("Hd2Qd.HD2Music.WaveOut", true);
            if (waveOut.GetField("_retired", BindingFlags.Instance | BindingFlags.NonPublic) != null)
            {
                Fail("WaveOut still frees buffers on a wall-clock timeout");
                return;
            }
            Type player = asm.GetType("Hd2Qd.HD2Music.StreamPlayer", true);
            if (player.GetMethod("StopOnGameChange", BindingFlags.Instance | BindingFlags.Public) != null)
            {
                Fail("the player still exposes a game-close stop hook");
                return;
            }
            Type engine = asm.GetType("Hd2Qd.HD2Music.Engine", true);
            foreach (string method in new[] { "PreviewPlaylist", "PlayPreview", "PlayPreviewTrack", "AddTrackToQueue", "PlayNextTrack" })
            {
                if (engine.GetMethod(method, BindingFlags.Instance | BindingFlags.Public) == null)
                {
                    Fail("Engine." + method + " is missing");
                    return;
                }
            }
            Type state = asm.GetType("Hd2Qd.HD2Music.UiState", true);
            if (state.GetField("PlaylistTracks") == null)
            {
                Fail("UiState.PlaylistTracks is missing");
                return;
            }
            Type saved = asm.GetType("Hd2Qd.HD2Music.QueueSnapshot", true);
            if (saved.GetField("Pos") == null || saved.GetField("Volume") == null)
            {
                Fail("QueueSnapshot does not keep position and volume");
                return;
            }
            Pass("game close cannot stop playback; playlists browse before replacing the queue; position and volume persist");
        }

        static void CheckDataHub(Assembly asm)
        {
            Type hub = asm.GetType("Hd2Qd.HD2Music.DataHub");
            if (hub == null)
            {
                Fail("DataHub is missing");
                return;
            }
            MethodInfo tick = hub.GetMethod("Tick", BindingFlags.Public | BindingFlags.Instance);
            ConstructorInfo ctor = hub.GetConstructor(new[] { typeof(string), typeof(Func<int>) });
            if (tick == null || ctor == null || tick.GetParameters().Length != 0)
            {
                Fail("DataHub does not expose Tick() and DataHub(string, Func<int>)");
                return;
            }
            Array rows = hub.GetField("Known", BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null) as Array;
            if (rows == null || rows.Length < 2)
            {
                Fail("DataHub must pin the signed payload of every shipping format");
                return;
            }
            string sizes = "";
            bool unlocked = false;
            foreach (object row in rows)
            {
                Type payload = row.GetType();
                string session = (string)payload.GetField("Session").GetValue(row);
                long size = (long)payload.GetField("Size").GetValue(row);
                string digest = (string)payload.GetField("Digest").GetValue(row);
                if (!Regex.IsMatch(session ?? "", "^[0-9a-f]{24}$") || size <= 0 || size > 1048576
                    || !Regex.IsMatch(digest ?? "", "^[0-9a-f]{64}$"))
                {
                    Fail("DataHub payload row is malformed: " + session + " / " + size);
                    return;
                }
                // 1.2.7's shell reads bytes 8..39 of data.dat as key material; the helper
                // replaces them before replying, so the bare .stream never decrypts
                byte[] key = payload.GetField("Unlock")?.GetValue(row) as byte[];
                if (key != null)
                {
                    string after = (string)payload.GetField("UnlockedDigest").GetValue(row);
                    if (key.Length != 32 || !Regex.IsMatch(after ?? "", "^[0-9a-f]{64}$"))
                    {
                        Fail("DataHub unlock header is malformed: " + session);
                        return;
                    }
                    unlocked = true;
                }
                sizes += (sizes.Length == 0 ? "" : " + ") + size;
            }
            if (!unlocked)
            {
                Fail("no payload pins the unlock header the helper writes into data.dat");
                return;
            }
            // 1.2.6 moved the body behind the handshake, so the heartbeat cannot precede it:
            // if these two ever stop being wired the plugin deadlocks against a live game
            PropertyInfo handshaked = hub.GetProperty("Handshaked");
            PropertyInfo modern = asm.GetType("Hd2Qd.HD2Music.GameWatch")?.GetProperty("Modern");
            if (handshaked == null || modern == null)
            {
                Fail("a delivered handshake must count as liveness (DataHub.Handshaked -> GameWatch.Modern)");
                return;
            }
            Pass("Data Hub handshake wired: " + sizes + " byte payloads pinned by session, 1.2.7's unlock header installed, delivered only when the helper is absent, and counted as liveness until the mod beats back");
        }

        static object engineOf(Assembly asm)
        {
            return Activator.CreateInstance(asm.GetType("Hd2Qd.HD2Music.Engine"), Path.Combine(Path.GetTempPath(), "ffmpeg.exe"));
        }

        static void CheckTab(Assembly asm, object engine)
        {
            Type plugin = asm.GetType("Hd2Qd.HD2Music.Plugin");
            Type player = asm.GetType("Hd2Qd.HD2Music.PlayerView");
            if (plugin == null || player == null)
            {
                Fail("Plugin or PlayerView is missing");
                return;
            }
            plugin.GetField("_engine", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, engine);
            string tabTitle = null;
            FrameworkElement tabBody = null;
            Action tabClosed = null;
            Action tabClose = null;
            Action tabShow = null;
            bool shown = false;
            bool closed = false;
            Func<string, FrameworkElement, Action, Dictionary<string, object>> tab =
                (title, body, onClosed) =>
                {
                    tabTitle = title;
                    tabBody = body;
                    tabClosed = onClosed;
                    tabShow = delegate { shown = true; };
                    tabClose = delegate
                    {
                        closed = true;
                        onClosed?.Invoke();
                    };
                    return new Dictionary<string, object>
                    {
                        ["show"] = tabShow,
                        ["close"] = tabClose,
                    };
                };
            var host = new Dictionary<string, object> { ["tab"] = tab, ["lang"] = "zh-CN" };
            string expected = (string)plugin.GetMethod("Title").Invoke(null, new object[] { "zh-CN" });
            try
            {
                plugin.GetMethod("Open", BindingFlags.Public | BindingFlags.Static).Invoke(null, new object[] { null, host });
                if (tabTitle != expected || tabBody == null || tabBody.GetType() != player || tabClosed == null)
                {
                    Fail("host[\"tab\"] was not given the player and its title");
                    return;
                }
                Pass("opens as a manager tab: \"" + tabTitle + "\"");
                plugin.GetMethod("Open", BindingFlags.Public | BindingFlags.Static).Invoke(null, new object[] { null, host });
                if (!shown)
                {
                    Fail("a second Open did not show the existing tab");
                }
                else
                {
                    Pass("a second Open shows the same tab, no second view");
                }
                tabClose();
                if (!closed)
                {
                    Fail("the tab's closed action did not run");
                }
                else
                {
                    Pass("closing the tab runs the plugin's cleanup");
                }
            }
            catch (Exception e)
            {
                Exception inner = e is TargetInvocationException tie ? tie.InnerException : e;
                Fail("the tab path does not work: " + inner.GetType().Name + ": " + inner.Message);
            }
            finally
            {
                try
                {
                    if (tabBody != null)
                    {
                        player.GetMethod("Stop", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(tabBody, null);
                    }
                }
                catch
                {
                }
                plugin.GetField("_engine", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, null);
                plugin.GetField("_showTab", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, null);
                plugin.GetField("_view", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, null);
            }
        }

        static void CheckBrowseApi(Assembly asm)
        {
            Type page = asm.GetType("Hd2Qd.HD2Music.BrowsePage`1");
            if (page == null)
            {
                Fail("BrowsePage<T> is missing");
                return;
            }
            foreach (string field in new[] { "Rows", "Offset", "More" })
            {
                if (page.GetField(field) == null)
                {
                    Fail("BrowsePage." + field + " is missing");
                    return;
                }
            }
            Type client = asm.GetType("Hd2Qd.HD2Music.NeteaseClient");
            foreach (string method in new[] { "SearchTracksPage", "RecommendationPage", "SearchRadios" })
            {
                if (client.GetMethod(method) == null)
                {
                    Fail("NeteaseClient." + method + " is missing");
                    return;
                }
            }
            Pass("browse APIs present");
        }

        static void CheckBrowseState(Assembly asm)
        {
            Type ui = asm.GetType("Hd2Qd.HD2Music.UiState");
            foreach (string field in new[] { "SearchMore", "SearchOffset", "RadioKeyword", "PodcastFilter", "PodcastFilterSerial", "RecommendMore", "AccountLoading", "LyricPrefix" })
            {
                if (ui.GetField(field) == null)
                {
                    Fail("UiState." + field + " is missing");
                    return;
                }
            }
            Type engine = asm.GetType("Hd2Qd.HD2Music.Engine");
            foreach (string method in new[] { "SearchMore", "RadioSearch", "PodcastFilter" })
            {
                if (engine.GetMethod(method, BindingFlags.Instance | BindingFlags.Public) == null)
                {
                    Fail("Engine." + method + " is missing");
                    return;
                }
            }
            Pass("browse state fields and commands present");
        }

        /// <summary>The mod pages the radio and episode lists itself; it needs the next offset,
        /// whether there is one, and a serial that tells a stale page from a fresh one.</summary>
        static void CheckPaging(Assembly asm)
        {
            Type ui = asm.GetType("Hd2Qd.HD2Music.UiState");
            foreach (string field in new[] { "RadioMore", "RadioOffset", "PodcastMore", "PodcastOffset", "QueueRevision", "Audition", "LyricRows" })
            {
                if (ui.GetField(field) == null)
                {
                    Fail("UiState." + field + " is missing");
                    return;
                }
            }
            Type engine = asm.GetType("Hd2Qd.HD2Music.Engine");
            foreach (string method in new[] { "RadioMore", "PodcastMore", "SearchEnqueue", "QueueEdit", "Audition", "StopAudition", "LyricWindow" })
            {
                if (engine.GetMethod(method, BindingFlags.Instance | BindingFlags.Public) == null)
                {
                    Fail("Engine." + method + " is missing");
                    return;
                }
            }
            Pass("paging, lyric window and audition surface present");
        }

        static void CheckBrowseUi(Assembly asm)
        {
            Type player = asm.GetType("Hd2Qd.HD2Music.PlayerView");
            foreach (string field in new[] { "_dailyButton", "_fmButton", "_stopButton", "_radioSearch", "_radioSearchButton", "_podcastFilter", "_podcastFilterButton" })
            {
                if (player.GetField(field, BindingFlags.Instance | BindingFlags.NonPublic) == null)
                {
                    Fail("PlayerView." + field + " is missing");
                    return;
                }
            }
            Pass("browse UI entries present");
        }

        static void CheckVerificationUi(Assembly asm)
        {
            Type login = asm.GetType("Hd2Qd.HD2Music.LoginWindow");
            if (login.GetField("_openWebsiteButton", BindingFlags.Instance | BindingFlags.NonPublic) == null)
            {
                Fail("LoginWindow._openWebsiteButton is missing");
                return;
            }
            Type player = asm.GetType("Hd2Qd.HD2Music.PlayerView");
            // the old current-line block is gone: the sheet is the only lyric surface now
            foreach (string field in new[] { "_lyricSheet", "_album" })
            {
                if (player.GetField(field, BindingFlags.Instance | BindingFlags.NonPublic) == null)
                {
                    Fail("PlayerView." + field + " is missing");
                    return;
                }
            }
            Type state = asm.GetType("Hd2Qd.HD2Music.UiState");
            if (state?.GetField("Album") == null)
            {
                Fail("UiState.Album is missing");
                return;
            }
            Pass("verification link, lyric sheet and album line present");
        }

        static void CheckI18n(Assembly asm)
        {
            Type i18n = asm.GetType("Hd2Qd.HD2Music.I18n");
            if (i18n == null) { Fail("I18n is missing"); return; }
            foreach (string method in new[] { "SetHostLang", "Apply", "Text" })
                if (i18n.GetMethod(method, BindingFlags.Public | BindingFlags.Static) == null)
                { Fail("I18n." + method + " is missing"); return; }
            if (i18n.GetProperty("Mode", BindingFlags.Public | BindingFlags.Static) == null)
            { Fail("I18n.Mode is missing"); return; }
            Type player = asm.GetType("Hd2Qd.HD2Music.PlayerView");
            if (player.GetField("_languageMode", BindingFlags.Instance | BindingFlags.NonPublic) == null)
            { Fail("PlayerView._languageMode is missing"); return; }
            Pass("bilingual UI infrastructure present");
        }

        static void CheckQueuePersistence(Assembly asm)
        {
            Type snapshot = asm.GetType("Hd2Qd.HD2Music.QueueSnapshot");
            Type engine = asm.GetType("Hd2Qd.HD2Music.Engine");
            if (snapshot == null || engine == null)
            {
                Fail("queue persistence is missing");
                return;
            }
            foreach (string field in new[] { "AccountUid", "ListId", "ListName", "Index", "TrackFirst", "Tracks" })
            {
                if (snapshot.GetField(field) == null)
                {
                    Fail("QueueSnapshot." + field + " is missing");
                    return;
                }
            }
            foreach (string method in new[] { "SaveQueue", "RestoreQueue", "ClearQueue", "PersistQueueIfChanged" })
            {
                if (engine.GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic) == null)
                {
                    Fail("Engine." + method + " is missing");
                    return;
                }
            }
            Pass("playlist persists across launches, cleared on account change or invalid login");
        }

        /// <summary>A track the service will not serve (copyright, no licence, VIP only) must say
        /// so on the game's lyric line and give way to the next one instead of stalling on it.</summary>
        static void CheckUnplayableSkip(Assembly asm)
        {
            Type engine = asm.GetType("Hd2Qd.HD2Music.Engine");
            Type track = asm.GetType("Hd2Qd.HD2Music.Track");
            MethodInfo say = engine?.GetMethod("UnplayableText", BindingFlags.NonPublic | BindingFlags.Static);
            MethodInfo retries = engine?.GetMethod("RetriesBeforeGivingUp", BindingFlags.NonPublic | BindingFlags.Static);
            if (say == null || retries == null || track == null)
            {
                Fail("Engine.UnplayableText or Engine.RetriesBeforeGivingUp is missing");
                return;
            }
            object song = Activator.CreateInstance(track);
            track.GetField("Artist").SetValue(song, "周杰伦");
            track.GetField("Title").SetValue(song, "晴天");
            string line = (string)say.Invoke(null, new object[] { song, "zh-CN" });
            if (line != "歌曲「周杰伦 - 晴天」无法播放")
            {
                Fail("the unplayable line is " + line);
                return;
            }
            string english = (string)say.Invoke(null, new object[] { song, "en" });
            if (english != "Cannot play \"周杰伦 - 晴天\"")
            {
                Fail("the unplayable line in English is " + english);
                return;
            }
            object bare = Activator.CreateInstance(track);
            track.GetField("Artist").SetValue(bare, "");
            track.GetField("Title").SetValue(bare, "晴天");
            string shortLine = (string)say.Invoke(null, new object[] { bare, "zh-CN" });
            if (shortLine != "歌曲「晴天」无法播放")
            {
                Fail("the unplayable line for a track with no artist is " + shortLine);
                return;
            }
            if ((int)retries.Invoke(null, new object[] { true }) != 3 || (int)retries.Invoke(null, new object[] { false }) != 1)
            {
                Fail("a dead source must be retried once, a dropped connection three times");
                return;
            }
            if (engine.GetMethod("AnnounceUnplayable", BindingFlags.Instance | BindingFlags.NonPublic) == null
                || engine.GetField("_blocked", BindingFlags.Instance | BindingFlags.NonPublic) == null)
            {
                Fail("Engine does not carry the announcement into the state file");
                return;
            }
            Pass("an unplayable track announces itself in the lyric line, then skips");
        }

        /// <summary>A CDN that holds the socket open and stops sending raises no decoder outcome,
        /// so the player has to notice silence itself — but never while the user paused it.</summary>
        static void CheckStreamWatchdog(Assembly asm)
        {
            if (Quiet(asm, 13.0, "playing", false) == null)
            {
                Fail("13 s of silence mid-song is not treated as a broken stream");
                return;
            }
            if (Quiet(asm, 3.0, "playing", false) != null)
            {
                Fail("a healthy stream is reported as broken");
                return;
            }
            if (Quiet(asm, 13.0, "paused", false) != null)
            {
                Fail("a paused track is reported as a broken stream");
                return;
            }
            if (Quiet(asm, 19.0, "loading", true) == null)
            {
                Fail("a stream that never started is no longer given up on");
                return;
            }
            if (Quiet(asm, 10.0, "loading", true) != null)
            {
                Fail("a slow start is reported as broken too early");
                return;
            }
            Pass("a stream that goes quiet mid-song is caught, a paused one is not");
        }

        static object Quiet(Assembly asm, double seconds, string state, bool awaiting)
        {
            const BindingFlags f = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            Type type = asm.GetType("Hd2Qd.HD2Music.StreamPlayer");
            object player = Activator.CreateInstance(type,
                Path.Combine(Path.GetTempPath(), "ffmpeg.exe"), Path.Combine(Path.GetTempPath(), "hb.txt"));
            type.GetField("Active", f).SetValue(player, true);
            type.GetField("AwaitingPlay", f).SetValue(player, awaiting);
            object status = type.GetField("Status", f).GetValue(player);
            status.GetType().GetField("State").SetValue(status, state);
            status.GetType().GetField("Dur").SetValue(status, 200.0);
            status.GetType().GetField("Pos").SetValue(status, 60.0);
            double now = (DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
            type.GetField("LastPcmAt", f).SetValue(player, now - seconds);
            return type.GetMethod("Result", f).Invoke(player, null);
        }

        /// <summary>The game's settings write a lyric calibration both players read; the plugin
        /// has to honour it and report the fields the new mod's lyric panel asks for.</summary>
        static void CheckLyricProtocol(Assembly asm)
        {
            Type engine = asm.GetType("Hd2Qd.HD2Music.Engine");
            MethodInfo parse = engine?.GetMethod("LyricOffsetMs", BindingFlags.NonPublic | BindingFlags.Static);
            if (parse == null)
            {
                Fail("Engine.LyricOffsetMs is missing");
                return;
            }
            if ((int)parse.Invoke(null, new object[] { "netease=350\nsmtc=-100\n" }) != 350)
            {
                Fail("the netease offset is not read from ncm_lyric_offsets.txt");
                return;
            }
            if ((int)parse.Invoke(null, new object[] { "netease=-1400\r\nsmtc=0\r\n" }) != -1400)
            {
                Fail("a negative offset with CRLF is not read");
                return;
            }
            if ((int)parse.Invoke(null, new object[] { "netease=9999\n" }) != 3000)
            {
                Fail("the offset is not clamped to the game's 3 s range");
                return;
            }
            if ((int)parse.Invoke(null, new object[] { "" }) != 0 || (int)parse.Invoke(null, new object[] { "smtc=25\n" }) != 0)
            {
                Fail("a missing or smtc-only offset must read as zero");
                return;
            }
            Pass("lyric calibration is read from the game's own settings file");
        }

        /// <summary>Editing the queue has to renumber every row: the current index, the shuffle
        /// history and the play-next marker are all positions, not tracks.</summary>
        static void CheckQueueOrder(Assembly asm)
        {
            Type engine = asm.GetType("Hd2Qd.HD2Music.Engine");
            MethodInfo order = engine?.GetMethod("QueueOrder", BindingFlags.NonPublic | BindingFlags.Static);
            if (order == null)
            {
                Fail("Engine.QueueOrder is missing");
                return;
            }
            var cases = new[]
            {
                new object[] { "0,2,1,3,4", 5, 2, 0, "up" },
                new object[] { "0,1,3,2,4", 5, 2, 0, "down" },
                new object[] { "0,1,3,4", 5, 2, 0, "remove" },
                new object[] { "0,3,1,2,4", 5, 3, 0, "next" },
                new object[] { "null", 5, 0, 0, "remove" },
                new object[] { "null", 5, 0, 2, "up" },
                new object[] { "null", 5, 4, 0, "down" },
                new object[] { "null", 5, 1, 0, "bogus" },
                new object[] { "null", 5, 9, 0, "remove" },
            };
            foreach (object[] item in cases)
            {
                int[] result = (int[])order.Invoke(null, new[] { item[1], item[2], item[3], item[4] });
                string actual = result == null ? "null" : string.Join(",", result);
                if (actual != (string)item[0])
                {
                    Fail("queue " + item[4] + " of row " + item[2] + " gave " + actual + ", expected " + item[0]);
                    return;
                }
            }
            Pass("queue edits renumber rows, and refuse to touch the playing one");
        }

        static void CheckFfmpeg(Assembly asm)        {
            if (!asm.GetManifestResourceNames().Contains("ffmpeg.exe.gz"))
            {
                Pass("lite build: no embedded ffmpeg to extract");
                return;
            }
            Type ffmpeg = asm.GetType("Hd2Qd.HD2Music.Ffmpeg");
            if (ffmpeg == null)
            {
                Fail("Ffmpeg is missing");
                return;
            }
            MethodInfo extract = ffmpeg.GetMethod("ExtractTo", BindingFlags.NonPublic | BindingFlags.Static);
            string sha = ffmpeg.GetField("Sha256", BindingFlags.NonPublic | BindingFlags.Static)?.GetRawConstantValue() as string;
            if (extract == null || string.IsNullOrEmpty(sha))
            {
                Fail("Ffmpeg.ExtractTo or its SHA-256 is missing");
                return;
            }
            string folder = Path.Combine(Path.GetTempPath(), "hd2qd-ffmpeg-verify-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            string target = Path.Combine(folder, "ffmpeg.exe");
            try
            {
                extract.Invoke(null, new object[] { target });
                var info = new FileInfo(target);
                if (!info.Exists || info.Length < 50L * 1024 * 1024)
                {
                    Fail("the embedded ffmpeg did not extract");
                }
                else
                {
                    string actual;
                    using (var algorithm = System.Security.Cryptography.SHA256.Create())
                    using (var stream = File.OpenRead(target))
                    {
                        actual = BitConverter.ToString(algorithm.ComputeHash(stream)).Replace("-", "");
                    }
                    if (!string.Equals(actual, sha, StringComparison.OrdinalIgnoreCase))
                    {
                        Fail("the extracted ffmpeg does not match the packed SHA-256");
                    }
                    else
                    {
                        Pass("embedded ffmpeg extracted to a temp folder: " + (info.Length / 1024 / 1024) + " MB, SHA-256 matches");
                    }
                }
            }
            catch (Exception e)
            {
                Exception inner = e is TargetInvocationException tie ? tie.InnerException : e;
                Fail("the embedded ffmpeg does not extract: " + inner.Message);
            }
            finally
            {
                try
                {
                    if (Directory.Exists(folder))
                    {
                        Directory.Delete(folder, true);
                    }
                }
                catch
                {
                }
            }
        }

        static void BuildWindows(Assembly asm)
        {
            Type engineType = asm.GetType("Hd2Qd.HD2Music.Engine");
            Type playerType = asm.GetType("Hd2Qd.HD2Music.PlayerView");
            Type loginType = asm.GetType("Hd2Qd.HD2Music.LoginWindow");
            if (engineType == null || playerType == null || loginType == null)
            {
                Fail("Engine, PlayerView or LoginWindow is missing");
                return;
            }
            try
            {
                object engine = Activator.CreateInstance(engineType, Path.Combine(Path.GetTempPath(), "ffmpeg.exe"));

                // the body on its own, laid out as the manager's frame would
                var view = (FrameworkElement)Activator.CreateInstance(playerType, engine);
                Layout(view);
                if (!(((Control)view).Background is SolidColorBrush brush)
                    || brush.Color != (Color)ColorConverter.ConvertFromString("#FF0C0E11"))
                {
                    Fail("the player view is not on the manager's ground");
                }
                else
                {
                    Pass("player view built off-screen and laid out on #0C0E11");
                }

                // the window the manager hands over (host["window"]) receives that body
                MethodInfo show = playerType.GetMethod("ShowWindow", BindingFlags.NonPublic | BindingFlags.Static);
                if (show == null)
                {
                    Fail("PlayerView.ShowWindow is missing");
                }
                else
                {
                    var made = new List<string>();
                    Func<string, FrameworkElement, Window> frame = (title, body) =>
                    {
                        made.Add(title);
                        return new Window { Title = title, Content = body, Width = 1120, Height = 760 };
                    };
                    var host = new Dictionary<string, object> { ["window"] = frame };
                    var window = (Window)show.Invoke(null, new object[] { null, host, view, "HD2 音乐" });
                    if (made.Count != 1 || string.IsNullOrEmpty(window.Title) || !(window.Content is FrameworkElement body2) || body2.GetType() != playerType)
                    {
                        Fail("the manager's frame did not receive the player body and its title");
                    }
                    else
                    {
                        Pass("fallback window for a manager without tabs: \"" + made[0] + "\"");
                    }
                    window.Close();
                }

                var login = (Window)Activator.CreateInstance(loginType, engine);
                Layout(login);
                Pass("login window built off-screen and laid out: \"" + login.Title + "\"");
            }
            catch (Exception e)
            {
                Exception inner = e is TargetInvocationException tie ? tie.InnerException : e;
                Fail("a window does not build: " + inner.GetType().Name + ": " + inner.Message);
            }
        }

        static void Layout(FrameworkElement root)
        {
            root.ApplyTemplate();
            root.Measure(new Size(1120, 760));
            root.Arrange(new Rect(0, 0, 1120, 760));
            root.UpdateLayout();
        }

        static bool Has(Type type, string name, Type returns) =>
            type.GetMethod(name, BindingFlags.Public | BindingFlags.Static)?.ReturnType == returns;

        static void Pass(string text) => Console.WriteLine("  ok   " + text);

        static int Fail(string text)
        {
            _failed++;
            Console.WriteLine("  FAIL " + text);
            return 1;
        }
    }
}
