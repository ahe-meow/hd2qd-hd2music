using System.IO;
using System.Reflection;
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

        /// <summary>The Latin UI font renders CJK small. Give translated text a readable floor;
        /// the operation is idempotent so a 300 ms refresh cannot keep growing the font.</summary>
        const double CjkMinimumFontSize = 12.0;

        static bool HasCjk(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            foreach (char c in text)
                if (c >= 0x4E00 && c <= 0x9FFF) return true;
            return false;
        }

        static void Readable(FrameworkElement element, string text)
        {
            if (element == null || !HasCjk(text)) return;
            PropertyInfo size = element.GetType().GetProperty("FontSize");
            if (size == null || size.PropertyType != typeof(double)) return;
            double current = (double)size.GetValue(element, null);
            if (current > 0.0 && current < CjkMinimumFontSize)
                size.SetValue(element, CjkMinimumFontSize, null);
        }

        static void Walk(DependencyObject node)
        {
            if (node is TextBlock block)
            {
                block.Text = Text(block.Text);
                Readable(block, block.Text);
            }
            else if (node is Button button && button.Content is string buttonText)
            {
                button.Content = Text(buttonText);
                Readable(button, buttonText);
            }
            else if (node is CheckBox check && check.Content is string checkText)
            {
                check.Content = Text(checkText);
                Readable(check, checkText);
            }
            else if (node is TabItem tab && tab.Header is string tabText)
            {
                tab.Header = Text(tabText);
                Readable(tab, tabText);
            }
            else if (node is ComboBoxItem item && item.Content is string itemText)
            {
                item.Content = Text(itemText);
                Readable(item, itemText);
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
