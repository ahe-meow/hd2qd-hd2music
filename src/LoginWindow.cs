using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Hd2Qd.HD2Music;

public sealed class LoginWindow : Window
{
	private readonly Engine _engine;

	private readonly Image _qr = new Image();

	private readonly TextBlock _status = new TextBlock
	{
		TextWrapping = TextWrapping.Wrap,
		Margin = new Thickness(0.0, 8.0, 0.0, 8.0)
	};

	private readonly TextBox _cookie = new TextBox
	{
		Height = 74.0,
		AcceptsReturn = true,
		TextWrapping = TextWrapping.Wrap,
		VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
		Background = Theme.Ground900,
		Foreground = Theme.Ink,
		BorderBrush = Theme.Grid,
		Style = Theme.S("Input")
	};

	private readonly TextBlock _cookieStatus = new TextBlock
	{
		TextWrapping = TextWrapping.Wrap,
		Margin = new Thickness(0.0, 6.0, 0.0, 0.0)
	};

	private readonly TextBox _smsPhone = new TextBox
	{
		Height = 32.0,
		Background = Theme.Ground900,
		Foreground = Theme.Ink,
		BorderBrush = Theme.Grid,
		Style = Theme.S("Input")
	};

	private readonly TextBox _smsCode = new TextBox
	{
		Height = 32.0,
		Width = 160.0,
		Background = Theme.Ground900,
		Foreground = Theme.Ink,
		BorderBrush = Theme.Grid,
		Style = Theme.S("Input"),
		Margin = new Thickness(0.0, 0.0, 8.0, 0.0)
	};

	private readonly TextBlock _smsStatus = new TextBlock
	{
		TextWrapping = TextWrapping.Wrap,
		Foreground = Theme.Muted,
		Margin = new Thickness(0.0, 6.0, 0.0, 0.0)
	};

	private string _matrix = "";

