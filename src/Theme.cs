using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;

namespace Hd2Qd.HD2Music
{
    /// <summary>The manager's colours, fonts and control styles: its own resources when the
    /// manager is drawing the plugin, and the same values copied in Ui.xaml when it is not
    /// (a test window, an older manager). Nothing here is a Windows default: a plugin's window
    /// must look like the manager's, not like a stock WPF window dropped on top of it.</summary>
    static class Theme
    {
        static ResourceDictionary _fallback;

        static ResourceDictionary Fallback
        {
            get
            {
                if (_fallback != null) return _fallback;
                var asm = typeof(Theme).Assembly;
                using (var r = new StreamReader(asm.GetManifestResourceStream("Ui.xaml")))
                    _fallback = (ResourceDictionary)XamlReader.Parse(r.ReadToEnd());
                return _fallback;
            }
        }

        static object Get(string key) =>
            (Application.Current != null ? Application.Current.TryFindResource(key) : null) ?? Fallback[key];

        /// <summary>When the drawing application is not the manager (a test window, an older
        /// manager), the copied dictionary goes into its resources too: the implicit styles of
        /// a scrollbar and a menu are found there and nowhere else. With the manager's resources
        /// present nothing is added, so its theme is never overridden.</summary>
        internal static void Adopt()
        {
            var app = Application.Current;
            if (app == null) return;
            if (app.TryFindResource("Ink") != null) return;
            foreach (var dictionary in app.Resources.MergedDictionaries)
                if (ReferenceEquals(dictionary, Fallback)) return;
            app.Resources.MergedDictionaries.Add(Fallback);
        }

        internal static Brush B(string key)
        {
            Adopt();
            return Get(key) as Brush ?? Brushes.Transparent;
        }

        internal static Style S(string key) => Get(key) as Style;

        internal static FontFamily Font(string key) => Get(key) as FontFamily ?? new FontFamily("Microsoft YaHei UI");

        // the design board's tokens, by name, so a window never writes a colour of its own
        internal static Brush Ground900 => B("Ground900");
        internal static Brush Ground700 => B("Ground700");
        internal static Brush Grid => B("Grid");
        internal static Brush Ink => B("Ink");
        internal static Brush Muted => B("Muted");
        internal static Brush Yellow => B("Yellow");
        internal static Brush Blue => B("Blue");
        internal static Brush TapeInk => B("TapeInk");
        internal static Brush Band => B("Band");

        /// <summary>A colour of the theme as #AARRGGBB, for the templates a window builds from
        /// text: those cannot reach the manager's dictionary, so the values go in.</summary>
        internal static string Hex(string key) =>
            (B(key) as SolidColorBrush)?.Color.ToString() ?? "#FF000000";

        /// <summary>A template of this window, its $Key$ placeholders being the theme's colours
        /// and fonts, so a copied template cannot drift from the manager's theme.</summary>
        internal static T Parse<T>(string xaml) where T : class
        {
            var text = xaml
                .Replace("$Ground900$", Hex("Ground900"))
                .Replace("$Ground700$", Hex("Ground700"))
                .Replace("$Grid$", Hex("Grid"))
                .Replace("$Ink$", Hex("Ink"))
                .Replace("$Muted$", Hex("Muted"))
                .Replace("$Yellow$", Hex("Yellow"))
                .Replace("$Blue$", Hex("Blue"))
                .Replace("$TapeInk$", Hex("TapeInk"))
                .Replace("$Band$", Hex("Band"))
                .Replace("$Ui$", Font("Ui").Source)
                .Replace("$Mono$", Font("Mono").Source);
            return (T)XamlReader.Parse(text);
        }

        /// <summary>A panel of the manager: the ground one step up, a grid line round it.</summary>
        internal static Border Card(UIElement child, double padding) => new Border
        {
            Background = B("Ground700"),
            BorderBrush = B("Grid"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(padding),
            Child = child,
        };

        internal static TextBlock Words(string text, double size, Brush colour, bool bold = false) => new TextBlock
        {
            Text = text,
            FontSize = size,
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = colour,
            VerticalAlignment = VerticalAlignment.Center,
        };
    }

