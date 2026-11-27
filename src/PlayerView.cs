using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Hd2Qd.HD2Music
{
    /// <summary>The plugin's window body: a console of the manager's own parts — a command bar,
    /// the now-playing panel with its lyrics and spectrum, a rail of what plays next and the
    /// player's state, and four tables (library, queue, search, radio). Cards are grid-lined
    /// ground, labels are small caps, numbers are mono, and the only accent is the manager's
    /// yellow.</summary>
    public sealed class PlayerView : UserControl
    {
        readonly Engine _engine;
        readonly DispatcherTimer _tick;
        readonly DispatcherTimer _spectrumTick;
        readonly List<TabRef> _tabs = new List<TabRef>();

        Grid _pages;
        string _page = "console";

        Border _led;
        TextBlock _stateText;
        Button _accountButton;
        Border _sheet;
        Grid _overlay;

        Image _cover;
        TextBlock _title;
        TextBlock _artist;
        TextBlock _meta;
        TextBlock _previous;
        TextBlock _lyric;

        TextBlock _lyricPrefixLine;
        TextBlock _translation;
        TextBlock _next;
        TextBlock _notice;
        TextBlock _time;
        TextBlock _duration;
        Slider _progress;
        Slider _volume;
        Button _play;
        ListBox _lyricSheet;
        Button _auditionButton;
        bool _auditioning;
        string _lyricSheetSignature = "";
        int _lyricShown = -1;
        SpectrumBars _spectrum;
        ListBox _peek;
        TextBlock _peekCount;
        TextBlock _sysState;
        TextBlock _sysLyrics;
        TextBlock _sysQueue;
        TextBlock _sysFfmpeg;
        TextBlock _sysHelper;
        TextBlock _sysPlaylist;
        TextBlock _sysTrack;

        WrapPanel _cards;
        TextBlock _libraryCount;
        TextBlock _libraryEmpty;
        Button _recommendedButton;
        bool _showRecommended;
        string _librarySignature = "";

        /// <summary>Width the queue rows give their action column; the header uses the same,
        /// or the two columns drift apart.</summary>
        const double EditWidth = 100.0;

        ListBox _queue;
        TextBlock _queueCount;
        TextBlock _queueEmpty;
        string _queueSignature = "";

        TextBox _search;

        Button _dailyButton;

        Button _fmButton;

        Button _stopButton;

        TextBox _radioSearch;

        Button _radioSearchButton;

        TextBox _podcastFilter;

        Button _podcastFilterButton;

        StackPanel _languageMode;

        string _appliedLang = "";

        Dictionary<string, object> _host;
        ListBox _results;
        TextBlock _searchEmpty;
        TextBlock _searchHint;
        string _searchSignature = "";

        ListBox _channels;
        ListBox _episodes;
        TextBlock _channelEmpty;
        TextBlock _episodeEmpty;
        TextBlock _episodeTitle;
        Button _channelMore;
        Button _episodeMore;
        string _radioSignature = "";
        string _podcastId = "";
        bool _suppressRadio;

        TextBlock _ffmpeg;
        readonly List<Segment> _qualitySegments = new List<Segment>();
        readonly List<Segment> _orderSegments = new List<Segment>();
        CheckBox _lyricsTick;
        CheckBox _automixTick;
        TextBlock _sheetAccount;
        TextBlock _sheetLock;

        TextBlock _statusNotice;
        TextBlock _statusAccount;
        TextBlock _statusQueue;
        TextBlock _statusQuality;
        TextBlock _statusFfmpeg;

        bool _draggingProgress;
        bool _suppressVolume;
        bool _stopped;
        bool _initialized;
        string _coverUrl = "";
        LoginWindow _login;
        Window _window;

        public PlayerView(Engine engine) : this(engine, (string)null)
        {
        }

        public PlayerView(Engine engine, Dictionary<string, object> host) : this(engine, Words.Lang(host))
        {
            _host = host;
        }

        public PlayerView(Engine engine, string hostLang)
        {
            _engine = engine;
            I18n.SetHostLang(hostLang);
            Background = Theme.Ground900;
            Foreground = Theme.Ink;
            FontFamily = Theme.Font("Ui");
            FontSize = 13.0;

            Grid root = new Grid
            {
                Background = Theme.Ground900,
                RowDefinitions =
                {
                    new RowDefinition { Height = new GridLength(58.0) },
                    new RowDefinition { Height = new GridLength(1.0, GridUnitType.Star) },
                    new RowDefinition { Height = new GridLength(26.0) }
                }
            };
            root.Children.Add(BuildTopBar());
            _pages = BuildPages();
            Grid.SetRow(_pages, 1);
            root.Children.Add(_pages);
            FrameworkElement status = BuildStatusBar();
            Grid.SetRow(status, 2);
            root.Children.Add(status);

            _overlay = BuildSettingsSheet();
            Grid.SetRowSpan(_overlay, 3);
            root.Children.Add(_overlay);

            Content = root;
            ApplyLanguage();

            _tick = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300.0) };
            _tick.Tick += delegate { Refresh(); };
            _spectrumTick = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80.0) };
            _spectrumTick.Tick += delegate { PumpSpectrum(); };

            Loaded += delegate
            {
                if (!_initialized)
                {
                    _initialized = true;
                    ShowPage("console");
                }
                _tick.Start();
                _spectrumTick.Start();
                Window host = Window.GetWindow(this);
                if (host != null && host != _window)
                {
                    if (_window != null)
                    {
                        _window.PreviewKeyDown -= OnWindowKey;
                    }
                    _window = host;
                    _window.PreviewKeyDown += OnWindowKey;
                }
                Refresh();
            };
        }

        /// <summary>Stops the clocks and lets go of the mod's playlist covers. Called when the
        /// manager closes the tab (its ×, Ctrl+W, the program ending); idempotent.</summary>
        internal void Stop()
        {
            if (_stopped)
            {
                return;
            }
            _stopped = true;
            _tick.Stop();
            _spectrumTick.Stop();
            if (_window != null)
            {
                _window.PreviewKeyDown -= OnWindowKey;
                _window = null;
            }
            try
            {
                if (_login != null && _login.IsVisible)
                {
                    _login.Close();
                }
            }
            catch
            {
            }
            try
            {
                _engine.SetVisiblePlaylists(null);
            }
            catch
            {
            }
        }

        /// <summary>A manager before 0.25.0 (no host["tab"]): a window with the manager's frame,
        /// or a plain one when there is no such factory.</summary>
        internal static Window ShowWindow(Window owner, Dictionary<string, object> host, FrameworkElement content, string title)
        {
            Window window;
            if (host != null && host.TryGetValue("window", out object factory)
                && factory is Func<string, FrameworkElement, Window> make)
            {
                window = make(title, content);
            }
            else
            {
                window = new Window
                {
                    Title = title,
                    Content = content,
                    Background = Theme.Ground900,
                    Foreground = Theme.Ink,
                    WindowStartupLocation = (owner != null) ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen
                };
                if (owner != null)
                {
                    window.Owner = owner;
                }
            }
            window.Width = 1180.0;
            window.Height = 780.0;
            window.MinWidth = 980.0;
            window.MinHeight = 640.0;
            window.Show();
            window.Activate();
            return window;
        }

        void OnWindowKey(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape && _overlay.Visibility == Visibility.Visible)
            {
                CloseSheet();
                e.Handled = true;
            }
        }

        // ---------------------------------------------------------------- command bar

        FrameworkElement BuildTopBar()
        {
            Border bar = new Border
            {
                Background = Theme.Ground700,
                BorderBrush = Theme.Grid,
                BorderThickness = new Thickness(0.0, 0.0, 0.0, 1.0)
            };
            Grid grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            StackPanel brand = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(18.0, 0.0, 0.0, 0.0),
                VerticalAlignment = VerticalAlignment.Center
            };
            brand.Children.Add(new Border
            {
                Width = 10.0,
                Height = 10.0,
                Background = Theme.Yellow,
                Margin = new Thickness(0.0, 0.0, 10.0, 0.0),
                VerticalAlignment = VerticalAlignment.Center
            });
            StackPanel brandWords = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            brandWords.Children.Add(new TextBlock
            {
                Text = "HD2 MUSIC",
                FontSize = 16.0,
                FontWeight = FontWeights.Bold,
                Foreground = Theme.Ink
            });
            brandWords.Children.Add(new TextBlock
            {
                Text = "NETEASE · MANAGER LINK",
                FontFamily = Theme.Font("Mono"),
                FontSize = 8.5,
                Foreground = Theme.Muted,
                Margin = new Thickness(0.0, 1.0, 0.0, 0.0)
            });
            brand.Children.Add(brandWords);
            grid.Children.Add(brand);

            StackPanel tabs = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(34.0, 0.0, 0.0, 0.0)
            };
            tabs.Children.Add(Tab("console", "CONSOLE", "控制台：正在播放"));
            tabs.Children.Add(Tab("library", "LIBRARY", "音乐库：歌单与推荐"));
            tabs.Children.Add(Tab("queue", "QUEUE", "播放队列"));
            tabs.Children.Add(Tab("search", "SEARCH", "搜索歌曲"));
            tabs.Children.Add(Tab("radio", "RADIO", "电台与播客"));
            Grid.SetColumn(tabs, 1);
            grid.Children.Add(tabs);

            StackPanel right = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0.0, 0.0, 14.0, 0.0)
            };
            _led = new Border
            {
                Width = 7.0,
                Height = 7.0,
                Background = Theme.Muted,
                Margin = new Thickness(0.0, 0.0, 8.0, 0.0),
                VerticalAlignment = VerticalAlignment.Center
            };
            right.Children.Add(_led);
            _stateText = new TextBlock
            {
                Text = "IDLE",
                FontFamily = Theme.Font("Mono"),
                FontSize = 10.0,
                Foreground = Theme.Muted,
                Width = 58.0,
                VerticalAlignment = VerticalAlignment.Center
            };
            right.Children.Add(_stateText);
            _accountButton = new Button
            {
                Content = "未登录",
                Style = Theme.S("Ghost"),
                BorderThickness = new Thickness(0.0),
                Height = 34.0,
                MinHeight = 34.0,
                Padding = new Thickness(10.0, 0.0, 10.0, 0.0),
                FontSize = 12.0,
                MaxWidth = 170.0,
                ToolTip = "登录 / 切换账号"
            };
            _accountButton.Click += delegate { OpenLogin(); };
            right.Children.Add(_accountButton);
            Button gear = new Button
            {
                Content = "SET",
                Style = Theme.S("Ghost"),
                BorderThickness = new Thickness(0.0),
                Width = 46.0,
                Height = 34.0,
                MinHeight = 34.0,
                Padding = new Thickness(0.0),
                FontSize = 9.5,
                FontWeight = FontWeights.SemiBold,
                ToolTip = "设置"
            };
            gear.Click += delegate { OpenSheet(); };
            right.Children.Add(gear);
            Grid.SetColumn(right, 2);
            grid.Children.Add(right);

            bar.Child = grid;
            return bar;
        }

        sealed class TabRef
        {
            public string Page;
            public Button Button;
            public Border Line;
        }

        FrameworkElement Tab(string page, string label, string tip)
        {
            Grid host = new Grid { Height = 58.0, ToolTip = tip, Margin = new Thickness(0.0, 0.0, 2.0, 0.0) };
            Button button = new Button
            {
                Content = label,
                Style = Theme.S("Ghost"),
                BorderThickness = new Thickness(0.0),
                Background = Brushes.Transparent,
                Padding = new Thickness(13.0, 0.0, 13.0, 0.0),
                FontSize = 11.0,
                FontWeight = FontWeights.SemiBold,
                Foreground = Theme.Muted,
                VerticalAlignment = VerticalAlignment.Stretch
            };
            Border line = new Border
            {
                Height = 2.0,
                Background = Brushes.Transparent,
                VerticalAlignment = VerticalAlignment.Bottom
            };
            button.Click += delegate { ShowPage(page); };
            button.MouseEnter += delegate
            {
                if (_page != page)
                {
                    button.Foreground = Theme.Ink;
                }
            };
            button.MouseLeave += delegate
            {
                if (_page != page)
                {
                    button.Foreground = Theme.Muted;
                }
            };
            host.Children.Add(button);
            host.Children.Add(line);
            _tabs.Add(new TabRef { Page = page, Button = button, Line = line });
            return host;
        }

        FrameworkElement BuildStatusBar()
        {
            Border bar = new Border
            {
                Background = Theme.Ground700,
                BorderBrush = Theme.Grid,
                BorderThickness = new Thickness(0.0, 1.0, 0.0, 0.0)
            };
            DockPanel dock = new DockPanel { Margin = new Thickness(14.0, 0.0, 14.0, 0.0), LastChildFill = false };
            TextBlock version = new TextBlock
            {
                Text = "HD2 MOD QUICK DEPLOY · API 2 · v1.7.5 · " + I18n.Text(Ffmpeg.Embedded ? "FULL" : "LITE"),
                FontFamily = Theme.Font("Mono"),
                FontSize = 9.0,
                Foreground = Theme.Muted,
                VerticalAlignment = VerticalAlignment.Center
            };
            DockPanel.SetDock(version, Dock.Right);
            dock.Children.Add(version);

            StackPanel left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            _statusNotice = StatusPart(left, "STATUS —");
            _statusNotice.MaxWidth = 420.0;
            _statusNotice.FontSize = 9.5;
            _statusNotice.FontWeight = FontWeights.SemiBold;
            _statusNotice.TextTrimming = TextTrimming.CharacterEllipsis;
            _statusAccount = StatusPart(left, "ACCOUNT —");
            _statusQueue = StatusPart(left, "QUEUE 0");
            _statusQuality = StatusPart(left, "QUALITY —");
            _statusFfmpeg = StatusPart(left, "FFMPEG —");
            dock.Children.Add(left);

            bar.Child = dock;
            return bar;
        }

        static TextBlock StatusPart(StackPanel row, string text)
        {
            if (row.Children.Count > 0)
            {
                row.Children.Add(new Border
                {
                    Width = 1.0,
                    Height = 10.0,
                    Background = Theme.Grid,
                    Margin = new Thickness(12.0, 0.0, 12.0, 0.0),
                    VerticalAlignment = VerticalAlignment.Center
                });
            }
            TextBlock block = new TextBlock
            {
                Text = text,
                FontFamily = Theme.Font("Mono"),
                FontSize = 9.0,
                Foreground = Theme.Muted,
                VerticalAlignment = VerticalAlignment.Center
            };
            row.Children.Add(block);
            return block;
        }

        // ---------------------------------------------------------------- pages

        Grid BuildPages()
        {
            Grid pages = new Grid { Margin = new Thickness(18.0, 16.0, 18.0, 14.0) };
            pages.Children.Add(ConsolePage());
            pages.Children.Add(LibraryPage());
            pages.Children.Add(QueuePage());
            pages.Children.Add(SearchPage());
            pages.Children.Add(RadioPage());
            return pages;
        }

        FrameworkElement ConsolePage()
        {
            Grid page = new Grid();
            page.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
            page.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16.0) });
            page.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(344.0) });

            Border heroCard = Card(BuildHero());
            heroCard.Padding = new Thickness(18.0);
            page.Children.Add(heroCard);

            Grid rail = new Grid();
            rail.RowDefinitions.Add(new RowDefinition { Height = new GridLength(300.0) });
            rail.RowDefinitions.Add(new RowDefinition { Height = new GridLength(16.0) });
            rail.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.0, GridUnitType.Star) });
            rail.Children.Add(BuildQueueCard());
            FrameworkElement system = BuildSystemCard();
            Grid.SetRow(system, 2);
            rail.Children.Add(system);
            Grid.SetColumn(rail, 2);
            page.Children.Add(rail);
            return page;
        }

        FrameworkElement BuildHero()
        {
            Grid hero = new Grid();
            hero.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            hero.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.0, GridUnitType.Star) });
            hero.RowDefinitions.Add(new RowDefinition { Height = new GridLength(34.0) });
            hero.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            Grid top = new Grid();
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(302.0) });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22.0) });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });

            Grid coverHost = new Grid { Background = Theme.Ground900, ClipToBounds = true };
            coverHost.Children.Add(new TextBlock
            {
                Text = "♪",
                FontSize = 64.0,
                Foreground = Theme.Grid,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            });
            _cover = new Image { Stretch = Stretch.UniformToFill };
            coverHost.Children.Add(_cover);
            top.Children.Add(new Border
            {
                Width = 302.0,
                Height = 302.0,
                BorderBrush = Theme.Grid,
                BorderThickness = new Thickness(1.0),
                Child = coverHost
            });

            StackPanel info = new StackPanel { VerticalAlignment = VerticalAlignment.Top };
            info.Children.Add(SectionLabel("NOW PLAYING"));
            _title = new TextBlock
            {
                Text = "未选歌曲",
                FontSize = 26.0,
                FontWeight = FontWeights.SemiBold,
                Foreground = Theme.Ink,
                TextWrapping = TextWrapping.Wrap,
                MaxHeight = 76.0,
                Margin = new Thickness(0.0, 8.0, 0.0, 4.0)
            };
            info.Children.Add(_title);
            _artist = new TextBlock
            {
                Text = "",
                FontSize = 14.0,
                Foreground = Theme.Muted,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            info.Children.Add(_artist);
            _meta = new TextBlock
            {
                Text = "",
                FontFamily = Theme.Font("Mono"),
                FontSize = 9.5,
                Foreground = Theme.Muted,
                Margin = new Thickness(0.0, 10.0, 0.0, 0.0),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            info.Children.Add(_meta);
            info.Children.Add(new Border
            {
                Height = 1.0,
                Background = Theme.Grid,
                Margin = new Thickness(0.0, 14.0, 0.0, 12.0)
            });
            _notice = new TextBlock
            {
                Text = "",
                FontSize = 11.0,
                Foreground = Theme.Yellow,
                TextWrapping = TextWrapping.Wrap,
                MaxHeight = 52.0
            };
            info.Children.Add(_notice);
            Grid.SetColumn(info, 2);
            top.Children.Add(info);
            hero.Children.Add(top);

            StackPanel lyrics = new StackPanel();
            lyrics.Children.Add(SectionLabel("LYRICS"));
            _previous = new TextBlock
            {
                Text = "",
                FontSize = 11.0,
                Foreground = Theme.Muted,
                Opacity = 0.55,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0.0, 8.0, 0.0, 0.0)
            };
            lyrics.Children.Add(_previous);
            _lyric = new TextBlock
            {
                Text = "未在播放",
                FontSize = 13.0,
                Foreground = Theme.Muted,
                TextWrapping = TextWrapping.Wrap,
                MaxHeight = 96.0,
                Margin = new Thickness(0.0, 6.0, 0.0, 4.0)
            };
            lyrics.Children.Add(_lyric);
            _lyricPrefixLine = new TextBlock
            {
                Text = "",
                FontSize = 12.0,
                FontFamily = Theme.Font("Mono"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0.0, 0.0, 0.0, 4.0)
            };
            lyrics.Children.Add(_lyricPrefixLine);
            _translation = new TextBlock
            {
                Text = "",
                FontSize = 13.0,
                Foreground = Theme.Muted,
                TextWrapping = TextWrapping.Wrap,
                MaxHeight = 44.0
            };
            lyrics.Children.Add(_translation);
            _next = new TextBlock
            {
                Text = "",
                FontSize = 11.5,
                Foreground = Theme.Blue,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0.0, 8.0, 0.0, 0.0)
            };
            lyrics.Children.Add(_next);
            Border lyricBox = new Border
            {
                Background = Theme.Ground900,
                BorderBrush = Theme.Grid,
                BorderThickness = new Thickness(1.0),
                Padding = new Thickness(18.0, 14.0, 18.0, 14.0),
                Margin = new Thickness(0.0, 16.0, 0.0, 8.0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = lyrics
            };
            Grid lyricRow = new Grid();
            lyricRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
            lyricRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16.0) });
            lyricRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300.0) });
            lyricRow.Children.Add(lyricBox);
            FrameworkElement sheet = BuildLyricSheet();
            Grid.SetColumn(sheet, 2);
            lyricRow.Children.Add(sheet);
            Grid.SetRow(lyricRow, 1);
            hero.Children.Add(lyricRow);

            Grid spectrumRow = new Grid { Margin = new Thickness(0.0, 6.0, 0.0, 0.0) };
            spectrumRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80.0) });
            spectrumRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
            spectrumRow.Children.Add(new TextBlock
            {
                Text = "SPECTRUM",
                FontFamily = Theme.Font("Mono"),
                FontSize = 8.5,
                Foreground = Theme.Muted,
                VerticalAlignment = VerticalAlignment.Center
            });
            _spectrum = new SpectrumBars { Height = 20.0, MaxWidth = 520.0, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(_spectrum, 1);
            spectrumRow.Children.Add(_spectrum);
            Grid.SetRow(spectrumRow, 2);
            hero.Children.Add(spectrumRow);

            StackPanel bottom = new StackPanel { Margin = new Thickness(0.0, 4.0, 0.0, 0.0) };
            Grid progressRow = new Grid();
            progressRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
            progressRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _progress = new Slider
            {
                Minimum = 0.0,
                Maximum = 1.0,
                IsMoveToPointEnabled = true,
                Height = 22.0,
                VerticalAlignment = VerticalAlignment.Center,
                Style = Theme.S("Range")
            };
            _progress.PreviewMouseDown += delegate { _draggingProgress = true; };
            _progress.PreviewMouseUp += delegate
            {
                _draggingProgress = false;
                _engine.Seek(_progress.Value);
            };
            progressRow.Children.Add(_progress);
            StackPanel times = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12.0, 0.0, 0.0, 0.0)
            };
            _time = Mono("0:00", 10.0, Theme.Ink);
            _duration = Mono(" / 0:00", 10.0, Theme.Muted);
            times.Children.Add(_time);
            times.Children.Add(_duration);
            Grid.SetColumn(times, 1);
            progressRow.Children.Add(times);
            bottom.Children.Add(progressRow);

            Grid transport = new Grid { Margin = new Thickness(0.0, 6.0, 0.0, 0.0) };
            transport.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            transport.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
            transport.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            StackPanel controls = new StackPanel { Orientation = Orientation.Horizontal };
            controls.Children.Add(TransportButton("PREV", delegate { _engine.Previous(); }, false));
            _play = TransportButton("PLAY", delegate { _engine.Toggle(); }, true);
            controls.Children.Add(_play);
            controls.Children.Add(TransportButton("NEXT", delegate { _engine.Next(); }, false));
            transport.Children.Add(controls);

            StackPanel volume = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };
            volume.Children.Add(new TextBlock
            {
                Text = "VOL",
                FontFamily = Theme.Font("Mono"),
                FontSize = 8.5,
                Foreground = Theme.Muted,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0.0, 0.0, 8.0, 0.0)
            });
            _volume = new Slider
            {
                Minimum = 0.0,
                Maximum = 100.0,
                Width = 110.0,
                Height = 22.0,
                VerticalAlignment = VerticalAlignment.Center,
                Style = Theme.S("Range")
            };
            _volume.ValueChanged += delegate
            {
                if (!_suppressVolume)
                {
                    _engine.SetVolume((int)_volume.Value);
                }
            };
            volume.Children.Add(_volume);
            Grid.SetColumn(volume, 2);
            transport.Children.Add(volume);
            bottom.Children.Add(transport);
            Grid.SetRow(bottom, 3);
            hero.Children.Add(bottom);
            return hero;
        }

        Button TransportButton(string text, Action action, bool primary)
        {
            Button button = new Button
            {
                Content = text,
                Style = Theme.S(primary ? "Primary" : "Ghost"),
                Width = primary ? 68.0 : 58.0,
                Height = 34.0,
                MinHeight = 34.0,
                Padding = new Thickness(0.0),
                FontSize = 10.0,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0.0, 0.0, 6.0, 0.0)
            };
            button.Click += delegate
            {
                try
                {
                    action();
                }
                catch
                {
                }
            };
            return button;
        }

        FrameworkElement BuildQueueCard()
        {
            Border card = Card(null);
            Grid inner = new Grid();
            inner.RowDefinitions.Add(new RowDefinition { Height = new GridLength(38.0) });
            inner.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.0, GridUnitType.Star) });
            inner.RowDefinitions.Add(new RowDefinition { Height = new GridLength(38.0) });
            Border header = new Border { Padding = new Thickness(14.0, 0.0, 14.0, 0.0), BorderBrush = Theme.Grid, BorderThickness = new Thickness(0.0, 0.0, 0.0, 1.0) };
            DockPanel head = new DockPanel { LastChildFill = false };
            head.Children.Add(SectionLabel("UP NEXT"));
            _peekCount = Mono("0", 9.0, Theme.Muted);
            _peekCount.Margin = new Thickness(8.0, 0.0, 0.0, 0.0);
            DockPanel.SetDock(_peekCount, Dock.Right);
            head.Children.Add(_peekCount);
            header.Child = head;
            inner.Children.Add(header);

            _peek = new ListBox
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0.0),
                ItemContainerStyle = Theme.S("Row"),
                Margin = new Thickness(2.0, 4.0, 2.0, 4.0)
            };
            Grid.SetRow(_peek, 1);
            inner.Children.Add(_peek);

            Border footer = new Border { Padding = new Thickness(8.0), BorderBrush = Theme.Grid, BorderThickness = new Thickness(0.0, 1.0, 0.0, 0.0) };
            Button all = new Button
            {
                Content = "打开播放队列 →",
                Style = Theme.S("Ghost"),
                BorderThickness = new Thickness(0.0),
                Height = 30.0,
                MinHeight = 30.0,
                FontSize = 11.0,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            all.Click += delegate { ShowPage("queue"); };
            footer.Child = all;
            Grid.SetRow(footer, 2);
            inner.Children.Add(footer);
            card.Child = inner;
            return card;
        }

        FrameworkElement BuildSystemCard()
        {
            Border card = Card(null);
            StackPanel body = new StackPanel { Margin = new Thickness(14.0, 12.0, 14.0, 12.0) };
            body.Children.Add(SectionLabel("SYSTEM"));
            _sysState = SysRow(body, "STATE");
            body.Children.Add(SysSegmentRow("QUALITY", SegmentRow(Engine.Qualities, QualityName, _qualitySegments, delegate(string value)
            {
                _engine.SetQuality(value);
            }, compact: true)));
            body.Children.Add(SysSegmentRow("ORDER", SegmentRow(Engine.Orders, OrderName, _orderSegments, delegate(string value)
            {
                _engine.SetOrder(value);
            }, compact: true)));
            _sysLyrics = SysRow(body, "LYRICS");
            _sysQueue = SysRow(body, "QUEUE");
            _sysPlaylist = SysRow(body, "PLAYLIST");
            _sysTrack = SysRow(body, "TRACK");
            _sysFfmpeg = SysRow(body, "FFMPEG");
            _sysHelper = SysRow(body, "HELPER");
            Button diagnostics = GhostButton("导出诊断", delegate { _engine.ExportDiagnostics(); });
            diagnostics.Height = 26.0;
            diagnostics.MinHeight = 26.0;
            diagnostics.FontSize = 10.0;
            diagnostics.Padding = new Thickness(0.0);
            diagnostics.Margin = new Thickness(0.0, 12.0, 0.0, 0.0);
            diagnostics.ToolTip = "把当前状态写成 ncm_diagnostics.txt，排查问题时附上这个文件";
            body.Children.Add(diagnostics);
            card.Child = body;
            return card;
        }

        static FrameworkElement SysSegmentRow(string label, UIElement control)
        {
            Grid row = new Grid { Margin = new Thickness(0.0, 6.0, 0.0, 0.0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(76.0) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
            row.Children.Add(new TextBlock
            {
                Text = label,
                FontFamily = Theme.Font("Mono"),
                FontSize = 8.5,
                Foreground = Theme.Muted,
                VerticalAlignment = VerticalAlignment.Center
            });
            Grid.SetColumn(control, 1);
            row.Children.Add(control);
            return row;
        }

        static TextBlock SysRow(StackPanel body, string label)
        {
            Grid row = new Grid { Margin = new Thickness(0.0, 7.0, 0.0, 0.0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(76.0) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
            row.Children.Add(new TextBlock
            {
                Text = label,
                FontFamily = Theme.Font("Mono"),
                FontSize = 8.5,
                Foreground = Theme.Muted,
                VerticalAlignment = VerticalAlignment.Center
            });
            TextBlock value = new TextBlock
            {
                Text = "—",
                FontFamily = Theme.Font("Mono"),
                FontSize = 10.0,
                Foreground = Theme.Ink,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(value, 1);
            row.Children.Add(value);
            body.Children.Add(row);
            return value;
        }

        FrameworkElement LibraryPage()
        {
            Grid page = new Grid();
            page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.0, GridUnitType.Star) });

            DockPanel header = new DockPanel { Margin = new Thickness(0.0, 0.0, 0.0, 12.0), LastChildFill = false };
            StackPanel title = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            title.Children.Add(SectionLabel("LIBRARY"));
            _libraryCount = Mono("0", 9.0, Theme.Muted);
            _libraryCount.Margin = new Thickness(8.0, 0.0, 0.0, 0.0);
            title.Children.Add(_libraryCount);
            TextBlock libraryHint = new TextBlock
            {
                Text = "单击歌单查看曲目",
                FontFamily = Theme.Font("Mono"),
                FontSize = 8.5,
                Foreground = Theme.Muted,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12.0, 0.0, 0.0, 0.0)
            };
            title.Children.Add(libraryHint);
            header.Children.Add(title);

            StackPanel actions = new StackPanel { Orientation = Orientation.Horizontal };
            _recommendedButton = GhostButton("推荐歌单", delegate
            {
                _showRecommended = !_showRecommended;
                _librarySignature = "";
                if (_showRecommended)
                {
                    _engine.LoadRecommendations();
                }
                Refresh();
            });
            actions.Children.Add(_recommendedButton);
            _dailyButton = GhostButton("每日推荐", delegate { _engine.Daily(); });
            actions.Children.Add(_dailyButton);
            _fmButton = GhostButton("私人 FM", delegate { _engine.Fm(); });
            actions.Children.Add(_fmButton);
            actions.Children.Add(GhostButton("刷新", delegate
            {
                _engine.RefreshAccount();
                if (_showRecommended)
                {
                    _engine.LoadRecommendations();
                }
            }));
            DockPanel.SetDock(actions, Dock.Right);
            header.Children.Add(actions);
            page.Children.Add(header);

            Grid body = new Grid();
            ScrollViewer scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            _cards = new WrapPanel();
            scroll.Content = _cards;
            body.Children.Add(scroll);
            _libraryEmpty = new TextBlock
            {
                Text = "暂无歌单。登录后点「刷新」读取。",
                FontSize = 12.0,
                Foreground = Theme.Muted,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false
            };
            body.Children.Add(_libraryEmpty);
            Grid.SetRow(body, 1);
            page.Children.Add(body);
            return page;
        }

        FrameworkElement QueuePage()
        {
            Grid page = new Grid();
            page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(30.0) });
            page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.0, GridUnitType.Star) });

            DockPanel header = new DockPanel { Margin = new Thickness(0.0, 0.0, 0.0, 8.0), LastChildFill = false };
            StackPanel title = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            title.Children.Add(SectionLabel("QUEUE"));
            _queueCount = Mono("0", 9.0, Theme.Muted);
            _queueCount.Margin = new Thickness(8.0, 0.0, 0.0, 0.0);
            title.Children.Add(_queueCount);
            header.Children.Add(title);
            StackPanel queueActions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            TextBlock hint = new TextBlock
            {
                Text = "单击播放",
                FontFamily = Theme.Font("Mono"),
                FontSize = 8.5,
                Foreground = Theme.Muted,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0.0, 0.0, 12.0, 0.0)
            };
            queueActions.Children.Add(hint);
            Button playAll = GhostButton("播放全部", delegate { _engine.Play(0); });
            playAll.Height = 28.0;
            playAll.MinHeight = 28.0;
            playAll.Margin = new Thickness(0.0);
            queueActions.Children.Add(playAll);
            _stopButton = GhostButton("STOP", delegate { _engine.StopPlayback(); });
            _stopButton.Height = 28.0;
            _stopButton.MinHeight = 28.0;
            _stopButton.Margin = new Thickness(6.0, 0.0, 0.0, 0.0);
            queueActions.Children.Add(_stopButton);
            DockPanel.SetDock(queueActions, Dock.Right);
            header.Children.Add(queueActions);
            page.Children.Add(header);

            Grid tableHeader = TableHeader("#", "TRACK", "LENGTH", "EDIT");
            Grid.SetRow(tableHeader, 1);
            page.Children.Add(tableHeader);

            Grid body = new Grid();
            _queue = new ListBox
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0.0),
                ItemContainerStyle = Theme.S("Row")
            };
            body.Children.Add(_queue);
            _queueEmpty = new TextBlock
            {
                Text = "队列为空。从音乐库选一张歌单，或搜索一首歌。",
                FontSize = 12.0,
                Foreground = Theme.Muted,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false
            };
            body.Children.Add(_queueEmpty);
            Grid.SetRow(body, 2);
            page.Children.Add(body);
            return page;
        }

        FrameworkElement SearchPage()
        {
            Grid page = new Grid();
            page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(24.0) });
            page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(30.0) });
            page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.0, GridUnitType.Star) });

            Grid bar = new Grid();
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _search = new TextBox
            {
                Style = Theme.S("Input"),
                Height = 40.0,
                MinHeight = 40.0,
                FontSize = 14.0,
                Tag = "输入歌名或歌手，回车搜索",
                Margin = new Thickness(0.0, 0.0, 10.0, 0.0)
            };
            _search.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Return)
                {
                    _engine.Search(_search.Text);
                }
            };
            bar.Children.Add(_search);
            Button go = new Button
            {
                Content = "SEARCH",
                Style = Theme.S("Primary"),
                Height = 40.0,
                MinHeight = 40.0,
                Padding = new Thickness(26.0, 0.0, 26.0, 0.0),
                FontSize = 11.0,
                Margin = new Thickness(0.0, 0.0, 8.0, 0.0)
            };
            go.Click += delegate { _engine.Search(_search.Text); };
            Grid.SetColumn(go, 1);
            bar.Children.Add(go);
            Button paste = GhostButton("粘贴并搜索", delegate { _engine.SearchClipboard(); });
            paste.Height = 40.0;
            paste.MinHeight = 40.0;
            Grid.SetColumn(paste, 2);
            bar.Children.Add(paste);
            page.Children.Add(bar);

            _searchHint = new TextBlock
            {
                Text = "输入关键词后回车；结果会按歌曲列出。",
                FontSize = 11.0,
                Foreground = Theme.Muted,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetRow(_searchHint, 1);
            page.Children.Add(_searchHint);

            Grid tableHeader = TableHeader("#", "TRACK", "LENGTH", "QUEUE");
            Grid.SetRow(tableHeader, 2);
            page.Children.Add(tableHeader);

            Grid body = new Grid();
            _results = new ListBox
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0.0),
                ItemContainerStyle = Theme.S("Row")
            };
            body.Children.Add(_results);
            _searchEmpty = new TextBlock
            {
                Text = "输入关键词，结果会按歌曲列出。",
                FontSize = 12.0,
                Foreground = Theme.Muted,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false
            };
            body.Children.Add(_searchEmpty);
            Grid.SetRow(body, 3);
            page.Children.Add(body);
            return page;
        }

        FrameworkElement RadioPage()
        {
            Grid page = new Grid();
            page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.0, GridUnitType.Star) });

            DockPanel header = new DockPanel { Margin = new Thickness(0.0, 0.0, 0.0, 16.0), LastChildFill = false };
            header.Children.Add(SectionLabel("RADIO / PODCAST"));
            StackPanel actions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };
            Button refresh = GhostButton("刷新电台", delegate { _engine.RadioDirectory(); });
            refresh.Margin = new Thickness(0.0, 0.0, 14.0, 0.0);
            actions.Children.Add(refresh);

            StackPanel radioSearchGroup = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };
            _radioSearch = new TextBox
            {
                Width = 148.0,
                Height = 28.0,
                VerticalContentAlignment = VerticalAlignment.Center,
                Style = Theme.S("Input"),
                Tag = "搜索电台",
                ToolTip = "按名称搜索电台，回车执行",
                Margin = new Thickness(0.0, 0.0, 8.0, 0.0)
            };
            _radioSearch.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Return)
                {
                    _engine.RadioSearch(_radioSearch.Text);
                }
            };
            radioSearchGroup.Children.Add(_radioSearch);
            _radioSearchButton = GhostButton("搜索电台", delegate { _engine.RadioSearch(_radioSearch.Text); });
            _radioSearchButton.Margin = new Thickness(0.0, 0.0, 8.0, 0.0);
            radioSearchGroup.Children.Add(_radioSearchButton);
            Button radioPaste = GhostButton("粘贴电台", delegate { _engine.RadioClipboard(); });
            radioPaste.ToolTip = "从剪贴板粘贴电台关键词";
            radioPaste.Margin = new Thickness(0.0, 0.0, 14.0, 0.0);
            radioSearchGroup.Children.Add(radioPaste);
            actions.Children.Add(radioSearchGroup);

            actions.Children.Add(new Border
            {
                Width = 1.0,
                Height = 18.0,
                Background = Theme.Grid,
                Margin = new Thickness(0.0, 0.0, 16.0, 0.0),
                VerticalAlignment = VerticalAlignment.Center
            });

            StackPanel podcastFilterGroup = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };
            _podcastFilter = new TextBox
            {
                Width = 126.0,
                Height = 28.0,
                VerticalContentAlignment = VerticalAlignment.Center,
                Style = Theme.S("Input"),
                Tag = "筛选播客节目",
                ToolTip = "筛选当前播客节目，回车执行",
                Margin = new Thickness(0.0, 0.0, 8.0, 0.0)
            };
            _podcastFilter.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Return)
                {
                    _engine.PodcastFilter(_podcastFilter.Text);
                }
            };
            podcastFilterGroup.Children.Add(_podcastFilter);
            _podcastFilterButton = GhostButton("筛选", delegate { _engine.PodcastFilter(_podcastFilter.Text); });
            _podcastFilterButton.Margin = new Thickness(0.0, 0.0, 8.0, 0.0);
            podcastFilterGroup.Children.Add(_podcastFilterButton);
            Button filterPaste = GhostButton("粘贴筛选", delegate { _engine.PodcastFilterClipboard(); });
            filterPaste.ToolTip = "从剪贴板粘贴节目筛选词";
            filterPaste.Margin = new Thickness(0.0);
            podcastFilterGroup.Children.Add(filterPaste);
            actions.Children.Add(podcastFilterGroup);

            DockPanel.SetDock(actions, Dock.Right);
            header.Children.Add(actions);
            page.Children.Add(header);

            Grid body = new Grid();
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300.0) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16.0) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });

            Border channelsCard = Card(null);
            Grid channelsInner = new Grid();
            channelsInner.RowDefinitions.Add(new RowDefinition { Height = new GridLength(32.0) });
            channelsInner.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.0, GridUnitType.Star) });
            channelsInner.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28.0) });
            Border channelsHead = new Border { Padding = new Thickness(14.0, 0.0, 14.0, 0.0), BorderBrush = Theme.Grid, BorderThickness = new Thickness(0.0, 0.0, 0.0, 1.0) };
            channelsHead.Child = new TextBlock
            {
                Text = "CHANNELS",
                FontFamily = Theme.Font("Mono"),
                FontSize = 8.5,
                Foreground = Theme.Muted,
                VerticalAlignment = VerticalAlignment.Center
            };
            channelsInner.Children.Add(channelsHead);

            Grid channelsBody = new Grid();
            _channels = new ListBox
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0.0),
                ItemContainerStyle = Theme.S("Row"),
                Margin = new Thickness(2.0, 4.0, 2.0, 4.0)
            };
            _channels.SelectionChanged += delegate
            {
                if (_suppressRadio || _channels.SelectedItem is not ListBoxItem item || item.Tag is not int index)
                {
                    return;
                }
                UiState state = _engine.Snapshot();
                if (index >= 0 && index < state.Radios.Count)
                {
                    RadioChannel channel = state.Radios[index];
                    if (channel.Id != _podcastId)
                    {
                        _podcastId = channel.Id;
                        _engine.Podcast(channel.Id);
                    }
                }
            };
            _channels.MouseDoubleClick += delegate
            {
                if (_channels.SelectedItem is ListBoxItem item && item.Tag is int index)
                {
                    UiState state = _engine.Snapshot();
                    if (index >= 0 && index < state.Radios.Count)
                    {
                        _engine.RadioPlay(state.Radios[index]);
                    }
                }
            };
            channelsBody.Children.Add(_channels);
            _channelEmpty = new TextBlock
            {
                Text = "点「刷新电台」获取频道。",
                FontSize = 11.0,
                Foreground = Theme.Muted,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                IsHitTestVisible = false
            };
            channelsBody.Children.Add(_channelEmpty);
            Grid.SetRow(channelsBody, 1);
            channelsInner.Children.Add(channelsBody);
            _channelMore = MoreButton("更多频道", delegate { _engine.RadioMore(null); });
            Grid.SetRow(_channelMore, 2);
            channelsInner.Children.Add(_channelMore);
            channelsCard.Child = channelsInner;
            body.Children.Add(channelsCard);

            Border episodesCard = Card(null);
            Grid episodesInner = new Grid();
            episodesInner.RowDefinitions.Add(new RowDefinition { Height = new GridLength(32.0) });
            episodesInner.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.0, GridUnitType.Star) });
            episodesInner.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28.0) });
            Border episodesHead = new Border { Padding = new Thickness(14.0, 0.0, 14.0, 0.0), BorderBrush = Theme.Grid, BorderThickness = new Thickness(0.0, 0.0, 0.0, 1.0) };
            DockPanel episodesBar = new DockPanel { LastChildFill = false };
            episodesBar.Children.Add(new TextBlock
            {
                Text = "EPISODES",
                FontFamily = Theme.Font("Mono"),
                FontSize = 8.5,
                Foreground = Theme.Muted,
                VerticalAlignment = VerticalAlignment.Center
            });
            _episodeTitle = new TextBlock
            {
                Text = "",
                FontSize = 10.0,
                Foreground = Theme.Muted,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(12.0, 0.0, 0.0, 0.0)
            };
            episodesBar.Children.Add(_episodeTitle);
            episodesHead.Child = episodesBar;
            episodesInner.Children.Add(episodesHead);

            Grid episodesBody = new Grid();
            _episodes = new ListBox
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0.0),
                ItemContainerStyle = Theme.S("Row"),
                Margin = new Thickness(2.0, 4.0, 2.0, 4.0)
            };
            episodesBody.Children.Add(_episodes);
            _episodeEmpty = new TextBlock
            {
                Text = "选择左侧频道查看节目。",
                FontSize = 12.0,
                Foreground = Theme.Muted,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false
            };
            episodesBody.Children.Add(_episodeEmpty);
            Grid.SetRow(episodesBody, 1);
            episodesInner.Children.Add(episodesBody);
            _episodeMore = MoreButton("更多节目", delegate { _engine.PodcastMore(_podcastId, null); });
            Grid.SetRow(_episodeMore, 2);
            episodesInner.Children.Add(_episodeMore);
            episodesCard.Child = episodesInner;
            Grid.SetColumn(episodesCard, 2);
            body.Children.Add(episodesCard);

            Grid.SetRow(body, 1);
            page.Children.Add(body);
            return page;
        }

        /// <summary>The whole lyric sheet beside the current line: click a row to seek there, and
        /// the button repeats the line being sung so it can be heard again.</summary>
        FrameworkElement BuildLyricSheet()
        {
            Border card = Card(null);
            card.Margin = new Thickness(0.0, 16.0, 0.0, 8.0);
            Grid inner = new Grid();
            inner.RowDefinitions.Add(new RowDefinition { Height = new GridLength(30.0) });
            inner.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.0, GridUnitType.Star) });

            DockPanel head = new DockPanel { LastChildFill = false, Margin = new Thickness(10.0, 0.0, 10.0, 0.0) };
            head.Children.Add(new TextBlock
            {
                Text = "LYRIC SHEET",
                FontFamily = Theme.Font("Mono"),
                FontSize = 8.5,
                Foreground = Theme.Muted,
                VerticalAlignment = VerticalAlignment.Center
            });
            _auditionButton = GhostButton("试听本句", delegate { ToggleAudition(); });
            _auditionButton.Height = 22.0;
            _auditionButton.MinHeight = 22.0;
            _auditionButton.FontSize = 10.0;
            _auditionButton.Padding = new Thickness(8.0, 0.0, 8.0, 0.0);
            _auditionButton.Margin = new Thickness(0.0);
            DockPanel.SetDock(_auditionButton, Dock.Right);
            head.Children.Add(_auditionButton);
            inner.Children.Add(head);

            _lyricSheet = new ListBox
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0.0),
                ItemContainerStyle = Theme.S("Row"),
                Margin = new Thickness(2.0, 0.0, 2.0, 4.0)
            };
            ScrollViewer.SetHorizontalScrollBarVisibility(_lyricSheet, ScrollBarVisibility.Disabled);
            _lyricSheet.MouseLeftButtonUp += delegate
            {
                if (_lyricSheet.SelectedItem is ListBoxItem item && item.Tag is double at)
                {
                    _engine.Seek(at);
                }
            };
            Grid.SetRow(_lyricSheet, 1);
            inner.Children.Add(_lyricSheet);
            card.Child = inner;
            return card;
        }

        void ToggleAudition()
        {
            if (_auditioning)
            {
                _engine.StopAudition();
                return;
            }
            UiState state = _engine.Snapshot();
            int index = state.LyricIndex;
            if (index < 0 || index >= state.LyricRows.Count)
            {
                return;
            }
            LyricLine line = state.LyricRows[index];
            double end = index + 1 < state.LyricRows.Count ? state.LyricRows[index + 1].At : line.At + 6.0;
            _engine.Audition(line.At, end);
        }

        void RefreshLyricSheet(UiState state)
        {
            _auditioning = state.Audition;
            _auditionButton.Content = I18n.Text(_auditioning ? "停止试听" : "试听本句");
            _auditionButton.Visibility = state.LyricRows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            string signature = state.LyricRows.Count + "|" + state.Title
                + "|" + (state.LyricRows.Count > 0 ? state.LyricRows[0].Text : "");
            if (signature != _lyricSheetSignature)
            {
                _lyricSheetSignature = signature;
                _lyricSheet.Items.Clear();
                foreach (LyricLine line in state.LyricRows)
                {
                    Grid row = new Grid();
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(46.0) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
                    TextBlock at = Mono(Util.FormatTime(line.At), 8.5, Theme.Muted);
                    at.VerticalAlignment = VerticalAlignment.Center;
                    row.Children.Add(at);
                    TextBlock words = new TextBlock
                    {
                        Text = string.IsNullOrEmpty(line.Translation) ? line.Text : line.Text + "  " + line.Translation,
                        FontSize = 11.0,
                        Foreground = Theme.Muted,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    Grid.SetColumn(words, 1);
                    row.Children.Add(words);
                    row.Background = Brushes.Transparent;
                    _lyricSheet.Items.Add(new ListBoxItem { Tag = line.At, Content = row });
                }
                _lyricShown = -1;
            }
            if (state.LyricIndex != _lyricShown)
            {
                _lyricShown = state.LyricIndex;
                for (int i = 0; i < _lyricSheet.Items.Count; i++)
                {
                    if (_lyricSheet.Items[i] is not ListBoxItem item || item.Content is not Grid row)
                    {
                        continue;
                    }
                    bool current = i == state.LyricIndex;
                    row.Background = current ? Theme.Ground900 : Brushes.Transparent;
                    foreach (UIElement child in row.Children)
                    {
                        if (child is TextBlock block)
                        {
                            block.Foreground = current ? Theme.Yellow : Theme.Muted;
                            block.FontWeight = current ? FontWeights.SemiBold : FontWeights.Normal;
                        }
                    }
                }
                if (state.LyricIndex >= 0 && state.LyricIndex < _lyricSheet.Items.Count)
                {
                    _lyricSheet.ScrollIntoView(_lyricSheet.Items[state.LyricIndex]);
                }
            }
        }

        Grid TableHeader(string a, string b, string c, string d = null)
        {
            Grid row = new Grid { Height = 30.0 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36.0) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70.0) });
            row.Children.Add(HeaderCell(a, 0));
            row.Children.Add(HeaderCell(b, 1));
            row.Children.Add(HeaderCell(c, 2));
            int columns = 3;
            if (d != null)
            {
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(EditWidth) });
                TextBlock cell = HeaderCell(d, 3);
                cell.TextAlignment = TextAlignment.Right;
                row.Children.Add(cell);
                columns = 4;
            }
            Border line = new Border
            {
                Height = 1.0,
                Background = Theme.Grid,
                VerticalAlignment = VerticalAlignment.Bottom
            };
            Grid.SetColumnSpan(line, columns);
            row.Children.Add(line);
            return row;
        }

        static TextBlock HeaderCell(string text, int column)
        {
            TextBlock block = new TextBlock
            {
                Text = text,
                FontFamily = Theme.Font("Mono"),
                FontSize = 8.5,
                Foreground = Theme.Muted,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(block, column);
            return block;
        }

        // ---------------------------------------------------------------- settings sheet

        Grid BuildSettingsSheet()
        {
            Grid overlay = new Grid
            {
                Background = new SolidColorBrush(Color.FromArgb(0xA8, 0, 0, 0)),
                Visibility = Visibility.Collapsed
            };
            overlay.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                if (e.OriginalSource == overlay)
                {
                    CloseSheet();
                }
            };

            _sheet = new Border
            {
                Width = 440.0,
                HorizontalAlignment = HorizontalAlignment.Right,
                Background = Theme.Ground700,
                BorderBrush = Theme.Grid,
                BorderThickness = new Thickness(1.0, 0.0, 0.0, 0.0)
            };
            ScrollViewer scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            StackPanel body = new StackPanel { Margin = new Thickness(22.0, 18.0, 22.0, 22.0) };

            DockPanel header = new DockPanel { LastChildFill = false, Margin = new Thickness(0.0, 0.0, 0.0, 4.0) };
            header.Children.Add(SectionLabel("SETTINGS"));
            Button close = new Button
            {
                Content = "×",
                Style = Theme.S("Ghost"),
                BorderThickness = new Thickness(0.0),
                Width = 32.0,
                Height = 32.0,
                MinHeight = 32.0,
                Padding = new Thickness(0.0),
                FontSize = 18.0,
                ToolTip = "关闭"
            };
            close.Click += delegate { CloseSheet(); };
            DockPanel.SetDock(close, Dock.Right);
            header.Children.Add(close);
            body.Children.Add(header);
            body.Children.Add(new TextBlock
            {
                Text = "设置",
                FontSize = 22.0,
                FontWeight = FontWeights.SemiBold,
                Foreground = Theme.Ink,
                Margin = new Thickness(0.0, 2.0, 0.0, 4.0)
            });
            body.Children.Add(new TextBlock
            {
                Text = "音质与播放顺序在控制台的 SYSTEM 面板中切换。",
                FontSize = 11.0,
                Foreground = Theme.Muted,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0.0, 0.0, 0.0, 10.0)
            });

            body.Children.Add(SheetSection("LYRICS"));
            UiState initial = _engine.Snapshot();
            _lyricsTick = new CheckBox { Content = "显示歌词", Style = Theme.S("Tick"), Foreground = Theme.Ink, IsChecked = initial.LyricsEnabled };
            _lyricsTick.Checked += delegate { _engine.SetLyrics(enabled: true); };
            _lyricsTick.Unchecked += delegate { _engine.SetLyrics(enabled: false); };
            body.Children.Add(SettingRow("歌词", _lyricsTick));
            _automixTick = new CheckBox { Content = "无缝顺播", Style = Theme.S("Tick"), Foreground = Theme.Ink, IsChecked = initial.Automix };
            _automixTick.Checked += delegate { _engine.SetAutomix(enabled: true); };
            _automixTick.Unchecked += delegate { _engine.SetAutomix(enabled: false); };
            body.Children.Add(SettingRow("顺播", _automixTick));

            _languageMode = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (string mode in new[] { I18n.Follow, I18n.Chinese, I18n.English })
            {
                string captured = mode;
                Button option = GhostButton(
                    mode == I18n.Follow ? "跟随 HD2QD" : (mode == I18n.Chinese ? "强制中文" : "强制 English"),
                    delegate
                    {
                        I18n.Mode = captured;
                        MarkLanguageMode();
                        ApplyLanguage();
                        Refresh();
                    });
                option.Tag = mode;
                _languageMode.Children.Add(option);
            }
            MarkLanguageMode();
            body.Children.Add(SettingRow("语言", _languageMode));

            body.Children.Add(SheetSection("DECODER"));
            StackPanel ffmpegRow = new StackPanel { Orientation = Orientation.Horizontal };
            _ffmpeg = new TextBlock
            {
                FontFamily = Theme.Font("Mono"),
                FontSize = 10.0,
                Foreground = Theme.Muted,
                MaxWidth = 300.0,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0.0, 0.0, 10.0, 0.0)
            };
            ffmpegRow.Children.Add(_ffmpeg);
            Button pick = GhostButton("选择…", delegate
            {
                Microsoft.Win32.OpenFileDialog dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "选择 ffmpeg.exe",
                    Filter = "ffmpeg.exe|ffmpeg.exe|可执行文件|*.exe"
                };
                if (dialog.ShowDialog(Window.GetWindow(this)) == true)
                {
                    _engine.SetFfmpeg(dialog.FileName);
                    Refresh();
                }
            });
            pick.MinHeight = 30.0;
            pick.Height = 30.0;
            ffmpegRow.Children.Add(pick);
            body.Children.Add(SettingRow("FFmpeg", ffmpegRow));

            body.Children.Add(SheetSection("ACCOUNT"));
            _sheetAccount = new TextBlock
            {
                Text = "未登录",
                FontSize = 13.0,
                Foreground = Theme.Ink,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0.0, 0.0, 0.0, 8.0)
            };
            body.Children.Add(_sheetAccount);
            StackPanel accountActions = new StackPanel { Orientation = Orientation.Horizontal };
            accountActions.Children.Add(GhostButton("登录 / 切换", delegate { OpenLogin(); }));
            accountActions.Children.Add(GhostButton("刷新账号", delegate { _engine.RefreshAccount(); }));
            body.Children.Add(accountActions);
            _sheetLock = new TextBlock
            {
                Text = "",
                FontSize = 11.0,
                Foreground = Theme.Yellow,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0.0, 8.0, 0.0, 0.0)
            };
            body.Children.Add(_sheetLock);

            body.Children.Add(SheetSection("ABOUT"));
            body.Children.Add(new TextBlock
            {
                Text = "HD2 Music 模组与网易云在线助手由 @127_MU 制作；本插件把助手的播放功能搬进管理器，不包含模组文件。",
                FontSize = 11.5,
                Foreground = Theme.Muted,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 18.0
            });
            StackPanel links = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0.0, 10.0, 0.0, 0.0) };
            links.Children.Add(GhostButton("原作者主页", delegate { OpenUrl("https://space.bilibili.com/283469182"); }));
            links.Children.Add(GhostButton("本项目仓库", delegate { OpenUrl("https://github.com/ahe-meow/hd2qd-hd2music"); }));
            body.Children.Add(links);
            body.Children.Add(new TextBlock
            {
                Text = "HD2QD.HD2Music " + typeof(Plugin).Assembly.GetName().Version.ToString(3) + " · "
                    + I18n.Text(Ffmpeg.Embedded ? "内置解码器" : "外置解码器") + " · MIT",
                FontFamily = Theme.Font("Mono"),
                FontSize = 9.0,
                Foreground = Theme.Muted,
                Margin = new Thickness(0.0, 12.0, 0.0, 0.0)
            });

            scroll.Content = body;
            _sheet.Child = scroll;
            overlay.Children.Add(_sheet);
            return overlay;
        }

        static FrameworkElement SheetSection(string text)
        {
            StackPanel section = new StackPanel { Margin = new Thickness(0.0, 20.0, 0.0, 6.0) };
            section.Children.Add(new Border
            {
                Height = 1.0,
                Background = Theme.Grid,
                Margin = new Thickness(0.0, 0.0, 0.0, 8.0)
            });
            section.Children.Add(SectionLabel(text));
            return section;
        }

        static FrameworkElement SettingRow(string label, UIElement control)
        {
            Grid row = new Grid { Margin = new Thickness(0.0, 5.0, 0.0, 5.0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92.0) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
            row.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 12.0,
                Foreground = Theme.Muted,
                VerticalAlignment = VerticalAlignment.Center
            });
            Grid.SetColumn(control, 1);
            row.Children.Add(control);
            return row;
        }

        sealed class Segment
        {
            public string Value;
            public Button Button;
        }

        FrameworkElement SegmentRow(string[] values, Func<string, string> label, List<Segment> store, Action<string> choose, bool compact = false)
        {
            StackPanel row = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (string value in values)
            {
                Button button = new Button
                {
                    Content = label(value),
                    Style = Theme.S("Ghost"),
                    Height = compact ? 24.0 : 30.0,
                    MinHeight = compact ? 24.0 : 30.0,
                    Padding = new Thickness(compact ? 5.0 : 8.0, 0.0, compact ? 5.0 : 8.0, 0.0),
                    FontSize = compact ? 9.0 : 10.5,
                    Margin = new Thickness(0.0, 0.0, compact ? 4.0 : 6.0, 0.0)
                };
                string chosen = value;
                button.Click += delegate
                {
                    try
                    {
                        choose(chosen);
                        Refresh();
                    }
                    catch
                    {
                    }
                };
                store.Add(new Segment { Value = chosen, Button = button });
                row.Children.Add(button);
            }
            return row;
        }

        static void MarkSegments(List<Segment> segments, string value)
        {
            foreach (Segment segment in segments)
            {
                if (segment.Value == value)
                {
                    segment.Button.Background = Theme.Yellow;
                    segment.Button.Foreground = Theme.TapeInk;
                    segment.Button.BorderBrush = Theme.Yellow;
                }
                else
                {
                    segment.Button.ClearValue(Control.BackgroundProperty);
                    segment.Button.ClearValue(Control.ForegroundProperty);
                    segment.Button.ClearValue(Control.BorderBrushProperty);
                }
            }
        }

        void OpenSheet()
        {
            _overlay.Visibility = Visibility.Visible;
            Refresh();
        }

        void CloseSheet()
        {
            _overlay.Visibility = Visibility.Collapsed;
        }

        // ---------------------------------------------------------------- page switching

        void ShowPage(string page)
        {
            _page = page;
            string[] pages = { "console", "library", "queue", "search", "radio" };
            for (int i = 0; i < pages.Length && i < _pages.Children.Count; i++)
            {
                _pages.Children[i].Visibility = pages[i] == page ? Visibility.Visible : Visibility.Collapsed;
            }
            foreach (TabRef tab in _tabs)
            {
                bool active = tab.Page == page;
                tab.Button.Foreground = active ? Theme.Ink : Theme.Muted;
                tab.Line.Background = active ? Theme.Yellow : Brushes.Transparent;
            }
            if (page == "library")
            {
                UiState state = _engine.Snapshot();
                if (state.Lists.Count == 0 && _engine.Api != null)
                {
                    _engine.RefreshAccount();
                }
                _engine.SetVisiblePlaylists(state.Lists.Take(12).Select(p => p.Id).ToList());
            }
            else
            {
                _engine.SetVisiblePlaylists(null);
            }
            if (page == "search")
            {
                _search.Focus();
            }
            if (page == "radio")
            {
                UiState state = _engine.Snapshot();
                if (state.Radios.Count == 0 && _engine.Api != null)
                {
                    _engine.RadioDirectory();
                }
            }
            Refresh();
        }

        // ---------------------------------------------------------------- refresh

        void Refresh()
        {
            // a refresh timer must never take the manager down: a half-started engine, a
            // network reply already gone, an item list changed under it
            try
            {
                if (_host != null)
                {
                    I18n.SetHostLang(Words.Lang(_host));
                }
                RefreshCore();
                // page controls and dynamic labels are built/updated during RefreshCore;
                // translate afterwards so newly added controls are covered too
                I18n.Apply(this);
                _appliedLang = I18n.CurrentLang;
            }
            catch
            {
            }
        }

        void RefreshCore()
        {
            UiState state = _engine.Snapshot();
            string stateName = (state.State ?? "idle").ToUpperInvariant();
            _stateText.Text = stateName;
            _stateText.Foreground = state.State == "playing" ? Theme.Yellow : (state.State == "paused" || state.State == "buffering" ? Theme.Blue : Theme.Muted);
            _led.Background = state.State == "playing" ? Theme.Yellow : (state.State == "paused" || state.State == "buffering" ? Theme.Blue : Theme.Muted);

            string account = string.IsNullOrEmpty(state.Account) ? "未登录" : state.Account;
            _accountButton.Content = account;
            _accountButton.ToolTip = string.IsNullOrEmpty(state.Account) ? "登录网易云" : "账号：" + state.Account + "（点击切换）";

            _title.Text = string.IsNullOrEmpty(state.Title) ? I18n.Text("未选歌曲") : state.Title;
            _artist.Text = state.Artist ?? "";
            _meta.Text = (string.IsNullOrEmpty(state.PlaylistName) ? "—" : state.PlaylistName)
                + "   ·   " + Math.Max(0, state.Index + 1) + "/" + state.Count;

            bool hasLyric = !string.IsNullOrEmpty(state.Lyric);
            if (!hasLyric)
            {
                _lyric.Text = I18n.Text(state.State == "playing" || state.State == "paused" ? "暂无歌词" : "未在播放");
                _lyric.FontSize = 13.0;
                _lyric.Foreground = Theme.Muted;
            }
            else
            {
                _lyric.Text = state.Lyric;
                _lyric.FontSize = 23.0;
                _lyric.Foreground = Theme.Ink;
            }
            _previous.Text = hasLyric ? state.PreviousLyric : "";
            string prefix = hasLyric ? (state.LyricPrefix ?? "") : "";
            string whole = hasLyric ? (state.Lyric ?? "") : "";
            _lyricPrefixLine.Inlines.Clear();
            if (prefix.Length > 0)
            {
                _lyricPrefixLine.Inlines.Add(new Run(prefix) { Foreground = Theme.Yellow });
            }
            if (whole.Length > prefix.Length)
            {
                _lyricPrefixLine.Inlines.Add(new Run(whole.Substring(prefix.Length)) { Foreground = Theme.Muted });
            }
            _translation.Text = hasLyric ? state.Translation : "";
            _next.Text = hasLyric && !string.IsNullOrEmpty(state.NextLyric) ? I18n.Text("NEXT   ") + state.NextLyric : "";
            _notice.Text = I18n.Text(state.Notice ?? "");

            SetImage(state.CoverUrl);
            if (!_draggingProgress)
            {
                _progress.Maximum = Math.Max(1.0, state.Duration);
                _progress.Value = Math.Max(0.0, Math.Min(state.Pos, _progress.Maximum));
            }
            _time.Text = Util.FormatTime(_progress.Value);
            _duration.Text = " / " + Util.FormatTime(state.Duration);
            _play.Content = state.State == "playing" ? "PAUSE" : (state.State == "paused" ? "RESUME" : "PLAY");
            _suppressVolume = true;
            _volume.Value = state.Volume;
            _suppressVolume = false;

            MarkSegments(_qualitySegments, state.Quality);
            MarkSegments(_orderSegments, state.Order);
            _lyricsTick.IsChecked = state.LyricsEnabled;
            _automixTick.IsChecked = state.Automix;
            _ffmpeg.Text = I18n.Text(Ffmpeg.Embedded ? "内置 · " : "外置 · ") + _engine.FfmpegPath;
            _ffmpeg.Foreground = File.Exists(_engine.FfmpegPath) ? Theme.Muted : Theme.Yellow;
            _sheetAccount.Text = string.IsNullOrEmpty(state.Account) ? I18n.Text("未登录") : state.Account;
            _sheetLock.Text = _engine.IsLockedOut ? I18n.Text("网易云在线助手已在运行：点击播放会转交助手；本插件不写状态文件。") : "";

            _sysState.Text = I18n.Text(stateName);
            _sysState.Foreground = state.State == "playing" ? Theme.Yellow : Theme.Ink;
            _sysLyrics.Text = state.LyricsEnabled ? "ON" : "OFF";
            _sysQueue.Text = state.Count + I18n.Text(" TRACKS");
            _sysPlaylist.Text = string.IsNullOrEmpty(state.PlaylistName) ? "—" : state.PlaylistName;
            _sysTrack.Text = Math.Max(0, state.Index + 1) + " / " + state.Count;
            _sysFfmpeg.Text = File.Exists(_engine.FfmpegPath) ? "READY" : "MISSING";
            _sysFfmpeg.Foreground = File.Exists(_engine.FfmpegPath) ? Theme.Ink : Theme.Yellow;
            _sysHelper.Text = _engine.IsLockedOut ? "LOCKED" : "READY";
            _sysHelper.Foreground = _engine.IsLockedOut ? Theme.Yellow : Theme.Ink;

            if (_page == "library")
            {
                _engine.SetVisiblePlaylists(state.Lists.Take(12).Select(p => p.Id).ToList());
            }

            RefreshPeek(state);
            RefreshLibrary(state);
            RefreshQueue(state);
            RefreshSearch(state);
            RefreshRadio(state);
            RefreshLyricSheet(state);

            _statusAccount.Text = I18n.Text("ACCOUNT ") + (string.IsNullOrEmpty(state.Account) ? "—" : state.Account);
            string reason = state.Notice;
            if (string.IsNullOrWhiteSpace(reason))
            {
                reason = !state.GameRunning ? "未启动游戏 · 启动游戏后即可播放"
                    : state.State == "loading" ? "正在读取…"
                    : state.State == "buffering" ? "缓冲中…"
                    : state.State == "playing" ? "播放中"
                    : state.State == "paused" ? "已暂停"
                    : state.State == "finished" ? "播放结束"
                    : "空闲";
            }
            else if (!state.GameRunning && !reason.Contains("游戏") && !NoticeTakesPrecedence(reason))
            {
                // "已读取歌单，选择歌曲播放" alone hid the real reason a click does nothing
                reason = "未启动游戏 · " + reason;
            }
            _statusNotice.Text = I18n.Text("STATUS ") + I18n.Text(reason);
            bool busy = state.State == "loading" || state.State == "buffering" || reason.Contains("正在");
            bool blocked = NoticeTakesPrecedence(reason);
            _statusNotice.Foreground = (busy || blocked || !state.GameRunning || reason.Contains("游戏")) ? Theme.Yellow : Theme.Muted;
            _statusQueue.Text = I18n.Text("QUEUE ") + state.Count;
            _statusQuality.Text = I18n.Text("QUALITY ") + I18n.Text(state.Quality.ToUpperInvariant());
            bool ffmpegReady = File.Exists(_engine.FfmpegPath);
            _statusFfmpeg.Text = I18n.Text("FFMPEG ") + I18n.Text(ffmpegReady ? "OK" : "MISSING");
            _statusFfmpeg.Foreground = ffmpegReady ? Theme.Muted : Theme.Yellow;
        }

        void RefreshPeek(UiState state)
        {
            _peekCount.Text = state.Count.ToString();
            int start = state.Index >= 0 ? state.Index + 1 : 0;
            List<int> indices = new List<int>();
            for (int i = start; i < state.Queue.Count && indices.Count < 5; i++)
            {
                indices.Add(i);
            }
            string signature = string.Join(",", indices) + "|" + state.Index;
            if (_peek.Tag as string == signature)
            {
                return;
            }
            _peek.Tag = signature;
            _peek.Items.Clear();
            foreach (int i in indices)
            {
                Track track = state.Queue[i];
                Grid row = new Grid();
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22.0) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
                row.Children.Add(new TextBlock
                {
                    Text = (i + 1).ToString("00"),
                    FontFamily = Theme.Font("Mono"),
                    FontSize = 8.5,
                    Foreground = Theme.Muted,
                    VerticalAlignment = VerticalAlignment.Center
                });
                StackPanel words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                words.Children.Add(new TextBlock
                {
                    Text = track.Title,
                    FontSize = 11.5,
                    Foreground = Theme.Ink,
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
                words.Children.Add(new TextBlock
                {
                    Text = track.Artist,
                    FontSize = 9.5,
                    Foreground = Theme.Muted,
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
                Grid.SetColumn(words, 1);
                row.Children.Add(words);
                row.Background = Brushes.Transparent;
                int at = i;
                row.MouseLeftButtonUp += delegate { PlayQueueAt(at); };
                _peek.Items.Add(new ListBoxItem { Tag = i, Content = row });
            }
            if (_peek.Items.Count == 0)
            {
                _peek.Items.Add(new ListBoxItem
                {
                    Content = new TextBlock
                    {
                        Text = state.Count > 0 ? "已是最后一首" : "队列为空",
                        FontSize = 11.0,
                        Foreground = Theme.Muted
                    }
                });
            }
        }

        void RefreshLibrary(UiState state)
        {
            List<Playlist> source = _showRecommended ? state.Recommended : state.Lists;
            _libraryCount.Text = source.Count.ToString();
            _recommendedButton.Content = _showRecommended ? "我的歌单" : "推荐歌单";
            string signature = (_showRecommended ? "r:" : "p:") + string.Join("|", source.Select(p => p.Id + p.Name));
            if (signature == _librarySignature)
            {
                return;
            }
            _librarySignature = signature;
            _cards.Children.Clear();
            for (int i = 0; i < source.Count; i++)
            {
                _cards.Children.Add(PlaylistCard(i, source[i]));
            }
            _libraryEmpty.Visibility = source.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        FrameworkElement PlaylistCard(int index, Playlist playlist)
        {
            Border card = new Border
            {
                Width = 188.0,
                Height = 96.0,
                Margin = new Thickness(0.0, 0.0, 12.0, 12.0),
                Background = Theme.Ground700,
                BorderBrush = Theme.Grid,
                BorderThickness = new Thickness(1.0),
                Padding = new Thickness(14.0, 10.0, 14.0, 10.0),
                Cursor = Cursors.Hand
            };
            Grid inner = new Grid();
            inner.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            inner.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.0, GridUnitType.Star) });
            DockPanel head = new DockPanel { LastChildFill = false };
            TextBlock number = new TextBlock
            {
                Text = (index + 1).ToString("00"),
                FontFamily = Theme.Font("Mono"),
                FontSize = 9.0,
                Foreground = Theme.Muted
            };
            head.Children.Add(number);
            TextBlock kind = new TextBlock
            {
                Text = "PLAYLIST",
                FontFamily = Theme.Font("Mono"),
                FontSize = 7.5,
                Foreground = Theme.Muted
            };
            DockPanel.SetDock(kind, Dock.Right);
            head.Children.Add(kind);
            inner.Children.Add(head);
            TextBlock name = new TextBlock
            {
                Text = playlist.Name,
                FontSize = 14.0,
                FontWeight = FontWeights.SemiBold,
                Foreground = Theme.Ink,
                TextWrapping = TextWrapping.Wrap,
                MaxHeight = 44.0,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetRow(name, 1);
            inner.Children.Add(name);
            card.Child = inner;
            card.MouseEnter += delegate
            {
                card.BorderBrush = Theme.Yellow;
                number.Foreground = Theme.Yellow;
            };
            card.MouseLeave += delegate
            {
                card.BorderBrush = Theme.Grid;
                number.Foreground = Theme.Muted;
            };
            card.MouseLeftButtonUp += delegate
            {
                _engine.LoadPlaylist(playlist);
                ShowPage("queue");
            };
            return card;
        }

        void MarkLanguageMode()
        {
            if (_languageMode == null) return;
            foreach (Button button in _languageMode.Children.OfType<Button>())
            {
                button.Foreground = (string)button.Tag == I18n.Mode ? Theme.Yellow : Theme.Muted;
            }
        }

        void ApplyLanguage()
        {
            string lang = I18n.CurrentLang;
            I18n.Apply(this);
            _appliedLang = lang;
        }

        static bool NoticeTakesPrecedence(string notice)
        {
            // an account or helper problem blocks playback even after the game starts
            return notice.Contains("登录") || notice.Contains("扫码") || notice.Contains("二维码")
                || notice.Contains("COOKIE") || notice.Contains("短信") || notice.Contains("锁")
                || notice.Contains("失败") || notice.Contains("不可用") || notice.Contains("中断")
                || notice.Contains("版权") || notice.Contains("会员") || notice.Contains("助手已经在运行")
                || notice.Contains("转交") || notice.Contains("接管");
        }

        void PlayQueueAt(int index)
        {
            UiState state = _engine.Snapshot();
            if (!state.GameRunning)
            {
                // let the engine write its "start the game first" notice even when the
                // clicked row is the stale current one
                _engine.Play(index);
                return;
            }
            if (state.Index == index && (state.State == "playing" || state.State == "paused" || state.State == "loading"))
            {
                return;
            }
            _engine.Play(index);
        }

        void RefreshQueue(UiState state)
        {
            _queueCount.Text = state.Count.ToString();
            string signature = state.Count + ":" + state.Index + ":" + state.QueueRevision;
            if (signature == _queueSignature)
            {
                return;
            }
            _queueSignature = signature;
            _queue.Items.Clear();
            for (int i = 0; i < state.Queue.Count; i++)
            {
                int at = i;
                bool current = i == state.Index;
                // the row being decoded is not editable: moving it would desync the stream
                FrameworkElement[] actions = current ? null : new FrameworkElement[]
                {
                    TinyButton("↑", "上移", delegate { _engine.QueueEdit(at, "up"); }),
                    TinyButton("↓", "下移", delegate { _engine.QueueEdit(at, "down"); }),
                    TinyButton("✕", "移出队列", delegate { _engine.QueueEdit(at, "remove"); }),
                    TinyButton("⇢", "设为下一首", delegate { _engine.QueueEdit(at, "next"); })
                };
                _queue.Items.Add(TrackRow(i, state.Queue[i], current, delegate { PlayQueueAt(at); }, actions));
            }
            _queueEmpty.Visibility = state.Queue.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        void RefreshSearch(UiState state)
        {
            _searchHint.Text = string.IsNullOrEmpty(state.SearchKeyword)
                ? "输入关键词后回车；结果会按歌曲列出。"
                : "关键词：" + state.SearchKeyword + "   ·   " + state.SearchResults.Count + " 首   ·   单击结果开始播放，⇢ 插到当前歌曲之后";
            string signature = state.SearchSerial + ":" + state.SearchResults.Count + ":" + state.SearchState;
            if (signature == _searchSignature)
            {
                return;
            }
            _searchSignature = signature;
            _results.Items.Clear();
            for (int i = 0; i < state.SearchResults.Count; i++)
            {
                int at = i;
                Button enqueue = TinyButton("⇢", "加入下一首", delegate { _engine.SearchEnqueue(at); });
                enqueue.Width = 24.0;
                enqueue.MinWidth = 24.0;
                _results.Items.Add(TrackRow(i, state.SearchResults[i], false, delegate { _engine.SearchPlay(at); }, new FrameworkElement[] { enqueue }));
            }
            _searchEmpty.Visibility = state.SearchResults.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            _searchEmpty.Text = state.SearchState == "loading" ? "正在搜索…" : "输入关键词，结果会按歌曲列出。";
        }

        void RefreshRadio(UiState state)
        {
            _channelEmpty.Visibility = state.Radios.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            _episodeEmpty.Visibility = state.PodcastTracks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            _channelMore.Visibility = state.RadioMore == "ready" || state.RadioMore == "error" ? Visibility.Visible : Visibility.Collapsed;
            _channelMore.Content = I18n.Text(state.RadioMore == "error" ? "重试" : (state.RadioState == "loading" ? "正在加载…" : "更多频道"));
            _episodeMore.Visibility = state.PodcastMore == "ready" || state.PodcastMore == "error" ? Visibility.Visible : Visibility.Collapsed;
            _episodeMore.Content = I18n.Text(state.PodcastMore == "error" ? "重试" : (state.PodcastState == "loading" ? "正在加载…" : "更多节目"));
            string signature = state.Radios.Count + ":" + state.PodcastTracks.Count + ":" + state.PodcastState;
            if (signature == _radioSignature)
            {
                return;
            }
            _radioSignature = signature;
            _suppressRadio = true;
            _channels.Items.Clear();
            for (int i = 0; i < state.Radios.Count; i++)
            {
                RadioChannel channel = state.Radios[i];
                StackPanel words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                words.Children.Add(new TextBlock
                {
                    Text = channel.Name,
                    FontSize = 12.5,
                    Foreground = Theme.Ink,
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
                if (!string.IsNullOrEmpty(channel.Description))
                {
                    words.Children.Add(new TextBlock
                    {
                        Text = channel.Description,
                        FontSize = 9.5,
                        Foreground = Theme.Muted,
                        TextTrimming = TextTrimming.CharacterEllipsis
                    });
                }
                _channels.Items.Add(new ListBoxItem { Tag = i, Content = words });
            }
            if (_channels.SelectedIndex < 0 && _channels.Items.Count > 0)
            {
                _channels.SelectedIndex = 0;
            }
            _suppressRadio = false;
            if (_channels.Items.Count > 0 && string.IsNullOrEmpty(_podcastId) && state.Radios.Count > 0 && _engine.Api != null)
            {
                _podcastId = state.Radios[0].Id;
                _engine.Podcast(_podcastId);
            }

            _episodes.Items.Clear();
            for (int i = 0; i < state.PodcastTracks.Count; i++)
            {
                Track episode = state.PodcastTracks[i];
                Grid row = new Grid();
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56.0) });
                row.Children.Add(new TextBlock
                {
                    Text = episode.Title,
                    FontSize = 12.0,
                    Foreground = Theme.Ink,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = VerticalAlignment.Center
                });
                TextBlock length = Mono(Util.FormatTime(episode.Duration), 9.0, Theme.Muted);
                length.TextAlignment = TextAlignment.Right;
                length.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(length, 1);
                row.Children.Add(length);
                row.Background = Brushes.Transparent;
                int at = i;
                row.MouseLeftButtonUp += delegate { _engine.PodcastPlay(at); };
                _episodes.Items.Add(new ListBoxItem { Tag = i, Content = row });
            }
            _episodeTitle.Text = state.PodcastTracks.Count > 0 ? state.PodcastTracks.Count + " 期" : "";
        }

        ListBoxItem TrackRow(int index, Track track, bool current, Action play, FrameworkElement[] trailing = null)
        {
            Grid row = new Grid { Background = Brushes.Transparent };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36.0) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70.0) });
            TextBlock number = Mono(current ? "▶" : (index + 1).ToString("00"), 9.0, current ? Theme.Yellow : Theme.Muted);
            number.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(number);
            StackPanel words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            words.Children.Add(new TextBlock
            {
                Text = track.Title,
                FontSize = 12.0,
                FontWeight = current ? FontWeights.SemiBold : FontWeights.Normal,
                Foreground = current ? Theme.Yellow : Theme.Ink,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            words.Children.Add(new TextBlock
            {
                Text = track.Artist,
                FontSize = 9.5,
                Foreground = Theme.Muted,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            Grid.SetColumn(words, 1);
            row.Children.Add(words);
            TextBlock length = Mono(Util.FormatTime(track.Duration), 9.0, Theme.Muted);
            length.TextAlignment = TextAlignment.Right;
            length.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(length, 2);
            row.Children.Add(length);
            // the column is reserved even when it stays empty, or the length column would
            // shift on the one row that has no actions
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(EditWidth) });
            if (trailing != null && trailing.Length > 0)
            {
                StackPanel actions = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center
                };
                foreach (FrameworkElement item in trailing)
                {
                    actions.Children.Add(item);
                }
                Grid.SetColumn(actions, 3);
                row.Children.Add(actions);
            }
            if (play != null)
            {
                row.MouseLeftButtonUp += delegate { play(); };
            }
            return new ListBoxItem { Tag = index, Content = row };
        }

        /// <summary>A footer button that only appears when the service says there is another page:
        /// "更多" over a list that has ended is a promise the list cannot keep.</summary>
        Button MoreButton(string text, Action action)
        {
            Button button = GhostButton(text, action);
            button.Height = 24.0;
            button.MinHeight = 24.0;
            button.FontSize = 10.0;
            button.Margin = new Thickness(8.0, 2.0, 8.0, 2.0);
            button.Padding = new Thickness(0.0);
            button.HorizontalAlignment = HorizontalAlignment.Stretch;
            button.Visibility = Visibility.Collapsed;
            return button;
        }

        /// <summary>A one-glyph button for the row actions: small enough to sit in a 12 px line,
        /// loud enough on hover to be found.</summary>
        static Button TinyButton(string text, string tip, Action action)
        {
            Button button = new Button
            {
                Content = text,
                Style = Theme.S("Ghost"),
                Width = 22.0,
                Height = 20.0,
                MinHeight = 20.0,
                MinWidth = 22.0,
                Padding = new Thickness(0.0),
                FontSize = 11.0,
                Margin = new Thickness(2.0, 0.0, 0.0, 0.0),
                ToolTip = tip
            };
            button.Click += delegate
            {
                try
                {
                    action();
                }
                catch
                {
                }
            };
            return button;
        }

        // ---------------------------------------------------------------- small pieces

        static Border Card(UIElement child) => new Border
        {
            Background = Theme.Ground700,
            BorderBrush = Theme.Grid,
            BorderThickness = new Thickness(1.0),
            Child = child
        };

        static StackPanel SectionLabel(string text)
        {
            StackPanel line = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            line.Children.Add(new Border
            {
                Width = 6.0,
                Height = 6.0,
                Background = Theme.Yellow,
                Margin = new Thickness(0.0, 0.0, 8.0, 0.0),
                VerticalAlignment = VerticalAlignment.Center
            });
            line.Children.Add(new TextBlock
            {
                Text = text,
                FontFamily = Theme.Font("Mono"),
                FontSize = 9.0,
                FontWeight = FontWeights.SemiBold,
                Foreground = Theme.Muted,
                VerticalAlignment = VerticalAlignment.Center
            });
            return line;
        }

        static TextBlock Mono(string text, double size, Brush brush) => new TextBlock
        {
            Text = text,
            FontFamily = Theme.Font("Mono"),
            FontSize = size,
            Foreground = brush,
            VerticalAlignment = VerticalAlignment.Center
        };

        Button GhostButton(string text, Action action)
        {
            Button button = new Button
            {
                Content = text,
                Style = Theme.S("Ghost"),
                Height = 30.0,
                MinHeight = 30.0,
                Padding = new Thickness(14.0, 0.0, 14.0, 0.0),
                FontSize = 11.0,
                Margin = new Thickness(0.0, 0.0, 8.0, 0.0)
            };
            button.Click += delegate
            {
                try
                {
                    action();
                }
                catch
                {
                }
            };
            return button;
        }

        static string QualityName(string value) => value switch
        {
            "higher" => "较高",
            "exhigh" => "极高",
            "lossless" => "无损",
            "hires" => "Hi-Res",
            _ => "标准",
        };

        static string OrderName(string value) => value switch
        {
            "shuffle" => "随机播放",
            "repeat" => "单曲循环",
            _ => "顺序播放",
        };

        void PumpSpectrum()
        {
            try
            {
                _spectrum.SetSpectrum(_engine.Player?.Spectrum());
            }
            catch
            {
            }
        }

        void SetImage(string url)
        {
            if (string.IsNullOrEmpty(url))
            {
                _coverUrl = "";
                _cover.Source = null;
                return;
            }
            if (url == _coverUrl)
            {
                return;
            }
            _coverUrl = url;
            try
            {
                BitmapImage image = new BitmapImage();
                image.BeginInit();
                image.UriSource = new Uri(url, UriKind.Absolute);
                image.CacheOption = BitmapCacheOption.OnDemand;
                image.DecodePixelWidth = 360;
                image.EndInit();
                _cover.Source = image;
            }
            catch
            {
                _cover.Source = null;
            }
        }

        void OpenLogin()
        {
            if (_login == null || !_login.IsVisible)
            {
                _login = new LoginWindow(_engine) { Owner = Window.GetWindow(this) };
                _login.Closed += delegate { _login = null; };
                _login.Show();
            }
            else
            {
                _login.Activate();
            }
        }

        static void OpenUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch
            {
            }
        }

        /// <summary>Sixteen thin bars of the manager's yellow: the same bands the mod draws in
        /// game, read from the player's own spectrum.</summary>
        sealed class SpectrumBars : FrameworkElement
        {
            readonly double[] _levels = new double[16];
            readonly Brush _fill = Theme.Yellow;
            readonly Brush _empty = Theme.Grid;

            public SpectrumBars()
            {
                SnapsToDevicePixels = true;
                IsHitTestVisible = false;
            }

            public void SetSpectrum(string hex)
            {
                bool changed = false;
                for (int i = 0; i < _levels.Length; i++)
                {
                    double value = 0.0;
                    if (!string.IsNullOrEmpty(hex) && hex.Length >= (i + 1) * 2)
                    {
                        try
                        {
                            value = Convert.ToByte(hex.Substring(i * 2, 2), 16) / 255.0;
                        }
                        catch
                        {
                            value = 0.0;
                        }
                    }
                    if (Math.Abs(value - _levels[i]) > 0.01)
                    {
                        _levels[i] = value;
                        changed = true;
                    }
                }
                if (changed)
                {
                    InvalidateVisual();
                }
            }

            protected override void OnRender(DrawingContext dc)
            {
                double width = ActualWidth;
                double height = ActualHeight;
                if (width <= 0.0 || height <= 0.0)
                {
                    return;
                }
                double gap = 3.0;
                double barWidth = Math.Max(2.0, (width - gap * (_levels.Length - 1)) / _levels.Length);
                dc.DrawRectangle(_empty, null, new Rect(0.0, height - 1.0, width, 1.0));
                for (int i = 0; i < _levels.Length; i++)
                {
                    double x = i * (barWidth + gap);
                    dc.DrawRectangle(_empty, null, new Rect(x, height - 3.0, barWidth, 1.0));
                    if (_levels[i] > 0.005)
                    {
                        double barHeight = Math.Max(2.0, _levels[i] * (height - 2.0));
                        dc.DrawRectangle(_fill, null, new Rect(x, height - barHeight, barWidth, barHeight));
                    }
                }
            }
        }
    }
}
