using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Hd2Qd.HD2Music;

public sealed class PlayerView : UserControl
{
	private readonly Engine _engine;

	private readonly DispatcherTimer _timer;

	private readonly TextBlock _account = new TextBlock
	{
		Foreground = Theme.Ink,
		FontSize = 14.0,
		TextTrimming = TextTrimming.CharacterEllipsis
	};

	private readonly TextBlock _notice = new TextBlock
	{
		Foreground = Theme.Yellow,
		TextWrapping = TextWrapping.Wrap,
		FontSize = 12.0,
		Margin = new Thickness(0.0, 4.0, 0.0, 0.0)
	};

	private readonly TextBlock _title = new TextBlock
	{
		Foreground = Theme.Ink,
		FontSize = 15.0,
		FontWeight = FontWeights.SemiBold,
		TextTrimming = TextTrimming.CharacterEllipsis
	};

	private readonly TextBlock _artist = new TextBlock
	{
		Foreground = Theme.Muted,
		FontSize = 12.0,
		TextTrimming = TextTrimming.CharacterEllipsis
	};

	private readonly TextBlock _lyric = new TextBlock
	{
		Foreground = Theme.Ink,
		FontSize = 16.0,
		TextWrapping = TextWrapping.Wrap,
		TextAlignment = TextAlignment.Center
	};

	private readonly TextBlock _translation = new TextBlock
	{
		Foreground = Theme.Muted,
		FontSize = 13.0,
		TextWrapping = TextWrapping.Wrap,
		TextAlignment = TextAlignment.Center,
		Margin = new Thickness(0.0, 6.0, 0.0, 0.0)
	};

	private readonly Image _coverImage = new Image
	{
		Width = 160.0,
		Height = 160.0,
		Stretch = Stretch.UniformToFill,
		HorizontalAlignment = HorizontalAlignment.Left,
		Margin = new Thickness(0.0, 0.0, 0.0, 12.0)
	};

	private readonly Image _playlistCover = new Image
	{
		Width = 140.0,
		Height = 140.0,
		Stretch = Stretch.UniformToFill,
		HorizontalAlignment = HorizontalAlignment.Left,
		Margin = new Thickness(0.0, 0.0, 0.0, 10.0),
		Visibility = Visibility.Collapsed
	};

	private string _coverUrl = "";

	private readonly TextBlock _time = new TextBlock
	{
		Foreground = Theme.Muted,
		FontFamily = Theme.Font("Mono"),
		FontSize = 11.0,
		Width = 92.0,
		TextAlignment = TextAlignment.Center
	};

	private readonly Button _play = new Button();

	private readonly Slider _progress = new Slider
	{
		Minimum = 0.0,
		Maximum = 1.0,
		IsMoveToPointEnabled = true
	};

	private readonly Slider _volume = new Slider
	{
		Minimum = 0.0,
		Maximum = 100.0,
		Width = 110.0,
		VerticalAlignment = VerticalAlignment.Center
	};

	private readonly ListBox _lists = new ListBox();

	private readonly TextBlock _pageTitle = new TextBlock
	{
		Foreground = Theme.Ink,
		FontSize = 22.0,
		FontWeight = FontWeights.Bold,
		Margin = new Thickness(0.0, 4.0, 0.0, 12.0)
	};

	private readonly StackPanel _home = new StackPanel();

	private readonly StackPanel _searchPanel = new StackPanel();

	private readonly StackPanel _settingsPanel = new StackPanel();

	private readonly StackPanel _radioPanel = new StackPanel();

	private readonly StackPanel _recommendPanel = new StackPanel();

	private readonly StackPanel _usagePanel = new StackPanel();

	private readonly TextBox _searchBox = new TextBox();

	private readonly Button _quality = new Button();

	private readonly Button _order = new Button();

	private readonly TextBlock _ffmpeg = new TextBlock
	{
		Foreground = Theme.Muted,
		FontFamily = Theme.Font("Mono"),
		FontSize = 12.0,
		MaxWidth = 360.0,
		TextTrimming = TextTrimming.CharacterEllipsis,
		VerticalAlignment = VerticalAlignment.Center,
		Margin = new Thickness(0.0, 0.0, 10.0, 0.0)
	};

	private readonly CheckBox _lyricsBox = new CheckBox
	{
		Content = "显示歌词",
		Foreground = Theme.Ink
	};

