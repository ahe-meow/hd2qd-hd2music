using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Hd2Qd.HD2Music
{
    /// <summary>Language selection shared by the manager tab and the login window.
    /// Mode is persisted in ncm_language.txt; "follow" uses the manager's own language.</summary>
    static class I18n
    {
        public const string Follow = "follow";
        public const string Chinese = "zh-CN";
        public const string English = "en";

        static readonly string SettingFile = Path.Combine(Util.Root, "ncm_language.txt");

        static string _hostLang = "";
        static string _mode = LoadMode();

        public static string Mode
        {
            get { return _mode; }
            set
            {
                string next = string.IsNullOrEmpty(value) ? Follow : value;
                if (next == _mode) return;
                _mode = next;
                try
                {
                    Util.AtomicWrite(SettingFile, _mode);
                }
                catch
                {
                }
            }
        }

        public static void SetHostLang(string lang)
        {
            _hostLang = lang ?? "";
        }

        public static string CurrentLang
        {
            get
            {
                if (Mode != Follow) return Mode;
                return string.IsNullOrEmpty(_hostLang) ? English : _hostLang;
            }
        }

        static string LoadMode()
        {
            try
            {
                string text = (Util.ReadText(SettingFile) ?? "").Trim();
                return text == Chinese || text == English ? text : Follow;
            }
            catch
            {
                return Follow;
            }
        }

        /// <summary>Translate a known literal; leave song names and other dynamic text alone.</summary>
        public static string Text(string text)
        {
            return string.IsNullOrEmpty(text) ? text : Words.Get(CurrentLang, text);
        }

        public static void Apply(FrameworkElement root)
        {
            if (root != null)
            {
                Walk(root);
            }
        }

        static void Walk(DependencyObject node)
        {
            if (node is TextBlock block)
            {
                block.Text = Text(block.Text);
            }
            else if (node is Button button && button.Content is string buttonText)
            {
                button.Content = Text(buttonText);
            }
            else if (node is CheckBox check && check.Content is string checkText)
            {
                check.Content = Text(checkText);
            }
            else if (node is TabItem tab && tab.Header is string tabText)
            {
                tab.Header = Text(tabText);
            }
            else if (node is ComboBoxItem item && item.Content is string itemText)
            {
                item.Content = Text(itemText);
            }
            else if (node is TextBox box)
            {
                if (box.ToolTip is string tip) box.ToolTip = Text(tip);
                if (box.Tag is string tag) box.Tag = Text(tag);
            }
            int count = VisualTreeHelper.GetChildrenCount(node);
            for (int i = 0; i < count; i++)
            {
                Walk(VisualTreeHelper.GetChild(node, i));
            }
        }
    }
}
