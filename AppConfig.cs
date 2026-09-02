using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace MoviePilot_V3
{
    /// 应用常量与路径配置。
    public static class AppConfig
    {
        public const string APP_NAME = "MoviePilot-V3";

        // 版本号：运行时读取程序集版本（AssemblyVersion），与 AssemblyInfo 保持一致；
        // 取前 3 段（如 1.0.0.0 → 1.0.0），避免硬编码导致版本不同步
        public static readonly string APP_VERSION = typeof(AppConfig).Assembly.GetName().Version.ToString(3);

        // 站点地址：随面板配置的 nginx 监听端口动态变化
        public static string SITE_URL
        {
            get { return "http://127.0.0.1:" + AppSettings.Current.NginxPort; }
        }

        // exe 位于 BASE_DIR目录
        public static readonly string BASE_DIR = Path.GetFullPath(Application.StartupPath);
        public static readonly string BIN_DIR = Path.Combine(BASE_DIR, "runtime");
        public static readonly string CONFIG_DIR = Path.Combine(BASE_DIR, "config");
        public static readonly string LOGS_DIR = Path.Combine(BASE_DIR, "logs");
        // 下载临时目录（便携版压缩包与中间解压目录，位于 BASE_DIR\tmp）
        public static readonly string TMP_DIR = Path.Combine(BASE_DIR, "tmp");
        public static readonly string NGINX_DIR = Path.Combine(BIN_DIR, "Nginx");
        // Python 便携版目录（首次运行自动下载 3.14.7）
        public static readonly string PYTHON_DIR = Path.Combine(BIN_DIR, "Python3.14.7");
        // uv 便携版目录（官方依赖管理工具：Python 3.14 时代后端依赖按 pyproject.toml + uv.lock 安装）
        public static readonly string UV_DIR = Path.Combine(BIN_DIR, "uv");
        public static readonly string GIT_DIR = Path.Combine(BIN_DIR, "Git");
        // Python 虚拟环境目录（后端运行在 venv 中，与便携版解压目录隔离）
        public static readonly string VENV_DIR = Path.Combine(BIN_DIR, "venv");
        public static readonly string BACKEND_DIR = Path.Combine(BASE_DIR, "server", "MoviePilot-V3");
        public static readonly string FRONTEND_DIR = Path.Combine(BASE_DIR, "mp-web");
        // 站点资源目录（sites.cp314-win_amd64.pyd / user.sites.v3.bin，缺失将无法启动后端）
        public static readonly string SITE_DIR = Path.Combine(BACKEND_DIR, "app", "application", "site");
        public static readonly string MP_CONF_DIR = Path.Combine(BACKEND_DIR, "config");
        public static readonly string MP_LOG_DIR = Path.Combine(MP_CONF_DIR, "logs");
        public static readonly string MP_TRMP_DIR = Path.Combine(MP_CONF_DIR, "temp");
        // 站点资源强制更新标记文件（BACKEND_DIR\config\logs；存在时启动后端前重新下载站点资源，失败保留旧文件）
        public static readonly string DOWNLOAD_FLAG_FILE = Path.Combine(MP_LOG_DIR, "download.flag");

        // ---- freethreaded 版（MoviePilot-V3-T）独立环境目录：解释器 / venv / 后端代码与标准版完全隔离 ----
        public static readonly string PYTHON_T_DIR = Path.Combine(BIN_DIR, "Python3.14.7t");
        public static readonly string VENV_DIR_T = Path.Combine(BIN_DIR, "venv_t");
        public static readonly string BACKEND_DIR_T = Path.Combine(BASE_DIR, "server", "MoviePilot-V3-T");
        public static readonly string SITE_DIR_T = Path.Combine(BACKEND_DIR_T, "app", "application", "site");
        public static readonly string MP_CONF_DIR_T = Path.Combine(BACKEND_DIR_T, "config");
        public static readonly string MP_LOG_DIR_T = Path.Combine(MP_CONF_DIR_T, "logs");
        public static readonly string MP_TRMP_DIR_T = Path.Combine(MP_CONF_DIR_T, "temp");
        public static readonly string DOWNLOAD_FLAG_FILE_T = Path.Combine(MP_LOG_DIR_T, "download.flag");

        // ---- 当前运行版本（面板配置"运行版本"决定：标准版 MoviePilot-V3 / freethreaded 版 MoviePilot-V3-T）----
        // 两个版本共用 nginx、端口与前端，仅 Python 解释器、虚拟环境与后端代码目录不同；
        // 一次只运行一个版本（共用端口），切换版本后下次启动自动停旧起新
        public static bool IsTVersion
        {
            get { return string.Equals(AppSettings.Current.RunVersion, "MoviePilot-V3-T", StringComparison.OrdinalIgnoreCase); }
        }
        public static string CurrentPythonDir { get { return IsTVersion ? PYTHON_T_DIR : PYTHON_DIR; } }
        public static string CurrentVenvDir { get { return IsTVersion ? VENV_DIR_T : VENV_DIR; } }
        public static string CurrentBackendDir { get { return IsTVersion ? BACKEND_DIR_T : BACKEND_DIR; } }
        public static string CurrentSiteDir { get { return IsTVersion ? SITE_DIR_T : SITE_DIR; } }
        public static string CurrentMpConfDir { get { return IsTVersion ? MP_CONF_DIR_T : MP_CONF_DIR; } }
        public static string CurrentMpLogDir { get { return IsTVersion ? MP_LOG_DIR_T : MP_LOG_DIR; } }
        public static string CurrentMpTempDir { get { return IsTVersion ? MP_TRMP_DIR_T : MP_TRMP_DIR; } }
        public static string CurrentDownloadFlagFile { get { return IsTVersion ? DOWNLOAD_FLAG_FILE_T : DOWNLOAD_FLAG_FILE; } }

        /// 是否存在站点资源强制更新标记（download.flag，按当前运行版本目录）。
        public static bool DownloadFlagExists
        {
            get { return File.Exists(CurrentDownloadFlagFile); }
        }
        // nginx 配置同步目录（首次运行时把 config 目录的 nginx.conf / common.conf 拷贝到这里）
        public static readonly string NGINX_CONFIG_DIR = Path.Combine(NGINX_DIR, "conf");


        // Git 相关目录
        public static readonly string GIT_CMD_DIR = Path.Combine(GIT_DIR, "cmd");
        public static readonly string GIT_BIN_DIR = Path.Combine(GIT_DIR, "bin");
        public static readonly string GIT_USR_BIN_DIR = Path.Combine(GIT_DIR, "usr", "bin");

        /// 托盘图标路径（脚本目录下），不存在时使用系统默认图标。
        public static readonly string APP_ICON = Path.Combine(BASE_DIR, "app.ico");

        /// 获取应用图标：优先从 BASE_DIR 下的 app.ico 加载大尺寸帧（请求 256x256，
        /// .NET 的 Icon 构造不支持 ico 内 256x256 PNG 帧会落到 128x128，
        /// 对标题栏 16~32 与任务栏 48~64 的渲染均绰绰有余），
        /// 加载失败时回退 exe 内嵌图标（Icon.ExtractAssociatedIcon 只返回 32x32，
        /// 高 DPI 下放大渲染会模糊），仍无则返回 null（调用方用系统默认图标）。
        /// 返回新实例，调用方负责释放。
        public static Icon GetAppIcon()
        {
            if (File.Exists(APP_ICON))
            {
                try
                {
                    return new Icon(APP_ICON, 256, 256);
                }
                catch
                {
                    // app.ico 加载失败：回退 exe 内嵌图标
                }
            }
            try
            {
                Icon icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (icon != null)
                {
                    return icon;
                }
            }
            catch
            {
                // exe 内嵌图标提取失败：返回 null，调用方用系统默认图标
            }
            return null;
        }

        /// 面板日志命名管道：命令行模式（-c）把日志实时发送到面板运行日志区（面板未运行时静默）。
        public const string PANEL_LOG_PIPE = "MoviePilotV3.PanelLog";

        /// Python 可执行文件：优先虚拟环境（按当前运行版本），未创建时回退便携版目录。
        public static string GetPythonExe()
        {
            string venvPython = Path.Combine(CurrentVenvDir, "Scripts", "python.exe");
            return File.Exists(venvPython) ? venvPython : Path.Combine(CurrentPythonDir, "python.exe");
        }

        /// 构建服务所需的环境变量 PATH（按当前运行版本的 venv / Python）。
        public static string BuildEnvPath()
        {
            return GIT_CMD_DIR + ";" + GIT_BIN_DIR + ";" + GIT_USR_BIN_DIR + ";" +
                   Path.Combine(CurrentVenvDir, "Scripts") + ";" +
                   Path.Combine(CurrentPythonDir, "Scripts") + ";" + CurrentPythonDir + ";" +
                   UV_DIR + ";" + NGINX_DIR;
        }
    }
}