	private readonly CheckBox _automixBox = new CheckBox
	{
		Content = "无缝/自动顺播",
		Foreground = Theme.Ink
	};

	private readonly TextBlock _cookieNotice = new TextBlock
	{
		Foreground = Theme.Muted,
		TextWrapping = TextWrapping.Wrap,
		FontSize = 12.0
	};

	private LoginWindow _login;

	private UiState _state;

	private string _page = "home";

	private string _listSignature = "";

	private bool _draggingProgress;

	private bool _suppressVolume;

	internal const string WindowTitle = "HD2 音乐";

	/// <summary>The body of the plugin's window, in the manager's own frame when it hands one
	/// over (host["window"]); otherwise a plain window with the same ground and title.</summary>
	internal static Window Show(Window owner, Dictionary<string, object> host, Engine engine)
	{
		PlayerView view = new PlayerView(engine);
		Window window;
		if (host != null && host.TryGetValue("window", out object factory)
			&& factory is Func<string, FrameworkElement, Window> make)
		{
			window = make(WindowTitle, view);
		}
		else
		{
			window = new Window
			{
				Title = WindowTitle,
				Content = view,
				Background = Theme.Ground900,
				Foreground = Theme.Ink,
				WindowStartupLocation = (owner != null) ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen
			};
			if (owner != null)
			{
				window.Owner = owner;
			}
		}
		window.Width = 1120.0;
		window.Height = 760.0;
		window.MinWidth = 820.0;
		window.MinHeight = 560.0;
		window.Show();
		window.Activate();
		return window;
	}

	public PlayerView(Engine engine)
	{
		_engine = engine;
		base.Background = Theme.Ground900;
		base.Foreground = Theme.Ink;
		base.FontFamily = Theme.Font("Ui");
		base.FontSize = 14.0;
		Grid grid = new Grid
		{
			Background = Theme.Ground900,
			RowDefinitions = 
			{
				new RowDefinition
				{
					Height = new GridLength(1.0, GridUnitType.Star)
				},
				new RowDefinition
				{
					Height = new GridLength(96.0)
				}
			}
		};
		Grid grid2 = new Grid
		{
			ColumnDefinitions = 
			{
				new ColumnDefinition
				{
					Width = new GridLength(220.0)
				},
				new ColumnDefinition
				{
					Width = new GridLength(1.0, GridUnitType.Star)
				}
			},
			Children = 
			{
				BuildSidebar(),
				BuildContent()
			}
		};
		Grid.SetColumn(grid2.Children[1], 1);
		grid.Children.Add(grid2);
		UIElement element = BuildPlayerBar();
		Grid.SetRow(element, 1);
		grid.Children.Add(element);
		base.Content = grid;
		_timer = new DispatcherTimer
		{
			Interval = TimeSpan.FromMilliseconds(300.0)
		};
		_timer.Tick += delegate
		{
			Refresh();
		};
		_timer.Start();
		base.Unloaded += delegate
		{
			_timer.Stop();
			_engine.SetVisiblePlaylists(null);
		};		base.Loaded += delegate
		{
			ShowPage("home");
			Refresh();
		};
		Refresh();
	}

