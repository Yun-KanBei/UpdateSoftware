using System.Windows;
using System.Windows.Media;

namespace UpdateSoftware
{
    public static class ThemeManager
    {
        public class ThemeInfo
        {
            public string Name { get; set; }
            public int Index { get; set; }

            public Color Accent { get; set; }
            public Color Success { get; set; }
            public Color Danger { get; set; }

            public Color WindowBg { get; set; }
            public Color ControlBg { get; set; }
            public Color SideBarBg { get; set; }
            public Color InputBg { get; set; }
            public Color BorderColor { get; set; }
            public Color PageText { get; set; }
            public Color TextSecondary { get; set; }
            public Color TextMuted { get; set; }
        }

        private static ThemeInfo[] _themes;

        public static ThemeInfo[] Themes
        {
            get
            {
                if (_themes == null)
                {
                    _themes = new[]
                    {
                        // ===== 夜间模式（默认）=====
                        new ThemeInfo
                        {
                            Name = "夜间模式", Index = 0,
                            Accent = Color.FromRgb(0x00, 0x7A, 0xCC),
                            Success = Color.FromRgb(0x38, 0x8E, 0x3C),
                            Danger = Color.FromRgb(0xD3, 0x2F, 0x2F),
                            WindowBg = Color.FromRgb(0x1E, 0x1E, 0x1E),
                            ControlBg = Color.FromRgb(0x2D, 0x2D, 0x30),
                            SideBarBg = Color.FromRgb(0x25, 0x25, 0x26),
                            InputBg = Color.FromRgb(0x3C, 0x3C, 0x3C),
                            BorderColor = Color.FromRgb(0x44, 0x44, 0x44),
                            PageText = Color.FromRgb(0xE0, 0xE0, 0xE0),
                            TextSecondary = Color.FromRgb(0x99, 0x99, 0x99),
                            TextMuted = Color.FromRgb(0x77, 0x77, 0x77),
                        },
                        // ===== 日间模式（浅色）=====
                        new ThemeInfo
                        {
                            Name = "日间模式", Index = 1,
                            Accent = Color.FromRgb(0x00, 0x7A, 0xCC),
                            Success = Color.FromRgb(0x38, 0x8E, 0x3C),
                            Danger = Color.FromRgb(0xD3, 0x2F, 0x2F),
                            WindowBg = Color.FromRgb(0xF0, 0xF0, 0xF0),
                            ControlBg = Color.FromRgb(0xE8, 0xE8, 0xE8),
                            SideBarBg = Color.FromRgb(0xF5, 0xF5, 0xF5),
                            InputBg = Color.FromRgb(0xFF, 0xFF, 0xFF),
                            BorderColor = Color.FromRgb(0xCC, 0xCC, 0xCC),
                            PageText = Color.FromRgb(0x1E, 0x1E, 0x1E),
                            TextSecondary = Color.FromRgb(0x55, 0x55, 0x55),
                            TextMuted = Color.FromRgb(0x88, 0x88, 0x88),
                        },
                    };
                }
                return _themes;
            }
        }

        private static int _currentIndex = 0;
        public static int CurrentIndex => _currentIndex;

        /// <summary>应用指定索引的主题（刷新所有颜色资源）</summary>
        public static void ApplyTheme(int index)
        {
            if (index < 0 || index >= Themes.Length) return;
            _currentIndex = index;
            var t = Themes[index];
            var res = Application.Current.Resources;

            res["AccentColor"] = new SolidColorBrush(t.Accent);
            res["SuccessColor"] = new SolidColorBrush(t.Success);
            res["DangerColor"] = new SolidColorBrush(t.Danger);

            res["WindowBg"] = new SolidColorBrush(t.WindowBg);
            res["ControlBg"] = new SolidColorBrush(t.ControlBg);
            res["SideBarBg"] = new SolidColorBrush(t.SideBarBg);
            res["InputBg"] = new SolidColorBrush(t.InputBg);
            res["BorderColor"] = new SolidColorBrush(t.BorderColor);

            res["PageText"] = new SolidColorBrush(t.PageText);
            res["TextSecondary"] = new SolidColorBrush(t.TextSecondary);
            res["TextMuted"] = new SolidColorBrush(t.TextMuted);
        }
    }
}
