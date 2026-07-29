using System;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using Wpf.Ui.Appearance;

namespace UpdateSoftware
{
    public partial class App : Application
    {
        private static Mutex _mutex;

        /// <summary>当前用户是否已登录验证（共享状态，供 MainWindow 关闭时判断用）</summary>
        public static bool IsAuthenticated { get; set; }

        protected override void OnStartup(StartupEventArgs e)
        {
            // 单实例：使用全局 Mutex 检查是否已有实例运行
            const string mutexName = "Global\\UpdateSoftware_SingleInstance";
            bool createdNew;
            _mutex = new Mutex(true, mutexName, out createdNew);

            if (!createdNew)
            {
                System.Windows.MessageBox.Show("程序已在运行中！", "提示",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                Environment.Exit(0);
                return;
            }

            base.OnStartup(e);

            // 显示启动画面
            var splash = CreateSplashWindow();
            splash.Show();
            splash.UpdateLayout();

            // 加载 WPF-UI 主题（此时用户已看到启动画面）
            ApplicationThemeManager.Apply(ApplicationTheme.Dark);

            // 创建主窗口
            var mainWindow = new MainWindow();

            mainWindow.ContentRendered += (s, args) =>
            {
                splash.Close();
                mainWindow.Activate();
                ThemeManager.ForceRefreshAllComboBoxes();
            };

            mainWindow.Show();
        }

        private static Window CreateSplashWindow()
        {
            var splash = new Window
            {
                Width = 420,
                Height = 260,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = System.Windows.Media.Brushes.Transparent,
                ShowInTaskbar = false,
                Topmost = true,
                ResizeMode = ResizeMode.NoResize
            };

            var splashBorder = new System.Windows.Controls.Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E)),
                CornerRadius = new System.Windows.CornerRadius(12),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x40, 0x9E, 0xFF)),
                BorderThickness = new System.Windows.Thickness(1)
            };

            var stackPanel = new System.Windows.Controls.StackPanel
            {
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center
            };

            try
            {
                var iconBitmap = new System.Windows.Media.Imaging.BitmapImage(
                    new Uri("pack://application:,,,/COLIBRI.ico"));
                stackPanel.Children.Add(new System.Windows.Controls.Image
                {
                    Source = iconBitmap,
                    Width = 48,
                    Height = 48,
                    Margin = new System.Windows.Thickness(0, 0, 0, 16)
                });
            }
            catch { }

            stackPanel.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = "EXE版本工具",
                FontSize = 20,
                FontWeight = System.Windows.FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x40, 0x9E, 0xFF)),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                Margin = new System.Windows.Thickness(0, 0, 0, 8)
            });

            stackPanel.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = "V10.0",
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                Margin = new System.Windows.Thickness(0, 0, 0, 20)
            });

            stackPanel.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = "正在加载...",
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(0xB0, 0xB0, 0xB0)),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center
            });

            splashBorder.Child = stackPanel;
            splash.Content = splashBorder;
            return splash;
        }

        protected override void OnExit(ExitEventArgs e)
        {
            if (_mutex != null)
            {
                try
                {
                    _mutex.ReleaseMutex();
                }
                catch { }
                _mutex.Dispose();
            }
            base.OnExit(e);
        }
    }
}
