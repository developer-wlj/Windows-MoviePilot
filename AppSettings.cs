using System;
using System.IO;
using System.Text;

namespace MoviePilot_V3
{
    /// <summary>
    /// 面板配置参数：优雅退出超时、Nginx 端口、后端端口、启动开关。
    /// 持久化到 config 目录下的 app.ini（key=value 格式）。
    /// </summary>
    public class AppSettings
    {
        public const string ConfigFileName = "app.ini";

        // UTF-8 无 BOM 编码（.NET Framework 的 Encoding.UTF8 会写入 BOM，配置文件统一使用无 BOM）
        private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

        // 默认值
        public const int DefaultShutdownTimeoutSec = 30;
        public const int DefaultNginxPort = 3000;
        public const int DefaultBackendPort = 3001;
        // 服务状态监控间隔（秒）：面板定时刷新 nginx / python 存活状态，默认 5 秒
        public const int DefaultStatusMonitorSec = 5;
        // 启动时驻留系统托盘（不显示主窗口），默认关闭（启动时显示主窗口）
        public const bool DefaultStartMinimizedToTray = false;
        // 更新时强制更新前端资源与后端认证 / 站点资源，默认开启：官方可能对同一版本号重新发布不同内容
        public const bool DefaultForceUpdateResources = true;
        // 运行版本默认值：标准版（MoviePilot-V3；freethreaded 版为 MoviePilot-V3-T）
        public const string DefaultRunVersion = "MoviePilot-V3";

        /// 当前生效的配置（程序启动时加载一次）
        public static AppSettings Current { get; } = Load();

        public int ShutdownTimeoutSec { get; set; } = DefaultShutdownTimeoutSec;
        public int NginxPort { get; set; } = DefaultNginxPort;
        public int BackendPort { get; set; } = DefaultBackendPort;
        // 服务状态监控间隔（秒），默认 5 秒
        public int StatusMonitorSec { get; set; } = DefaultStatusMonitorSec;
        // 运行版本：MoviePilot-V3 标准版（默认）/ MoviePilot-V3-T freethreaded 版；
        // 启动服务时先读取该值，决定使用哪套 Python / venv / 后端代码目录（共用端口，一次只运行一个版本）
        public string RunVersion { get; set; } = DefaultRunVersion;
        // 启动时驻留系统托盘（不显示主窗口），默认关闭
        public bool StartMinimizedToTray { get; set; } = DefaultStartMinimizedToTray;
        // 更新时（手动升级 / 修复运行环境）强制更新前端资源与后端认证 / 站点资源（默认开启）：
        // 前端 dist.zip 可能同一版本号被官方重新发布（版本号不变、内容变化），仅按版本号比较会漏更；
        // 认证资源（sites.cp314(-t)-win_amd64.pyd）与站点资源（user.sites.v3.bin）也一并强制重新下载覆盖
        public bool ForceUpdateResources { get; set; } = DefaultForceUpdateResources;

        // 启动时检查并更新版本（对比官方最新标签，默认关闭）
        public bool AutoUpdateOnStart { get; set; } = false;
        // 启动时自动启动 Nginx 和 Python（默认关闭）
        public bool AutoStartServices { get; set; } = false;
        // 打印 Debug 日志（uv / pip / curl / git 等子进程命令输出），默认关闭；
        // 关闭时面板只显示 INFO / ERROR 级别的主流程日志
        public bool DebugLog { get; set; } = false;
        // 阻止 Windows 空闲休眠/睡眠（面板运行期间生效，退出面板时自动恢复），默认关闭
        public bool PreventSleep { get; set; } = false;

        // GitHub Token（下载 GitHub 资源文件时携带 Authorization 请求头，为空则不携带）
        public string GitHubToken { get; set; } = "";
        // 代理类型："" 关闭 / "http" / "socks5"；配置后应用到 git 全局代理与所有下载
        public string ProxyType { get; set; } = "";
        // 代理地址（如 127.0.0.1）
        public string ProxyHost { get; set; } = "";
        // 代理端口
        public int ProxyPort { get; set; } = 0;

        // 上次成功同步补丁（v3-rebase）时补丁分支最新提交的时间（"yyyy-MM-dd HH:mm:ss"），
        // 按运行版本目录分开记录（v3 / V3T 各一个）：两个目录的补丁同步进度互不污染，
        // 避免 A 目录同步了新补丁后，切换到的 B 目录（补丁落后）被共用旧记录误判“无新补丁”而漏补丁；
        // 空表示该目录从未同步过
        public string LastRebasePatchTimeV3 { get; set; } = "";
        public string LastRebasePatchTimeT { get; set; } = "";

        /// 按当前运行版本（MoviePilot-V3 / MoviePilot-V3-T）读写对应目录的补丁同步时间记录
        public string CurrentLastRebasePatchTime
        {
            get { return AppConfig.IsTVersion ? LastRebasePatchTimeT : LastRebasePatchTimeV3; }
            set
            {
                if (AppConfig.IsTVersion) LastRebasePatchTimeT = value;
                else LastRebasePatchTimeV3 = value;
            }
        }

        /// 配置文件完整路径（BASE_DIR\config\app.ini）
        public static string ConfigPath
        {
            get { return Path.Combine(AppConfig.CONFIG_DIR, ConfigFileName); }
        }