    /// <summary>
    /// Media icons from Remix Icon (https://remixicon.com), Remix Icon License v1.0.
    /// Path data is kept as geometry so a button is a Path, never a font glyph or a PNG.
    /// </summary>
    internal static class Icon
    {
        static Geometry Parse(string path) => Geometry.Parse(path);

        public static readonly Geometry Previous = Parse("M8 11.3333L18.2227 4.51823C18.4524 4.36506 18.7628 4.42714 18.916 4.65691C18.9708 4.73904 19 4.83555 19 4.93426V19.0657C19 19.3419 18.7761 19.5657 18.5 19.5657C18.4013 19.5657 18.3048 19.5365 18.2227 19.4818L8 12.6667V19C8 19.5523 7.55228 20 7 20C6.44772 20 6 19.5523 6 19V5C6 4.44772 6.44772 4 7 4C7.55228 4 8 4.44772 8 5V11.3333Z");

        public static readonly Geometry Play = Parse("M19.376 12.4161L8.77735 19.4818C8.54759 19.635 8.23715 19.5729 8.08397 19.3432C8.02922 19.261 8 19.1645 8 19.0658V4.93433C8 4.65818 8.22386 4.43433 8.5 4.43433C8.59871 4.43433 8.69522 4.46355 8.77735 4.5183L19.376 11.584C19.6057 11.7372 19.6678 12.0477 19.5146 12.2774C19.478 12.3323 19.4309 12.3795 19.376 12.4161Z");

        public static readonly Geometry Pause = Parse("M6 5H8V19H6V5ZM16 5H18V19H16V5Z");

        public static readonly Geometry Next = Parse("M16 12.6667L5.77735 19.4818C5.54759 19.6349 5.23715 19.5729 5.08397 19.3431C5.02922 19.261 5 19.1645 5 19.0657V4.93426C5 4.65812 5.22386 4.43426 5.5 4.43426C5.59871 4.43426 5.69522 4.46348 5.77735 4.51823L16 11.3333V5C16 4.44772 16.4477 4 17 4C17.5523 4 18 4.44772 18 5V19C18 19.5523 17.5523 20 17 20C16.4477 20 16 19.5523 16 19V12.6667Z");

        public static readonly Geometry Volume = Parse("M2 16.0001H5.88889L11.1834 20.3319C11.2727 20.405 11.3846 20.4449 11.5 20.4449C11.7761 20.4449 12 20.2211 12 19.9449V4.05519C12 3.93977 11.9601 3.8279 11.887 3.73857C11.7121 3.52485 11.3971 3.49335 11.1834 3.66821L5.88889 8.00007H2C1.44772 8.00007 1 8.44778 1 9.00007V15.0001C1 15.5524 1.44772 16.0001 2 16.0001ZM23 12C23 15.292 21.5539 18.2463 19.2622 20.2622L17.8445 18.8444C19.7758 17.1937 21 14.7398 21 12C21 9.26016 19.7758 6.80629 17.8445 5.15557L19.2622 3.73779C21.5539 5.75368 23 8.70795 23 12ZM18 12C18 10.0883 17.106 8.38548 15.7133 7.28673L14.2842 8.71584C15.3213 9.43855 16 10.64 16 12C16 13.36 15.3213 14.5614 14.2842 15.2841L15.7133 16.7132C17.106 15.6145 18 13.9116 18 12Z");

