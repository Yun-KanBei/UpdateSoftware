using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Net.NetworkInformation;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using UpdateSoftware;

namespace UpdateSoftware.Pages
{
    public partial class UpdateEXE : Page
    {
        /// <summary>当前是否为非本机模式</summary>
        private bool _isRemoteMode;

        /// <summary>标记控件是否已完全初始化，防止 InitializeComponent 过程中事件触发导致空引用</summary>
        private bool _isInitialized;

        /// <summary>参数设定表格数据</summary>
        private ObservableCollection<ParamConfigItem> _paramConfigItems;

        /// <summary>是否已验证身份</summary>
        private bool _isAuthenticated;
        /// <summary>当前登录用户姓名</summary>
        private string _authUserName;

        /// <summary>日志文件目录（默认 %APPDATA%\UpdateSoftware\UpdateEXE）</summary>
        private string _logDirectoryPath;
        /// <summary>配置文件完整路径（默认 {BaseDir}\KB_ToolsBox_Config\UpdateEXE\Prpgram.json）</summary>
        private string _configFilePath;

        /// <summary>默认日志目录（%APPDATA%\UpdateSoftware）</summary>
        private static string DefaultLogDir => System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "UpdateSoftware");
        /// <summary>默认配置文件路径（{程序目录}\UpdateSoftware_Config\Param.json）</summary>
        private static string DefaultConfigPath => System.IO.Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "UpdateSoftware_Config", "Param.json");

        public UpdateEXE()
        {
            InitializeComponent();
            _isInitialized = true;

            _paramConfigItems = new ObservableCollection<ParamConfigItem>();
            DataGridParamConfig.ItemsSource = _paramConfigItems;
            InitDefaultParamRows();

            // 默认加载本机D盘CCD程序目录
            var defaultPath = @"D:\CCD程序";
            if (TxtBasePath != null)
            {
                TxtBasePath.Text = defaultPath;
            }
            LoadDirectoryTree(defaultPath);

            // 自动识别本机桌面路径作为快捷方式创建地址
            AutoDetectDesktopPath();

            // 初始化新设备配置
            InitDeviceConfig();

            // 初始化日志路径与配置文件路径（默认值）
            _logDirectoryPath = DefaultLogDir;
            _configFilePath = DefaultConfigPath;
            TxtLogPath.Text = _logDirectoryPath;
            TxtConfigPath.Text = _configFilePath;

            // 加载关闭行为设置
            LoadCloseBehaviorSetting();
        }

        /// <summary>换肤按钮 → 切换夜间/日间模式</summary>
        private void BtnThemeCycle_Click(object sender, RoutedEventArgs e)
        {
            int next = (ThemeManager.CurrentIndex + 1) % ThemeManager.Themes.Length;
            ThemeManager.ApplyTheme(next);
        }

        /// <summary>浏览日志目录</summary>
        private void BtnBrowseLogPath_Click(object sender, RoutedEventArgs e)
        {
            using (var dlg = new System.Windows.Forms.FolderBrowserDialog())
            {
                dlg.Description = "选择日志文件存放目录";
                dlg.SelectedPath = _logDirectoryPath;
                if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    _logDirectoryPath = dlg.SelectedPath;
                    TxtLogPath.Text = _logDirectoryPath;
                }
            }
        }
        /// <summary>重置日志目录为默认值</summary>
        private void BtnResetLogPath_Click(object sender, RoutedEventArgs e)
        {
            _logDirectoryPath = DefaultLogDir;
            TxtLogPath.Text = _logDirectoryPath;
        }
        /// <summary>浏览配置文件路径</summary>
        private void BtnBrowseConfigPath_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = "选择配置文件保存位置",
                FileName = "Prpgram.json",
                Filter = "JSON文件|*.json|所有文件|*.*",
                InitialDirectory = System.IO.Path.GetDirectoryName(_configFilePath)
            };
            if (dlg.ShowDialog() == true)
            {
                _configFilePath = dlg.FileName;
                TxtConfigPath.Text = _configFilePath;
            }
        }
        /// <summary>重置配置文件路径为默认值</summary>
        private void BtnResetConfigPath_Click(object sender, RoutedEventArgs e)
        {
            _configFilePath = DefaultConfigPath;
            TxtConfigPath.Text = _configFilePath;
        }
        /// <summary>打开日志目录</summary>
        private void BtnOpenLogDir_Click(object sender, RoutedEventArgs e)
        {
            string dir = TxtLogPath.Text.Trim();
            if (!string.IsNullOrEmpty(dir))
            {
                System.IO.Directory.CreateDirectory(dir);
                Process.Start("explorer.exe", dir);
            }
        }
        /// <summary>打开配置文件所在目录</summary>
        private void BtnOpenConfigDir_Click(object sender, RoutedEventArgs e)
        {
            string path = TxtConfigPath.Text.Trim();
            if (!string.IsNullOrEmpty(path))
            {
                string dir = System.IO.Path.GetDirectoryName(path);
                System.IO.Directory.CreateDirectory(dir);
                Process.Start("explorer.exe", dir);
            }
        }

        /// <summary>初始化新设备配置</summary>
        private void InitDeviceConfig()
        {
            _deviceConfigRows = new ObservableCollection<DeviceConfigRow>();
            DataGridDeviceConfig.ItemsSource = _deviceConfigRows;

            // 优先从合并的 Prpgram.json 加载
            try
            {
                string configDir = System.IO.Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory, "KB_ToolsBox_Config", "UpdateEXE");
                string mainFilePath = System.IO.Path.Combine(configDir, "Prpgram.json");
                if (System.IO.File.Exists(mainFilePath))
                {
                    string json = System.IO.File.ReadAllText(mainFilePath);
                    var combined = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
                    if (combined != null && combined.TryGetValue("DeviceConfigRows", out object devObj) && devObj != null)
                    {
                        string devJson = devObj.ToString();
                        var loaded = Newtonsoft.Json.JsonConvert.DeserializeObject<List<DeviceConfigRow>>(devJson);
                        if (loaded != null && loaded.Count > 0)
                        {
                            foreach (var item in loaded)
                                _deviceConfigRows.Add(item);
                            return;
                        }
                    }
                }
            }
            catch { }

            // 兼容旧格式：从单独的 DeviceConfig.json 加载
            try
            {
                string configDir = System.IO.Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory, "KB_ToolsBox_Config", "UpdateEXE");
                string oldFilePath = System.IO.Path.Combine(configDir, DEVICE_CONFIG_FILE_NAME);
                if (System.IO.File.Exists(oldFilePath))
                {
                    string json = System.IO.File.ReadAllText(oldFilePath);
                    var loaded = Newtonsoft.Json.JsonConvert.DeserializeObject<List<DeviceConfigRow>>(json);
                    if (loaded != null && loaded.Count > 0)
                    {
                        foreach (var item in loaded)
                            _deviceConfigRows.Add(item);
                        return;
                    }
                }
            }
            catch { }

            // 没有已保存的配置，添加默认行
            AddDefaultDeviceConfigRows();
        }

        /// <summary>自动识别本机桌面路径</summary>
        private void AutoDetectDesktopPath()
        {
            var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            if (TxtDesktopPath != null)
            {
                TxtDesktopPath.Text = desktopPath;
            }
        }

        // ==================== 模式切换 ====================

        /// <summary>
        /// 本机/非本机模式切换
        /// 本机模式：TextBox + 文件夹浏览
        /// 非本机模式：ComboBox（从参数设定表格的远程访问地址列获取下拉列表）+ 手动输入
        /// </summary>
        private void RbtnAccessMode_Checked(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;

            _isRemoteMode = RbtnRemoteMode.IsChecked == true;

            if (TxtBasePath == null || CbRemotePath == null) return;

            if (_isRemoteMode)
            {
                // 切换到非本机模式
                TxtBasePath.Visibility = Visibility.Collapsed;
                CbRemotePath.Visibility = Visibility.Visible;
                if (BtnAccessPath != null) BtnAccessPath.Content = "访问";

                // 从参数设定表格获取远程访问地址列表
                CbRemotePath.Items.Clear();
                if (_paramConfigItems != null)
                {
                    var paths = _paramConfigItems
                        .Select(p => p.RemotePath)
                        .Where(p => !string.IsNullOrWhiteSpace(p))
                        .Distinct()
                        .ToList();
                    foreach (var path in paths)
                    {
                        CbRemotePath.Items.Add(path);
                    }
                }

                if (string.IsNullOrWhiteSpace(CbRemotePath.Text) && CbRemotePath.Items.Count > 0)
                {
                    CbRemotePath.Text = CbRemotePath.Items[0].ToString();
                }
            }
            else
            {
                // 切换到本机模式
                CbRemotePath.Visibility = Visibility.Collapsed;
                TxtBasePath.Visibility = Visibility.Visible;
                if (BtnAccessPath != null) BtnAccessPath.Content = "浏览";

                if (string.IsNullOrWhiteSpace(TxtBasePath.Text))
                {
                    TxtBasePath.Text = @"D:\CCD程序";
                }
            }
        }
        /// <summary>
        /// "访问"按钮 - 根据当前模式执行不同操作
        /// 本机模式：弹出文件夹选择对话框
        /// 非本机模式：自动匹配参数设定表格中的账户密码进行身份验证，成功后加载目录树
        /// </summary>
        private async void BtnAccessPath_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;

            if (_isRemoteMode)
            {
                var path = CbRemotePath?.Text;
                if (string.IsNullOrWhiteSpace(path))
                {
                    return;
                }

                // 非本机模式：异步连接远程共享，不卡界面
                await RemoteAuthenticateAndLoadAsync(path);
            }
            else
            {
                var dialog = new System.Windows.Forms.FolderBrowserDialog();
                if (TxtBasePath != null && !string.IsNullOrWhiteSpace(TxtBasePath.Text) && Directory.Exists(TxtBasePath.Text))
                {
                    dialog.SelectedPath = TxtBasePath.Text;
                }
                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    if (TxtBasePath != null) TxtBasePath.Text = dialog.SelectedPath;
                    LoadDirectoryTree(dialog.SelectedPath);
                }
            }
        }

        // ==================== 左侧目录树相关 ====================

        private async void BtnRefreshTree_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;

            string path;
            if (_isRemoteMode)
            {
                path = CbRemotePath?.Text;
            }
            else
            {
                path = TxtBasePath?.Text;
            }

            if (!string.IsNullOrWhiteSpace(path))
            {
                if (_isRemoteMode)
                {
                    await RemoteAuthenticateAndLoadAsync(path);
                }
                else
                {
                    // 刷新时保留展开状态
                    RefreshDirectoryTree(path);
                }
            }
            else
            {
            }
        }

        /// <summary>刷新目录树，保持当前展开和选中状态不变</summary>
        private void RefreshDirectoryTree(string rootPath)
        {
            if (TreeViewDirectory == null || !Directory.Exists(rootPath)) return;

            try
            {
                // 保存当前展开状态
                var expandedPaths = new HashSet<string>();
                SaveExpandedState(TreeViewDirectory, expandedPaths);
                string selectedPath = null;
                if (TreeViewDirectory.SelectedItem is TreeViewItem selItem && selItem.Tag is string selPath)
                {
                    selectedPath = selPath;
                }

                // 重建树
                TreeViewDirectory.Items.Clear();
                var rootItem = new TreeViewItem
                {
                    Header = new DirectoryInfo(rootPath).Name,
                    Tag = rootPath,
                    IsExpanded = true
                };
                PopulateTreeView(rootItem, rootPath);
                TreeViewDirectory.Items.Add(rootItem);

                // 恢复展开状态
                RestoreExpandedState(TreeViewDirectory, expandedPaths);

                // 恢复选中状态
                if (selectedPath != null)
                {
                    RestoreSelectedState(TreeViewDirectory, selectedPath);
                }

            }
            catch (Exception ex)
            {
            }
        }

        /// <summary>递归保存所有展开节点的路径</summary>
        private void SaveExpandedState(ItemsControl itemsControl, HashSet<string> expandedPaths)
        {
            foreach (var item in itemsControl.Items)
            {
                if (item is TreeViewItem treeItem)
                {
                    if (treeItem.IsExpanded && treeItem.Tag is string path)
                    {
                        expandedPaths.Add(path);
                    }
                    SaveExpandedState(treeItem, expandedPaths);
                }
            }
        }

        /// <summary>递归恢复展开状态</summary>
        private void RestoreExpandedState(ItemsControl itemsControl, HashSet<string> expandedPaths)
        {
            foreach (var item in itemsControl.Items)
            {
                if (item is TreeViewItem treeItem)
                {
                    if (treeItem.Tag is string path && expandedPaths.Contains(path))
                    {
                        // 触发懒加载并展开
                        if (treeItem.Items.Count == 1 && treeItem.Items[0] == null)
                        {
                            treeItem.Items.Clear();
                            PopulateTreeView(treeItem, path);
                        }
                        treeItem.IsExpanded = true;
                    }
                    RestoreExpandedState(treeItem, expandedPaths);
                }
            }
        }

        /// <summary>递归恢复选中状态</summary>
        private void RestoreSelectedState(ItemsControl itemsControl, string targetPath)
        {
            foreach (var item in itemsControl.Items)
            {
                if (item is TreeViewItem treeItem)
                {
                    if (treeItem.Tag is string path && string.Equals(path, targetPath, StringComparison.OrdinalIgnoreCase))
                    {
                        treeItem.IsSelected = true;
                        treeItem.BringIntoView();
                        return;
                    }
                    RestoreSelectedState(treeItem, targetPath);
                }
            }
        }

        private void BtnExpandAllTree_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            ExpandAllTreeViewItems(TreeViewDirectory);
        }

        private void BtnCollapseAllTree_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            CollapseAllTreeViewItems(TreeViewDirectory);
        }

        private void BtnExpandOneLevel_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;

            // 对当前 TreeView 中所有已展开的节点，将其子节点再展开一级
            ExpandOneLevelRecursive(TreeViewDirectory);
        }

        /// <summary>递归遍历，对每个已展开的节点展开其子节点（仅一级）</summary>
        private void ExpandOneLevelRecursive(ItemsControl itemsControl)
        {
            foreach (var item in itemsControl.Items)
            {
                if (item is TreeViewItem treeItem && treeItem.IsExpanded)
                {
                    // 如果节点有懒加载占位，先加载子目录
                    if (treeItem.Items.Count == 1 && treeItem.Items[0] == null && treeItem.Tag is string path)
                    {
                        treeItem.Items.Clear();
                        PopulateTreeView(treeItem, path);
                    }

                    // 展开所有直接子节点（仅一级）
                    foreach (var child in treeItem.Items)
                    {
                        if (child is TreeViewItem childItem)
                        {
                            childItem.IsExpanded = true;
                        }
                    }
                }
            }
        }

        /// <summary>当前选中文件夹的匹配结果缓存（用于修正命名判断）</summary>
        private List<MatchedInfoItem> _currentMatchedInfos = new List<MatchedInfoItem>();

        /// <summary>当前选中文件夹中所有EXE对应的候选配置组（每组阴/阳等），用于改名弹窗</summary>
        private List<List<ParamConfigItem>> _currentRenameCandidateGroups = new List<List<ParamConfigItem>>();

        /// <summary>当前选中文件夹路径</summary>
        private string _currentSelectedFolderPath;

        // ==================== 新设备配置相关字段 ====================
        /// <summary>新设备配置表格数据</summary>
        private ObservableCollection<DeviceConfigRow> _deviceConfigRows;
        private const string DEVICE_CONFIG_FILE_NAME = "DeviceConfig.json";
        private static readonly string DeviceConfigDir = System.IO.Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "KB_ToolsBox_Config", "UpdateEXE");
        private string DeviceConfigFilePath => System.IO.Path.Combine(DeviceConfigDir, DEVICE_CONFIG_FILE_NAME);

        // 可用磁盘列表（供DataGrid绑定）
        public static List<string> DriveList { get; } = GetDriveList();
        // 默认跨工控机IP列表
        public static List<string> DefaultIPList { get; } = new List<string>
        {
            "192.168.250.31", "192.168.250.32", "192.168.250.35",
            "192.168.250.36", "192.168.250.37", "192.168.250.38"
        };
        private static List<string> GetDriveList()
        {
            var drives = new List<string>();
            try
            {
                foreach (var drive in DriveInfo.GetDrives())
                {
                    if (drive.DriveType == DriveType.Fixed || drive.DriveType == DriveType.Removable)
                        drives.Add(drive.Name.TrimEnd('\\'));
                }
            }
            catch { }
            if (drives.Count == 0) drives.Add("D:");
            return drives;
        }

        private void TreeViewDirectory_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (!_isInitialized) return;

            if (e.NewValue is TreeViewItem selectedItem && selectedItem.Tag is string fullPath)
            {
                if (TxtSelectedPath != null) TxtSelectedPath.Text = fullPath;
                _currentSelectedFolderPath = fullPath;
                UpdateMatchedProgramInfo(fullPath);
            }
        }

        /// <summary>根据选中的文件夹路径，在参数设定表格中匹配所有程序信息并更新显示</summary>
        private void UpdateMatchedProgramInfo(string folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath) || _paramConfigItems == null)
            {
                ClearMatchedInfo();
                return;
            }

            string folderName = new DirectoryInfo(folderPath).Name;
            string parentFolderName = GetParentFolderName(folderPath);
            _currentMatchedInfos = new List<MatchedInfoItem>();
            _currentRenameCandidateGroups = new List<List<ParamConfigItem>>();

            try
            {
                var exeFiles = Directory.GetFiles(folderPath, "*.exe", SearchOption.TopDirectoryOnly);
                foreach (var exeFile in exeFiles)
                {
                    string fileName = System.IO.Path.GetFileName(exeFile);

                    // 先按EXE名称筛选出所有匹配的行
                    var candidates = _paramConfigItems
                        .Where(p => string.Equals(p.ExeNamePattern, fileName, StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    if (candidates.Count == 0) continue;

                    // 保存候选配置组（用于改名弹窗，每个EXE一组）
                    _currentRenameCandidateGroups.Add(candidates);

                    // 根据上级文件夹名称匹配工位（用 StationFolderPattern 匹配父级文件夹名）
                    ParamConfigItem matched = null;
                    bool canDetermineStation = true;
                    if (candidates.Count > 1)
                    {
                        // 有阴阳之分，通过上级文件夹名匹配工位名称要求
                        if (!string.IsNullOrWhiteSpace(parentFolderName))
                        {
                            matched = candidates.FirstOrDefault(p =>
                                string.Equals(parentFolderName, p.StationFolderPattern, StringComparison.OrdinalIgnoreCase) ||
                                parentFolderName.Contains(p.StationFolderPattern) ||
                                p.StationFolderPattern.Contains(parentFolderName));
                        }
                        // 仍无法确定
                        if (matched == null)
                        {
                            canDetermineStation = false;
                        }
                    }
                    else
                    {
                        matched = candidates[0];
                    }

                    string version = "未知";
                    try
                    {
                        var vi = FileVersionInfo.GetVersionInfo(exeFile);
                        version = vi.FileVersion ?? "未知";
                    }
                    catch { }

                    if (matched != null)
                    {
                        // 检查文件夹命名是否与表中规则全字匹配
                        string expectedFolderName = matched.FolderNamingRule.Replace("{版本号}", version);
                        bool nameMatch = string.Equals(folderName, expectedFolderName, StringComparison.OrdinalIgnoreCase);

                        _currentMatchedInfos.Add(new MatchedInfoItem
                        {
                            StationPattern = matched.StationFolderPattern,
                            ExeName = fileName,
                            FileVersion = version,
                            FolderName = folderName,
                            ExpectedFolderName = expectedFolderName,
                            IsNameMatch = nameMatch,
                            MatchedConfig = matched
                        });

                    }
                    else
                    {
                        // 无法判断工位，添加一条提示
                        _currentMatchedInfos.Add(new MatchedInfoItem
                        {
                            StationPattern = "无法判断工位",
                            ExeName = fileName,
                            FileVersion = version,
                            FolderName = folderName,
                            IsNameMatch = false,
                            DisplayText = $"工位: 无法判断工位  |  文件夹: {folderName}  |  程序: {fileName}  V{version}  ← 上级文件夹名不匹配工位要求",
                            TextColor = Brushes.Red,
                            MatchedConfig = null
                        });

                    }
                }
            }
            catch
            {
                // 无权限等忽略
            }

            if (_currentMatchedInfos.Count > 0)
            {
                var displayItems = new List<MatchedInfoItem>();

                foreach (var item in _currentMatchedInfos)
                {
                    if (item.DisplayText == null)
                    {
                        if (!item.IsNameMatch)
                        {
                            item.DisplayText = $"工位: {item.StationPattern}  |  文件夹: {item.FolderName}  |  程序: {item.ExeName}  V{item.FileVersion}  ← 命名不匹配";
                            item.TextColor = Brushes.Red;
                        }
                        else
                        {
                            item.DisplayText = $"工位: {item.StationPattern}  |  文件夹: {item.FolderName}  |  程序: {item.ExeName}  V{item.FileVersion}";
                            item.TextColor = Brushes.LimeGreen;
                        }
                    }
                    displayItems.Add(item);
                }

                ItemsMatchedInfo.ItemsSource = displayItems;

                // 判断是否显示"修正文件夹命名"按钮
                bool needRename = _currentMatchedInfos.Any(i => !i.IsNameMatch);
                if (BtnRenameFolder != null)
                    BtnRenameFolder.Visibility = needRename ? Visibility.Visible : Visibility.Collapsed;
            }
            else
            {
                ClearMatchedInfo();
            }
        }

        /// <summary>获取上级文件夹名称</summary>
        private string GetParentFolderName(string path)
        {
            try
            {
                var parent = Directory.GetParent(path);
                return parent?.Name;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>解析EXE文件对应的工位名称（与 UpdateMatchedProgramInfo 逻辑一致）</summary>
        private string ResolveStationPattern(string exeFilePath, string exeFileName)
        {
            // exeFilePath 是EXE文件路径，父目录是程序文件夹，再上一级是工位文件夹
            string programFolderPath = System.IO.Path.GetDirectoryName(exeFilePath);
            string parentFolderName = GetParentFolderName(programFolderPath);

            var candidates = _paramConfigItems
                .Where(p => string.Equals(p.ExeNamePattern, exeFileName, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (candidates.Count == 0) return "";
            if (candidates.Count == 1) return candidates[0].StationFolderPattern;

            // 多个候选（阴/阳），通过上级文件夹名匹配工位名称要求
            if (!string.IsNullOrWhiteSpace(parentFolderName))
            {
                // 精确匹配：父文件夹名包含工位名，或工位名包含父文件夹名
                var matched = candidates.FirstOrDefault(p =>
                    string.Equals(parentFolderName, p.StationFolderPattern, StringComparison.OrdinalIgnoreCase) ||
                    parentFolderName.Contains(p.StationFolderPattern) ||
                    p.StationFolderPattern.Contains(parentFolderName));
                if (matched != null)
                    return matched.StationFolderPattern;
            }

            return "无法判断工位";
        }

        /// <summary>清空匹配信息显示</summary>
        private void ClearMatchedInfo()
        {
            _currentMatchedInfos = new List<MatchedInfoItem>();
            if (ItemsMatchedInfo != null)
            {
                ItemsMatchedInfo.ItemsSource = new List<MatchedInfoItem>
                {
                    new MatchedInfoItem { DisplayText = "(未匹配到程序)", TextColor = Brushes.Gray }
                };
            }
            if (BtnRenameFolder != null)
                BtnRenameFolder.Visibility = Visibility.Collapsed;
        }

        /// <summary>"修正文件夹命名"按钮点击</summary>
        private void BtnRenameFolder_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_currentSelectedFolderPath) || _currentMatchedInfos.Count == 0)
                return;

            // 获取所有候选组中每组超过1个的（即有阴/阳之分的组）
            var allGroupsWithMultiple = _currentRenameCandidateGroups
                .Where(g => g.Count > 1)
                .ToList();

            if (allGroupsWithMultiple.Count > 0)
            {
                // 弹窗让用户选择命名规则
                string version = _currentMatchedInfos.FirstOrDefault()?.FileVersion ?? "未知";

                var dialog = new Window
                {
                    Title = "选择命名",
                    Width = 500,
                    Height = 400,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Owner = Window.GetWindow(this),
                    Background = new SolidColorBrush(Color.FromRgb(45, 45, 48)),
                    Foreground = Brushes.White
                };

                var stack = new StackPanel { Margin = new Thickness(10) };

                // 构建所有选项，按组显示分组标题
                var allOptions = new List<dynamic>();
                foreach (var group in allGroupsWithMultiple)
                {
                    var first = group.First();
                    string exeName = first.ExeNamePattern;
                    // 添加分组标题
                    allOptions.Add(new { Display = $"── {exeName} ──", IsHeader = true, Config = (ParamConfigItem)null });
                    foreach (var c in group)
                    {
                        string expectedName = c.FolderNamingRule.Replace("{版本号}", version);
                        allOptions.Add(new { Display = $"  工位: {c.StationFolderPattern}  →  命名: {expectedName}", IsHeader = false, Config = c });
                    }
                }

                var listBox = new ListBox
                {
                    Height = 250,
                    Background = new SolidColorBrush(Color.FromRgb(60, 60, 64)),
                    Foreground = Brushes.White,
                    BorderThickness = new Thickness(0)
                };
                // 用 SelectionChanged 禁用选择分组标题行
                listBox.ItemsSource = allOptions.Select(o => o.Display).ToList();
                listBox.SelectedIndex = 0;
                listBox.SelectionChanged += (s, args) =>
                {
                    if (listBox.SelectedIndex >= 0 && listBox.SelectedIndex < allOptions.Count && allOptions[listBox.SelectedIndex].IsHeader)
                    {
                        // 选中标题行时自动跳到下一个可选行
                        listBox.SelectedIndex = listBox.SelectedIndex + 1;
                    }
                };
                stack.Children.Add(new TextBlock
                {
                    Text = "请选择要使用的命名规则：",
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 10)
                });
                stack.Children.Add(listBox);

                var btnPanel = new WrapPanel { Margin = new Thickness(0, 10, 0, 0), HorizontalAlignment = HorizontalAlignment.Right };
                var btnOk = new Button { Content = "确定", Width = 80, Height = 30, Margin = new Thickness(0, 0, 8, 0) };
                var btnCancel = new Button { Content = "取消", Width = 80, Height = 30 };

                btnOk.Click += (s, args) =>
                {
                    if (listBox.SelectedIndex >= 0)
                    {
                        var selected = allOptions[listBox.SelectedIndex];
                        if (selected.Config != null)
                        {
                            var tempItem = new MatchedInfoItem
                            {
                                MatchedConfig = selected.Config,
                                FileVersion = version,
                                FolderName = new DirectoryInfo(_currentSelectedFolderPath).Name
                            };
                            RenameFolderToMatch(tempItem);
                        }
                        dialog.Close();
                    }
                };
                btnCancel.Click += (s, args) => dialog.Close();

                btnPanel.Children.Add(btnOk);
                btnPanel.Children.Add(btnCancel);
                stack.Children.Add(btnPanel);
                dialog.Content = stack;
                dialog.ShowDialog();
            }
            else
            {
                // 单个程序，直接修正
                RenameFolderToMatch(_currentMatchedInfos[0]);
            }
        }

        // ==================== 身份验证 ====================

        /// <summary>检查是否已验证身份，未验证则提示并返回 false</summary>
        private bool RequireAuth()
        {
            if (!_isInitialized) return false;
            if (_isAuthenticated) return true;

            MessageBox.Show("请先点击顶部「身份验证」按钮进行登录后再使用此功能。",
                "需要身份验证", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        /// <summary>记录身份验证日志到文件</summary>
        private void LogAuth(string name, bool success, string customStatus = null)
        {
            string logDir = _logDirectoryPath ?? DefaultLogDir;
            try
            {
                Directory.CreateDirectory(logDir);
                string logFile = System.IO.Path.Combine(logDir, "Auth.log");
                string status = customStatus ?? (success ? "成功" : "失败");
                string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 身份验证{status} - 姓名: {name}";
                System.IO.File.AppendAllText(logFile, line + Environment.NewLine);
            }
            catch { }
        }

        /// <summary>身份验证按钮（已登录时变为「退出登录」）</summary>
        private void BtnAuth_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;

            // 已登录 → 退出
            if (_isAuthenticated)
            {
                string userName = _authUserName;
                _isAuthenticated = false;
                App.IsAuthenticated = false;
                _authUserName = null;
                TxtAuthUser.Text = "";
                BorderAuthOverlay.Visibility = Visibility.Visible;
                BtnAuth.Content = "身份验证";
                BtnAuth.Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xA5, 0x00)); // 橙色
                TxtStatusBar.Text = "已退出登录";
                LogAuth(userName, true, "退出登录");
                AppendSmartLog($"用户 {userName} 退出登录");
                return;
            }

            // 未登录 → 打开验证
            var dialog = new LoginDialog { Owner = Window.GetWindow(this) };
            if (dialog.ShowDialog() == true && dialog.IsAuthenticated)
            {
                _isAuthenticated = true;
                App.IsAuthenticated = true;
                _authUserName = dialog.UserName;
                TxtAuthUser.Text = $"当前用户: {_authUserName}";
                BorderAuthOverlay.Visibility = Visibility.Collapsed;
                BtnAuth.Content = "退出登录";
                BtnAuth.Background = new SolidColorBrush(Color.FromRgb(0xCC, 0x44, 0x44)); // 红色
                TxtStatusBar.Text = $"已验证身份 - 当前用户: {_authUserName}";
                LogAuth(_authUserName, true);
            }
            else
            {
                LogAuth(dialog.UserName ?? "未知", false);
            }
        }

        /// <summary>"为所选程序创建快捷方式"按钮</summary>
        private void BtnCreateShortcut_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            if (!RequireAuth()) return;

            if (string.IsNullOrWhiteSpace(_currentSelectedFolderPath) || _currentMatchedInfos.Count == 0)
            {
                return;
            }

            // 检查是否有多个EXE程序
            var distinctExes = _currentMatchedInfos.Select(m => m.ExeName).Distinct().ToList();
            if (distinctExes.Count > 1)
            {
                MessageBox.Show("此文件夹内存在多个不同EXE程序，请先在\"程序扫描\"页面的\"当前选中\"区域修正文件夹命名后再操作。",
                    "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 检查是否有无法判断工位的
            if (_currentMatchedInfos.Any(m => m.MatchedConfig == null))
            {
                MessageBox.Show("当前文件夹无法判断所属工位（上级文件夹名不含阴阳信息），请先在\"程序扫描\"页面的\"当前选中\"区域处理后再操作。",
                    "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var item = _currentMatchedInfos[0];
            if (item.MatchedConfig == null) return;

            // 显示重命名目标
            string targetName = item.MatchedConfig.ShortcutNamingRule.Replace("{版本号}", item.FileVersion);
            string sourceName = new DirectoryInfo(_currentSelectedFolderPath).Name;
            string desktopPath = TxtDesktopPath?.Text;
            if (string.IsNullOrWhiteSpace(desktopPath))
                desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            string fullShortcutPath = System.IO.Path.Combine(desktopPath, $"{targetName}.lnk");

            var result = MessageBox.Show(
                $"当前文件夹: {sourceName}\n创建位置: {desktopPath}\n快捷方式名: {targetName}.lnk\n\n确认创建？",
                "创建快捷方式", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                CreateShortcut(item.MatchedConfig, item.FileVersion);
            }
        }

        /// <summary>创建快捷方式</summary>
        private void CreateShortcut(ParamConfigItem config, string version)
        {
            try
            {
                // 获取目标EXE路径
                string exePath = null;
                var exeFiles = Directory.GetFiles(_currentSelectedFolderPath, "*.exe", SearchOption.TopDirectoryOnly);
                foreach (var exeFile in exeFiles)
                {
                    string fileName = System.IO.Path.GetFileName(exeFile);
                    if (string.Equals(fileName, config.ExeNamePattern, StringComparison.OrdinalIgnoreCase))
                    {
                        exePath = exeFile;
                        break;
                    }
                }

                if (exePath == null)
                {
                    return;
                }

                // 获取快捷方式目标路径
                string desktopPath = TxtDesktopPath?.Text;
                if (string.IsNullOrWhiteSpace(desktopPath))
                {
                    desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                }

                if (!Directory.Exists(desktopPath))
                {
                    MessageBox.Show($"快捷方式目录不存在: {desktopPath}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                string shortcutName = config.ShortcutNamingRule.Replace("{版本号}", version);
                string shortcutPath = System.IO.Path.Combine(desktopPath, $"{shortcutName}.lnk");

                // 使用 WScript.Shell 创建快捷方式
                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null)
                {
                    return;
                }

                dynamic shell = Activator.CreateInstance(shellType);
                dynamic shortcut = shell.CreateShortcut(shortcutPath);
                shortcut.TargetPath = exePath;
                shortcut.WorkingDirectory = System.IO.Path.GetDirectoryName(exePath);
                shortcut.Description = config.StationFolderPattern;
                shortcut.Save();

                System.Runtime.InteropServices.Marshal.ReleaseComObject(shortcut);
                System.Runtime.InteropServices.Marshal.ReleaseComObject(shell);

                MessageBox.Show($"快捷方式已创建成功！\n{shortcutPath}", "完成", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"创建快捷方式失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>"修正选中文件夹名称"按钮</summary>
        private void BtnFixFolderName_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            if (!RequireAuth()) return;

            if (string.IsNullOrWhiteSpace(_currentSelectedFolderPath) || _currentMatchedInfos.Count == 0)
            {
                return;
            }

            // 检查是否有多个不同EXE程序
            var distinctExes = _currentMatchedInfos.Select(m => m.ExeName).Distinct().ToList();
            if (distinctExes.Count > 1)
            {
                MessageBox.Show("此文件夹内存在多个不同EXE程序，请先使用\"修正文件夹命名\"按钮处理。",
                    "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 检查是否有无法判断工位的
            if (_currentMatchedInfos.Any(m => m.MatchedConfig == null))
            {
                MessageBox.Show("当前文件夹无法判断所属工位（上级文件夹名不含阴阳信息），请先使用\"修正文件夹命名\"按钮处理。",
                    "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var item = _currentMatchedInfos[0];
            if (item.MatchedConfig == null) return;

            string currentName = new DirectoryInfo(_currentSelectedFolderPath).Name;
            string newName = item.MatchedConfig.FolderNamingRule.Replace("{版本号}", item.FileVersion);

            var result = MessageBox.Show(
                $"当前文件夹名称: {currentName}\n修改后文件夹名称: {newName}\n\n确认修改？",
                "修正文件夹名称", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                RenameFolderToMatch(item);
            }
        }

        /// <summary>重命名文件夹以匹配规则</summary>
        private void RenameFolderToMatch(MatchedInfoItem item)
        {
            try
            {
                string parentDir = System.IO.Path.GetDirectoryName(_currentSelectedFolderPath);
                string newFolderName = item.MatchedConfig.FolderNamingRule.Replace("{版本号}", item.FileVersion);
                string newPath = System.IO.Path.Combine(parentDir, newFolderName);

                if (string.Equals(_currentSelectedFolderPath, newPath, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                if (Directory.Exists(newPath))
                {
                    var result = MessageBox.Show($"目标文件夹 {newFolderName} 已存在，是否覆盖？",
                        "文件夹已存在", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                    if (result != MessageBoxResult.Yes) return;
                    Directory.Delete(newPath, true);
                }

                Directory.Move(_currentSelectedFolderPath, newPath);

                // 刷新目录树并选中新路径
                string rootPath = _isRemoteMode ? CbRemotePath?.Text : TxtBasePath?.Text;
                if (!string.IsNullOrWhiteSpace(rootPath))
                {
                    RefreshDirectoryTree(rootPath);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"重命名失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ==================== 操作按钮 ====================

        /// <summary>扫描结果数据集合</summary>
        private ObservableCollection<ScanResultItem> _scanResults;

        /// <summary>所有最新程序信息缓存（用于"获取所有最新程序信息"按钮）</summary>
        private List<ScanResultItem> _latestProgramInfos = new List<ScanResultItem>();

        private async void BtnScanVersion_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;

            // 获取选中路径（统一从目录树选中项获取，本机和非本机都一样）
            string scanPath = null;
            if (TreeViewDirectory.SelectedItem is TreeViewItem selectedItem && selectedItem.Tag is string path)
            {
                scanPath = path;
            }
            else if (TxtSelectedPath != null && !string.IsNullOrWhiteSpace(TxtSelectedPath.Text)
                && TxtSelectedPath.Text != "(未选择)")
                {
                    scanPath = TxtSelectedPath.Text;
                }

            if (string.IsNullOrWhiteSpace(scanPath) || !Directory.Exists(scanPath))
            {
                return;
            }

            bool deepSearch = ChkDeepSearch?.IsChecked == true;

            TxtStatusBar.Text = "扫描中...";

            // 异步执行扫描
            var results = await Task.Run(() => ScanForExeFiles(scanPath, deepSearch));

            // 主线程更新 UI
            _scanResults = new ObservableCollection<ScanResultItem>(results);
            ListBoxScanResults.ItemsSource = _scanResults;

            // 标记颜色
            ApplyColorMarking();

            TxtStatusBar.Text = $"扫描完成，找到 {results.Count} 个匹配的EXE文件";
        }

        /// <summary>扫描文件夹中的EXE文件，匹配参数设定表格中的EXE名称</summary>
        private List<ScanResultItem> ScanForExeFiles(string folderPath, bool deepSearch)
        {
            var results = new List<ScanResultItem>();
            var exePatterns = _paramConfigItems?
                .Select(p => p.ExeNamePattern)
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Distinct()
                .ToList();

            if (exePatterns == null || exePatterns.Count == 0)
            {
                return results;
            }

            var searchOption = deepSearch ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

            try
            {
                var exeFiles = Directory.GetFiles(folderPath, "*.exe", searchOption);
                foreach (var exeFile in exeFiles)
                {
                    string fileName = System.IO.Path.GetFileName(exeFile);
                    // 检查文件名是否匹配任意一个配置的EXE名称
                    if (exePatterns.Any(p => string.Equals(fileName, p, StringComparison.OrdinalIgnoreCase)))
                    {
                        try
                        {
                            var fi = new FileInfo(exeFile);
                            var versionInfo = FileVersionInfo.GetVersionInfo(exeFile);
                            results.Add(new ScanResultItem
                            {
                                ExeName = fileName,
                                FileVersion = versionInfo.FileVersion ?? "未知",
                                CreationTime = fi.CreationTime,
                                LastWriteTime = fi.LastWriteTime,
                                LastAccessTime = fi.LastAccessTime,
                                FolderName = fi.Directory?.Name ?? "",
                                FullPath = exeFile
                            });
                        }
                        catch
                        {
                            // 跳过无法读取版本信息的文件
                        }
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
                // 无权限时跳过
            }
            catch (Exception ex)
            {
            }

            return results;
        }

        /// <summary>对扫描结果标记颜色：同名EXE中最新版本绿色，其余红色</summary>
        private void ApplyColorMarking()
        {
            if (_scanResults == null || _scanResults.Count == 0) return;

            // 按EXE名称分组，每组内选版本最新的标记为最新
            var groups = _scanResults.GroupBy(r => r.ExeName, StringComparer.OrdinalIgnoreCase);
            var latestItems = new HashSet<ScanResultItem>();

            foreach (var group in groups)
            {
                // 按版本号排序取最新的（使用 Version.TryParse 比较）
                var sorted = group
                    .OrderByDescending(r =>
                    {
                        if (Version.TryParse(r.FileVersion, out var v))
                            return v;
                        return new Version(0, 0);
                    })
                    .ThenByDescending(r => r.LastWriteTime)
                    .ToList();

                if (sorted.Count > 0)
                {
                    // 版本相同的可能有多个，都标记为最新
                    var topVersion = sorted[0].FileVersion;
                    foreach (var item in sorted.Where(s => s.FileVersion == topVersion))
                    {
                        latestItems.Add(item);
                    }
                }
            }

            foreach (var item in _scanResults)
            {
                item.IsLatest = latestItems.Contains(item);
            }

            // 刷新 ListBox 显示
            var view = CollectionViewSource.GetDefaultView(_scanResults);
            view?.Refresh();
        }

        /// <summary>过滤旧版本结果</summary>
        private void ChkFilterOldVersions_Checked(object sender, RoutedEventArgs e)
        {
            ApplyFilter();
        }

        private void ChkFilterOldVersions_Unchecked(object sender, RoutedEventArgs e)
        {
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            if (_scanResults == null) return;

            var view = CollectionViewSource.GetDefaultView(_scanResults);
            if (view == null) return;

            bool filterOld = ChkFilterOldVersions?.IsChecked == true;

            if (filterOld)
            {
                view.Filter = item =>
                {
                    if (item is ScanResultItem result)
                        return result.IsLatest;
                    return true;
                };
            }
            else
            {
                view.Filter = null;
            }

            TxtStatusBar.Text = $"扫描结果: {_scanResults.Count} 个 (显示: {view.Cast<object>().Count()})";
        }

        /// <summary>获取所有最新程序信息（供后续使用）</summary>
        private void BtnGetLatestInfos_Click(object sender, RoutedEventArgs e)
        {
            if (_scanResults == null || _scanResults.Count == 0)
            {
                return;
            }

            // 只收集用户勾选的行
            _latestProgramInfos = _scanResults
                .Where(r => r.IsSelected)
                .ToList();

            if (_latestProgramInfos.Count == 0)
            {
                return;
            }


            // 显示到状态栏
            TxtStatusBar.Text = $"已获取 {_latestProgramInfos.Count} 个所选程序信息";

            // 将数据转换为匹配项并追加到程序匹配页面（不覆盖已有数据）
            var existingItems = DataGridMatchPrograms.ItemsSource as ObservableCollection<MatchProgramItem>;
            if (existingItems == null)
            {
                existingItems = new ObservableCollection<MatchProgramItem>();
                DataGridMatchPrograms.ItemsSource = existingItems;
            }

            foreach (var item in _latestProgramInfos)
            {
                // 检查是否已存在相同ExeName+FilePath的记录，避免重复追加
                if (!existingItems.Any(x => string.Equals(x.ExeName, item.ExeName, StringComparison.OrdinalIgnoreCase)
                                         && string.Equals(x.FullPath, item.FullPath, StringComparison.OrdinalIgnoreCase)))
                {
                    // 使用与 UpdateMatchedProgramInfo 相同的逻辑匹配工位
                    string stationPattern = ResolveStationPattern(item.FullPath, item.ExeName);

                    var matchItem = new MatchProgramItem
                    {
                        ExeName = item.ExeName,
                        FileVersion = item.FileVersion,
                        FolderName = item.FolderName,
                        FullPath = item.FullPath,
                        StationPattern = stationPattern
                    };
                    matchItem.GetFolderNamingRule = (exeName) =>
                    {
                        // 按ExeName+StationPattern精确匹配
                        var cfg = _paramConfigItems?.FirstOrDefault(p =>
                            string.Equals(p.ExeNamePattern, exeName, StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(p.StationFolderPattern, stationPattern, StringComparison.OrdinalIgnoreCase));
                        if (cfg == null && !string.IsNullOrWhiteSpace(stationPattern))
                        {
                            // 若精确匹配不到，尝试通过StationPattern去阴/阳后缀匹配
                            string cleanPattern = stationPattern.Replace("阳极", "").Replace("阴极", "").Replace("Yang", "").Replace("Yin", "").Replace("yang", "").Replace("yin", "").Trim();
                            if (!string.IsNullOrWhiteSpace(cleanPattern))
                            {
                                cfg = _paramConfigItems?.FirstOrDefault(p =>
                                    string.Equals(p.ExeNamePattern, exeName, StringComparison.OrdinalIgnoreCase) &&
                                    (p.StationFolderPattern.Contains(cleanPattern) || cleanPattern.Contains(p.StationFolderPattern)));
                            }
                        }
                        return cfg?.FolderNamingRule;
                    };
                    existingItems.Add(matchItem);
                }
            }

        }

        /// <summary>"浏览"待推广程序路径按钮</summary>
        /// <summary>"清除选中项"按钮 - 删除程序匹配页面中勾选的行</summary>
        private void BtnClearSelectedMatchItems_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;

            var items = DataGridMatchPrograms.ItemsSource as ObservableCollection<MatchProgramItem>;
            if (items == null || items.Count == 0) return;

            var toRemove = items.Where(i => i.IsSelected).ToList();
            if (toRemove.Count == 0)
            {
                MessageBox.Show("请先在表格中勾选需要清除的项。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            foreach (var item in toRemove)
            {
                items.Remove(item);
            }

        }

        // ==================== 程序匹配拖放支持 ====================

        /// <summary>拖放进入时显示可拖放效果</summary>
        private void TextBox_PreviewDragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
            }
        }

        /// <summary>TextBox拖放释放</summary>
        private void TextBox_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files.Length > 0)
                {
                    string folderPath = files[0];
                    // 如果是文件则取所在目录
                    if (File.Exists(folderPath))
                    {
                        folderPath = System.IO.Path.GetDirectoryName(folderPath);
                    }
                    ApplyTargetFolder(folderPath, sender as TextBox);
                }
            }
        }

        /// <summary>Grid拖放释放（兼容整个单元格区域）</summary>
        private void Grid_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files.Length > 0)
                {
                    string folderPath = files[0];
                    if (File.Exists(folderPath))
                    {
                        folderPath = System.IO.Path.GetDirectoryName(folderPath);
                    }
                    // 找到单元格内的TextBox
                    var grid = sender as Grid;
                    if (grid != null)
                    {
                        var textBox = FindVisualChild<TextBox>(grid);
                        if (textBox != null)
                        {
                            ApplyTargetFolder(folderPath, textBox);
                        }
                    }
                }
            }
        }

        private void Grid_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
            }
        }

        /// <summary>查找可视化树中的子元素</summary>
        private static T FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T t)
                    return t;
                var result = FindVisualChild<T>(child);
                if (result != null)
                    return result;
            }
            return null;
        }

        /// <summary>应用目标文件夹路径到绑定的数据项</summary>
        private void ApplyTargetFolder(string folderPath, TextBox textBox)
        {
            if (textBox == null) return;

            // 通过 DataGrid 获取当前行绑定的数据项
            var dataGrid = FindVisualParent<DataGrid>(textBox);
            if (dataGrid != null && dataGrid.SelectedItem is MatchProgramItem selectedItem)
            {
                selectedItem.TargetFolderPath = folderPath;
                ScanTargetFolderVersion(selectedItem, folderPath);
            }
            else
            {
                // 如果选中行不对，尝试从 DataContext 获取
                var item = textBox.DataContext as MatchProgramItem;
                if (item != null)
                {
                    item.TargetFolderPath = folderPath;
                    ScanTargetFolderVersion(item, folderPath);
                }
            }
        }

        /// <summary>查找可视化树中的父元素</summary>
        private static T FindVisualParent<T>(DependencyObject child) where T : DependencyObject
        {
            var parent = VisualTreeHelper.GetParent(child);
            while (parent != null && !(parent is T))
            {
                parent = VisualTreeHelper.GetParent(parent);
            }
            return parent as T;
        }

        /// <summary>扫描目标文件夹中匹配的EXE版本</summary>
        private void ScanTargetFolderVersion(MatchProgramItem item, string folderPath)
        {
            try
            {
                var exeFiles = Directory.GetFiles(folderPath, "*.exe", SearchOption.TopDirectoryOnly);
                foreach (var exeFile in exeFiles)
                {
                    string fileName = System.IO.Path.GetFileName(exeFile);
                    if (string.Equals(fileName, item.ExeName, StringComparison.OrdinalIgnoreCase))
                    {
                        var vi = FileVersionInfo.GetVersionInfo(exeFile);
                        item.TargetFileVersion = vi.FileVersion ?? "未知";
                        item.CheckMatch();
                        return;
                    }
                }
                item.TargetFileVersion = "";
                item.CheckMatch();
            }
            catch
            {
                item.TargetFileVersion = "";
                item.CheckMatch();
            }
        }

        private void BtnBrowseTargetPath_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;

            var dialog = new System.Windows.Forms.FolderBrowserDialog();
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                string folderPath = dialog.SelectedPath;

                // 获取当前 DataGrid 选中的行
                if (DataGridMatchPrograms.SelectedItem is MatchProgramItem selectedItem)
                {
                    selectedItem.TargetFolderPath = folderPath;
                    ScanTargetFolderVersion(selectedItem, folderPath);
                }
                else
                {
                    // 没有选中行，尝试找到焦点行或第一行
                    var items = DataGridMatchPrograms.ItemsSource as ObservableCollection<MatchProgramItem>;
                    if (items != null && items.Count > 0)
                    {
                        items[0].TargetFolderPath = folderPath;
                        ScanTargetFolderVersion(items[0], folderPath);
                    }
                }
            }
        }

        /// <summary>
        /// "更新(复制-替换匹配-重命名)"按钮
        /// 对每个勾选的行：
        /// 1. 复制程序位置所在文件夹(A) 为 A - 副本
        /// 2. 将待推广程序路径(B)中的全部文件复制到 A - 副本并覆盖
        /// 3. 将 A - 副本 重命名为 推广后新文件夹名称
        /// </summary>
        private void BtnUpdateCopyReplaceRename_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;

            var items = DataGridMatchPrograms.ItemsSource as ObservableCollection<MatchProgramItem>;
            if (items == null || items.Count == 0)
            {
                MessageBox.Show("程序匹配表格中无数据。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var selectedItems = items.Where(i => i.IsSelected).ToList();
            if (selectedItems.Count == 0)
            {
                MessageBox.Show("请先在表格中勾选需要更新的行。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // 检查每行数据是否完整
            var invalidItems = selectedItems
                .Where(i => string.IsNullOrWhiteSpace(i.TargetFolderPath) || string.IsNullOrWhiteSpace(i.NewFolderName))
                .Select(i => i.ExeName)
                .ToList();
            if (invalidItems.Count > 0)
            {
                MessageBox.Show($"以下行的待推广程序路径或推广后新文件夹名称为空，请先完善：\n{string.Join("\n", invalidItems)}",
                    "数据不完整", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var result = MessageBox.Show(
                $"共勾选了 {selectedItems.Count} 条记录，确认执行更新操作？\n\n" +
                "操作流程：\n" +
                "1. 复制程序位置所在的文件夹 → 生成 -副本\n" +
                "2. 将待推广路径内的文件复制到 -副本 中覆盖\n" +
                "3. 将 -副本 重命名为推广后新文件夹名称",
                "确认更新", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes) return;

            int successCount = 0;
            int failCount = 0;
            var errorMessages = new List<string>();

            foreach (var item in selectedItems)
            {
                try
                {
                    // 步骤1: 获取程序位置所在的文件夹路径 (A)
                    string exeFilePath = item.FullPath; // 完整EXE路径，如 D:\CCD程序\阳极瑕疵\IVision_CHKR.exe
                    string folderAPath = System.IO.Path.GetDirectoryName(exeFilePath); // A 文件夹路径
                    if (string.IsNullOrWhiteSpace(folderAPath) || !Directory.Exists(folderAPath))
                    {
                        errorMessages.Add($"[{item.ExeName}] 程序所在文件夹不存在: {folderAPath}");
                        failCount++;
                        continue;
                    }

                    string parentDir = System.IO.Path.GetDirectoryName(folderAPath); // A的上级目录
                    string folderAName = new DirectoryInfo(folderAPath).Name; // A的文件夹名

                    // 检查待推广程序路径 (B) 是否存在
                    string folderBPath = item.TargetFolderPath;
                    if (!Directory.Exists(folderBPath))
                    {
                        errorMessages.Add($"[{item.ExeName}] 待推广程序路径不存在: {folderBPath}");
                        failCount++;
                        continue;
                    }

                    // 步骤2: 创建 A - 副本
                    string copyFolderName = $"{folderAName} - 副本";
                    string copyFolderPath = System.IO.Path.Combine(parentDir, copyFolderName);

                    // 如果已存在则先删除
                    if (Directory.Exists(copyFolderPath))
                    {
                        Directory.Delete(copyFolderPath, true);
                    }

                    // 复制A文件夹到 A - 副本
                    CopyDirectory(folderAPath, copyFolderPath);

                    // 步骤3: 将B文件夹内的全部内容复制到 A - 副本，覆盖同名文件
                    CopyDirectoryWithOverwrite(folderBPath, copyFolderPath);

                    // 步骤4: 将 A - 副本 重命名为 推广后新文件夹名称
                    string newFolderName = item.NewFolderName;
                    string newFolderPath = System.IO.Path.Combine(parentDir, newFolderName);

                    // 如果目标已存在则先删除
                    if (Directory.Exists(newFolderPath))
                    {
                        Directory.Delete(newFolderPath, true);
                    }

                    Directory.Move(copyFolderPath, newFolderPath);

                    successCount++;
                }
                catch (Exception ex)
                {
                    errorMessages.Add($"[{item.ExeName}] 更新失败: {ex.Message}");
                    failCount++;
                }
            }

            // 显示结果
            // 写更新日志
            AppendSmartLog($"===== 更新(复制-替换匹配-重命名) [{DateTime.Now:yyyy-MM-dd HH:mm:ss}] =====");
            foreach (var item in selectedItems)
                AppendSmartLog($"  {item.ExeName} ({item.StationPattern}): V{item.FileVersion} → V{item.TargetFileVersion}");
            if (successCount > 0)
                AppendSmartLog($"更新成功: {successCount} 条");
            foreach (var err in errorMessages.Take(10))
                AppendSmartLog($"更新失败: {err}");
            AppendSmartLog($"===== 结束 =====");

            string summary = $"操作完成！成功: {successCount} 条，失败: {failCount} 条。";
            if (errorMessages.Count > 0)
            {
                summary += "\n\n错误详情：\n" + string.Join("\n", errorMessages.Take(10));
                if (errorMessages.Count > 10)
                    summary += $"\n...等共 {errorMessages.Count} 条错误";
            }

            MessageBox.Show(summary, "更新结果", MessageBoxButton.OK,
                failCount > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);

            TxtStatusBar.Text = $"更新完成：成功 {successCount} / 共 {selectedItems.Count} 条";

            // 成功后刷新目录树
            string rootPath = _isRemoteMode ? CbRemotePath?.Text : TxtBasePath?.Text;
            if (!string.IsNullOrWhiteSpace(rootPath) && Directory.Exists(rootPath))
            {
                RefreshDirectoryTree(rootPath);
            }
        }

        /// <summary>递归复制目录及其所有内容</summary>
        private void CopyDirectory(string sourceDir, string destDir)
        {
            Directory.CreateDirectory(destDir);

            // 复制文件
            foreach (var file in Directory.GetFiles(sourceDir))
            {
                string destFile = System.IO.Path.Combine(destDir, System.IO.Path.GetFileName(file));
                File.Copy(file, destFile, true);
            }

            // 递归复制子目录
            foreach (var dir in Directory.GetDirectories(sourceDir))
            {
                string dirName = new DirectoryInfo(dir).Name;
                string destSubDir = System.IO.Path.Combine(destDir, dirName);
                CopyDirectory(dir, destSubDir);
            }
        }

        /// <summary>将源目录中的文件复制到目标目录（覆盖同名文件，不复制子目录结构）</summary>
        private void CopyDirectoryWithOverwrite(string sourceDir, string destDir)
        {
            if (!Directory.Exists(destDir))
            {
                Directory.CreateDirectory(destDir);
            }

            // 复制源目录顶层所有文件到目标目录，覆盖同名文件
            foreach (var file in Directory.GetFiles(sourceDir))
            {
                string destFile = System.IO.Path.Combine(destDir, System.IO.Path.GetFileName(file));
                File.Copy(file, destFile, true);
            }

            // 递归复制子目录及其内容到目标对应子目录
            foreach (var dir in Directory.GetDirectories(sourceDir))
            {
                string dirName = new DirectoryInfo(dir).Name;
                string destSubDir = System.IO.Path.Combine(destDir, dirName);
                CopyDirectory(dir, destSubDir);
            }
        }

        // ==================== 新设备配置方法 ====================

        /// <summary>添加默认新设备配置行</summary>
        private void AddDefaultDeviceConfigRows()
        {
            if (_deviceConfigRows == null || _deviceConfigRows.Count > 0) return;
            string defaultDrive = DriveList.FirstOrDefault(d => d.StartsWith("D")) ?? "D:";

            var ipMap = new Dictionary<string, string>
            {
                { "阳极尺寸", "192.168.250.31" },
                { "阳极瑕疵", "192.168.250.31" },
                { "阴极尺寸", "192.168.250.32" },
                { "阴极瑕疵", "192.168.250.32" },
                { "工位1", "192.168.250.35" },
                { "工位2", "192.168.250.36" },
                { "工位3", "192.168.250.37" },
                { "工位4", "192.168.250.38" },
            };

            string[] subFolders = { "阳极尺寸", "阴极尺寸", "阳极瑕疵", "阴极瑕疵",
                                    "工位1", "工位2", "工位3", "工位4", "" };

            for (int i = 0; i < 9; i++)
            {
                bool isMes = (i == 8);
                string sub = subFolders[i];
                string root = isMes ? "MES上位机程序" : "CCD程序";
                ipMap.TryGetValue(sub, out string ip);
                if (isMes && string.IsNullOrEmpty(ip)) ip = "192.168.250.38";

                _deviceConfigRows.Add(new DeviceConfigRow
                {
                    IsSelected = true,
                    IsCrossMachine = !string.IsNullOrEmpty(ip),
                    RemoteIP = ip ?? "",
                    Account = @"CATLBATTERY\YBSJ-CL01",
                    Password = "Aa147.258",
                    SelectedDrive = defaultDrive,
                    ShareName = "d",
                    RootFolderName = root,
                    SubFolderName = sub
                });
            }
        }

        /// <summary>保存新设备配置到文件（保存到合并的 JSON）</summary>
        private void SaveDeviceConfigToFile()
        {
            try
            {
                string configDir = System.IO.Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory, "KB_ToolsBox_Config", "UpdateEXE");
                Directory.CreateDirectory(configDir);
                string filePath = System.IO.Path.Combine(configDir, "Prpgram.json");

                // 读取已有的参数配置，合并设备配置后一同保存
                object existingParamItems = null;
                if (System.IO.File.Exists(filePath))
                {
                    try
                    {
                        string existingJson = System.IO.File.ReadAllText(filePath);
                        var existing = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, object>>(existingJson);
                        if (existing != null && existing.TryGetValue("ParamConfigItems", out object p))
                            existingParamItems = p;
                    }
                    catch { }
                }

                var combined = new Dictionary<string, object>
                {
                    ["ParamConfigItems"] = existingParamItems ?? _paramConfigItems.Select(p => new
                    {
                        p.StationFolderPattern,
                        p.ExeNamePattern,
                        p.FolderNamingRule,
                        p.ShortcutNamingRule,
                        p.RemoteAccount,
                        p.RemotePassword,
                        p.RemotePath
                    }).ToList(),
                    ["DeviceConfigRows"] = _deviceConfigRows.ToList()
                };

                string json = Newtonsoft.Json.JsonConvert.SerializeObject(combined, Newtonsoft.Json.Formatting.Indented);
                System.IO.File.WriteAllText(filePath, json);
            }
            catch (Exception ex)
            {
            }
        }

        /// <summary>添加行</summary>
        private void BtnDeviceAddRow_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            string defaultDrive = DriveList.FirstOrDefault(d => d.StartsWith("D")) ?? "D:";
            _deviceConfigRows.Add(new DeviceConfigRow
            {
                IsSelected = true,
                SelectedDrive = defaultDrive,
                RootFolderName = "",
                SubFolderName = ""
            });
            if (_deviceConfigRows.Count > 0)
                DataGridDeviceConfig.ScrollIntoView(_deviceConfigRows.Last());
        }

        /// <summary>删除行</summary>
        private void BtnDeviceDeleteRow_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            var selected = DataGridDeviceConfig.SelectedItem as DeviceConfigRow;
            if (selected == null)
            {
                MessageBox.Show("请先在表格中选择要删除的行", "提示",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            _deviceConfigRows.Remove(selected);
        }

        /// <summary>创建程序目录</summary>
        private async void BtnDeviceCreateDirs_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;

            bool usePrefix = ChkUseDevicePrefix.IsChecked == true;
            string deviceNumber = "";
            if (usePrefix)
            {
                deviceNumber = TxtDeviceNumber.Text.Trim();
                if (string.IsNullOrWhiteSpace(deviceNumber))
                {
                    MessageBox.Show("已勾选「写入设备号」，请先填写设备号！", "设备号为空",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    TxtDeviceNumber.Focus();
                    return;
                }
            }

            var selectedRows = _deviceConfigRows.Where(r => r.IsSelected).ToList();
            if (selectedRows.Count == 0)
            {
                MessageBox.Show("没有已勾选的配置行", "提示",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var invalidRows = selectedRows.Where(r => string.IsNullOrWhiteSpace(r.RootFolderName)).ToList();
            if (invalidRows.Count > 0)
            {
                MessageBox.Show("以下行的一级文件夹名称为空，请填写完整", "信息不完整",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var crossRowsNoIP = selectedRows.Where(r => r.IsCrossMachine && string.IsNullOrWhiteSpace(r.RemoteIP)).ToList();
            if (crossRowsNoIP.Count > 0)
            {
                MessageBox.Show("以下行勾选了跨工控机但未填写IP地址，请补全", "IP地址为空",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 生成预览路径
            var previewInfos = selectedRows.Select(r =>
            {
                string root = r.RootFolderName;
                string sub = usePrefix && !string.IsNullOrWhiteSpace(r.SubFolderName)
                    ? $"{deviceNumber}#{r.SubFolderName}"
                    : r.SubFolderName;
                string localPath = string.IsNullOrWhiteSpace(sub)
                    ? System.IO.Path.Combine(r.SelectedDrive + "\\", root)
                    : System.IO.Path.Combine(r.SelectedDrive + "\\", root, sub);
                string actualPath = r.IsCrossMachine
                    ? ConvertToUNCPath(r.RemoteIP, localPath, r.ShareName)
                    : localPath;
                return new { Row = r, LocalPath = localPath, ActualPath = actualPath };
            }).ToList();

            var prefixStatus = usePrefix ? $"? 已启用设备号前缀 (设备号: {deviceNumber})" : "? 未启用设备号前缀";
            var crossInfo = previewInfos.Any(p => p.Row.IsCrossMachine)
                ? "\n? 包含跨工控机路径（UNC: \\\\IP\\...）" : "";
            var msgResult = MessageBox.Show($"{prefixStatus}{crossInfo}\n\n" +
                $"将创建 {previewInfos.Count} 个目录：\n\n{string.Join("\n", previewInfos.Select(p => p.ActualPath))}",
                "确认创建目录", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (msgResult != MessageBoxResult.Yes) return;

            BtnDeviceCreateDirs.IsEnabled = false;
            try
            {
                await Task.Run(() =>
                {
                    var remoteAuthNeeded = previewInfos
                        .Where(p => p.Row.IsCrossMachine && !string.IsNullOrWhiteSpace(p.Row.RemoteIP))
                        .Select(p => new { p.Row.RemoteIP, p.Row.Account, p.Row.Password })
                        .GroupBy(x => x.RemoteIP)
                        .Select(g => g.First())
                        .ToList();

                    var authenticatedIPs = new HashSet<string>();
                    var authErrors = new Dictionary<string, string>();

                    System.Threading.Tasks.Parallel.ForEach(remoteAuthNeeded,
                        new ParallelOptions { MaxDegreeOfParallelism = 8 }, auth =>
                    {
                        try
                        {
                            if (TryAuthenticateRemote(auth.RemoteIP, auth.Account, auth.Password, out string authErr))
                            {
                                lock (authenticatedIPs) authenticatedIPs.Add(auth.RemoteIP);
                            }
                            else
                            {
                                lock (authErrors) authErrors[auth.RemoteIP] = authErr ?? "未知错误";
                            }
                        }
                        catch (Exception ex)
                        {
                            lock (authErrors) authErrors[auth.RemoteIP] = ex.Message;
                        }
                    });

                    int successCount = 0, failCount = 0;
                    object countLock = new object();
                    var failDetails = new List<string>();
                    object failLock = new object();

                    System.Threading.Tasks.Parallel.ForEach(previewInfos,
                        new ParallelOptions { MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount) }, info =>
                    {
                        try
                        {
                            var row = info.Row;
                            string location = row.IsCrossMachine ? $"[{row.RemoteIP}]" : "[本地]";
                            if (row.IsCrossMachine && !string.IsNullOrWhiteSpace(row.RemoteIP))
                            {
                                if (!authenticatedIPs.Contains(row.RemoteIP))
                                {
                                    string authReason = authErrors.TryGetValue(row.RemoteIP, out var reason)
                                        ? reason : "未知错误";
                                    lock (failLock) failDetails.Add($"{location} {row.RootFolderName}\\{row.SubFolderName}: {authReason}");
                                    lock (countLock) failCount++;
                                    return;
                                }
                            }
                            if (!Directory.Exists(info.ActualPath))
                            {
                                Directory.CreateDirectory(info.ActualPath);
                            }
                            else
                            {
                            }
                            lock (countLock) successCount++;
                        }
                        catch (Exception ex)
                        {
                            lock (failLock) failDetails.Add($"[{info.Row.RemoteIP}] {info.Row.RootFolderName}: {ex.Message}");
                            lock (countLock) failCount++;
                        }
                    });

                    int finalSuccess = successCount, finalFail = failCount;
                    var details = new List<string>(failDetails);
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        // 写更新日志
                        AppendSmartLog($"===== 创建程序目录 [{DateTime.Now:yyyy-MM-dd HH:mm:ss}] =====");
                        foreach (var info in previewInfos)
                            AppendSmartLog($"  {info.ActualPath}");
                        if (finalSuccess > 0)
                            AppendSmartLog($"创建成功: {finalSuccess} 个目录");
                        foreach (var d in details.Take(10))
                            AppendSmartLog($"创建失败: {d}");
                        AppendSmartLog($"===== 结束 =====");

                        if (finalFail > 0)
                        {
                            MessageBox.Show($"创建完成。\n成功: {finalSuccess}\n失败: {finalFail}\n\n失败详情：\n{string.Join("\n", details)}",
                                "创建结果", MessageBoxButton.OK, MessageBoxImage.Warning);
                        }
                        else
                        {
                            MessageBox.Show($"? 全部创建成功！共创建 {finalSuccess} 个目录。", "创建成功",
                                MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                        string statusSuffix = usePrefix ? $" [设备号: {deviceNumber}]" : "";
                        TxtStatusBar.Text = $"目录创建完成: 成功 {finalSuccess}, 失败 {finalFail}{statusSuffix}";
                        SaveDeviceConfigToFile();
                    }));
                });
            }
            catch (Exception ex)
            {
                string msg = ex.Message;
                if (ex is AggregateException agg && agg.InnerExceptions.Count > 0)
                    msg = string.Join("\n", agg.InnerExceptions.Select(ie => ie.Message));
                AppendSmartLog($"创建程序目录异常: {msg}");
                MessageBox.Show($"创建过程出现未预期的异常:\n{msg}", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                BtnDeviceCreateDirs.IsEnabled = true;
            }
        }

        // ==================== 跨工控机认证辅助方法 ====================

        /// <summary>将本地路径转换为UNC路径（使用指定的共享名）</summary>
        private static string ConvertToUNCPath(string ip, string localPath, string shareName)
        {
            if (string.IsNullOrWhiteSpace(ip) || string.IsNullOrWhiteSpace(localPath))
                return localPath;
            int colonIdx = localPath.IndexOf(':');
            if (colonIdx < 0) return localPath;
            string restPath = localPath.Substring(colonIdx + 1).TrimStart('\\');
            string actualShare = string.IsNullOrWhiteSpace(shareName)
                ? localPath.Substring(0, colonIdx) + "$"
                : shareName;
            return $@"\\{ip}\{actualShare}\{restPath}";
        }

        /// <summary>检查远程主机 TCP 端口是否开放（用于判断 SMB 445 是否可达）</summary>
        private static bool CheckTcpPort(string ip, int port, int timeoutMs = 1500)
        {
            try
            {
                using (var client = new System.Net.Sockets.TcpClient())
                {
                    var ar = client.BeginConnect(ip, port, null, null);
                    bool connected = ar.AsyncWaitHandle.WaitOne(TimeSpan.FromMilliseconds(timeoutMs))
                                     && client.Connected;
                    return connected;
                }
            }
            catch { return false; }
        }

        /// <summary>尝试远程认证：先匿名，再用账户密码</summary>
        private static bool TryAuthenticateRemote(string ip, string account, string password, out string errorMsg)
        {
            errorMsg = null;

            // 第一步：Ping 检测
            bool pingOk;
            try
            {
                using (var ping = new Ping())
                {
                    var reply = ping.Send(ip, 1000);
                    pingOk = reply != null && reply.Status == IPStatus.Success;
                }
            }
            catch { pingOk = false; }

            if (!pingOk)
            {
                errorMsg = $"IP 不可达（Ping 超时）";
                return false;
            }

            // 第二步：检测 445 端口是否开放
            bool port445Open = CheckTcpPort(ip, 445);
            if (!port445Open)
            {
                errorMsg = $"445 端口未开放或被防火墙拦截";
                return false;
            }

            // 第三步：尝试匿名连接 IPC$
            try
            {
                if (Directory.Exists($@"\\{ip}\IPC$"))
                {
                    errorMsg = "直接连接成功";
                    return true;
                }
            }
            catch { }

            // 第四步：带账户密码认证
            if (!string.IsNullOrWhiteSpace(account))
            {
                string result = RunNetUse($@"\\{ip}\IPC$", account, password);
                if (string.IsNullOrEmpty(result))
                {
                    errorMsg = $"认证成功 (账户: {account})";
                    return true;
                }
                if (!account.Contains("\\") && !account.StartsWith("."))
                {
                    result = RunNetUse($@"\\{ip}\IPC$", $@".\{account}", password);
                    if (string.IsNullOrEmpty(result))
                    {
                        errorMsg = $"认证成功 (本地账户: {account})";
                        return true;
                    }
                }
                errorMsg = $"身份验证失败 - {result}";
                return false;
            }

            errorMsg = "SMB 匿名访问被拒绝且未提供账户密码";
            return false;
        }


        /// <summary>执行 net use 命令</summary>
        private static string RunNetUse(string uncPath, string username, string password)
        {
            try
            {
                string args = $@"use {uncPath} /persistent:no";
                if (!string.IsNullOrEmpty(username))
                    args += $" /user:{username}";
                if (!string.IsNullOrEmpty(password))
                    args += $" {password}";
                var psi = new ProcessStartInfo("net", args)
                {
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                };
                using (var proc = Process.Start(psi))
                {
                    string output = proc.StandardOutput.ReadToEnd();
                    string error = proc.StandardError.ReadToEnd();
                    proc.WaitForExit(10000);
                    if (proc.ExitCode == 0) return null;
                    return (error + " " + output).Trim();
                }
            }
            catch (Exception ex) { return ex.Message; }
        }


        // ==================== 参数设定表格操作 ====================

        private void BtnAddParamRow_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            _paramConfigItems.Add(new ParamConfigItem());
        }

        private void BtnDeleteParamRow_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            var selected = DataGridParamConfig.SelectedItem as ParamConfigItem;
            if (selected != null)
            {
                _paramConfigItems.Remove(selected);
            }
        }

        // ==================== 快捷方式地址 ====================

        private void BtnDetectDesktop_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            if (TxtDesktopPath != null)
            {
                TxtDesktopPath.Text = desktopPath;
            }
        }

        // ==================== 配置保存/读取 ====================

        private void BtnSaveConfig_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            if (!RequireAuth()) return;

            try
            {
                // 从界面同步路径
                _logDirectoryPath = TxtLogPath.Text.Trim();
                _configFilePath = TxtConfigPath.Text.Trim();

                // 确保目录存在
                string configDir = System.IO.Path.GetDirectoryName(_configFilePath);
                if (!string.IsNullOrEmpty(configDir))
                    Directory.CreateDirectory(configDir);

                // 合并参数配置、设备配置和路径设置到一个JSON
                var combined = new
                {
                    ParamConfigItems = _paramConfigItems.Select(p => new
                    {
                        p.StationFolderPattern,
                        p.ExeNamePattern,
                        p.FolderNamingRule,
                        p.ShortcutNamingRule,
                        p.RemoteAccount,
                        p.RemotePassword,
                        p.RemotePath
                    }).ToList(),
                    DeviceConfigRows = _deviceConfigRows?.ToList(),
                    LogDirectoryPath = _logDirectoryPath,
                    ConfigFilePath = _configFilePath
                };

                string json = Newtonsoft.Json.JsonConvert.SerializeObject(combined, Newtonsoft.Json.Formatting.Indented);
                File.WriteAllText(_configFilePath, json);
                TxtStatusBar.Text = $"参数已保存到: {_configFilePath}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存参数失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnLoadConfig_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            if (!RequireAuth()) return;

            try
            {
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "选择配置文件",
                    Filter = "JSON文件|*.json|所有文件|*.*",
                    DefaultExt = ".json",
                    FileName = _configFilePath
                };

                if (dialog.ShowDialog() == true)
                {
                    string json = File.ReadAllText(dialog.FileName);
                    string loadedFilePath = dialog.FileName;

                    // 尝试解析新格式（合并后的）
                    try
                    {
                        var combined = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
                        if (combined != null && combined.Count > 0)
                        {
                            // 加载参数配置
                            if (combined.TryGetValue("ParamConfigItems", out object paramObj) && paramObj != null)
                            {
                                string paramJson = paramObj.ToString();
                                var loadedParams = Newtonsoft.Json.JsonConvert.DeserializeObject<List<ParamConfigItem>>(paramJson);
                                if (loadedParams != null)
                                {
                                    _paramConfigItems.Clear();
                                    foreach (var item in loadedParams)
                                        _paramConfigItems.Add(item);
                                }
                            }

                            // 加载设备配置
                            if (combined.TryGetValue("DeviceConfigRows", out object devObj) && devObj != null)
                            {
                                string devJson = devObj.ToString();
                                var loadedDevices = Newtonsoft.Json.JsonConvert.DeserializeObject<List<DeviceConfigRow>>(devJson);
                                if (loadedDevices != null && loadedDevices.Count > 0)
                                {
                                    _deviceConfigRows.Clear();
                                    foreach (var item in loadedDevices)
                                        _deviceConfigRows.Add(item);
                                }
                            }

                            // 加载路径设置
                            if (combined.TryGetValue("LogDirectoryPath", out object logObj) && logObj != null)
                            {
                                _logDirectoryPath = logObj.ToString();
                                TxtLogPath.Text = _logDirectoryPath;
                            }
                            if (combined.TryGetValue("ConfigFilePath", out object cfgObj) && cfgObj != null)
                            {
                                _configFilePath = cfgObj.ToString();
                                TxtConfigPath.Text = _configFilePath;
                            }
                            else
                            {
                                _configFilePath = loadedFilePath;
                                TxtConfigPath.Text = _configFilePath;
                            }

                            TxtStatusBar.Text = $"配置已加载: {loadedFilePath}";
                            return;
                        }
                    }
                    catch { }

                    // 兼容旧格式（只有参数配置的列表）
                    var loadedItems = Newtonsoft.Json.JsonConvert.DeserializeObject<List<ParamConfigItem>>(json);
                    if (loadedItems != null && loadedItems.Count > 0)
                    {
                        _paramConfigItems.Clear();
                        foreach (var item in loadedItems)
                        {
                            _paramConfigItems.Add(item);
                        }
                    }
                    else
                    {
                        MessageBox.Show("配置文件中没有有效数据。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"读取配置失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ==================== 辅助方法 ====================

        /// <summary>初始化默认参数设定行</summary>
        private void InitDefaultParamRows()
        {
            var rows = new (string station, string exe)[]
            {
                ("阳极瑕疵", "IVision_CHKR.exe"),
                ("阳极尺寸", "IVision_EMR.exe"),
                ("阴极瑕疵", "IVision_CHKR.exe"),
                ("阴极尺寸", "IVision_EMR.exe"),
                ("工位1", "IVision_EVSR.exe"),
                ("工位2", "IVision_EVSR.exe"),
                ("工位3", "IVision_EVSR.exe"),
                ("工位4", "IVision_EVSR.exe"),
            };

            const string remoteAccount = @"CATLBATTERY\YBSJ-CL01";
            const string remotePassword = "Aa147.258";
            var remotePaths = new[]
            {
                @"\\192.168.250.31\d\CCD程序",
                @"\\192.168.250.31\d\CCD程序",
                @"\\192.168.250.32\d\CCD程序",
                @"\\192.168.250.32\d\CCD程序",
                @"\\192.168.250.35\d\CCD程序",
                @"\\192.168.250.36\d\CCD程序",
                @"\\192.168.250.37\d\CCD程序",
                @"\\192.168.250.38\d\CCD程序",
            };

            for (int i = 0; i < rows.Length; i++)
            {
                var (station, exe) = rows[i];

                // 根据工位名称生成文件夹命名规则
                // 阴极 → _Yin, 阳极 → _Yang
                string suffix = "";
                if (station.Contains("阴极"))
                    suffix = "_Yin";
                else if (station.Contains("阳极"))
                    suffix = "_Yang";

                string exeNameNoExt = exe.Replace(".exe", "");
                // 程序文件夹命名：{exe名称(不带.exe)}{阴阳后缀} - V{版本号}
                string folderRule = $"{exeNameNoExt}{suffix} - V{{版本号}}";

                _paramConfigItems.Add(new ParamConfigItem
                {
                    StationFolderPattern = station,
                    ExeNamePattern = exe,
                    // 程序文件夹命名：{exe名称(不带.exe)}{阴阳后缀} - V{版本号}
                    FolderNamingRule = folderRule,
                    // 快捷方式命名：{工位名称} - V{版本号}
                    ShortcutNamingRule = $"{station} - V{{版本号}}",
                    RemoteAccount = remoteAccount,
                    RemotePassword = remotePassword,
                    RemotePath = remotePaths[i]
                });
            }
        }

        /// <summary>加载目录树</summary>
        private void LoadDirectoryTree(string rootPath)
        {
            if (TreeViewDirectory == null || !Directory.Exists(rootPath)) return;

            try
            {
                TreeViewDirectory.Items.Clear();
                var rootItem = new TreeViewItem
                {
                    Header = new DirectoryInfo(rootPath).Name,
                    Tag = rootPath,
                    IsExpanded = true
                };
                PopulateTreeView(rootItem, rootPath);
                TreeViewDirectory.Items.Add(rootItem);
            }
            catch (Exception ex)
            {
            }
        }

        /// <summary>递归填充子目录</summary>
        private void PopulateTreeView(TreeViewItem parentItem, string path)
        {
            try
            {
                foreach (var dir in Directory.GetDirectories(path))
                {
                    var dirInfo = new DirectoryInfo(dir);
                    var item = new TreeViewItem
                    {
                        Header = dirInfo.Name,
                        Tag = dir
                    };
                    // 如果有子目录则添加占位符（实现懒加载）
                    if (Directory.GetDirectories(dir).Length > 0)
                    {
                        item.Items.Add(null); // 占位
                        item.Expanded += TreeViewItem_Expanded;
                    }
                    parentItem.Items.Add(item);
                }
            }
            catch (UnauthorizedAccessException)
            {
                // 无权限访问时跳过
            }
        }

        /// <summary>子节点展开时动态加载</summary>
        private void TreeViewItem_Expanded(object sender, RoutedEventArgs e)
        {
            if (sender is TreeViewItem item && item.Items.Count == 1 && item.Items[0] == null)
            {
                item.Items.Clear();
                if (item.Tag is string path)
                {
                    PopulateTreeView(item, path);
                }
            }
        }

        /// <summary>展开所有节点</summary>
        private void ExpandAllTreeViewItems(ItemsControl itemsControl)
        {
            foreach (var item in itemsControl.Items)
            {
                if (item is TreeViewItem treeItem)
                {
                    treeItem.IsExpanded = true;
                    ExpandAllTreeViewItems(treeItem);
                }
            }
        }

        /// <summary>折叠所有节点</summary>
        private void CollapseAllTreeViewItems(ItemsControl itemsControl)
        {
            foreach (var item in itemsControl.Items)
            {
                if (item is TreeViewItem treeItem)
                {
                    treeItem.IsExpanded = false;
                    CollapseAllTreeViewItems(treeItem);
                }
            }
        }

        /// <summary>
        /// 非本机模式下：从参数设定表格匹配所选路径对应的账户密码，异步建立远程连接后加载目录树
        /// </summary>
        private async Task RemoteAuthenticateAndLoadAsync(string remotePath)
        {
            if (!_isInitialized) return;

            // 从远程路径中提取 IP/主机名（格式如 \\192.168.250.32\d\CCD程序）
            string ip = ExtractIpFromPath(remotePath);
            if (string.IsNullOrWhiteSpace(ip))
            {
                MessageBox.Show("无法从路径中解析出目标IP地址，请检查路径格式。", "路径解析失败",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 在参数设定表格中查找匹配该IP的行
            var matched = _paramConfigItems?
                .FirstOrDefault(p => !string.IsNullOrWhiteSpace(p.RemotePath)
                    && p.RemotePath.StartsWith($@"\\{ip}", StringComparison.OrdinalIgnoreCase));

            string account = matched?.RemoteAccount;
            string password = matched?.RemotePassword;

            if (string.IsNullOrWhiteSpace(account) || string.IsNullOrWhiteSpace(password))
            {
                // 没有账户密码也尝试直接访问
                // 异步执行 net use 连接到完整共享路径，带超时
                bool connected = await TryConnectNetworkPathAsync(remotePath, account, password);

                if (!connected)
                {
                    var result = MessageBox.Show(
                        $"使用配置的账户连接 {remotePath} 失败，请检查账户密码是否正确。\n\n是否继续尝试直接访问？",
                        "身份验证失败",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);

                    if (result != MessageBoxResult.Yes)
                    {
                        return;
                    }
                }
                else
                {
                }
            }

            // 尝试加载目录树
            try
            {
                // 检查远程路径是否可访问
                bool accessible = await Task.Run(() =>
                {
                    try
                    {
                        return Directory.Exists(remotePath);
                    }
                    catch
                    {
                        return false;
                    }
                });

                if (accessible)
                {
                    LoadDirectoryTree(remotePath);
                }
                else
                {
                    MessageBox.Show($"无法访问路径：{remotePath}\n请确认共享路径存在且账户有权限。", "访问失败",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"访问远程路径时出错：{ex.Message}", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>从远程路径中提取IP或主机名</summary>
        private static string ExtractIpFromPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            // 格式如 \\192.168.250.32\d\CCD程序 或 \\hostname\d\CCD程序
            if (path.StartsWith(@"\\"))
            {
                var parts = path.TrimStart('\\').Split('\\');
                if (parts.Length > 0)
                {
                    return parts[0];
                }
            }
            return null;
        }

        /// <summary>
        /// 异步执行 net use 连接远程共享路径，带超时判定（默认10秒）
        /// 直接连接到具体共享路径（如 \\192.168.250.31\d），而非仅连 ipc$，
        /// 避免因共享名权限不同导致 ipc$ 成功但具体共享访问失败的问题。
        /// </summary>
        private async Task<bool> TryConnectNetworkPathAsync(string fullRemotePath, string account, string password)
        {
            CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            try
            {
                return await Task.Run(() =>
                {
                    try
                    {
                        // 提取共享根路径（如 \\192.168.250.31\d）
                        string shareRoot = GetShareRoot(fullRemotePath);

                        // 先断开该共享的已有连接（忽略错误）
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = "net",
                            Arguments = $"use \"{shareRoot}\" /delete /y",
                            CreateNoWindow = true,
                            WindowStyle = ProcessWindowStyle.Hidden,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            UseShellExecute = false
                        })?.WaitForExit(2000);

                        // 建立新连接 - 直接连接到共享根路径
                        var psi = new ProcessStartInfo
                        {
                            FileName = "net",
                            Arguments = $"use \"{shareRoot}\" {password} /USER:{account}",
                            CreateNoWindow = true,
                            WindowStyle = ProcessWindowStyle.Hidden,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            UseShellExecute = false
                        };

                        var process = Process.Start(psi);
                        if (process == null) return false;

                        using (process)
                        {
                            // 等待进程退出（最多10秒）
                            bool exited = process.WaitForExit(10000);
                            if (!exited)
                            {
                                try { process.Kill(); } catch { }
                                return false;
                            }

                            string output = process.StandardOutput.ReadToEnd();
                            string error = process.StandardError.ReadToEnd();
                            string combined = output + error;

                            // net use 命令成功时 exit code 为 0
                            if (process.ExitCode == 0)
                            {
                                return true;
                            }

                            return false;
                        }
                    }
                    catch (Exception ex)
                    {
                        return false;
                    }
                }, cts.Token);
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (Exception ex)
            {
                return false;
            }
            finally
            {
                if (cts != null) cts.Dispose();
            }
        }

        /// <summary>从完整远程路径中提取共享根路径（如 \\192.168.250.31\d）</summary>
        private string GetShareRoot(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            if (path.StartsWith(@"\\"))
            {
                var parts = path.TrimStart('\\').Split('\\');
                // parts[0] = IP, parts[1] = 共享名，取前两级
                if (parts.Length >= 2)
                {
                    return $@"\\{parts[0]}\{parts[1]}";
                }
            }
            return null;
        }

        // ==================== 智能操作 ====================

        /// <summary>新版本统一存放路径</summary>
        private string _smartSourcePath;
        /// <summary>是否为本地测试模式</summary>
        private bool _isSmartLocalMode;
        /// <summary>智能扫描结果</summary>
        private ObservableCollection<SmartScanItem> _smartScanItems;
        /// <summary>监听模式 - 文件监视器</summary>
        private FileSystemWatcher _smartWatcher;
        /// <summary>监听模式 - 防抖定时器</summary>
        private System.Timers.Timer _watchDebounceTimer;
        /// <summary>监听模式 - 轮询后备定时器（解决 FileSystemWatcher 缓冲区溢出不通知的问题）</summary>
        private System.Timers.Timer _watchPollTimer;
        /// <summary>监听模式是否已启用</summary>
        private bool _isSmartWatching;
        /// <summary>轮询后备 - 上一次扫描到的 EXE 数量（用于检测是否有新增）</summary>
        private int _lastPollExeCount = -1;
        /// <summary>更新日志内存缓存</summary>
        private System.Text.StringBuilder _smartLogBuilder = new System.Text.StringBuilder();
        /// <summary>更新日志文件目录</summary>
        private const int MAX_LOG_LINES = 200;

        /// <summary>写入更新日志（文件 + 内存 + UI）</summary>
        private void AppendSmartLog(string message)
        {
            if (!_isInitialized) return;
            var time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            string logLine = $"[{time}] {message}";

            // 写文件
            try
            {
                string logDir = _logDirectoryPath ?? DefaultLogDir;
                Directory.CreateDirectory(logDir);
                System.IO.File.AppendAllText(System.IO.Path.Combine(logDir, "Update.log"), logLine + Environment.NewLine);
            }
            catch { }

            // 写内存
            lock (_smartLogBuilder)
            {
                _smartLogBuilder.AppendLine(logLine);
                if (_smartLogBuilder.Length > MAX_LOG_LINES * 80)
                {
                    var lines = _smartLogBuilder.ToString().Split(new[] { Environment.NewLine }, StringSplitOptions.None);
                    _smartLogBuilder.Clear();
                    for (int i = Math.Max(0, lines.Length - MAX_LOG_LINES); i < lines.Length; i++)
                        _smartLogBuilder.AppendLine(lines[i]);
                }
            }

            // 更新UI
            if (TxtSmartLog != null)
                TxtSmartLog.Text = _smartLogBuilder.ToString();
        }

        /// <summary>浏览新版本统一存放路径</summary>
        private void BtnSmartBrowseSource_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            var dialog = new System.Windows.Forms.FolderBrowserDialog();
            if (!string.IsNullOrWhiteSpace(TxtSmartSourcePath?.Text) && Directory.Exists(TxtSmartSourcePath.Text))
                dialog.SelectedPath = TxtSmartSourcePath.Text;
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                TxtSmartSourcePath.Text = dialog.SelectedPath;
                _smartSourcePath = dialog.SelectedPath;
                TxtStatusBar.Text = $"新版本源路径已设置: {_smartSourcePath}";
            }
        }

        /// <summary>扫描模式切换：远程 / 本机测试</summary>
        private void RbtnSmartScanMode_Checked(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            _isSmartLocalMode = RbtnSmartLocalMode.IsChecked == true;

            if (TxtSmartLocalRoot != null)
                TxtSmartLocalRoot.IsEnabled = _isSmartLocalMode;
            if (BtnSmartBrowseLocalRoot != null)
                BtnSmartBrowseLocalRoot.IsEnabled = _isSmartLocalMode;
        }

        /// <summary>浏览本机测试根目录</summary>
        private void BtnSmartBrowseLocalRoot_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            var dialog = new System.Windows.Forms.FolderBrowserDialog();
            if (!string.IsNullOrWhiteSpace(TxtSmartLocalRoot?.Text) && Directory.Exists(TxtSmartLocalRoot.Text))
                dialog.SelectedPath = TxtSmartLocalRoot.Text;
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                TxtSmartLocalRoot.Text = dialog.SelectedPath;
                TxtStatusBar.Text = $"本机测试根目录已设置: {dialog.SelectedPath}";
            }
        }

        // ==================== 监听模式 ====================

        /// <summary>启用监听</summary>
        private void TogSmartWatch_Checked(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;

            _smartSourcePath = TxtSmartSourcePath?.Text?.Trim();
            if (string.IsNullOrWhiteSpace(_smartSourcePath) || !Directory.Exists(_smartSourcePath))
            {
                MessageBox.Show("请先设置有效的新版本统一存放路径。", "路径未设置",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                TogSmartWatch.IsChecked = false;
                return;
            }

            try
            {
                _smartWatcher = new FileSystemWatcher(_smartSourcePath)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime,
                    Filter = "*.exe",
                    InternalBufferSize = 65536 // 64KB，避免复制大量文件时缓冲区溢出导致静默丢失事件
                };

                _smartWatcher.Created += SmartWatcher_OnChanged;
                _smartWatcher.Changed += SmartWatcher_OnChanged;
                _smartWatcher.Error += SmartWatcher_OnError; // 订阅错误事件，检测缓冲区溢出
                _smartWatcher.EnableRaisingEvents = true;

                _watchDebounceTimer = new System.Timers.Timer(3000); // 3秒防抖
                _watchDebounceTimer.AutoReset = false;
                _watchDebounceTimer.Elapsed += SmartWatchDebounce_Elapsed;

                // 轮询后备定时器：每10秒检查一次源目录是否有新文件
                // 解决 FileSystemWatcher 在大量文件操作时可能丢事件的问题
                _watchPollTimer = new System.Timers.Timer(10000);
                _watchPollTimer.AutoReset = true;
                _watchPollTimer.Elapsed += SmartWatchPoll_Elapsed;
                _watchPollTimer.Start();

                _isSmartWatching = true;
                TxtSmartWatchStatus.Text = "监听中 ?（等待新EXE文件...）";
                TxtSmartWatchStatus.Foreground = Brushes.LimeGreen;
                TxtStatusBar.Text = "监听模式已启动";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"启动监听失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                TogSmartWatch.IsChecked = false;
            }
        }

        /// <summary>关闭监听</summary>
        private void TogSmartWatch_Unchecked(object sender, RoutedEventArgs e)
        {
            _isSmartWatching = false;

            if (_smartWatcher != null)
            {
                _smartWatcher.EnableRaisingEvents = false;
                _smartWatcher.Dispose();
                _smartWatcher = null;
            }

            if (_watchDebounceTimer != null)
            {
                _watchDebounceTimer.Stop();
                _watchDebounceTimer.Dispose();
                _watchDebounceTimer = null;
            }

            if (_watchPollTimer != null)
            {
                _watchPollTimer.Stop();
                _watchPollTimer.Dispose();
                _watchPollTimer = null;
            }

            _lastPollExeCount = -1; // 重置轮询计数

            TxtSmartWatchStatus.Text = "监听已关闭";
            TxtSmartWatchStatus.Foreground = Brushes.Gray;
            TxtStatusBar.Text = "监听模式已关闭";
        }

        /// <summary>文件变化触发 - 重置防抖</summary>
        private void SmartWatcher_OnChanged(object sender, FileSystemEventArgs e)
        {
            if (!_isSmartWatching) return;
            _watchDebounceTimer?.Stop();
            _watchDebounceTimer?.Start();
        }

        /// <summary>FileSystemWatcher 错误事件处理（如缓冲区溢出）</summary>
        private void SmartWatcher_OnError(object sender, ErrorEventArgs e)
        {
            if (!_isSmartWatching) return;
            var ex = e.GetException();
            AppendSmartLog($"FileSystemWatcher 内部错误: {ex?.Message} (自动启用轮询后备)");
        }

        /// <summary>轮询后备定时器 - 定时检查源目录是否有新增 EXE 文件</summary>
        private void SmartWatchPoll_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            if (!_isSmartWatching) return;

            try
            {
                int currentCount = Directory.GetFiles(_smartSourcePath, "*.exe", SearchOption.AllDirectories).Length;
                if (_lastPollExeCount >= 0 && currentCount > _lastPollExeCount)
                {
                    // 检测到新增文件，触发防抖
                    _watchDebounceTimer?.Stop();
                    _watchDebounceTimer?.Start();
                }
                _lastPollExeCount = currentCount;
            }
            catch
            {
                // 轮询异常不做处理，下次继续
            }
        }

        /// <summary>防抖结束 - 执行自动扫描和更新</summary>
        private async void SmartWatchDebounce_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            if (!_isSmartWatching) return;

            await Dispatcher.BeginInvoke(new Action(async () =>
            {
                TxtSmartWatchStatus.Text = "检测到新文件，正在自动扫描...";
                TxtStatusBar.Text = "监听模式 - 自动扫描中...";

                // 获取当前模式
                string localRoot = null;
                if (RbtnSmartLocalMode.IsChecked == true)
                {
                    localRoot = TxtSmartLocalRoot?.Text?.Trim();
                    if (string.IsNullOrWhiteSpace(localRoot) || !Directory.Exists(localRoot))
                        localRoot = null;
                }

                // 获取设备号前缀（与一键扫描一致）
                bool usePrefix = ChkUseDevicePrefix?.IsChecked == true;
                string watchDevicePrefix = usePrefix ? TxtDeviceNumber?.Text?.Trim() + "#" : null;
                if (usePrefix && string.IsNullOrWhiteSpace(TxtDeviceNumber?.Text?.Trim()))
                    watchDevicePrefix = null;

                // 执行扫描（传入设备号前缀）
                var results = await Task.Run(() => SmartScanAllStations(localRoot, watchDevicePrefix));
                _smartScanItems = new ObservableCollection<SmartScanItem>(results);
                DataGridSmartScan.ItemsSource = _smartScanItems;

                var canUpdateItems = results.Where(r => r.CanUpdate).ToList();
                AppendSmartLog($"自动扫描: 共 {results.Count} 个工位");
                if (canUpdateItems.Count == 0)
                {
                    AppendSmartLog("全部已是最新，无需更新");
                    TxtSmartWatchStatus.Text = "监听中 ?（已是最新，无更新需要）";
                    TxtSmartSummary.Text = $"自动扫描完成，共 {results.Count} 个工位，全部已是最新";
                    TxtStatusBar.Text = "监听模式 - 扫描完成，无更新";
                    return;
                }

                AppendSmartLog($"发现 {canUpdateItems.Count} 个可更新工位，开始自动更新");
                TxtSmartWatchStatus.Text = $"发现 {canUpdateItems.Count} 个可更新工位，自动执行更新...";

                // 自动执行更新
                int successCount = 0, failCount = 0;
                var errors = new List<string>();
                await Task.Run(() =>
                {
                    foreach (var item in canUpdateItems)
                    {
                        try
                        {
                            SmartUpdateStation(item, out string errMsg);
                            if (errMsg == null)
                            {
                                System.Threading.Interlocked.Increment(ref successCount);
                                item.StatusText = "更新成功";
                                item.StatusColor = Brushes.LimeGreen;
                            }
                            else
                            {
                                lock (errors) errors.Add($"[{item.StationName}] {errMsg}");
                                System.Threading.Interlocked.Increment(ref failCount);
                                item.StatusText = "更新失败";
                                item.StatusColor = Brushes.Red;
                            }
                        }
                        catch (Exception ex)
                        {
                            lock (errors) errors.Add($"[{item.StationName}] {ex.Message}");
                            System.Threading.Interlocked.Increment(ref failCount);
                            item.StatusText = "更新失败";
                            item.StatusColor = Brushes.Red;
                        }
                    }
                });

                DataGridSmartScan.Items.Refresh();

                // 根据复选框创建快捷方式
                if (ChkSmartAutoShortcut?.IsChecked == true)
                {
                    foreach (var item in canUpdateItems)
                    {
                        if (item.StatusText == "更新成功" || item.StatusText == "success")
                        {
                            CreateShortcutForUpdatedItem(item);
                        }
                    }
                }

                // 写入更新日志
                AppendSmartLog($"===== 监听自动更新 [{DateTime.Now:yyyy-MM-dd HH:mm:ss}] =====");
                AppendSmartLog($"扫描工位: {results.Count} 个，可更新: {canUpdateItems.Count} 个");
                foreach (var item in canUpdateItems)
                    AppendSmartLog($"  {item.StationName}: V{item.CurrentVersion} → V{item.NewVersion}");

                if (successCount > 0)
                    AppendSmartLog($"更新成功: {successCount} 个工位");
                foreach (var err in errors.Take(10))
                    AppendSmartLog($"更新失败: {err}");

                AppendSmartLog($"===== 结束 =====");

                // 显示结果
                TxtSmartSummary.Text = $"监听自动更新: 成功 {successCount} / {canUpdateItems.Count} 个工位";
                TxtSmartWatchStatus.Text = $"监听中 ?（上次更新: {DateTime.Now:HH:mm}）";
                TxtStatusBar.Text = $"监听模式 - 自动更新完成：成功 {successCount}，失败 {failCount}";

                BtnSmartUpdateAll.IsEnabled = results.Any(r => r.CanUpdate);
            }));
        }

        /// <summary>一键扫描所有工位</summary>
        private async void BtnSmartScanAll_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            if (_paramConfigItems == null || _paramConfigItems.Count == 0)
            {
                MessageBox.Show("参数设定表格中无配置数据，请先在「参数设定」页面添加工位配置。", "提示",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _smartSourcePath = TxtSmartSourcePath?.Text?.Trim();
            if (string.IsNullOrWhiteSpace(_smartSourcePath) || !Directory.Exists(_smartSourcePath))
            {
                MessageBox.Show("请先设置有效的新版本统一存放路径（本机目录）。\n将新版EXE文件放入此目录，系统自动匹配各工位。", "源路径未设置",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 本机测试模式：校验本地根目录
            _isSmartLocalMode = RbtnSmartLocalMode.IsChecked == true;
            string localRoot = null;
            if (_isSmartLocalMode)
            {
                localRoot = TxtSmartLocalRoot?.Text?.Trim();
                if (string.IsNullOrWhiteSpace(localRoot) || !Directory.Exists(localRoot))
                {
                    MessageBox.Show("本机测试模式已勾选，请设置有效的本机测试根目录。\n\n系统将用此目录替代参数配置表中的远程路径。\n例如：目录下应有「阳极瑕疵」「阴极尺寸」等工位子文件夹。", "本地目录未设置",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            BtnSmartScanAll.IsEnabled = false;
            TxtStatusBar.Text = "正在扫描所有工位...";
            TxtSmartSummary.Text = "正在扫描源路径...";

            string capturedLocalRoot = localRoot;
            bool usePrefix = ChkUseDevicePrefix.IsChecked == true;
            string devicePrefix = usePrefix ? TxtDeviceNumber.Text.Trim() + "#" : null;
            if (usePrefix && string.IsNullOrWhiteSpace(TxtDeviceNumber.Text.Trim()))
            {
                devicePrefix = null; // 设备号为空时视为未启用
            }
            string capturedPrefix = devicePrefix;
            try
            {
                var results = await Task.Run(() => SmartScanAllStations(capturedLocalRoot, capturedPrefix));

                _smartScanItems = new ObservableCollection<SmartScanItem>(results);
                DataGridSmartScan.ItemsSource = _smartScanItems;

                int canUpdateCount = results.Count(r => r.CanUpdate);
                int totalCount = results.Count;
                int unreachableCount = results.Count(r => r.StatusText == "不可达" || r.StatusText == "超时");
                TxtSmartSummary.Text = $"扫描完成：共 {totalCount} 个工位，可更新 {canUpdateCount} 个，不可达 {unreachableCount} 个";
                TxtStatusBar.Text = TxtSmartSummary.Text;

                BtnSmartUpdateAll.IsEnabled = canUpdateCount > 0;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"扫描过程出错: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                TxtSmartSummary.Text = $"扫描出错: {ex.Message}";
            }
            finally
            {
                BtnSmartScanAll.IsEnabled = true;
            }
        }

        /// <summary>并发扫描所有工位，每个工位带超时控制</summary>
        /// <param name="localRoot">本机测试根目录（null=远程模式）</param>
        private List<SmartScanItem> SmartScanAllStations(string localRoot = null, string devicePrefix = null)
        {
            // 1. 预先扫描本地源路径（递归子目录），按「工位文件夹」分组只保留最新版本
            // sourceInfo: exeName → list of (full path, version, 工位文件夹名)
            // 工位文件夹名为 EXE 所在文件夹的上一级（如「阳极尺寸 - 更新设备版本」），用于阴阳匹配
            var sourceInfo = new Dictionary<string, List<(string ExePath, string Version, string StationFolder)>>(StringComparer.OrdinalIgnoreCase);
            try
            {
                // 临时分组：(exeName, 工位文件夹) → (exePath, version)，同组只保留版本最高的
                var grouped = new Dictionary<string, Dictionary<string, (string ExePath, string Version)>>(StringComparer.OrdinalIgnoreCase);

                foreach (var exe in Directory.GetFiles(_smartSourcePath, "*.exe", SearchOption.AllDirectories))
                {
                    string name = System.IO.Path.GetFileName(exe);
                    string exeDir = System.IO.Path.GetDirectoryName(exe);
                    // 上一级目录名 = 工位文件夹（如「瑕疵 - 更新设备版本」）
                    string stationFolder = new DirectoryInfo(exeDir).Parent?.Name ?? "";
                    string version = "";
                    try
                    {
                        var vi = FileVersionInfo.GetVersionInfo(exe);
                        version = vi.FileVersion ?? "未知";
                    }
                    catch { version = "未知"; }

                    if (!grouped.ContainsKey(name))
                        grouped[name] = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);

                    // 同工位文件夹下，同名EXE有多个版本子文件夹，只保留文件版本最高的
                    if (!grouped[name].ContainsKey(stationFolder))
                    {
                        grouped[name][stationFolder] = (exe, version);
                    }
                    else
                    {
                        var existing = grouped[name][stationFolder];
                        if (Version.TryParse(version, out var newVer) &&
                            Version.TryParse(existing.Version, out var oldVer) &&
                            newVer > oldVer)
                        {
                            grouped[name][stationFolder] = (exe, version);
                        }
                    }
                }

                // 扁平化到 sourceInfo
                foreach (var exeKv in grouped)
                {
                    var list = new List<(string, string, string)>();
                    foreach (var stKv in exeKv.Value)
                        list.Add((stKv.Value.ExePath, stKv.Value.Version, stKv.Key));
                    sourceInfo[exeKv.Key] = list;
                }
            }
            catch { }

            // 远程模式：过滤掉无远程路径的配置；本地模式：用 StationFolderPattern 过滤
            var configs = _paramConfigItems
                .Where(c => !string.IsNullOrWhiteSpace(c.ExeNamePattern)
                    && (localRoot != null || !string.IsNullOrWhiteSpace(c.RemotePath)))
                .ToList();

            if (configs.Count == 0) return new List<SmartScanItem>();

            var results = new List<SmartScanItem>();
            object lockObj = new object();

            // 2. 并发扫描每个工位
            int maxDop = localRoot != null ? 8 : 4; // 本地模式可更高并发
            System.Threading.Tasks.Parallel.ForEach(configs,
                new ParallelOptions { MaxDegreeOfParallelism = maxDop }, config =>
            {
                var item = SmartScanOneStation(config, sourceInfo, localRoot, devicePrefix);
                if (item != null)
                {
                    lock (lockObj) results.Add(item);
                }
            });

            return results;
        }

        /// <summary>快速检测路径是否可达（Ping + 短超时）</summary>
        private static bool QuickCheckPathAccessible(string path)
        {
            // 本机路径直接返回 true
            if (!path.StartsWith(@"\\")) return true;

            try
            {
                // 从 UNC 路径中提取 IP/主机名
                string host = path.TrimStart('\\').Split('\\')[0];
                using (var ping = new System.Net.NetworkInformation.Ping())
                {
                    var reply = ping.Send(host, 1500); // 1.5 秒超时
                    if (reply == null || reply.Status != System.Net.NetworkInformation.IPStatus.Success)
                        return false;
                }
                // Ping 通后再检查目录（短超时）
                var checkTask = Task.Run(() => Directory.Exists(path));
                return checkTask.Wait(2000) && checkTask.Result;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>扫描单个工位（带超时控制，不阻塞线程池）</summary>
        /// <param name="localRoot">本机测试根目录（null=远程模式）</param>
        private static SmartScanItem SmartScanOneStation(ParamConfigItem config, Dictionary<string, List<(string ExePath, string Version, string StationFolder)>> sourceInfo, string localRoot = null, string devicePrefix = null)
        {
            // 如果启用了设备号前缀，在工位名称前加上设备号前缀（以匹配创建时加前缀的目录）
            string effectiveStationPattern = !string.IsNullOrWhiteSpace(devicePrefix)
                ? devicePrefix + config.StationFolderPattern
                : config.StationFolderPattern;

            string basePath = localRoot ?? config.RemotePath;
            string stationPath = !string.IsNullOrWhiteSpace(effectiveStationPattern)
                ? System.IO.Path.Combine(basePath, effectiveStationPattern)
                : basePath;

            string currentVersion = "";
            string statusText = "";
            Brush statusColor = Brushes.Gray;
            bool isAccessible = false;

            bool isLocalMode = localRoot != null;

            // 本机模式：直接检查目录是否存在；远程模式：快速可达性检测 + 自动认证
            if (isLocalMode)
            {
                isAccessible = Directory.Exists(stationPath);
                if (!isAccessible)
                {
                    statusText = "本地目录不存在";
                    statusColor = Brushes.Red;
                }
            }
            else
            {
                // 先快速检测是否已可达
                isAccessible = QuickCheckPathAccessible(stationPath);

                // 不可达但有账户密码配置时尝试自动认证
                if (!isAccessible && !string.IsNullOrWhiteSpace(config.RemoteAccount))
                {
                    string ip = ExtractIpFromPath(stationPath);
                    if (!string.IsNullOrWhiteSpace(ip))
                    {
                        TryAuthenticateRemote(ip, config.RemoteAccount, config.RemotePassword, out string authMsg);
                        // 认证后再检查一次
                        isAccessible = QuickCheckPathAccessible(stationPath);
                    }
                }

                if (!isAccessible)
                {
                    statusText = "不可达";
                    statusColor = Brushes.Red;
                }
            }

            // 用于记录扫描到的EXE完整路径
            string foundExePath = null;

            if (isAccessible)
            {
                try
                {
                    string foundVersion = "";
                    if (isLocalMode)
                    {
                        // 本机模式：直接同步扫（递归子目录），取最新版本
                        string latestExe = null;
                        Version latestVer = null;
                        foreach (var exeFile in Directory.GetFiles(stationPath, "*.exe", SearchOption.AllDirectories))
                        {
                            if (string.Equals(System.IO.Path.GetFileName(exeFile), config.ExeNamePattern, StringComparison.OrdinalIgnoreCase))
                            {
                                if (latestExe == null) { latestExe = exeFile; continue; }
                                try
                                {
                                    var vi = FileVersionInfo.GetVersionInfo(exeFile);
                                    if (Version.TryParse(vi.FileVersion, out var ver) && (latestVer == null || ver > latestVer))
                                    {
                                        latestVer = ver;
                                        latestExe = exeFile;
                                    }
                                }
                                catch { }
                            }
                        }
                        if (latestExe != null)
                        {
                            foundExePath = latestExe;
                            try
                            {
                                var vi = FileVersionInfo.GetVersionInfo(latestExe);
                                foundVersion = vi.FileVersion ?? "未知";
                            }
                            catch { foundVersion = "未知"; }
                        }
                    }
                    else
                    {
                        // 远程模式：用 Task.Run + 超时（递归子目录），取最新版本
                        var scanTask = Task.Run(() =>
                        {
                            string best = "";
                            Version bestVer = null;
                            foreach (var exeFile in Directory.GetFiles(stationPath, "*.exe", SearchOption.AllDirectories))
                            {
                                if (string.Equals(System.IO.Path.GetFileName(exeFile), config.ExeNamePattern, StringComparison.OrdinalIgnoreCase))
                                {
                                    if (string.IsNullOrEmpty(best)) { best = exeFile; continue; }
                                    try
                                    {
                                        var vi = FileVersionInfo.GetVersionInfo(exeFile);
                                        if (Version.TryParse(vi.FileVersion, out var ver) && (bestVer == null || ver > bestVer))
                                        {
                                            bestVer = ver;
                                            best = exeFile;
                                        }
                                    }
                                    catch { }
                                }
                            }
                            return best; // 返回EXE完整路径，空串表示未找到
                        });

                        if (scanTask.Wait(5000))
                        {
                            string best = scanTask.Result;
                            if (!string.IsNullOrEmpty(best))
                            {
                                foundExePath = best;
                                try
                                {
                                    var vi = FileVersionInfo.GetVersionInfo(best);
                                    foundVersion = vi.FileVersion ?? "未知";
                                }
                                catch { foundVersion = "未知"; }
                            }
                        }
                        else
                        {
                            statusText = "超时";
                            statusColor = Brushes.Red;
                            isAccessible = false;
                        }
                    }
                    currentVersion = foundVersion;
                }
                catch
                {
                    statusText = "不可达";
                    statusColor = Brushes.Red;
                }
            }

            // 从源路径获取新版本（考虑阴阳匹配），同时记录具体源文件夹路径
            string newVersion = null;
            string sourceExeFolder = null; // 匹配到的源EXE所在文件夹，用于更新时只复制该文件夹内容
            if (sourceInfo.TryGetValue(config.ExeNamePattern, out var candidates))
            {
                // 按优先级匹配：阴阳精确 → 中性 → 任意第一个
                bool stationIsYang = config.StationFolderPattern.Contains("阳极");
                bool stationIsYin = config.StationFolderPattern.Contains("阴极");

                (string ExePath, string Version, string StationFolder) best = default;

                // 优先级1: 阴阳精确匹配
                foreach (var c in candidates)
                {
                    bool srcIsYang = c.StationFolder.Contains("阳极") || c.StationFolder.Contains("Yang") || c.StationFolder.Contains("yang");
                    bool srcIsYin = c.StationFolder.Contains("阴极") || c.StationFolder.Contains("Yin") || c.StationFolder.Contains("yin");
                    if ((stationIsYang && srcIsYang && !srcIsYin) ||
                        (stationIsYin && srcIsYin && !srcIsYang))
                    {
                        best = c;
                        break;
                    }
                }

                // 优先级2: 中性源
                if (best.ExePath == null)
                {
                    foreach (var c in candidates)
                    {
                        bool srcIsYang = c.StationFolder.Contains("阳极") || c.StationFolder.Contains("Yang") || c.StationFolder.Contains("yang");
                        bool srcIsYin = c.StationFolder.Contains("阴极") || c.StationFolder.Contains("Yin") || c.StationFolder.Contains("yin");
                        if (!srcIsYang && !srcIsYin) { best = c; break; }
                    }
                }

                // 优先级3: 取第一个
                if (best.ExePath == null) best = candidates[0];

                newVersion = best.Version;
                sourceExeFolder = System.IO.Path.GetDirectoryName(best.ExePath);
            }

            string exeName = config.ExeNamePattern;
            bool canUpdate = false;
            string compareText = "";
            Brush compareColor = Brushes.Gray;

            if (!isAccessible && string.IsNullOrWhiteSpace(statusText))
                statusText = "不可达";
            else if (string.IsNullOrWhiteSpace(currentVersion))
            {
                statusText = "不可达";
                statusColor = Brushes.Red;
                compareText = "-";
            }
            else if (string.IsNullOrWhiteSpace(newVersion))
            {
                statusText = "未找到新版本";
                statusColor = Brushes.Gray;
                compareText = $"V{currentVersion} → ?";
                compareColor = Brushes.Gray;
            }
            else
            {
                compareText = $"V{currentVersion} → V{newVersion}";
                try
                {
                    var v1 = new Version(currentVersion);
                    var v2 = new Version(newVersion);
                    if (v2 > v1)
                    {
                        compareColor = Brushes.LimeGreen;
                        statusText = "可更新";
                        statusColor = Brushes.Orange;
                        canUpdate = true;
                    }
                    else if (v2 < v1)
                    {
                        compareColor = Brushes.Red;
                        statusText = "源版本更低";
                        statusColor = Brushes.Red;
                    }
                    else
                    {
                        compareColor = Brushes.Gray;
                        statusText = "已是最新";
                        statusColor = Brushes.LimeGreen;
                    }
                }
                catch
                {
                    compareColor = Brushes.Orange;
                    statusText = "版本格式异常";
                    statusColor = Brushes.Orange;
                }
            }

            return new SmartScanItem
            {
                StationName = config.StationFolderPattern,
                ExeName = exeName,
                CurrentVersion = currentVersion,
                NewVersion = newVersion ?? "",
                RemotePath = stationPath,
                SourcePath = sourceExeFolder,
                CurrentExePath = foundExePath,
                CompareText = compareText,
                CompareColor = compareColor,
                StatusText = statusText,
                StatusColor = statusColor,
                CanUpdate = canUpdate,
                IsSelected = canUpdate,
                MatchedConfig = config
            };
        }

        /// <summary>一键更新所有过期工位</summary>
        private async void BtnSmartUpdateAll_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            if (_smartScanItems == null || _smartScanItems.Count == 0) return;

            var selectedItems = _smartScanItems.Where(i => i.IsSelected && i.CanUpdate).ToList();
            if (selectedItems.Count == 0)
            {
                MessageBox.Show("没有可更新的工位（请先执行一键扫描）。", "提示",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show(
                $"即将更新 {selectedItems.Count} 个工位：\n{string.Join("\n", selectedItems.Select(i => $"  {i.StationName}: V{i.CurrentVersion} → V{i.NewVersion}"))}\n\n" +
                "操作流程：\n1. 复制当前程序文件夹 → 生成 -副本\n" +
                "2. 将新版本源路径内的文件复制到 -副本 覆盖\n" +
                "3. 将 -副本 重命名为新版本文件夹名\n\n确认执行？",
                "一键更新确认", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes) return;

            BtnSmartUpdateAll.IsEnabled = false;
            TxtStatusBar.Text = "正在更新...";

            int successCount = 0, failCount = 0;
            var errors = new List<string>();

            await Task.Run(() =>
            {
                foreach (var item in selectedItems)
                {
                    try
                    {
                        SmartUpdateStation(item, out string errMsg);
                        if (errMsg == null)
                        {
                            System.Threading.Interlocked.Increment(ref successCount);
                            item.StatusText = "更新成功";
                            item.StatusColor = Brushes.LimeGreen;
                        }
                        else
                        {
                            lock (errors) errors.Add($"[{item.StationName}] {errMsg}");
                            System.Threading.Interlocked.Increment(ref failCount);
                            item.StatusText = "更新失败";
                            item.StatusColor = Brushes.Red;
                        }
                    }
                    catch (Exception ex)
                    {
                        lock (errors) errors.Add($"[{item.StationName}] {ex.Message}");
                        System.Threading.Interlocked.Increment(ref failCount);
                        item.StatusText = "更新失败";
                        item.StatusColor = Brushes.Red;
                    }
                }
            });

            // 根据复选框创建快捷方式
            if (ChkSmartAutoShortcut?.IsChecked == true)
            {
                foreach (var item in selectedItems)
                {
                    if (item.StatusText == "更新成功" || item.StatusText == "success")
                    {
                        CreateShortcutForUpdatedItem(item);
                    }
                }
            }

            // 刷新UI
            DataGridSmartScan.Items.Refresh();

            // 写入更新日志
            AppendSmartLog($"===== 手动更新 [{DateTime.Now:yyyy-MM-dd HH:mm:ss}] =====");
            foreach (var item in selectedItems)
                AppendSmartLog($"  {item.StationName}: V{item.CurrentVersion} → V{item.NewVersion}");
            if (successCount > 0)
                AppendSmartLog($"更新成功: {successCount} 个工位");
            foreach (var err in errors.Take(10))
                AppendSmartLog($"更新失败: {err}");
            AppendSmartLog($"===== 结束 =====");

            string summary = $"更新完成：成功 {successCount} / 共 {selectedItems.Count} 个工位";
            if (errors.Count > 0)
                summary += $"\n失败详情：\n{string.Join("\n", errors.Take(5))}";
            TxtSmartSummary.Text = summary;
            TxtStatusBar.Text = $"更新完成：成功 {successCount}，失败 {failCount}";

            MessageBox.Show(summary, "更新结果", MessageBoxButton.OK,
                failCount > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);

            BtnSmartUpdateAll.IsEnabled = true;
        }

        /// <summary>带重试的文件操作（解决文件被临时占用问题）</summary>
        private static void RetryFileOp(Action action, int maxRetries = 3, int delayMs = 500)
        {
            int retry = 0;
            while (true)
            {
                try
                {
                    action();
                    return;
                }
                catch when (++retry <= maxRetries)
                {
                    System.Threading.Thread.Sleep(delayMs);
                }
            }
        }

        /// <summary>更新单个工位</summary>
        private void SmartUpdateStation(SmartScanItem item, out string errorMsg)
        {
            errorMsg = null;
            try
            {
                // 检查源文件夹路径（从扫描时已匹配好）
                if (string.IsNullOrWhiteSpace(item.SourcePath) || !Directory.Exists(item.SourcePath))
                {
                    errorMsg = $"新版本源文件夹不存在: {item.SourcePath}";
                    return;
                }

                // 递归查找工位内最新版本的EXE（与扫描逻辑一致）
                string exePath = null;
                Version bestVer = null;
                foreach (var exeFile in Directory.GetFiles(item.RemotePath, "*.exe", SearchOption.AllDirectories))
                {
                    if (string.Equals(System.IO.Path.GetFileName(exeFile), item.ExeName, StringComparison.OrdinalIgnoreCase))
                    {
                        if (exePath == null) { exePath = exeFile; continue; }
                        try
                        {
                            var vi = FileVersionInfo.GetVersionInfo(exeFile);
                            if (Version.TryParse(vi.FileVersion, out var ver) && (bestVer == null || ver > bestVer))
                            {
                                bestVer = ver;
                                exePath = exeFile;
                            }
                        }
                        catch { }
                    }
                }

                if (exePath == null)
                {
                    errorMsg = "未找到匹配的EXE文件";
                    return;
                }

                string folderAPath = System.IO.Path.GetDirectoryName(exePath);
                if (string.IsNullOrWhiteSpace(folderAPath) || !Directory.Exists(folderAPath))
                {
                    errorMsg = $"程序文件夹不存在: {folderAPath}";
                    return;
                }

                string parentDir = System.IO.Path.GetDirectoryName(folderAPath);
                string folderAName = new DirectoryInfo(folderAPath).Name;

                // 计算新文件夹名
                string newFolderName = item.MatchedConfig?.FolderNamingRule?.Replace("{版本号}", item.NewVersion);
                if (string.IsNullOrWhiteSpace(newFolderName))
                {
                    errorMsg = "无法计算新文件夹名称";
                    return;
                }

                string newFolderPath = System.IO.Path.Combine(parentDir, newFolderName);

                // 如果源文件夹和目标文件夹相同 → 直接原地覆盖新版本文件即可
                if (string.Equals(folderAPath, newFolderPath, StringComparison.OrdinalIgnoreCase))
                {
                    RetryFileOp(() => CopyDirectoryWithOverwrite(item.SourcePath, folderAPath));
                    return;
                }

                // 用 GUID 作为临时文件夹名，杜绝任何命名冲突
                string tempFolderName = $"{Guid.NewGuid():N}";
                string tempFolderPath = System.IO.Path.Combine(parentDir, tempFolderName);

                // 复制当前程序文件夹到临时文件夹（带重试）
                RetryFileOp(() => CopyDirectory(folderAPath, tempFolderPath));

                // 将新版本源文件复制到临时文件夹覆盖（带重试）
                RetryFileOp(() => CopyDirectoryWithOverwrite(item.SourcePath, tempFolderPath));

                // 删除已存在的目标文件夹（带重试）
                if (Directory.Exists(newFolderPath))
                    RetryFileOp(() => Directory.Delete(newFolderPath, true));

                // 重命名临时文件夹为目标名（带重试）
                RetryFileOp(() => Directory.Move(tempFolderPath, newFolderPath));
            }
            catch (Exception ex)
            {
                errorMsg = ex.Message;
            }
        }

        /// <summary>为更新成功的工位在目标位置创建快捷方式</summary>
        private void CreateShortcutForUpdatedItem(SmartScanItem item)
        {
            try
            {
                if (item.MatchedConfig == null) return;

                // 根据 FolderNamingRule 算出新文件夹名，只找更新后的那个文件夹中的 EXE
                string newFolderName = item.MatchedConfig.FolderNamingRule?.Replace("{版本号}", item.NewVersion);
                if (string.IsNullOrWhiteSpace(newFolderName) || string.IsNullOrWhiteSpace(item.RemotePath)) return;

                string newFolderPath = System.IO.Path.Combine(item.RemotePath, newFolderName);
                if (!Directory.Exists(newFolderPath)) return;

                string exePath = Directory.GetFiles(newFolderPath, item.ExeName, SearchOption.TopDirectoryOnly)
                    .FirstOrDefault();
                if (exePath == null) return;

                // 快捷方式命名：EXE程序名 - V版本号（从文件属性读取）
                string exeNameOnly = System.IO.Path.GetFileNameWithoutExtension(exePath);
                string fileVersion = item.NewVersion;
                // 如果 NewVersion 为空，尝试从文件属性读取
                if (string.IsNullOrWhiteSpace(fileVersion))
                {
                    try
                    {
                        var vi = System.Diagnostics.FileVersionInfo.GetVersionInfo(exePath);
                        fileVersion = vi.FileVersion;
                    }
                    catch { }
                }
                string shortcutName = $"{exeNameOnly} - V{fileVersion}";

                // 确定快捷方式存放目录：优先用参数配置中的「发送快捷方式地址」
                string destDir = item.MatchedConfig.ShortcutDestPath;
                if (string.IsNullOrWhiteSpace(destDir))
                    destDir = TxtDesktopPath?.Text;
                if (string.IsNullOrWhiteSpace(destDir))
                    destDir = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                if (string.IsNullOrWhiteSpace(destDir)) return;

                // 如果是远程路径（UNC），先认证再检查目录存在
                bool isRemoteDest = destDir.StartsWith(@"\\");
                if (isRemoteDest)
                {
                    string remoteIp = ExtractIpFromPath(destDir);
                    if (!string.IsNullOrWhiteSpace(remoteIp))
                    {
                        string account = item.MatchedConfig.RemoteAccount;
                        string password = item.MatchedConfig.RemotePassword;
                        TryAuthenticateRemote(remoteIp, account, password, out _);
                    }
                }

                // 检查目标目录是否存在，不存在则尝试创建
                if (!Directory.Exists(destDir))
                {
                    try { Directory.CreateDirectory(destDir); }
                    catch { return; }
                }

                string shortcutPath = System.IO.Path.Combine(destDir, $"{shortcutName}.lnk");

                // 使用 WScript.Shell 创建快捷方式
                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null) return;

                dynamic shell = Activator.CreateInstance(shellType);
                dynamic shortcut = shell.CreateShortcut(shortcutPath);
                shortcut.TargetPath = exePath;
                shortcut.WorkingDirectory = System.IO.Path.GetDirectoryName(exePath);
                shortcut.Description = item.StationName;
                shortcut.Save();

                System.Runtime.InteropServices.Marshal.ReleaseComObject(shortcut);
                System.Runtime.InteropServices.Marshal.ReleaseComObject(shell);

                AppendSmartLog($"  创建快捷方式: {shortcutName}.lnk → {destDir}");
            }
            catch (Exception ex)
            {
                AppendSmartLog($"  创建快捷方式失败 [{item.StationName}]: {ex.Message}");
            }
        }

        /// <summary>加载关闭行为设置</summary>
        private void LoadCloseBehaviorSetting()
        {
            try
            {
                var settings = AppSettings.Load();
                if (CbCloseBehavior != null)
                {
                    // 先解除事件绑定，避免设置 SelectedIndex 时触发 SelectionChanged 导致误写入文件
                    CbCloseBehavior.SelectionChanged -= CbCloseBehavior_SelectionChanged;
                    if (settings.CloseToTray)
                        CbCloseBehavior.SelectedIndex = 1;
                    else
                        CbCloseBehavior.SelectedIndex = 0;
                    CbCloseBehavior.SelectionChanged += CbCloseBehavior_SelectionChanged;
                }
            }
            catch { }
        }

        /// <summary>关闭行为选择变更</summary>
        private void CbCloseBehavior_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            try
            {
                // InitializeComponent 过程中事件会触发，此时控件未完全初始化，跳过
                if (!_isInitialized) return;
                if (CbCloseBehavior == null || CbCloseBehavior.SelectedItem == null) return;

                var item = CbCloseBehavior.SelectedItem as System.Windows.Controls.ComboBoxItem;
                if (item == null) return;

                var settings = AppSettings.Load();
                settings.CloseToTray = (item.Tag as string) == "Tray";
                settings.Save();
            }
            catch { }
        }

    }

    /// <summary>
    /// 参数设定表格行数据模型
    /// </summary>
    public class ParamConfigItem : INotifyPropertyChanged
    {
        private string _stationFolderPattern;
        private string _exeNamePattern;
        private string _folderNamingRule;
        private string _shortcutNamingRule;
        private string _shortcutDestPath;
        private string _remoteAccount;
        private string _remotePassword;
        private string _remotePath;

        /// <summary>工位文件夹名称要求</summary>
        public string StationFolderPattern
        {
            get => _stationFolderPattern;
            set { _stationFolderPattern = value; OnPropertyChanged(); }
        }

        /// <summary>EXE程序名称要求（全匹配）</summary>
        public string ExeNamePattern
        {
            get => _exeNamePattern;
            set { _exeNamePattern = value; OnPropertyChanged(); }
        }

        /// <summary>程序文件夹命名（程序名称 - V程序文件属性的版本号）</summary>
        public string FolderNamingRule
        {
            get => _folderNamingRule;
            set { _folderNamingRule = value; OnPropertyChanged(); }
        }

        /// <summary>程序快捷方式命名与扫描规则（程序所在工位名称 - V版本号）</summary>
        public string ShortcutNamingRule
        {
            get => _shortcutNamingRule;
            set { _shortcutNamingRule = value; OnPropertyChanged(); }
        }

        /// <summary>发送快捷方式地址（目标桌面路径）</summary>
        public string ShortcutDestPath
        {
            get => _shortcutDestPath;
            set { _shortcutDestPath = value; OnPropertyChanged(); }
        }

        /// <summary>远程账户</summary>
        public string RemoteAccount
        {
            get => _remoteAccount;
            set { _remoteAccount = value; OnPropertyChanged(); }
        }

        /// <summary>远程密码</summary>
        public string RemotePassword
        {
            get => _remotePassword;
            set { _remotePassword = value; OnPropertyChanged(); }
        }

        /// <summary>远程访问地址</summary>
        public string RemotePath
        {
            get => _remotePath;
            set { _remotePath = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// 程序匹配页面数据模型（用于显示匹配信息和版本对比）
    /// </summary>
    public class MatchProgramItem : INotifyPropertyChanged
    {
        private string _exeName;
        private string _fileVersion;
        private string _folderName;
        private string _fullPath;
        private string _stationPattern;
        private string _targetFolderPath;
        private string _targetFileVersion;
        private string _matchStatusText;
        private Brush _matchStatusColor;
        private string _versionCompareText;
        private Brush _versionCompareColor;
        private bool _isSelected;
        private string _newFolderName;

        public string ExeName
        {
            get => _exeName;
            set { _exeName = value; OnPropertyChanged(); }
        }
        public string FileVersion
        {
            get => _fileVersion;
            set { _fileVersion = value; OnPropertyChanged(); }
        }
        public string FolderName
        {
            get => _folderName;
            set { _folderName = value; OnPropertyChanged(); }
        }
        public string FullPath
        {
            get => _fullPath;
            set { _fullPath = value; OnPropertyChanged(); }
        }
        public string StationPattern
        {
            get => _stationPattern;
            set { _stationPattern = value; OnPropertyChanged(); }
        }
        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); }
        }
        public string TargetFolderPath
        {
            get => _targetFolderPath;
            set { _targetFolderPath = value; OnPropertyChanged(); CheckMatch(); }
        }
        public string TargetFileVersion
        {
            get => _targetFileVersion;
            set { _targetFileVersion = value; OnPropertyChanged(); OnPropertyChanged(nameof(VersionCompareText)); OnPropertyChanged(nameof(VersionCompareColor)); ComputeNewFolderName(); }
        }
        public string MatchStatusText
        {
            get => _matchStatusText;
            set { _matchStatusText = value; OnPropertyChanged(); }
        }
        public Brush MatchStatusColor
        {
            get => _matchStatusColor;
            set { _matchStatusColor = value; OnPropertyChanged(); }
        }
        public string VersionCompareText
        {
            get => _versionCompareText;
            set { _versionCompareText = value; OnPropertyChanged(); }
        }
        public Brush VersionCompareColor
        {
            get => _versionCompareColor;
            set { _versionCompareColor = value; OnPropertyChanged(); }
        }

        /// <summary>推广后新文件夹名称（用待推广版本号替换命名规则中的占位符）</summary>
        public string NewFolderName
        {
            get => _newFolderName;
            set { _newFolderName = value; OnPropertyChanged(); }
        }

        /// <summary>外部设置的文件夹命名规则查找函数（exeName → namingRule）</summary>
        public Func<string, string> GetFolderNamingRule { get; set; }

        /// <summary>检查匹配状态和版本对比</summary>
        public void CheckMatch()
        {
            // 先检查程序是否匹配
            if (string.IsNullOrWhiteSpace(TargetFolderPath))
            {
                MatchStatusText = "待设置路径";
                MatchStatusColor = Brushes.Gray;
                VersionCompareText = "";
                VersionCompareColor = Brushes.Gray;
                return;
            }

            if (string.IsNullOrWhiteSpace(TargetFileVersion))
            {
                MatchStatusText = "程序不匹配";
                MatchStatusColor = Brushes.Red;
                VersionCompareText = "程序不匹配";
                VersionCompareColor = Brushes.Red;
                return;
            }

            // 程序匹配，显示版本对比
            MatchStatusText = "程序匹配";
            MatchStatusColor = Brushes.LimeGreen;

            VersionCompareText = $"V{FileVersion} → V{TargetFileVersion}";

            // 比较版本大小
            try
            {
                var v1 = new Version(FileVersion);
                var v2 = new Version(TargetFileVersion);
                if (v2 > v1)
                    VersionCompareColor = Brushes.LimeGreen;
                else if (v2 < v1)
                    VersionCompareColor = Brushes.Red;
                else
                    VersionCompareColor = Brushes.Orange;
            }
            catch
            {
                VersionCompareColor = Brushes.Orange;
            }

            ComputeNewFolderName();
        }

        /// <summary>根据参数设定表的文件夹命名规则计算推广后的新文件夹名称</summary>
        private void ComputeNewFolderName()
        {
            if (string.IsNullOrWhiteSpace(ExeName) || string.IsNullOrWhiteSpace(TargetFileVersion))
            {
                NewFolderName = "";
                return;
            }

            if (GetFolderNamingRule == null)
            {
                NewFolderName = "";
                return;
            }

            string rule = GetFolderNamingRule(ExeName);
            if (string.IsNullOrWhiteSpace(rule))
            {
                NewFolderName = "";
                return;
            }

            NewFolderName = rule.Replace("{版本号}", TargetFileVersion);
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    /// <summary>
    /// 当前选中文件夹匹配信息项（用于显示和命名判断）
    /// </summary>
    public class MatchedInfoItem
    {
        public string StationPattern { get; set; }
        public string ExeName { get; set; }
        public string FileVersion { get; set; }
        public string FolderName { get; set; }
        public string ExpectedFolderName { get; set; }
        public bool IsNameMatch { get; set; }
        public bool IsWarning { get; set; }
        public ParamConfigItem MatchedConfig { get; set; }
        public string DisplayText { get; set; }
        public Brush TextColor { get; set; } = Brushes.Gray;
    }

    /// <summary>
    /// 新设备配置行数据模型（从UpdateProgram移植）
    /// </summary>
    public class DeviceConfigRow : INotifyPropertyChanged
    {
        private bool _isSelected = true;
        private bool _isCrossMachine = false;
        private string _remoteIP = "";
        private string _account = "";
        private string _password = "";
        private string _selectedDrive = "D:";
        private string _shareName = "";
        private string _rootFolderName = "";
        private string _subFolderName = "";

        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(nameof(IsSelected)); }
        }
        public bool IsCrossMachine
        {
            get => _isCrossMachine;
            set { _isCrossMachine = value; OnPropertyChanged(nameof(IsCrossMachine)); }
        }
        public string RemoteIP
        {
            get => _remoteIP;
            set { _remoteIP = value; OnPropertyChanged(nameof(RemoteIP)); }
        }
        public string Account
        {
            get => _account;
            set { _account = value; OnPropertyChanged(nameof(Account)); }
        }
        public string Password
        {
            get => _password;
            set { _password = value; OnPropertyChanged(nameof(Password)); }
        }
        public string SelectedDrive
        {
            get => _selectedDrive;
            set
            {
                _selectedDrive = value;
                OnPropertyChanged(nameof(SelectedDrive));
            }
        }
        /// <summary>共享名，留空时从 SelectedDrive 自动推导</summary>
        public string ShareName
        {
            get => string.IsNullOrWhiteSpace(_shareName) ? DeriveShareName(_selectedDrive) : _shareName;
            set
            {
                _shareName = value;
                OnPropertyChanged(nameof(ShareName));
            }
        }
        public string RootFolderName
        {
            get => _rootFolderName;
            set { _rootFolderName = value; OnPropertyChanged(nameof(RootFolderName)); }
        }
        public string SubFolderName
        {
            get => _subFolderName;
            set { _subFolderName = value; OnPropertyChanged(nameof(SubFolderName)); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private static string DeriveShareName(string drive)
        {
            if (string.IsNullOrWhiteSpace(drive)) return "d";
            int colonIdx = drive.IndexOf(':');
            if (colonIdx > 0)
                return drive.Substring(0, colonIdx).ToLowerInvariant();
            return drive.ToLowerInvariant();
        }
    }

    /// <summary>
    /// 扫描结果数据模型
    /// </summary>
    public class ScanResultItem : INotifyPropertyChanged
    {
        private string _exeName;
        private string _fileVersion;
        private DateTime _creationTime;
        private DateTime _lastWriteTime;
        private DateTime _lastAccessTime;
        private string _folderName;
        private string _fullPath;
        private bool _isLatest;
        private bool _isSelected;

        /// <summary>EXE程序名称</summary>
        public string ExeName
        {
            get => _exeName;
            set { _exeName = value; OnPropertyChanged(); }
        }

        /// <summary>EXE程序文件版本</summary>
        public string FileVersion
        {
            get => _fileVersion;
            set { _fileVersion = value; OnPropertyChanged(); }
        }

        /// <summary>创建时间</summary>
        public DateTime CreationTime
        {
            get => _creationTime;
            set { _creationTime = value; OnPropertyChanged(); }
        }

        /// <summary>修改时间</summary>
        public DateTime LastWriteTime
        {
            get => _lastWriteTime;
            set { _lastWriteTime = value; OnPropertyChanged(); }
        }

        /// <summary>访问时间</summary>
        public DateTime LastAccessTime
        {
            get => _lastAccessTime;
            set { _lastAccessTime = value; OnPropertyChanged(); }
        }

        /// <summary>所在文件夹名称</summary>
        public string FolderName
        {
            get => _folderName;
            set { _folderName = value; OnPropertyChanged(); }
        }

        /// <summary>所在完整路径</summary>
        public string FullPath
        {
            get => _fullPath;
            set { _fullPath = value; OnPropertyChanged(); }
        }

        /// <summary>匹配的工位名称</summary>
        public string StationPattern { get; set; }

        /// <summary>是否为同EXE名称中最新版本（用于文字颜色标记）</summary>
        public bool IsLatest
        {
            get => _isLatest;
            set { _isLatest = value; OnPropertyChanged(); OnPropertyChanged(nameof(TextColor)); }
        }

        /// <summary>根据 IsLatest 返回文字颜色</summary>
        public Brush TextColor => IsLatest ? Brushes.LimeGreen : Brushes.Red;

        /// <summary>用户是否勾选了该项（用于传递给程序匹配页面）</summary>
        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// 智能操作扫描结果数据模型
    /// </summary>
    public class SmartScanItem : INotifyPropertyChanged
    {
        private bool _isSelected;
        private string _stationName;
        private string _exeName;
        private string _currentVersion;
        private string _newVersion;
        private string _remotePath;
        private string _sourcePath;
        private string _currentExePath;
        private string _compareText;
        private Brush _compareColor;
        private string _statusText;
        private Brush _statusColor;
        private bool _canUpdate;

        /// <summary>用户是否勾选</summary>
        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); }
        }
        /// <summary>工位名称</summary>
        public string StationName
        {
            get => _stationName;
            set { _stationName = value; OnPropertyChanged(); }
        }
        /// <summary>EXE程序名称</summary>
        public string ExeName
        {
            get => _exeName;
            set { _exeName = value; OnPropertyChanged(); }
        }
        /// <summary>当前版本(A)</summary>
        public string CurrentVersion
        {
            get => _currentVersion;
            set { _currentVersion = value; OnPropertyChanged(); }
        }
        /// <summary>新版本(B)</summary>
        public string NewVersion
        {
            get => _newVersion;
            set { _newVersion = value; OnPropertyChanged(); }
        }
        /// <summary>远程工位路径</summary>
        public string RemotePath
        {
            get => _remotePath;
            set { _remotePath = value; OnPropertyChanged(); }
        }
        /// <summary>当前扫描到的EXE完整路径（含版本子文件夹）</summary>
        public string CurrentExePath
        {
            get => _currentExePath;
            set { _currentExePath = value; OnPropertyChanged(); }
        }
        /// <summary>新版本源路径</summary>
        public string SourcePath
        {
            get => _sourcePath;
            set { _sourcePath = value; OnPropertyChanged(); }
        }
        /// <summary>版本对比文字</summary>
        public string CompareText
        {
            get => _compareText;
            set { _compareText = value; OnPropertyChanged(); }
        }
        /// <summary>版本对比颜色</summary>
        public Brush CompareColor
        {
            get => _compareColor;
            set { _compareColor = value; OnPropertyChanged(); }
        }
        /// <summary>状态文字</summary>
        public string StatusText
        {
            get => _statusText;
            set { _statusText = value; OnPropertyChanged(); }
        }
        /// <summary>状态颜色</summary>
        public Brush StatusColor
        {
            get => _statusColor;
            set { _statusColor = value; OnPropertyChanged(); }
        }
        /// <summary>是否可更新</summary>
        public bool CanUpdate
        {
            get => _canUpdate;
            set { _canUpdate = value; OnPropertyChanged(); }
        }

        /// <summary>关联的参数配置（用于计算新文件夹名等）</summary>
        public ParamConfigItem MatchedConfig { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}

