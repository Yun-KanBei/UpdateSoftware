using System.Windows;
using System.Windows.Media;
using Wpf.Ui.Appearance;

namespace UpdateSoftware
{
    public static class ThemeManager
    {
        /// <summary>主题颜色数据</summary>
        public class ThemeInfo
        {
            public string Name { get; set; }
            public int Index { get; set; }

            // 背景
            public Color PrimaryBackground { get; set; }
            public Color SecundaryBackground { get; set; }
            public Color TertiaryBackground { get; set; }

            // 文字
            public Color PrimaryText { get; set; }
            public Color SecundaryText { get; set; }
            public Color TertiaryText { get; set; }

            // 功能色（双主题保持一致，但留接口以便未来自定义）
            public Color PrimaryBlue { get; set; }
            public Color PrimaryGreen { get; set; }
            public Color PrimaryRed { get; set; }
            public Color PrimaryOrange { get; set; }
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
                            PrimaryBackground = Color.FromRgb(0x12, 0x12, 0x12),
                            SecundaryBackground = Color.FromRgb(0x1E, 0x1E, 0x1E),
                            TertiaryBackground = Color.FromRgb(0x2C, 0x2C, 0x2C),
                            PrimaryText = Color.FromRgb(0xFF, 0xFF, 0xFF),
                            SecundaryText = Color.FromRgb(0xB0, 0xB0, 0xB0),
                            TertiaryText = Color.FromRgb(0x88, 0x88, 0x88),
                            PrimaryBlue = Color.FromRgb(0x40, 0x9E, 0xFF),
                            PrimaryGreen = Color.FromRgb(0x05, 0xA6, 0x60),
                            PrimaryRed = Color.FromRgb(0xE6, 0x35, 0x35),
                            PrimaryOrange = Color.FromRgb(0xE6, 0x7A, 0x00),
                        },
                        // ===== 日间模式（浅色）=====
                        new ThemeInfo
                        {
                            Name = "日间模式", Index = 1,
                            PrimaryBackground = Color.FromRgb(0xEA, 0xEA, 0xEF),
                            SecundaryBackground = Color.FromRgb(0xF2, 0xF4, 0xF8),
                            TertiaryBackground = Color.FromRgb(0xFF, 0xFF, 0xFF),
                            PrimaryText = Color.FromRgb(0x00, 0x00, 0x00),
                            SecundaryText = Color.FromRgb(0x33, 0x33, 0x33),
                            TertiaryText = Color.FromRgb(0x6A, 0x6A, 0x6A),
                            PrimaryBlue = Color.FromRgb(0x40, 0x9E, 0xFF),
                            PrimaryGreen = Color.FromRgb(0x05, 0xA6, 0x60),
                            PrimaryRed = Color.FromRgb(0xE6, 0x35, 0x35),
                            PrimaryOrange = Color.FromRgb(0xE6, 0x7A, 0x00),
                        },
                    };
                }
                return _themes;
            }
        }

        private static int _currentIndex = 0;
        public static int CurrentIndex => _currentIndex;

        /// <summary>应用指定索引的主题</summary>
        public static void ApplyTheme(int index)
        {
            if (index < 0 || index >= Themes.Length) return;
            _currentIndex = index;
            var t = Themes[index];
            var res = Application.Current.Resources;

            // 切换 WPF-UI 系统主题
            if (index == 0)
                ApplicationThemeManager.Apply(ApplicationTheme.Dark);
            else
                ApplicationThemeManager.Apply(ApplicationTheme.Light);

            // 刷新新颜色体系
            SetBrush(res, "PrimaryBackgroundColor", t.PrimaryBackground);
            SetBrush(res, "SecundaryBackgroundColor", t.SecundaryBackground);
            SetBrush(res, "TertiaryBackgroundColor", t.TertiaryBackground);
            SetBrush(res, "PrimaryTextColor", t.PrimaryText);
            SetBrush(res, "SecundaryTextColor", t.SecundaryText);
            SetBrush(res, "TertiaryTextColor", t.TertiaryText);
            SetBrush(res, "PrimaryBlueColor", t.PrimaryBlue);
            SetBrush(res, "PrimaryGreenColor", t.PrimaryGreen);
            SetBrush(res, "PrimaryRedColor", t.PrimaryRed);
            SetBrush(res, "PrimaryOrangeColor", t.PrimaryOrange);

            // 刷新旧版兼容键
            SetBrush(res, "Transparent", Colors.Transparent);
            SetBrush(res, "WindowBg", t.PrimaryBackground);
            SetBrush(res, "PageText", t.PrimaryText);
            SetBrush(res, "TextSecondary", t.SecundaryText);
            SetBrush(res, "TextMuted", t.TertiaryText);
            SetBrush(res, "ControlBg", t.TertiaryBackground);
            SetBrush(res, "SideBarBg", t.SecundaryBackground);
            SetBrush(res, "InputBg", t.TertiaryBackground);
            SetBrush(res, "BorderColor", t.TertiaryBackground);
            SetBrush(res, "AccentColor", t.PrimaryBlue);
            SetBrush(res, "SuccessColor", t.PrimaryGreen);
            SetBrush(res, "DangerColor", t.PrimaryRed);

            // 强制刷新所有窗口中的 ComboBox 控件颜色
            ForceRefreshAllComboBoxes();

            // 强制刷新主窗口标题栏颜色
            ForceRefreshTitleBar();
        }

        /// <summary>遍历所有窗口，强制刷新 ComboBox 为白底黑字（解决 WPF-UI 全局样式覆盖问题）</summary>
        public static void ForceRefreshAllComboBoxes()
        {
            foreach (Window window in Application.Current.Windows)
            {
                ForceRefreshComboBoxTree(window);
            }
        }

        private static void ForceRefreshComboBoxTree(System.Windows.DependencyObject parent)
        {
            if (parent == null) return;

            var comboBox = parent as System.Windows.Controls.ComboBox;
            if (comboBox != null)
            {
                // 跳过 DataGrid 内部的 ComboBox（它们使用 DataGrid 的局部样式）
                if (!IsInsideDataGrid(comboBox))
                {
                    comboBox.SetCurrentValue(System.Windows.Controls.ComboBox.ForegroundProperty,
                        System.Windows.Media.Brushes.Black);
                    comboBox.SetCurrentValue(System.Windows.Controls.ComboBox.BackgroundProperty,
                        System.Windows.Media.Brushes.White);

                    // 设置 ItemContainerStyle（选中项为橙色）
                    var itemStyle = new System.Windows.Style(typeof(System.Windows.Controls.ComboBoxItem));
                    itemStyle.Setters.Add(new System.Windows.Setter(System.Windows.Controls.ComboBoxItem.BackgroundProperty, System.Windows.Media.Brushes.White));
                    itemStyle.Setters.Add(new System.Windows.Setter(System.Windows.Controls.ComboBoxItem.ForegroundProperty, System.Windows.Media.Brushes.Black));
                    itemStyle.Setters.Add(new System.Windows.Setter(System.Windows.Controls.ComboBoxItem.PaddingProperty, new System.Windows.Thickness(6, 4, 6, 4)));

                    var mouseOverTrigger = new System.Windows.Trigger { Property = System.Windows.Controls.ComboBoxItem.IsMouseOverProperty, Value = true };
                    mouseOverTrigger.Setters.Add(new System.Windows.Setter(System.Windows.Controls.ComboBoxItem.BackgroundProperty, new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0xF0, 0xE0))));
                    mouseOverTrigger.Setters.Add(new System.Windows.Setter(System.Windows.Controls.ComboBoxItem.ForegroundProperty, System.Windows.Media.Brushes.Black));
                    itemStyle.Triggers.Add(mouseOverTrigger);

                    var selectedTrigger = new System.Windows.Trigger { Property = System.Windows.Controls.ComboBoxItem.IsSelectedProperty, Value = true };
                    selectedTrigger.Setters.Add(new System.Windows.Setter(System.Windows.Controls.ComboBoxItem.BackgroundProperty, new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xE6, 0x7A, 0x00))));
                    selectedTrigger.Setters.Add(new System.Windows.Setter(System.Windows.Controls.ComboBoxItem.ForegroundProperty, System.Windows.Media.Brushes.White));
                    itemStyle.Triggers.Add(selectedTrigger);

                    var highlightedTrigger = new System.Windows.Trigger { Property = System.Windows.Controls.ComboBoxItem.IsHighlightedProperty, Value = true };
                    highlightedTrigger.Setters.Add(new System.Windows.Setter(System.Windows.Controls.ComboBoxItem.BackgroundProperty, new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0xF0, 0xE0))));
                    highlightedTrigger.Setters.Add(new System.Windows.Setter(System.Windows.Controls.ComboBoxItem.ForegroundProperty, System.Windows.Media.Brushes.Black));
                    itemStyle.Triggers.Add(highlightedTrigger);

                    comboBox.SetCurrentValue(System.Windows.Controls.ComboBox.ItemContainerStyleProperty, itemStyle);
                }
            }

            int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                ForceRefreshComboBoxTree(System.Windows.Media.VisualTreeHelper.GetChild(parent, i));
            }
        }

        /// <summary>检查 ComboBox 是否在 DataGrid 内部</summary>
        private static bool IsInsideDataGrid(System.Windows.DependencyObject element)
        {
            var parent = System.Windows.Media.VisualTreeHelper.GetParent(element);
            while (parent != null)
            {
                if (parent is System.Windows.Controls.DataGrid)
                    return true;
                if (parent is System.Windows.Controls.DataGridCell)
                    return true;
                parent = System.Windows.Media.VisualTreeHelper.GetParent(parent);
            }
            return false;
        }

        /// <summary>强制刷新主窗口标题栏背景色</summary>
        private static void ForceRefreshTitleBar()
        {
            foreach (Window window in Application.Current.Windows)
            {
                if (window is MainWindow mainWin)
                {
                    // 通过资源刷新触发 DynamicResource 更新，标题栏 Border 自动跟随
                    var bg = (System.Windows.Media.Brush)Application.Current.Resources["WindowBg"];
                    mainWin.Background = bg;
                }
            }
        }

        private static void SetBrush(ResourceDictionary res, string key, Color color)
        {
            res[key] = new SolidColorBrush(color);
        }
    }
}