	private UIElement BuildSidebar()
	{
		StackPanel obj = new StackPanel
		{
			Background = Theme.Ground700,
			Margin = new Thickness(0.0),
			Children = 
			{
				(UIElement)new TextBlock
				{
					Text = "HD2 MUSIC",
					FontSize = 18.0,
					FontWeight = FontWeights.Bold,
					Foreground = Theme.Yellow,
					Margin = new Thickness(18.0, 20.0, 18.0, 4.0)
				},
				(UIElement)new TextBlock
				{
					Text = "网易云在线助手 · 管理器版",
					FontSize = 11.0,
					Foreground = Theme.Muted,
					Margin = new Thickness(18.0, 0.0, 18.0, 14.0)
				}
			}
		};
		_account.Margin = new Thickness(18.0, 0.0, 18.0, 2.0);
		obj.Children.Add(_account);
		_notice.Margin = new Thickness(18.0, 0.0, 18.0, 8.0);
		obj.Children.Add(_notice);
		Button element = NavButton("登录 / 切换账号", delegate
		{
			OpenLogin();
		});
		obj.Children.Add(element);
		obj.Children.Add(Separator());
		obj.Children.Add(NavButton("首页", delegate
		{
			ShowPage("home");
		}));
		obj.Children.Add(NavButton("我的歌单", delegate
		{
			ShowPage("playlists");
		}));
		obj.Children.Add(NavButton("播放队列", delegate
		{
			ShowPage("queue");
		}));
		obj.Children.Add(NavButton("搜索", delegate
		{
			ShowPage("search");
		}));
		obj.Children.Add(NavButton("每日推荐", delegate
		{
			_engine.Daily();
			ShowPage("queue");
		}));
		obj.Children.Add(NavButton("私人 FM", delegate
		{
			_engine.Fm();
			ShowPage("queue");
		}));
		obj.Children.Add(NavButton("电台 / 播客", delegate
		{
			_engine.RadioDirectory();
			ShowPage("radio");
		}));
		obj.Children.Add(NavButton("推荐歌单", delegate
		{
			_engine.LoadRecommendations();
			ShowPage("recommend");
		}));
		obj.Children.Add(NavButton("设置", delegate
		{
			ShowPage("settings");
		}));
		obj.Children.Add(NavButton("关于", delegate
		{
			ShowPage("usage");
		}));
		obj.Children.Add(Separator());
		obj.Children.Add(new TextBlock
		{
			Text = "ESC 在游戏中打开播放器\n播放需要游戏运行",
			FontSize = 11.0,
			Foreground = Theme.Muted,
			Margin = new Thickness(18.0, 8.0, 18.0, 0.0),
			TextWrapping = TextWrapping.Wrap
		});
		return obj;
	}

	private static FrameworkElement Separator()
	{
		return new Border
		{
			Height = 1.0,
			Background = Theme.Grid,
			Margin = new Thickness(18.0, 6.0, 18.0, 6.0)
		};
	}

