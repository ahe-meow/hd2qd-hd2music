using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;

namespace Hd2Qd.HD2Music
{
    /// <summary>A plugin of HD2 Mod Quick Deploy: the NetEase Cloud Music helper of the HD2 Music
    /// mod as a window of the manager — sign-in, playlists, search, playback, lyrics, and the
    /// state the mod reads in game. The manager finds the one public class named Plugin in an
    /// HD2QD.*.dll and calls it by name; nothing is shared but strings, numbers, bools, delegates
    /// and Json shapes.</summary>
    public static class Plugin
    {
        /// <summary>The contract's version. A plugin of another one is refused.</summary>
        public const int Api = 2;

        /// <summary>"window": a window of its own, opened from the card (Open).</summary>
        public const string Kind = "window";

        static Engine _engine;
        static Window _window;
        static PlayerView _view;
        static Action _showTab;
        static bool _hooked;

        public static string Title(string lang)
        {
            string title = Words.Get(lang, "title");
            return Ffmpeg.Embedded ? title : title + " Lite";
        }

        public static string Summary(string lang)
        {
            string summary = Words.Get(lang, "summary");
            if (Ffmpeg.Embedded)
            {
                return summary;
            }
            return summary + (Words.IsChinese(lang) ? " 外置解码器版。" : " External decoder edition.");
        }

        /// <summary>Kind "window": the card's Open. In Quick Deploy 0.25.0 and later the player
        /// is a tab of the manager's window (host["tab"]); an older manager gets the same
        /// content in a window with the manager's frame.</summary>
        public static void Open(Window owner, Dictionary<string, object> host)
        {
            if (_showTab != null)
            {
                try
                {
                    _showTab();
                    return;
                }
                catch
                {
                    _showTab = null;
                    _view = null;
                }
            }
            if (_window != null && _window.IsVisible)
            {
                if (_window.WindowState == WindowState.Minimized)
                {
                    _window.WindowState = WindowState.Normal;
                }
                _window.Activate();
                return;
            }

            Engine engine = EnsureEngine(host);
            string title = Title(Words.Lang(host));
            PlayerView view = new PlayerView(engine);
            _view = view;
            if (host != null && host.TryGetValue("tab", out object factory)
                && factory is Func<string, FrameworkElement, Action, Dictionary<string, object>> tab)
            {
                Dictionary<string, object> handle = tab(title, view, delegate
                {
                    _showTab = null;
                    if (_view == view)
                    {
                        _view = null;
                    }
                    view.Stop();
                });
                _showTab = (handle != null && handle.TryGetValue("show", out object show)) ? show as Action : delegate { };
                return;
            }
            _window = PlayerView.ShowWindow(owner, host, view, title);
            _window.Closed += delegate
            {
                _window = null;
                if (_view == view)
                {
                    _view = null;
                }
                view.Stop();
            };
        }

        /// <summary>Optional: commands added to the manager's command line, as rows of [name,
        /// help]. A name the manager has itself is left out.</summary>
        public static readonly string[][] Commands =
        {
            new[] { "hd2music", "NetEase Cloud Music of HD2 Music: status | play | pause | next | prev | stop | search <words> | login | lyrics <0|1> | vol <0-100>" },
        };