        public static readonly Geometry Mute = Parse("M5.88889 16.0001H2C1.44772 16.0001 1 15.5524 1 15.0001V9.00007C1 8.44778 1.44772 8.00007 2 8.00007H5.88889L11.1834 3.66821C11.3971 3.49335 11.7121 3.52485 11.887 3.73857C11.9601 3.8279 12 3.93977 12 4.05519V19.9449C12 20.2211 11.7761 20.4449 11.5 20.4449C11.3846 20.4449 11.2727 20.405 11.1834 20.3319L5.88889 16.0001ZM20.4142 12.0001L23.9497 15.5356L22.5355 16.9498L19 13.4143L15.4645 16.9498L14.0503 15.5356L17.5858 12.0001L14.0503 8.46454L15.4645 7.05032L19 10.5859L22.5355 7.05032L23.9497 8.46454L20.4142 12.0001Z");

        public static readonly Geometry Hq = Parse("M3 3H21C21.5523 3 22 3.44772 22 4V20C22 20.5523 21.5523 21 21 21H3C2.44772 21 2 20.5523 2 20V4C2 3.44772 2.44772 3 3 3ZM7.5 11.25V9H6V15H7.5V12.75H9.5V15H11V9H9.5V11.25H7.5ZM16.25 15H17C17.5523 15 18 14.5523 18 14V10C18 9.44772 17.5523 9 17 9H14C13.4477 9 13 9.44772 13 10V14C13 14.5523 13.4477 15 14 15H14.75V16.5H16.25V15ZM14.5 10.5H16.5V13.5H14.5V10.5Z");

        public static readonly Geometry Order = Parse("M17 3.99998V2.0675C17 1.79136 17.2239 1.5675 17.5 1.5675C17.617 1.5675 17.7302 1.60851 17.8201 1.68339L21.9391 5.11587C22.1512 5.29266 22.1799 5.60794 22.0031 5.82008C21.9081 5.93407 21.7674 5.99998 21.619 5.99998H2V3.99998H17ZM2 18H22V20H2V18ZM2 11H22V13H2V11Z");

        public static readonly Geometry Shuffle = Parse("M18 17.8832V16L23 19L18 22V19.9095C14.9224 19.4698 12.2513 17.4584 11.0029 14.5453L11 14.5386L10.9971 14.5453C9.57893 17.8544 6.32508 20 2.72483 20H2V18H2.72483C5.52503 18 8.05579 16.3312 9.15885 13.7574L9.91203 12L9.15885 10.2426C8.05579 7.66878 5.52503 6 2.72483 6H2V4H2.72483C6.32508 4 9.57893 6.14557 10.9971 9.45473L11 9.46141L11.0029 9.45473C12.2513 6.5416 14.9224 4.53022 18 4.09051V2L23 5L18 8V6.11684C15.7266 6.53763 13.7737 8.0667 12.8412 10.2426L12.088 12L12.8412 13.7574C13.7737 15.9333 15.7266 17.4624 18 17.8832Z");

        public static readonly Geometry Repeat = Parse("M6 4H21C21.5523 4 22 4.44772 22 5V12H20V6H6V9L1 5L6 1V4ZM18 20H3C2.44772 20 2 19.5523 2 19V12H4V18H18V15L23 19L18 23V20Z");

        public static readonly Geometry RepeatOne = Parse("M8 20V21.9325C8 22.2086 7.77614 22.4325 7.5 22.4325C7.38303 22.4325 7.26977 22.3915 7.17991 22.3166L3.06093 18.8841C2.84879 18.7073 2.82013 18.392 2.99691 18.1799C3.09191 18.0659 3.23264 18 3.38103 18H8L18 18C19.1046 18 20 17.1046 20 16V8H22V16C22 18.2091 20.2091 20 18 20H8ZM16 4V2.0675C16 1.79136 16.2239 1.5675 16.5 1.5675C16.617 1.5675 16.7302 1.60851 16.8201 1.68339L20.9391 5.11587C21.1512 5.29266 21.1799 5.60794 21.0031 5.82008C20.9081 5.93407 20.7674 5.99998 20.619 5.99998H16L6 6C4.89543 6 4 6.89543 4 8V16H2V8C2 5.79086 3.79086 4 6 4H16ZM11 8H13V16H11V10H9V9L11 8Z");
    }
}