	private Button NavButton(string text, Action action)
	{
		Button button = new Button();
		button.Content = text;
		button.HorizontalContentAlignment = HorizontalAlignment.Left;
		button.Height = 36.0;
		button.MinHeight = 36.0;
		button.Margin = new Thickness(12.0, 1.0, 12.0, 1.0);
		button.Padding = new Thickness(10.0, 0.0, 10.0, 0.0);
		button.Style = Theme.S("Ghost");
		button.BorderThickness = new Thickness(0.0);
		button.FontSize = 13.0;
		// the sidebar's rows read from the left edge, unlike a page's action button
		button.Template = Theme.Parse<ControlTemplate>(
			@"<ControlTemplate TargetType=""Button""
				xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation""
				xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"">
				<Border Background=""Transparent"" Padding=""{TemplateBinding Padding}"">
					<ContentPresenter HorizontalAlignment=""Left"" VerticalAlignment=""Center""/>
				</Border>
				<ControlTemplate.Triggers>
					<Trigger Property=""IsMouseOver"" Value=""True"">
						<Setter Property=""Foreground"" Value=""$Yellow$""/>
					</Trigger>
				</ControlTemplate.Triggers>
			</ControlTemplate>");
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

	private UIElement BuildContent()
	{
		StackPanel stackPanel = new StackPanel
		{
			Margin = new Thickness(24.0, 18.0, 24.0, 18.0)
		};
		stackPanel.Children.Add(_pageTitle);
		_home.Children.Add(new TextBlock
		{
			Text = "游戏内按 Esc 打开播放器；在「我的歌单」选择歌单，在「搜索」找歌。",
			TextWrapping = TextWrapping.Wrap,
			Foreground = Theme.Muted,
			FontSize = 13.0,
			Margin = new Thickness(0.0, 0.0, 0.0, 20.0)
		});
		_lyric.Margin = new Thickness(0.0, 30.0, 0.0, 6.0);
		_home.Children.Add(_coverImage);
		_home.Children.Add(_lyric);
		_home.Children.Add(_translation);
		stackPanel.Children.Add(_home);
		StackPanel stackPanel2 = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			Margin = new Thickness(0.0, 0.0, 0.0, 12.0)
		};
		_searchBox.Width = 420.0;
		_searchBox.Height = 34.0;
		_searchBox.FontSize = 14.0;
		_searchBox.Style = Theme.S("Input");
		_searchBox.Margin = new Thickness(0.0, 0.0, 10.0, 0.0);
		_searchBox.KeyDown += delegate(object sender, KeyEventArgs e)
		{
			if (e.Key == Key.Return)
			{
				_engine.Search(_searchBox.Text);
			}
		};
		stackPanel2.Children.Add(_searchBox);
		stackPanel2.Children.Add(ActionButton("搜索", delegate
		{
			_engine.Search(_searchBox.Text);
		}));
		stackPanel2.Children.Add(ActionButton("粘贴并搜索", delegate
		{
			_engine.SearchClipboard();
		}));
		_searchPanel.Children.Add(stackPanel2);
		TextBlock element = new TextBlock
		{
			Text = "输入歌名或歌手后回车；双击结果开始播放。",
			Foreground = Theme.Muted,
			FontSize = 12.0,
			Margin = new Thickness(0.0, 0.0, 0.0, 10.0)
		};
		_searchPanel.Children.Add(element);
		stackPanel.Children.Add(_searchPanel);
		_settingsPanel.Children.Add(SettingRow("音质", _quality));
		SetupCycleButton(_quality, delegate
		{
			_engine.SetQuality(Next(Engine.Qualities, (_state != null) ? _state.Quality : "standard"));
		});
		_settingsPanel.Children.Add(SettingRow("播放顺序", _order));
		SetupCycleButton(_order, delegate
		{
			_engine.SetOrder(Next(Engine.Orders, (_state != null) ? _state.Order : "order"));
		});
		StackPanel ffmpegRow = new StackPanel
		{
			Orientation = Orientation.Horizontal
		};
		ffmpegRow.Children.Add(_ffmpeg);
		Button pick = ActionButton("选择…", delegate
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
		ffmpegRow.Children.Add(pick);
		_settingsPanel.Children.Add(SettingRow("FFmpeg", ffmpegRow));
		_lyricsBox.Style = Theme.S("Tick");
		_lyricsBox.Margin = new Thickness(0.0, 12.0, 0.0, 6.0);
		_lyricsBox.Checked += delegate
		{
			_engine.SetLyrics(enabled: true);
		};
		_lyricsBox.Unchecked += delegate
		{
			_engine.SetLyrics(enabled: false);
		};
		_settingsPanel.Children.Add(_lyricsBox);
		_automixBox.Style = Theme.S("Tick");
		_automixBox.Margin = new Thickness(0.0, 4.0, 0.0, 6.0);
		_automixBox.Checked += delegate
		{
			_engine.SetAutomix(enabled: true);
		};
		_automixBox.Unchecked += delegate
		{
			_engine.SetAutomix(enabled: false);
		};
		_settingsPanel.Children.Add(_automixBox);
		_settingsPanel.Children.Add(_cookieNotice);
		stackPanel.Children.Add(_settingsPanel);
		StackPanel stackPanel3 = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			Margin = new Thickness(0.0, 0.0, 0.0, 10.0)
		};
		stackPanel3.Children.Add(ActionButton("刷新电台", delegate
		{
			_engine.RadioDirectory();
		}));
		stackPanel3.Children.Add(ActionButton("查看节目", delegate
		{
			if (_state != null && _state.Radios.Count > 0 && _lists.SelectedIndex >= 0 && _lists.SelectedIndex < _state.Radios.Count)
			{
				_engine.Podcast(_state.Radios[_lists.SelectedIndex].Id);
			}
		}));
		_radioPanel.Children.Add(stackPanel3);
		stackPanel.Children.Add(_radioPanel);
		StackPanel stackPanel4 = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			Margin = new Thickness(0.0, 0.0, 0.0, 10.0)
		};
		stackPanel4.Children.Add(ActionButton("刷新推荐", delegate
		{
			_engine.LoadRecommendations();
		}));
		_recommendPanel.Children.Add(stackPanel4);
		stackPanel.Children.Add(_recommendPanel);
		_usagePanel.Children.Add(new TextBlock
		{
			Text = "获取方式",
			FontSize = 16.0,
			FontWeight = FontWeights.SemiBold,
			Foreground = Theme.Ink,
			Margin = new Thickness(0.0, 0.0, 0.0, 8.0)
		});
		_usagePanel.Children.Add(new TextBlock
		{
			Text = "HD2 Music 模组与网易云在线助手由 @127_MU 制作；本插件把助手的播放功能搬进管理器，不包含模组文件。请在作者页面获取模组，用 Arsenal 部署后，游戏内按 Esc 打开播放器；播放需要游戏运行。",
			TextWrapping = TextWrapping.Wrap,
			Foreground = Theme.Muted,
			FontSize = 13.0,
			Margin = new Thickness(0.0, 0.0, 0.0, 16.0)
		});
		Button button = new Button
		{
			Content = "作者主页（B 站）",
			Height = 38.0,
			MinHeight = 38.0,
			Padding = new Thickness(18.0, 0.0, 18.0, 0.0),
			FontSize = 14.0,
			Style = Theme.S("Primary"),
			HorizontalAlignment = HorizontalAlignment.Left
		};
		button.Click += delegate
		{
			OpenUrl("https://space.bilibili.com/283469182");
		};
		_usagePanel.Children.Add(button);
		_usagePanel.Children.Add(new TextBlock
		{
			Text = "插件 HD2QD.HD2Music 1.0.0 · github.com/ahe-meow/hd2qd-hd2music",
			TextWrapping = TextWrapping.Wrap,
			FontFamily = Theme.Font("Mono"),
			Foreground = Theme.Muted,
			FontSize = 11.0,
			Margin = new Thickness(0.0, 10.0, 0.0, 0.0)
		});
		TextBlock textBlock = new TextBlock
		{
			FontSize = 13.0,
			Foreground = Theme.Muted,
			Margin = new Thickness(0.0, 20.0, 0.0, 0.0)
		};
		textBlock.Inlines.Add(new Run("模组原作者 "));
		Hyperlink hyperlink = new Hyperlink();
		hyperlink.Inlines.Add(new Run("@127_MU"));
		hyperlink.Foreground = Theme.Yellow;
		hyperlink.TextDecorations = TextDecorations.Underline;
		hyperlink.Cursor = Cursors.Hand;
		hyperlink.Click += delegate
		{
			OpenUrl("https://space.bilibili.com/283469182");
		};
		textBlock.Inlines.Add(hyperlink);
		_usagePanel.Children.Add(textBlock);
		stackPanel.Children.Add(_usagePanel);
		_lists.Height = 440.0;
		_lists.Background = Theme.Ground700;
		_lists.Foreground = Theme.Ink;
		_lists.BorderThickness = new Thickness(0.0);
		_lists.ItemContainerStyle = Theme.S("Row");
		_lists.MouseDoubleClick += delegate
		{
			OpenSelected();
		};
		_lists.SelectionChanged += delegate
		{
			UpdateSelectionCover();
		};
		stackPanel.Children.Add(_playlistCover);
		stackPanel.Children.Add(_lists);
		return new ScrollViewer
		{
			Content = stackPanel,
			VerticalScrollBarVisibility = ScrollBarVisibility.Auto
		};
	}