	public LoginWindow(Engine engine)
	{
		_engine = engine;
		base.Title = "网易云登录 · HD2QD.HD2Music";
		base.Width = 440.0;
		base.MinHeight = 520.0;
		base.MaxHeight = SystemParameters.WorkArea.Height - 80.0;
		base.SizeToContent = SizeToContent.Height;
		base.WindowStartupLocation = WindowStartupLocation.CenterOwner;
		base.Background = Theme.Ground900;
		base.Foreground = Theme.Ink;
		base.FontFamily = Theme.Font("Ui");
		base.FontSize = 14.0;
		StackPanel stackPanel = new StackPanel
		{
			Margin = new Thickness(20.0)
		};
		stackPanel.Children.Add(new TextBlock
		{
			Text = "扫码登录",
			FontSize = 20.0,
			FontWeight = FontWeights.SemiBold
		});
		stackPanel.Children.Add(new TextBlock
		{
			Text = "用网易云音乐 App 扫码并确认；登录状态会保存在本机。",
			TextWrapping = TextWrapping.Wrap,
			Foreground = Theme.Muted,
			Margin = new Thickness(0.0, 4.0, 0.0, 10.0)
		});
		_qr.Width = 220.0;
		_qr.Height = 220.0;
		_qr.Stretch = Stretch.Uniform;
		_qr.HorizontalAlignment = HorizontalAlignment.Center;
		stackPanel.Children.Add(_qr);
		stackPanel.Children.Add(_status);
		WrapPanel element = new WrapPanel
		{
			Children = 
			{
				(UIElement)Button("刷新二维码", delegate
				{
					_engine.BeginQrLogin();
				}),
				(UIElement)Button("打开网易云官网", delegate
				{
					try
					{
						Process.Start("https://music.163.com/");
					}
					catch
					{
					}
				}),
				(UIElement)Button("取消", delegate
				{
					Close();
				})
			}
		};
		stackPanel.Children.Add(element);
		stackPanel.Children.Add(new Separator
		{
			Margin = new Thickness(0.0, 10.0, 0.0, 8.0),
			Background = Theme.Grid
		});
		stackPanel.Children.Add(new TextBlock
		{
			Text = "手动 Cookie（备用）",
			FontSize = 16.0,
			FontWeight = FontWeights.SemiBold
		});
		stackPanel.Children.Add(new TextBlock
		{
			Text = "浏览器登录 music.163.com 后，从网络请求里复制包含 MUSIC_U 的 Cookie。",
			TextWrapping = TextWrapping.Wrap,
			Foreground = Theme.Muted,
			Margin = new Thickness(0.0, 4.0, 0.0, 8.0)
		});
		stackPanel.Children.Add(_cookie);
		Button element2 = Button("验证并保存 Cookie", delegate
		{
			_engine.CookieLogin(_cookie.Text);
		});
		stackPanel.Children.Add(element2);
		stackPanel.Children.Add(_cookieStatus);
		stackPanel.Children.Add(new Separator
		{
			Margin = new Thickness(0.0, 10.0, 0.0, 8.0),
			Background = Theme.Grid
		});
		stackPanel.Children.Add(new TextBlock
		{
			Text = "短信验证码登录",
			FontSize = 16.0,
			FontWeight = FontWeights.SemiBold
		});
		stackPanel.Children.Add(new TextBlock
		{
			Text = "手机号，例如 13800000000 或 +86 13800000000。",
			TextWrapping = TextWrapping.Wrap,
			Foreground = Theme.Muted,
			Margin = new Thickness(0.0, 4.0, 0.0, 8.0)
		});
		stackPanel.Children.Add(_smsPhone);
		StackPanel stackPanel2 = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			Margin = new Thickness(0.0, 8.0, 0.0, 0.0)
		};
		stackPanel2.Children.Add(_smsCode);
		stackPanel2.Children.Add(Button("发送验证码", delegate
		{
			_engine.SendSms(_smsPhone.Text);
		}));
		stackPanel2.Children.Add(Button("验证码登录", delegate
		{
			_engine.SubmitSms(_smsCode.Text);
		}));
		stackPanel.Children.Add(stackPanel2);
		stackPanel.Children.Add(_smsStatus);
		base.Content = new ScrollViewer
		{
			Content = stackPanel,
			VerticalScrollBarVisibility = ScrollBarVisibility.Auto
		};
		base.Loaded += delegate
		{
			_engine.BeginQrLogin();
		};
		DispatcherTimer timer = new DispatcherTimer
		{
			Interval = TimeSpan.FromMilliseconds(500.0)
		};
		timer.Tick += delegate
		{
			Update();
		};
		timer.Start();
		base.Closed += delegate
		{
			timer.Stop();
		};
	}

	private Button Button(string text, Action action)
	{
		Button button = new Button();
		button.Content = text;
		button.MinHeight = 34.0;
		button.Padding = new Thickness(14.0, 0.0, 14.0, 0.0);
		button.Margin = new Thickness(0.0, 0.0, 10.0, 8.0);
		button.HorizontalAlignment = HorizontalAlignment.Left;
		button.Style = Theme.S("Ghost");
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

	private void Update()
	{
		UiState uiState = _engine.Snapshot();
		_status.Text = (string.IsNullOrEmpty(uiState.Notice) ? "正在等待二维码…" : uiState.Notice);
		if (uiState.QrMatrix != _matrix)
		{
			_matrix = uiState.QrMatrix;
			_qr.Source = QrBitmap(_matrix);
		}
		_cookieStatus.Text = _engine.CookieNotice;
		_smsStatus.Text = uiState.SmsNotice;
	}

	private static BitmapSource QrBitmap(string matrix)
	{
		if (string.IsNullOrEmpty(matrix))
		{
			return null;
		}
		string[] array = matrix.Split('/');
		int num = array.Length;
		if (num == 0)
		{
			return null;
		}
		int num2 = 4;
		int num3 = Math.Max(2, 220 / (num + num2 * 2));
		int num4 = (num + num2 * 2) * num3;
		int[] array2 = new int[num4 * num4];
		for (int i = 0; i < array2.Length; i++)
		{
			array2[i] = -1;
		}
		for (int j = 0; j < num; j++)
		{
			string text = array[j];
			for (int k = 0; k < num && k < text.Length; k++)
			{
				if (text[k] != '1')
				{
					continue;
				}
				for (int l = 0; l < num3; l++)
				{
					int num5 = ((j + num2) * num3 + l) * num4 + (k + num2) * num3;
					for (int m = 0; m < num3; m++)
					{
						array2[num5 + m] = -16777216;
					}
				}
			}
		}
		BitmapSource bitmapSource = BitmapSource.Create(num4, num4, 96.0, 96.0, PixelFormats.Bgra32, null, array2, num4 * 4);
		bitmapSource.Freeze();
		return bitmapSource;
	}
}
