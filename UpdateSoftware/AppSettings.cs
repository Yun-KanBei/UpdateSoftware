using System;
using System.IO;
using Newtonsoft.Json;

namespace UpdateSoftware
{
    /// <summary>
    /// 应用程序设置（关闭行为等）
    /// </summary>
    public class AppSettings
    {
        /// <summary>关闭窗口时是否最小化到系统托盘（true=到托盘，false=退出程序）</summary>
        public bool CloseToTray { get; set; } = false;

        private static readonly string SettingsFileName = "AppSettings.json";

        private static string SettingsFilePath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "UpdateSoftware_Config", SettingsFileName);

        /// <summary>从文件加载设置</summary>
        public static AppSettings Load()
        {
            try
            {
                var path = SettingsFilePath;
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                if (File.Exists(path))
                {
                    var json = File.ReadAllText(path);
                    return JsonConvert.DeserializeObject<AppSettings>(json) ?? new AppSettings();
                }
            }
            catch { }
            return new AppSettings();
        }

        /// <summary>保存设置到文件</summary>
        public void Save()
        {
            try
            {
                var path = SettingsFilePath;
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var json = JsonConvert.SerializeObject(this, Formatting.Indented);
                File.WriteAllText(path, json);
            }
            catch { }
        }
    }
}
