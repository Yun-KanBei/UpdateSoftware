using System;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Forms;

namespace UpdateSoftware
{
    public partial class MainWindow : Window
    {
        private NotifyIcon _notifyIcon;
        private bool _forceClose;

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
            Closed += MainWindow_Closed;
            // 在窗口显示前先加载页面，避免白屏
            MainFrame.Navigate(new Pages.UpdateEXE());
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // 窗口显示后再创建托盘图标
            CreateNotifyIcon();
        }

        /// <summary>创建系统托盘图标</summary>
        private void CreateNotifyIcon()
        {
            try
            {
                _notifyIcon = new NotifyIcon
                {
                    Text = "程序更新工具",
                    Visible = false
                };

                // 从可执行文件中提取关联图标
                _notifyIcon.Icon = System.Drawing.Icon.ExtractAssociatedIcon(
                    System.Reflection.Assembly.GetExecutingAssembly().Location);

                // 双击托盘图标恢复窗口
                _notifyIcon.DoubleClick += (s, args) => RestoreFromTray();

                // 右键菜单
                var contextMenu = new ContextMenuStrip();
                contextMenu.Items.Add("显示主窗口", null, (s, args) => RestoreFromTray());
                contextMenu.Items.Add(new ToolStripSeparator());
                contextMenu.Items.Add("退出程序", null, (s, args) =>
                {
                    _forceClose = true;
                    Close();
                });
                _notifyIcon.ContextMenuStrip = contextMenu;
            }
            catch { }
        }

        /// <summary>从托盘恢复到窗口</summary>
        private void RestoreFromTray()
        {
            if (_notifyIcon != null)
                _notifyIcon.Visible = false;

            Show();
            WindowState = WindowState.Normal;
            Activate();
            Topmost = true;
            Topmost = false;
        }

        /// <summary>隐藏到系统托盘（仅在关闭时触发）</summary>
        private void HideToTray()
        {
            if (_notifyIcon == null) return;

            Hide();
            _notifyIcon.Visible = true;
            _notifyIcon.ShowBalloonTip(1000, "程序更新工具", "程序已最小化到系统托盘", ToolTipIcon.Info);
        }

        /// <summary>窗口关闭时，根据设置决定是退出还是最小化到托盘</summary>
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (_forceClose)
            {
                // 从右键菜单"退出程序"触发
                CleanupNotifyIcon();
                base.OnClosing(e);
                return;
            }

            // 未登录时始终直接退出，不弹窗也不去托盘
            if (!App.IsAuthenticated)
            {
                CleanupNotifyIcon();
                base.OnClosing(e);
                return;
            }

            var settingsPath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "UpdateSoftware_Config", "AppSettings.json");

            // 已登录 + 无设置文件：首次关闭，弹窗让用户选择
            if (!File.Exists(settingsPath))
            {
                e.Cancel = true;
                var dialog = new FirstRunDialog { Owner = this };
                dialog.ShowDialog();

                if (dialog.DialogResult == true)
                {
                    var settings = new AppSettings { CloseToTray = dialog.CloseToTray };
                    settings.Save();

                    if (dialog.CloseToTray)
                    {
                        HideToTray();
                    }
                    else
                    {
                        e.Cancel = false;
                        CleanupNotifyIcon();
                        base.OnClosing(e);
                    }
                }
                return;
            }

            // 已登录 + 已有设置，按配置执行
            var settings2 = AppSettings.Load();
            if (settings2.CloseToTray)
            {
                e.Cancel = true;
                HideToTray();
            }
            else
            {
                CleanupNotifyIcon();
                base.OnClosing(e);
            }
        }

        private void MainWindow_Closed(object sender, EventArgs e)
        {
            CleanupNotifyIcon();
        }

        private void CleanupNotifyIcon()
        {
            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
                _notifyIcon = null;
            }
        }
    }
}
