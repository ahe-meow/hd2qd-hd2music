using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Hd2Qd.HD2Music
{
    /// <summary>Sign-in as one compact panel: a QR code, a cookie, or a phone and an SMS code,
    /// each behind its own tab. No column to scroll through — the three ways are alternatives,
    /// so only one is on screen at a time. The manager's ground, grid lines, yellow and fonts.</summary>
    public sealed class LoginWindow : Window
    {
        readonly Engine _engine;
        readonly DispatcherTimer _timer;
        readonly DispatcherTimer _closeTimer;
        readonly List<TabRef> _tabs = new List<TabRef>();

        Image _qr;
        TextBlock _status;
        TextBox _cookie;
        TextBlock _cookieStatus;
        TextBox _smsPhone;
        TextBox _smsCode;
        TextBlock _smsStatus;
        Grid _qrPanel;
        Grid _cookiePanel;
        Grid _smsPanel;
        string _tab = "qr";
        string _matrix = "";
        bool _closing;

        Button _openWebsiteButton;

        sealed class TabRef
        {
            public string Page;
            public Border Host;
            public TextBlock Label;
        }

        public LoginWindow(Engine engine)
        {
            _engine = engine;
            Title = "登录 · HD2 音乐";
            Width = 560.0;
            MinHeight = 400.0;
            MaxHeight = SystemParameters.WorkArea.Height - 80.0;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = Theme.Ground900;
            Foreground = Theme.Ink;
            FontFamily = Theme.Font("Ui");
            FontSize = 13.0;

            Grid root = new Grid { Margin = new Thickness(22.0, 18.0, 22.0, 18.0) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(42.0) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            StackPanel header = new StackPanel();
            StackPanel label = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            label.Children.Add(new Border
            {
                Width = 6.0,
                Height = 6.0,
                Background = Theme.Yellow,
                Margin = new Thickness(0.0, 0.0, 8.0, 0.0),
                VerticalAlignment = VerticalAlignment.Center
            });
            label.Children.Add(new TextBlock
            {
                Text = "SIGN IN",
                FontFamily = Theme.Font("Mono"),
                FontSize = 9.0,
                FontWeight = FontWeights.SemiBold,
                Foreground = Theme.Muted
            });
            header.Children.Add(label);
            header.Children.Add(new TextBlock
            {
                Text = "网易云账号",
                FontSize = 22.0,
                FontWeight = FontWeights.SemiBold,
                Foreground = Theme.Ink,
                Margin = new Thickness(0.0, 4.0, 0.0, 2.0)
            });
            header.Children.Add(new TextBlock
            {
                Text = "登录状态只保存在本机。扫码、Cookie、短信验证码任选一种。",
                FontSize = 11.0,
                Foreground = Theme.Muted
            });
            root.Children.Add(header);

            StackPanel tabs = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0.0, 10.0, 0.0, 0.0)
            };
            tabs.Children.Add(Tab("qr", "扫码"));
            tabs.Children.Add(Tab("cookie", "COOKIE"));
            tabs.Children.Add(Tab("sms", "短信"));
            Grid.SetRow(tabs, 1);
            root.Children.Add(tabs);

            Grid content = new Grid { Margin = new Thickness(0.0, 14.0, 0.0, 0.0), MinHeight = 250.0 };
            _qrPanel = BuildQrPanel();
            _cookiePanel = BuildCookiePanel();
            _smsPanel = BuildSmsPanel();
            content.Children.Add(_qrPanel);
            content.Children.Add(_cookiePanel);
            content.Children.Add(_smsPanel);
            Grid.SetRow(content, 2);
            root.Children.Add(content);

            DockPanel footer = new DockPanel { LastChildFill = false, Margin = new Thickness(0.0, 16.0, 0.0, 0.0) };
            _openWebsiteButton = LinkButton("在浏览器登录 music.163.com", delegate
            {
                try
                {
                    Process.Start("https://music.163.com/");
                }
                catch
                {
                }
            });
            footer.Children.Add(_openWebsiteButton);
            Button close = LinkButton("关闭", delegate { Close(); });
            DockPanel.SetDock(close, Dock.Right);
            footer.Children.Add(close);
            Grid.SetRow(footer, 3);
            root.Children.Add(footer);

            Content = root;

            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500.0) };
            _timer.Tick += delegate { Update(); };
            _closeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900.0) };
            _closeTimer.Tick += delegate
            {
                _closeTimer.Stop();
                Close();
            };

            Loaded += delegate
            {
                ShowTab("qr");
                _timer.Start();
                Update();
            };
            Closed += delegate
            {
                _timer.Stop();
                _closeTimer.Stop();
                _engine.CancelLogin();
            };
            PreviewKeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Escape)
                {
                    Close();
                }
            };
        }

        FrameworkElement Tab(string page, string label)
        {
            TextBlock text = new TextBlock
            {
                Text = label,
                FontSize = 12.0,
                Foreground = Theme.Muted,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            Border host = new Border
            {
                Height = 42.0,
                Padding = new Thickness(18.0, 0.0, 18.0, 0.0),
                BorderThickness = new Thickness(0.0),
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                Margin = new Thickness(0.0, 0.0, 4.0, 0.0),
                Child = text
            };
            host.MouseLeftButtonUp += delegate { ShowTab(page); };
            host.MouseEnter += delegate
            {
                if (_tab != page)
                {
                    text.Foreground = Theme.Ink;
                }
            };
            host.MouseLeave += delegate
            {
                if (_tab != page)
                {
                    text.Foreground = Theme.Muted;
                }
            };
            _tabs.Add(new TabRef { Page = page, Host = host, Label = text });
            return host;
        }

        Grid BuildQrPanel()
        {
            Grid panel = new Grid();
            panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(222.0) });
            panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22.0) });
            panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });

            Grid qrHost = new Grid { Background = Theme.Ground700, ClipToBounds = true };
            qrHost.Children.Add(new TextBlock
            {
                Text = "QR",
                FontFamily = Theme.Font("Mono"),
                FontSize = 32.0,
                Foreground = Theme.Grid,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            });
            _qr = new Image { Stretch = Stretch.Uniform, Margin = new Thickness(10.0) };
            qrHost.Children.Add(_qr);
            panel.Children.Add(new Border
            {
                Width = 222.0,
                Height = 222.0,
                BorderBrush = Theme.Grid,
                BorderThickness = new Thickness(1.0),
                Child = qrHost
            });

            StackPanel side = new StackPanel { VerticalAlignment = VerticalAlignment.Top };
            side.Children.Add(new TextBlock
            {
                Text = "用网易云音乐 App 扫码",
                FontSize = 14.0,
                FontWeight = FontWeights.SemiBold,
                Foreground = Theme.Ink
            });
            _status = new TextBlock
            {
                Text = "正在获取二维码…",
                FontSize = 11.0,
                Foreground = Theme.Muted,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0.0, 8.0, 0.0, 0.0)
            };
            side.Children.Add(_status);
            side.Children.Add(new TextBlock
            {
                Text = "扫码后在手机上确认；登录凭证写入本机的 ncm_session.json。",
                FontSize = 10.5,
                Foreground = Theme.Muted,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 16.0,
                Margin = new Thickness(0.0, 14.0, 0.0, 0.0)
            });
            Button refresh = SmallButton("刷新二维码", delegate { _engine.BeginQrLogin(); });
            refresh.HorizontalAlignment = HorizontalAlignment.Left;
            refresh.Margin = new Thickness(0.0, 16.0, 0.0, 0.0);
            side.Children.Add(refresh);
            Grid.SetColumn(side, 2);
            panel.Children.Add(side);
            return panel;
        }

        Grid BuildCookiePanel()
        {
            Grid panel = new Grid();
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.0, GridUnitType.Star) });
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            panel.Children.Add(new TextBlock
            {
                Text = "在浏览器登录 music.163.com，从开发者工具的网络请求里复制包含 MUSIC_U 的 Cookie。",
                FontSize = 11.0,
                Foreground = Theme.Muted,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 17.0,
                Margin = new Thickness(0.0, 0.0, 0.0, 8.0)
            });
            _cookie = new TextBox
            {
                Style = Theme.S("Input"),
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Height = 132.0,
                Padding = new Thickness(10.0, 8.0, 10.0, 8.0),
                Tag = "MUSIC_U=..."
            };
            Grid.SetRow(_cookie, 1);
            panel.Children.Add(_cookie);
            _cookieStatus = new TextBlock
            {
                Text = "",
                FontSize = 11.0,
                Foreground = Theme.Yellow,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0.0, 8.0, 0.0, 0.0)
            };
            Grid.SetRow(_cookieStatus, 2);
            panel.Children.Add(_cookieStatus);
            Button save = SmallButton("验证并保存", delegate { _engine.CookieLogin(_cookie.Text); });
            save.HorizontalAlignment = HorizontalAlignment.Left;
            save.Margin = new Thickness(0.0, 10.0, 0.0, 0.0);
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(save, 3);
            panel.Children.Add(save);
            return panel;
        }

        Grid BuildSmsPanel()
        {
            Grid panel = new Grid();
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            panel.Children.Add(new TextBlock
            {
                Text = "手机号可写 13800000000，或带国家码 +86 13800000000。",
                FontSize = 11.0,
                Foreground = Theme.Muted,
                Margin = new Thickness(0.0, 0.0, 0.0, 8.0)
            });
            _smsPhone = new TextBox
            {
                Style = Theme.S("Input"),
                Height = 38.0,
                Tag = "手机号"
            };
            Grid.SetRow(_smsPhone, 1);
            panel.Children.Add(_smsPhone);

            Grid codeRow = new Grid { Margin = new Thickness(0.0, 10.0, 0.0, 0.0) };
            codeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180.0) });
            codeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            codeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _smsCode = new TextBox
            {
                Style = Theme.S("Input"),
                Height = 38.0,
                Tag = "验证码",
                Margin = new Thickness(0.0, 0.0, 8.0, 0.0)
            };
            codeRow.Children.Add(_smsCode);
            Button send = SmallButton("发送验证码", delegate { _engine.SendSms(_smsPhone.Text); });
            Grid.SetColumn(send, 1);
            codeRow.Children.Add(send);
            Button submit = SmallButton("验证码登录", delegate { _engine.SubmitSms(_smsCode.Text); });
            Grid.SetColumn(submit, 2);
            codeRow.Children.Add(submit);
            Grid.SetRow(codeRow, 2);
            panel.Children.Add(codeRow);

            _smsStatus = new TextBlock
            {
                Text = "",
                FontSize = 11.0,
                Foreground = Theme.Yellow,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0.0, 10.0, 0.0, 0.0)
            };
            Grid.SetRow(_smsStatus, 3);
            panel.Children.Add(_smsStatus);
            return panel;
        }

        Button SmallButton(string text, Action action)
        {
            Button button = new Button
            {
                Content = text,
                Style = Theme.S("Ghost"),
                Height = 32.0,
                MinHeight = 32.0,
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

        Button LinkButton(string text, Action action)
        {
            Button button = new Button
            {
                Content = text,
                Style = Theme.S("Ghost"),
                BorderThickness = new Thickness(0.0),
                Height = 28.0,
                MinHeight = 28.0,
                Padding = new Thickness(0.0, 0.0, 0.0, 0.0),
                FontSize = 10.5,
                Foreground = Theme.Muted
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

        void ShowTab(string page)
        {
            _tab = page;
            _qrPanel.Visibility = page == "qr" ? Visibility.Visible : Visibility.Collapsed;
            _cookiePanel.Visibility = page == "cookie" ? Visibility.Visible : Visibility.Collapsed;
            _smsPanel.Visibility = page == "sms" ? Visibility.Visible : Visibility.Collapsed;
            foreach (TabRef tab in _tabs)
            {
                bool active = tab.Page == page;
                tab.Label.Foreground = active ? Theme.Yellow : Theme.Muted;
                tab.Label.TextDecorations = active ? TextDecorations.Underline : null;
            }
            if (page == "qr" && string.IsNullOrEmpty(_matrix) && !_closing)
            {
                _engine.BeginQrLogin();
            }
            if (page == "cookie")
            {
                _cookie.Focus();
            }
            if (page == "sms")
            {
                _smsPhone.Focus();
            }
        }

        void Update()
        {
            UiState state = _engine.Snapshot();
            _status.Text = state.QrResult == "success"
                ? "登录成功"
                : (string.IsNullOrEmpty(state.Notice) ? "正在获取二维码…" : state.Notice);
            bool verify = state.QrResult == "verification";
            _openWebsiteButton.Content = verify ? "打开官网 · 完成安全验证" : "在浏览器登录 music.163.com";
            _openWebsiteButton.Foreground = verify ? Theme.Yellow : Theme.Muted;
            _cookieStatus.Text = _engine.CookieNotice;
            _smsStatus.Text = state.SmsNotice;
            if (state.QrMatrix != _matrix)
            {
                _matrix = state.QrMatrix;
                _qr.Source = QrBitmap(_matrix);
            }
            if (state.QrResult == "success" && !_closing)
            {
                _closing = true;
                _status.Text = "登录成功，正在返回播放器…";
                _closeTimer.Start();
            }
        }

        static BitmapSource QrBitmap(string matrix)
        {
            if (string.IsNullOrEmpty(matrix))
            {
                return null;
            }
            string[] rows = matrix.Split('/');
            int modules = rows.Length;
            if (modules == 0)
            {
                return null;
            }
            int quiet = 4;
            int scale = Math.Max(2, 220 / (modules + quiet * 2));
            int size = (modules + quiet * 2) * scale;
            int[] pixels = new int[size * size];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = -1;
            }
            for (int y = 0; y < modules; y++)
            {
                string row = rows[y];
                for (int x = 0; x < modules && x < row.Length; x++)
                {
                    if (row[x] != '1')
                    {
                        continue;
                    }
                    for (int dy = 0; dy < scale; dy++)
                    {
                        int at = ((y + quiet) * scale + dy) * size + (x + quiet) * scale;
                        for (int dx = 0; dx < scale; dx++)
                        {
                            pixels[at + dx] = -16777216;
                        }
                    }
                }
            }
            BitmapSource bitmap = BitmapSource.Create(size, size, 96.0, 96.0, PixelFormats.Bgra32, null, pixels, size * 4);
            bitmap.Freeze();
            return bitmap;
        }
    }
}