	private static StackPanel SettingRow(string label, UIElement control)
	{
		return new StackPanel
		{
			Orientation = Orientation.Horizontal,
			Margin = new Thickness(0.0, 6.0, 0.0, 6.0),
			Children = 
			{
				(UIElement)new TextBlock
				{
					Text = label,
					Width = 90.0,
					Foreground = Theme.Ink,
					VerticalAlignment = VerticalAlignment.Center
				},
				control
			}
		};
	}

	private Button ActionButton(string text, Action action)
	{
		Button button = new Button();
		button.Content = text;
		button.Style = Theme.S("Ghost");
		button.MinHeight = 34.0;
		button.Padding = new Thickness(14.0, 0.0, 14.0, 0.0);
		button.Margin = new Thickness(0.0, 0.0, 8.0, 0.0);
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

	private static void SetupCycleButton(Button button, Action action)
	{
		button.Style = Theme.S("Ghost");
		button.MinHeight = 34.0;
		button.Padding = new Thickness(14.0, 0.0, 14.0, 0.0);
		button.HorizontalContentAlignment = HorizontalAlignment.Left;
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
	}

	private static string Next(string[] values, string current)
	{
		int at = Array.IndexOf(values, current);
		return values[(at + 1) % values.Length];
	}

	private static string QualityName(string value) => value switch
	{
		"higher" => "较高",
		"exhigh" => "极高",
		"lossless" => "无损",
		"hires" => "Hi-Res",
		_ => "标准",
	};

	private static string OrderName(string value) => value switch
	{
		"shuffle" => "随机播放",
		"repeat" => "单曲循环",
		_ => "顺序播放",
	};

	private UIElement BuildPlayerBar()
	{
		Grid obj = new Grid
		{
			Background = Theme.Ground700,
			Margin = new Thickness(0.0, 1.0, 0.0, 0.0),
			ColumnDefinitions = 
			{
				new ColumnDefinition
				{
					Width = new GridLength(240.0)
				},
				new ColumnDefinition
				{
					Width = new GridLength(150.0)
				},
				new ColumnDefinition
				{
					Width = new GridLength(1.0, GridUnitType.Star)
				},
				new ColumnDefinition
				{
					Width = new GridLength(210.0)
				}
			}
		};
		StackPanel stackPanel = new StackPanel
		{
			Margin = new Thickness(20.0, 0.0, 10.0, 0.0),
			VerticalAlignment = VerticalAlignment.Center
		};
		stackPanel.Children.Add(_title);
		stackPanel.Children.Add(_artist);
		obj.Children.Add(stackPanel);
		StackPanel stackPanel2 = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center
		};
		stackPanel2.Children.Add(ActionButton("⏮", delegate
		{
			_engine.Previous();
		}));
		_play.Content = "▶";
		_play.Width = 48.0;
		_play.Height = 38.0;
		_play.MinHeight = 38.0;
		_play.FontSize = 18.0;
		_play.Padding = new Thickness(0.0);
		_play.Margin = new Thickness(6.0, 0.0, 6.0, 0.0);
		_play.Style = Theme.S("Primary");
		_play.Click += delegate
		{
			_engine.Toggle();
		};
		stackPanel2.Children.Add(_play);
		stackPanel2.Children.Add(ActionButton("⏭", delegate
		{
			_engine.Next();
		}));
		obj.Children.Add(stackPanel2);
		Grid.SetColumn(stackPanel2, 1);
		Grid grid = new Grid
		{
			Margin = new Thickness(10.0, 0.0, 10.0, 0.0),
			VerticalAlignment = VerticalAlignment.Center
		};
		grid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(1.0, GridUnitType.Star)
		});
		grid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(100.0)
		});
		_progress.Height = 22.0;
		_progress.Style = Theme.S("Range");
		_progress.PreviewMouseDown += delegate
		{
			_draggingProgress = true;
		};
		_progress.PreviewMouseUp += delegate
		{
			_draggingProgress = false;
			_engine.Seek(_progress.Value);
		};
		grid.Children.Add(_progress);
		_time.VerticalAlignment = VerticalAlignment.Center;
		Grid.SetColumn(_time, 1);
		grid.Children.Add(_time);
		obj.Children.Add(grid);
		Grid.SetColumn(grid, 2);
		StackPanel stackPanel3 = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			HorizontalAlignment = HorizontalAlignment.Right,
			VerticalAlignment = VerticalAlignment.Center,
			Margin = new Thickness(0.0, 0.0, 14.0, 0.0)
		};
		stackPanel3.Children.Add(new TextBlock
		{
			Text = "音量",
			Foreground = Theme.Muted,
			Margin = new Thickness(0.0, 0.0, 8.0, 0.0),
			VerticalAlignment = VerticalAlignment.Center
		});
		_volume.Value = 65.0;
		_volume.Style = Theme.S("Range");
		_volume.ValueChanged += delegate
		{
			if (!_suppressVolume)
			{
				_engine.SetVolume((int)_volume.Value);
			}
		};
		stackPanel3.Children.Add(_volume);
		stackPanel3.Children.Add(ActionButton("词", delegate
		{
			if (_state != null)
			{
				_engine.SetLyrics(!_state.LyricsEnabled);
			}
		}));
		stackPanel3.Children.Add(ActionButton("序", delegate
		{
			if (_state != null)
			{
				string order = ((_state.Order == "order") ? "shuffle" : ((_state.Order == "shuffle") ? "repeat" : "order"));
				_engine.SetOrder(order);
			}
		}));
		obj.Children.Add(stackPanel3);
		Grid.SetColumn(stackPanel3, 3);
		return obj;
	}

	private void OpenLogin()
	{
		if (_login == null || !_login.IsVisible)
		{
			_login = new LoginWindow(_engine)
			{
				Owner = Window.GetWindow(this)
			};
			_login.Closed += delegate
			{
				_login = null;
			};
			_login.Show();
		}
		else
		{
			_login.Activate();
		}
	}

	private void ShowPage(string page)
	{
		_page = page;
		_home.Visibility = ((!(page == "home")) ? Visibility.Collapsed : Visibility.Visible);
		_searchPanel.Visibility = ((!(page == "search")) ? Visibility.Collapsed : Visibility.Visible);
		_settingsPanel.Visibility = ((!(page == "settings")) ? Visibility.Collapsed : Visibility.Visible);
		_radioPanel.Visibility = ((!(page == "radio")) ? Visibility.Collapsed : Visibility.Visible);
		_recommendPanel.Visibility = ((!(page == "recommend")) ? Visibility.Collapsed : Visibility.Visible);
		_usagePanel.Visibility = ((!(page == "usage")) ? Visibility.Collapsed : Visibility.Visible);
		ListBox lists = _lists;
		int visibility;
		switch (page)
		{
		default:
			visibility = 2;
			break;
		case "playlists":
		case "queue":
		case "recommend":
		case "radio":
		case "podcast":
		case "search":
			visibility = 0;
			break;
		}
		lists.Visibility = (Visibility)visibility;
		_playlistCover.Visibility = ((!(page == "playlists") && !(page == "recommend")) ? Visibility.Collapsed : Visibility.Visible);
		if (page != "playlists")
		{
			_engine.SetVisiblePlaylists(null);
		}
		_pageTitle.Text = page switch
		{
			"usage" => "关于", 
			"recommend" => "推荐歌单", 
			"radio" => "电台 / 播客", 
			"search" => "搜索", 
			"queue" => "播放队列", 
			"playlists" => "我的歌单", 
			"home" => "首页", 
			_ => "设置", 
		};
		if (page == "playlists")
		{
			_engine.RefreshAccount();
		}
		if (page == "recommend")
		{
			_engine.LoadRecommendations();
		}
		if (page == "radio")
		{
			_engine.RadioDirectory();
		}
		_listSignature = "";
		Refresh();
	}

	private void Refresh()
	{
		if (_engine != null)
		{
			UiState uiState = (_state = _engine.Snapshot());
			_account.Text = (string.IsNullOrEmpty(uiState.Account) ? "未登录" : uiState.Account);
			_notice.Text = uiState.Notice;
			_title.Text = (string.IsNullOrEmpty(uiState.Title) ? "未选歌曲" : uiState.Title);
			_artist.Text = (string.IsNullOrEmpty(uiState.Artist) ? "" : uiState.Artist);
			SetCover(uiState.CoverUrl);
			_lyric.Text = uiState.Lyric;
			_translation.Text = uiState.Translation;
			_play.Content = ((uiState.State == "playing") ? "⏸" : "▶");
			_time.Text = Util.FormatTime(uiState.Pos) + " / " + Util.FormatTime(uiState.Duration);
			if (!_draggingProgress)
			{
				_progress.Maximum = Math.Max(1.0, uiState.Duration);
				_progress.Value = Math.Max(0.0, Math.Min(uiState.Pos, _progress.Maximum));
			}
			_suppressVolume = true;
			_volume.Value = uiState.Volume;
			_suppressVolume = false;
			_lyricsBox.IsChecked = uiState.LyricsEnabled;
			_automixBox.IsChecked = uiState.Automix;
			_quality.Content = QualityName(uiState.Quality);
			_order.Content = OrderName(uiState.Order);
			_ffmpeg.Text = _engine.FfmpegPath;
			_ffmpeg.Foreground = File.Exists(_engine.FfmpegPath) ? Theme.Muted : Theme.Yellow;
			_cookieNotice.Text = _engine.CookieNotice;
			RefreshList(uiState);
		}
	}

	private void RefreshList(UiState state)
	{
		List<string> list = new List<string>();
		string text;
		if (_page == "playlists")
		{
			text = "p:" + state.Lists.Count;
			foreach (Playlist list2 in state.Lists)
			{
				list.Add(list2.Name);
			}
		}
		else if (_page == "queue")
		{
			text = "q:" + state.Queue.Count + ":" + state.Index;
			for (int i = 0; i < state.Queue.Count; i++)
			{
				list.Add(i + 1 + ". " + state.Queue[i].Title + "  " + state.Queue[i].Artist);
			}
		}
		else if (_page == "recommend")
		{
			text = "r:" + state.Recommended.Count;
			foreach (Playlist item in state.Recommended)
			{
				list.Add(item.Name);
			}
		}
		else if (_page == "radio")
		{
			text = "radio:" + state.Radios.Count + ":" + state.PodcastTracks.Count;
			foreach (RadioChannel radio in state.Radios)
			{
				list.Add("电台  " + radio.Name);
			}
			foreach (Track podcastTrack in state.PodcastTracks)
			{
				list.Add("节目  " + podcastTrack.Title);
			}
		}
		else
		{
			if (!(_page == "search"))
			{
				return;
			}
			text = "s:" + state.SearchSerial + ":" + state.SearchResults.Count;
			foreach (Track searchResult in state.SearchResults)
			{
				list.Add(searchResult.Title + "  " + searchResult.Artist);
			}
		}
		if (text == _listSignature)
		{
			return;
		}
		_listSignature = text;
		_lists.Items.Clear();
		foreach (string item2 in list)
		{
			_lists.Items.Add(item2);
		}
		if (_lists.Items.Count > 0 && _lists.SelectedIndex < 0)
		{
			_lists.SelectedIndex = 0;
		}
		UpdateSelectionCover();
	}

	private void UpdateVisiblePlaylists()
	{
		if (_page != "playlists" || _state == null)
		{
			_engine.SetVisiblePlaylists(null);
			return;
		}
		int num = ((_lists.SelectedIndex >= 0) ? Math.Max(0, _lists.SelectedIndex - 4) : 0);
		int num2 = Math.Min(_state.Lists.Count, num + 12);
		List<string> list = new List<string>();
		for (int i = num; i < num2; i++)
		{
			list.Add(_state.Lists[i].Id);
		}
		_engine.SetVisiblePlaylists(list);
	}

	private void UpdateSelectionCover()
	{
		if (_state != null)
		{
			string url = "";
			int selectedIndex = _lists.SelectedIndex;
			if (_page == "playlists" && selectedIndex >= 0 && selectedIndex < _state.Lists.Count)
			{
				url = _state.Lists[selectedIndex].CoverUrl;
			}
			else if (_page == "recommend" && selectedIndex >= 0 && selectedIndex < _state.Recommended.Count)
			{
				url = _state.Recommended[selectedIndex].CoverUrl;
			}
			SetImage(_playlistCover, url);
			UpdateVisiblePlaylists();
		}
	}

	private void OpenSelected()
	{
		UiState uiState = _engine.Snapshot();
		int selectedIndex = _lists.SelectedIndex;
		if (selectedIndex >= 0)
		{
			if (_page == "playlists" && selectedIndex < uiState.Lists.Count)
			{
				_engine.OpenPlaylist(uiState.Lists[selectedIndex]);
			}
			else if (_page == "queue")
			{
				_engine.Play(selectedIndex);
			}
			else if (_page == "recommend" && selectedIndex < uiState.Recommended.Count)
			{
				_engine.OpenPlaylist(uiState.Recommended[selectedIndex]);
			}
			else if (_page == "radio" && selectedIndex < uiState.Radios.Count)
			{
				_engine.RadioPlay(uiState.Radios[selectedIndex]);
			}
			else if (_page == "radio")
			{
				_engine.PodcastPlay(selectedIndex - uiState.Radios.Count);
			}
			else if (_page == "search")
			{
				_engine.SearchPlay(selectedIndex);
			}
		}
	}

	private void SetCover(string url)
	{
		if (!(url == _coverUrl))
		{
			_coverUrl = url ?? "";
			SetImage(_coverImage, url);
		}
	}

	private static void OpenUrl(string url)
	{
		try
		{
			Process.Start(new ProcessStartInfo(url)
			{
				UseShellExecute = true
			});
		}
		catch
		{
		}
	}

	private void SetImage(Image target, string url)
	{
		if (string.IsNullOrEmpty(url))
		{
			target.Source = null;
			return;
		}
		try
		{
			BitmapImage bitmapImage = new BitmapImage();
			bitmapImage.BeginInit();
			bitmapImage.UriSource = new Uri(url, UriKind.Absolute);
			bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
			bitmapImage.DecodePixelWidth = 360;
			bitmapImage.EndInit();
			target.Source = bitmapImage;
		}
		catch
		{
			target.Source = null;
		}
	}
}