        /// <summary>Optional, with Commands: 0 done, 1 refused, 2 a wrong command line.</summary>
        public static int Run(string command, string[] args, TextWriter output, Dictionary<string, object> host)
        {
            Engine engine = EnsureEngine(host);
            string action = (args != null && args.Length != 0) ? args[0].ToLowerInvariant() : "status";
            switch (action)
            {
                case "status":
                {
                    UiState ui = engine.Snapshot();
                    if (engine.IsLockedOut)
                    {
                        output.WriteLine("locked: the NetEase helper of the mod is already running");
                    }
                    output.WriteLine("account: " + (string.IsNullOrEmpty(ui.Account) ? "(not signed in)" : ui.Account));
                    output.WriteLine("state: " + ui.State + "  " + ui.Title + " / " + ui.Artist);
                    output.WriteLine("queue: " + ui.Count + "  position: " + Util.FormatTime(ui.Pos) + " / " + Util.FormatTime(ui.Duration));
                    output.WriteLine("ffmpeg: " + engine.FfmpegPath);
                    output.WriteLine("notice: " + ui.Notice);
                    return 0;
                }
                case "open":
                    output.WriteLine("open the player from the manager's Plugins page; a command line has no window");
                    return 1;
                case "toggle":
                case "play":
                case "pause":
                    engine.Toggle();
                    return 0;
                case "next":
                    engine.Next();
                    return 0;
                case "prev":
                    engine.Previous();
                    return 0;
                case "stop":
                    engine.StopPlayback();
                    return 0;
                case "search":
                    if (args.Length < 2)
                    {
                        output.WriteLine("usage: hd2music search <words>");
                        return 2;
                    }
                    engine.Search(string.Join(" ", args.Skip(1)));
                    return 0;
                case "login":
                    engine.BeginQrLogin();
                    return 0;
                case "lyrics":
                    if (args.Length > 1 && (args[1] == "0" || args[1] == "1"))
                    {
                        engine.SetLyrics(args[1] == "1");
                        return 0;
                    }
                    output.WriteLine("usage: hd2music lyrics 0|1");
                    return 2;
                case "vol":
                    if (args.Length > 1 && int.TryParse(args[1], out int volume))
                    {
                        engine.SetVolume(volume);
                        return 0;
                    }
                    output.WriteLine("usage: hd2music vol 0-100");
                    return 2;
                default:
                    output.WriteLine("usage: hd2music status|play|pause|next|prev|stop|search|login|lyrics|vol");
                    return 2;
            }
        }

        /// <summary>Nothing starts at manager launch: the player takes the mod helper's single
        /// instance lock only when its window or a command actually needs it. The decoder is
        /// written out here, on the manager's background thread, so opening the window does not
        /// wait for 79 MB of ffmpeg.</summary>
        public static bool Start(Dictionary<string, object> host)
        {
            if (host != null && host.ContainsKey("home"))
            {
                try
                {
                    Ffmpeg.Ensure();
                }
                catch
                {
                }
            }
            return false;
        }

        static Engine EnsureEngine(Dictionary<string, object> host)
        {
            if (_engine != null)
            {
                return _engine;
            }
            Ffmpeg.Ensure();
            _engine = new Engine(FindFfmpeg(host));
            _engine.Start();
            HookExit();
            return _engine;
        }

        static void HookExit()
        {
            if (_hooked)
            {
                return;
            }
            _hooked = true;
            try
            {
                if (Application.Current != null)
                {
                    Application.Current.Exit += delegate { DisposeEngine(); };
                }
            }
            catch
            {
            }
            AppDomain.CurrentDomain.ProcessExit += delegate { DisposeEngine(); };
        }

        static void DisposeEngine()
        {
            try
            {
                _engine?.Dispose();
            }
            catch
            {
            }
            _engine = null;
        }

        /// <summary>The mod's package carries tools\ffmpeg.exe; a player who keeps it elsewhere
        /// picks it once on the settings page, and ncm_ffmpeg.txt remembers it. Nothing is
        /// assumed about someone's disks beyond the manager's own folders.</summary>
        static string FindFfmpeg(Dictionary<string, object> host)
        {
            string home = (host != null && host.TryGetValue("home", out object h)) ? h as string : null;
            string saved = Util.ReadText(Path.Combine(Util.Root, "ncm_ffmpeg.txt"))?.Trim();
            var candidates = new List<string>
            {
                Environment.GetEnvironmentVariable("HD2MUSIC_FFMPEG"),
                Environment.GetEnvironmentVariable("NETMUSIC_FFMPEG"),
                saved,
                Path.Combine(Util.PluginDir, "ffmpeg.exe"),
                Path.Combine(Util.PluginDir, "tools", "ffmpeg.exe"),
                Path.Combine(Util.Root, "ffmpeg.exe"),
                Path.Combine(Util.Root, "tools", "ffmpeg.exe"),
                string.IsNullOrEmpty(home) ? null : Path.Combine(home, "ffmpeg.exe"),
                string.IsNullOrEmpty(home) ? null : Path.Combine(home, "tools", "ffmpeg.exe"),
            };
            foreach (string candidate in candidates)
            {
                if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
                {
                    return candidate;
                }
            }
            return Path.Combine(Util.PluginDir, "ffmpeg.exe");
        }
    }
}
