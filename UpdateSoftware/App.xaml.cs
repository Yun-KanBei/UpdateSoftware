using System;
using System.Threading;
using System.Windows;

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
                // 已有实例在运行，提示并退出
                MessageBox.Show("程序已在运行中！", "提示",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                Environment.Exit(0);
                return;
            }

            base.OnStartup(e);

            // 手动创建并显示主窗口（由于移除了 StartupUri）
            var mainWindow = new MainWindow();
            mainWindow.Show();
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