        /// 从配置文件加载（文件不存在或格式错误时使用默认值）
        private static AppSettings Load()
        {
            AppSettings s = new AppSettings();
            try
            {
                if (File.Exists(ConfigPath))
                {
                    foreach (string line in File.ReadAllLines(ConfigPath, Utf8NoBom))
                    {
                        string trimmed = line.Trim();
                        if (trimmed.Length == 0 || trimmed.StartsWith("#"))
                        {
                            continue;
                        }
                        int idx = trimmed.IndexOf('=');
                        if (idx <= 0)
                        {
                            continue;
                        }
                        string key = trimmed.Substring(0, idx).Trim();
                        string value = trimmed.Substring(idx + 1).Trim();
                        switch (key)
                        {
                            case "shutdown_timeout_sec":
                                int t;
                                if (int.TryParse(value, out t) && t > 0)
                                {
                                    s.ShutdownTimeoutSec = t;
                                }
                                break;
                            case "nginx_port":
                                int n;
                                if (int.TryParse(value, out n) && n > 0 && n < 65536)
                                {
                                    s.NginxPort = n;
                                }
                                break;
                            case "backend_port":
                                int b;
                                if (int.TryParse(value, out b) && b > 0 && b < 65536)
                                {
                                    s.BackendPort = b;
                                }
                                break;
                            case "status_monitor_sec":
                                int sm;
                                if (int.TryParse(value, out sm) && sm >= 3 && sm <= 600)
                                {
                                    s.StatusMonitorSec = sm;
                                }
                                break;
                            case "run_version":
                                // 只接受两个合法值（标准版 / freethreaded 版），非法值保持默认
                                if (value == "MoviePilot-V3" || value == "MoviePilot-V3-T")
                                {
                                    s.RunVersion = value;
                                }
                                break;
                            case "start_minimized_to_tray":
                                bool smt;
                                if (bool.TryParse(value, out smt))
                                {
                                    s.StartMinimizedToTray = smt;
                                }
                                break;
                            case "auto_update_on_start":
                                bool au;
                                if (bool.TryParse(value, out au))
                                {
                                    s.AutoUpdateOnStart = au;
                                }
                                break;
                            case "force_update_resources":
                                bool fr;
                                if (bool.TryParse(value, out fr))
                                {
                                    s.ForceUpdateResources = fr;
                                }
                                break;
                            case "auto_start_services":
                                bool as2;
                                if (bool.TryParse(value, out as2))
                                {
                                    s.AutoStartServices = as2;
                                }
                                break;
                            case "debug_log":
                                bool dl;
                                if (bool.TryParse(value, out dl))
                                {
                                    s.DebugLog = dl;
                                }
                                break;
                            case "prevent_sleep":
                                bool ps;
                                if (bool.TryParse(value, out ps))
                                {
                                    s.PreventSleep = ps;
                                }
                                break;
                            case "github_token":
                                s.GitHubToken = value;
                                break;
                            case "proxy_type":
                                string pt = value.ToLowerInvariant();
                                s.ProxyType = (pt == "http" || pt == "socks5") ? pt : "";
                                break;
                            case "proxy_host":
                                s.ProxyHost = value;
                                break;
                            case "proxy_port":
                                int pp;
                                if (int.TryParse(value, out pp) && pp > 0 && pp < 65536)
                                {
                                    s.ProxyPort = pp;
                                }
                                break;
                            case "last_rebase_patch_time_v3":
                                s.LastRebasePatchTimeV3 = value;
                                break;
                            case "last_rebase_patch_time_t":
                                s.LastRebasePatchTimeT = value;
                                break;
                            // 旧版全局记录 last_rebase_patch_time 不迁移：无法区分归属哪个版本目录，
                            // 沿用可能让补丁落后的目录误判“无新补丁”而漏补丁；忽略后按“未记录（首次）”
                            // 处理，首次升级会触发一次幂等的重建+重打补丁，安全不漏
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("加载配置失败: " + ex.Message);
            }
            return s;
        }

        /// 保存到配置文件（目录不存在时自动创建）
        public void Save()
        {
            try
            {
                string dir = Path.GetDirectoryName(ConfigPath);
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("run_version=" + RunVersion);
                sb.AppendLine("shutdown_timeout_sec=" + ShutdownTimeoutSec);
                sb.AppendLine("nginx_port=" + NginxPort);
                sb.AppendLine("backend_port=" + BackendPort);
                sb.AppendLine("status_monitor_sec=" + StatusMonitorSec);
                sb.AppendLine("start_minimized_to_tray=" + StartMinimizedToTray);
                sb.AppendLine("auto_update_on_start=" + AutoUpdateOnStart);
                sb.AppendLine("force_update_resources=" + ForceUpdateResources);
                sb.AppendLine("auto_start_services=" + AutoStartServices);
                sb.AppendLine("debug_log=" + DebugLog);
                sb.AppendLine("prevent_sleep=" + PreventSleep);
                sb.AppendLine("github_token=" + GitHubToken);
                sb.AppendLine("proxy_type=" + ProxyType);
                sb.AppendLine("proxy_host=" + ProxyHost);
                sb.AppendLine("proxy_port=" + ProxyPort);
                sb.AppendLine("last_rebase_patch_time_v3=" + LastRebasePatchTimeV3);
                sb.AppendLine("last_rebase_patch_time_t=" + LastRebasePatchTimeT);
                File.WriteAllText(ConfigPath, sb.ToString(), Utf8NoBom);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("保存配置失败: " + ex.Message);
            }
        }
    }
}
