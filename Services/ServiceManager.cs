using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace MoviePilot_V3.Services
{
    /// 服务管理：Nginx / Python 后端的启动、停止、重启与状态检测。
    /// 耗时操作由调用方（UI 层）放入后台线程执行。
    public static class ServiceManager
    {
        // Windows 控制台 Ctrl+Break 事件（等价于按 Ctrl+Break，进程可捕获后优雅退出）
        private const uint CTRL_BREAK_EVENT = 1;

        // 等待进程优雅退出的首次宽限期（毫秒）：取面板配置（默认 30 秒，可在配置窗口修改）
        private static int GracefulExitTimeoutMs
        {
            get { return AppSettings.Current.ShutdownTimeoutSec * 1000; }
        }

        // 首次超时后再次发送信号的宽限期（毫秒）
        private const int GracefulExitRetryMs = 5000;

        // 内存记录：本进程（面板/命令行模式）启动过的 Python 后端 PID，供关机场景
        // WMI 查询合并兜底（仅本次运行有效，面板重启后不保留，依赖查询兜底）
        private static int launchedBackendPid;
        private static readonly object BackendPidLock = new object();

        // 控制台 Ctrl 事件处理程序委托（收到 Ctrl+C / Ctrl+Break 时被系统回调）
        private delegate bool ConsoleCtrlHandlerDelegate(uint dwCtrlType);

        // 静态持有委托引用，防止被 GC 回收导致回调失效
        private static readonly ConsoleCtrlHandlerDelegate ConsoleCtrlHandlerProc = OnConsoleCtrl;

        /// Ctrl 事件处理程序：返回 true 表示信号已处理，进程不会被默认终止。
        /// 注意：CTRL+BREAK 无法通过 NULL handler 忽略（默认行为是终止进程），
        /// 必须注册真正的处理程序并返回 true，面板自身才不会被关闭。
        private static bool OnConsoleCtrl(uint dwCtrlType)
        {
            return true;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AttachConsole(uint dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FreeConsole();

        [DllImport("kernel32.dll")]
        private static extern bool SetConsoleCtrlHandler(ConsoleCtrlHandlerDelegate handler, bool add);

        [DllImport("kernel32.dll")]
        private static extern bool GenerateConsoleCtrlEvent(uint dwCtrlEvent, uint dwProcessGroupId);

        /// 判断服务是否在运行：读取nginx PID 文件和使用powershell查询python进程，
        /// 仅当 PID 文件存在且对应进程仍在运行才视为运行中，避免把其他应用的同名进程误判进来。
        public static bool IsRunning(string processName)
        {
            int pid = GetPidNum(processName);
            if (pid == 0) return false;

            try
            {
                using (Process p = Process.GetProcessById(pid))
                {
                    return true;
                }
            }
            catch (ArgumentException)
            {
                return false; // PID 对应的进程不存在
            }
            catch (InvalidOperationException)
            {
                return false; // 进程已退出
            }
        }

        private static int GetPidNum(string processName)
        {
            if ("nginx".Equals(processName))
            {
                string pidFile = GetPidFile(processName);
                if (!File.Exists(pidFile)) return 0;
                int pid;
                if (!int.TryParse(File.ReadAllText(pidFile).Trim(), out pid) || pid <= 0) return 0;
                return pid;
            }

            List<int> pids = GetBackendPythonPids();
            if (pids.Count == 0) return 0;
            return pids.Min();
        }

        /// 获取服务对应的 PID 文件路径：nginx 为 logs/nginx.pid（python 后端不写 PID 文件，
        /// 进程为动态查询，此方法仅 nginx 使用）。
        private static string GetPidFile(string processName)
        {
            return processName.Equals("nginx", StringComparison.OrdinalIgnoreCase)
                ? Path.Combine(AppConfig.NGINX_DIR, "logs", "nginx.pid")
                : Path.Combine(AppConfig.CurrentBackendDir, "config", "logs", "mp.pid");
        }

        /// 查询当前运行版本（面板配置"运行版本"）的 Python 后端进程 PID 列表：
        /// 无参版本按当前版本转发，带参版本可指定标准版 / freethreaded 版（版本切换时停旧版用）。
        private static List<int> GetBackendPythonPids()
        {
            List<int> pids = new List<int>();
            string outFile = Path.Combine(Path.GetTempPath(), "mp_backend_pids_" + Guid.NewGuid().ToString("N") + ".txt");
            try
            {
                // PowerShell 脚本：抑制首次加载模块的进度输出；按命令行特征匹配本面板启动的后端，
                // 结果写入临时文件（不经过管道，避免 .NET Framework 管道句柄泄漏）
                // 匹配当前运行版本的 python（venv launcher 最终
                // 拉起的进程是基础解释器，命令行不含 venv 路径；
                // 不匹配系统其他 python 进程）
                // bin 目录路径含反斜杠，直接拼进正则会把 \v（垂直制表符）/ \r（回车）等当作转义
                // 序列，导致编译失败或误匹配；先转义为 \\（正则中匹配字面 \）再拼接
                string binMatch = AppConfig.BIN_DIR.Replace("\\", "\\\\");
                string script = "$ProgressPreference = 'SilentlyContinue'" + Environment.NewLine +
                    "Get-CimInstance Win32_Process -Filter \"Name='python.exe'\" | Where-Object { ($_.CommandLine -match '" + binMatch + "') -and $_.CommandLine -match '\\\\app\\\\main\\.py' } | ForEach-Object { $_.ProcessId } | Out-File -FilePath '" + outFile + "' -Encoding ascii";
                string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
                string psExe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                    "WindowsPowerShell", "v1.0", "powershell.exe");
                using (Process p = new Process())
                {
                    p.StartInfo = new ProcessStartInfo
                    {
                        FileName = psExe,
                        Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + encoded,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    p.Start();
                    if (!p.WaitForExit(8000))
                    {
                        // 超时强制结束（子进程退出后句柄随之回收）
                        try { p.Kill(); } catch { }
                    }
                }
                if (File.Exists(outFile))
                {
                    foreach (string line in File.ReadAllText(outFile).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        int pid;
                        if (int.TryParse(line.Trim(), out pid) && pid > 0)
                        {
                            pids.Add(pid);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // 查询失败（如 PowerShell 不可用）时按无进程处理，调用方静默跳过
                Debug.WriteLine("查询 Python 后端进程失败: " + ex.Message);
            }
            finally
            {
                try { if (File.Exists(outFile)) File.Delete(outFile); } catch { }
            }
            return pids;
        }

        /// 记录本进程（面板/命令行模式）启动过的后端 PID：仅内存保存，供关机场景 WMI
        /// 查询合并兜底；面板重启后不保留，后端仍在运行时依赖 WMI 查询兜底。
        private static void RecordBackendPid(int pid)
        {
            lock (BackendPidLock)
            {
                launchedBackendPid = pid;
            }
        }

        /// 关机/重启场景的进程查询：WMI 进程内查询（不启动任何子进程，避免关机序列中
        /// 拉起 PowerShell 报错弹窗），按命令行特征匹配本面板后端，并合并本进程启动过的
        /// 后端 PID（双通道取并集，均做存活验证）；WMI 连接使用后立即释放，仅关机时调用
        /// 一两次，句柄开销可忽略。
        private static List<int> GetBackendPythonPidsWmi(Action<string> log)
        {
            List<int> pids = new List<int>();
            int wmiMatched = 0;
            int pythonTotal = 0;
            try
            {
                // 按命令行特征匹配：可执行文件为运行版本的 python（venv launcher 最终拉起的
                // 进程是基础解释器，命令行不含 venv 路径）且入口为对应版本目录的 app\main.py，
                // 与 PowerShell 查询的匹配规则一致；pythonTotal 统计全部 python.exe（区分
                // “系统无 python 进程”与“有但命令行不匹配”两种情况）
                const string wql = "SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name = 'python.exe'";
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(wql))
                {
                    foreach (ManagementObject mo in searcher.Get())
                    {
                        using (mo)
                        {
                            try
                            {
                                string cmd = Convert.ToString(mo["CommandLine"]);
                                int pid = Convert.ToInt32(mo["ProcessId"]);
                                if (pid > 0 && cmd != null)
                                {
                                    pythonTotal++;
                                    if (cmd.IndexOf(AppConfig.BIN_DIR, StringComparison.OrdinalIgnoreCase) >= 0 &&
                                        cmd.IndexOf("app\\main.py", StringComparison.OrdinalIgnoreCase) >= 0)
                                    {
                                        pids.Add(pid);
                                        wmiMatched++;
                                    }
                                }
                            }
                            catch { }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // 查询失败（如 WMI 服务不可用）时按无进程处理，调用方静默跳过
                log("关机查询后端进程失败(WMI): " + ex.Message);
                Debug.WriteLine("WMI 查询 Python 后端进程失败: " + ex.Message);
            }

            // 合并本进程启动过的后端 PID（WMI 查询失败 / 漏查时兜底，验证存活防 PID 复用）
            int launched;
            lock (BackendPidLock)
            {
                launched = launchedBackendPid;
            }
            if (launched > 0)
            {
                if (IsPythonProcessAlive(launched))
                {
                    pids.Add(launched);
                }
                else
                {
                    log("内存 PID " + launched + " 已失效（进程不存在或非 python.exe）");
                }
            }
            log("关机查询后端进程: 系统 python.exe 共 " + pythonTotal + " 个, WMI 命中 " + wmiMatched + " 个, 内存 PID " + (launched > 0 ? launched.ToString() : "无") + ", 共 " + pids.Count + " 个候选");
            return pids;
        }

        /// 验证 PID 对应进程是否存在且为 python.exe（防 PID 复用误判）。
        private static bool IsPythonProcessAlive(int pid)
        {
            try
            {
                using (Process p = Process.GetProcessById(pid))
                {
                    return "python".Equals(p.ProcessName, StringComparison.OrdinalIgnoreCase);
                }
            }
            catch (ArgumentException)
            {
                return false; // 进程不存在
            }
            catch (InvalidOperationException)
            {
                return false; // 进程已退出
            }
        }

        /// 生成服务状态文本（Nginx / Python）。
        public static string GetStatusText()
        {
            string nginx = IsRunning("nginx") ? "运行中" : "已停止";
            string python = IsRunning("python") ? "运行中" : "已停止";
            return "Nginx:  " + nginx + Environment.NewLine +
                   "Python: " + python + Environment.NewLine;
        }

        /// 启动全部服务：Nginx、Python 后端。启动前先确保运行环境就绪（缺失的便携版
        /// nginx/git/python 会在首次启动时自动下载，下载包位于 BASE_DIR\tmp）。
        public static void StartServices(Action<string> log)
        {
            log("正在启动服务...");

            // 运行环境就绪检查（幂等：已安装时零下载）
            EnvironmentSetup.EnsureEnvironment(log);

            string envPath = AppConfig.BuildEnvPath();

            // 面板同步的配置在 config\nginx.conf，含面板端口 + upstream 后端端口）
            if (!IsRunning("nginx"))
            {
                string nginxConf = Path.Combine(AppConfig.NGINX_CONFIG_DIR, "nginx.conf");
                int nginxPid = 0;
                StartProcess(Path.Combine(AppConfig.NGINX_DIR, "nginx.exe"), "-c \"" + nginxConf + "\"", AppConfig.NGINX_DIR, envPath, onStarted: pid => nginxPid = pid);
                log("Nginx 已启动 (PID " + nginxPid + ")");
                Thread.Sleep(500);
            }
            else
            {
                log("Nginx 已在运行");
            }

            // 启动 Python 后端（入口优先级：main.py -> app.py；运行在虚拟环境中）
            if (!IsRunning("python"))
            {
                // 存在 download.flag（站点资源强制更新标记）时先重新下载站点资源：
                // 下载成功则清理标记；失败保留旧文件与标记（下次启动重试）
                if (AppConfig.DownloadFlagExists)
                {
                    if (EnvironmentSetup.RefreshSiteFiles(log))
                    {
                        try
                        {
                            File.Delete(AppConfig.CurrentDownloadFlagFile);
                            log("站点资源更新完成，已清理 download.flag 标记");
                        }
                        catch (Exception ex)
                        {
                            log("清理 download.flag 标记失败: " + ex.Message);
                        }
                    }
                    else
                    {
                        log("警告: 站点资源更新失败，已保留原有文件（download.flag 保留，下次启动重试）");
                    }
                }
                // 同步站点资源：升级包（config\temp\moviepilot-update\resources）附带 *.pyd / *.bin 时移动到站点资源目录
                EnvironmentSetup.SyncSiteResourcesFromUpdate(log);

                // 站点资源检查：缺失或不完整时后端无法启动，拒绝启动并提示
                if (!EnvironmentSetup.SiteFilesReady())
                {
                    log("错误: 站点资源文件缺失或不完整（" + EnvironmentSetup.SitesPydFileName + " / user.sites.v3.bin），后端无法启动");
                    log("请检查网络或 GitHub Token 后重启面板重试（配置窗口可填写 Token 与代理）");
                }
                else
                {
                    string pythonExe = AppConfig.GetPythonExe();
                    string entryFile = null;
                    string extraArgs = null;
                    if (File.Exists(Path.Combine(AppConfig.CurrentBackendDir, "app", "main.py")))
                    {
                        entryFile = Path.Combine(AppConfig.CurrentBackendDir, "app", "main.py");
                    }
                    else if (File.Exists(Path.Combine(AppConfig.CurrentBackendDir, "app", "manage.py")))
                    {
                        entryFile = Path.Combine(AppConfig.CurrentBackendDir, "app", "manage.py");
                        extraArgs = "runserver";
                    }

                    if (entryFile != null)
                    {
                        string args = "\"" + entryFile + "\"" + (extraArgs != null ? " " + extraArgs : "");
                        // 注入 PORT 环境变量：后端监听端口与面板配置（nginx upstream）保持一致；
                        // 同时注入代理环境变量（配置了代理时），后端网络请求与 git / curl / pip 走同一代理
                        int pythonPid = 0;
                        StartProcess(pythonExe, args, AppConfig.CurrentBackendDir, envPath, AppSettings.Current.BackendPort, true,
                            pid => { pythonPid = pid; RecordBackendPid(pid); });
                        // 打印 PID 供与任务管理器实际进程对比（venv launcher 启动真实解释器后自身会退出，
                        // 此处可能是 launcher 的 PID 而非最终运行的解释器 PID）
                        log("Python 后端已启动: " + Path.GetFileName(entryFile) + (extraArgs != null ? " " + extraArgs : "") +
                            (pythonExe.IndexOf(AppConfig.CurrentVenvDir, StringComparison.OrdinalIgnoreCase) >= 0 ? "（虚拟环境）" : "") +
                            " (PID " + pythonPid + ")");
                        Thread.Sleep(1000);
                    }
                    else
                    {
                        log("警告: 未找到Python后端入口文件");
                    }
                }
            }
            else
            {
                log("Python 后端已在运行");
            }

            log("所有服务启动完成");
        }

        /// 停止全部服务（nginx 用官方 -s quit；python 先查询后端进程 PID，再发送 Ctrl+Break
        /// 优雅退出；useWmi 为 true 时（关机/重启清理场景）改用 WMI 进程内查询，避免在
        /// 关机序列中启动 PowerShell 子进程报错弹窗）。
        public static void StopServices(Action<string> log, bool useWmi = false)
        {
            log("正在停止服务...");

            RunTaskKill("nginx.exe", log, useWmi);
            log("Nginx 已停止");

            RunTaskKill("python.exe", log, useWmi);
            log("Python 已停止");

            // 清理 nginx 的 PID 文件（服务已退出，不残留状态记录）
            string nginxPid = GetPidFile("nginx");
            if (File.Exists(nginxPid))
            {
                File.Delete(nginxPid);
            }
            log("所有服务已停止");

            // 停止后备份可能被用户修改过的 category.yaml（内容不同才覆盖，防止官方模板被覆盖后修改丢失）
            UpgradeService.BackupCategoryYaml(log);
        }

        /// 启动进程，并注入自定义 PATH 环境变量；port 非空时同时注入 PORT（MoviePilot 后端监听端口，
        /// 与 nginx upstream 对齐，环境变量优先于默认值 3001）；injectProxy 为 true 且配置了代理时
        /// 注入代理环境变量（Python 后端专用，网络请求与 git / curl / pip 走同一代理）。
        private static void StartProcess(string fileName, string arguments, string workingDir, string envPath, int? port = null, bool injectProxy = false, Action<int> onStarted = null)
        {
            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments ?? string.Empty,
                WorkingDirectory = workingDir,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.EnvironmentVariables["PATH"] = envPath + ";" + Environment.GetEnvironmentVariable("PATH");
            if (port.HasValue)
            {
                psi.EnvironmentVariables["PORT"] = port.Value.ToString();
                // 后端 Python 以 UTF-8 模式运行：文件读写 / 控制台输出统一 UTF-8（Python 3.7+ 官方推荐，
                // 避免中文环境（GBK 代码页）下编码不一致导致的乱码与异常）
                psi.EnvironmentVariables["PYTHONUTF8"] = "1";
            }
            if (injectProxy)
            {
                InjectProxyEnvironment(psi);
            }
            // 启动后立即释放 Process 对象（不影响子进程运行）：不持有引用避免句柄泄漏；
            // 回调拿到子进程 PID（供内存记录，关机场景 WMI 查询的补充）
            using (Process p = Process.Start(psi))
            {
                if (p != null && onStarted != null) onStarted(p.Id);
            }
        }

        /// 注入代理环境变量（仅当面板配置了 http / socks5 代理时）：
        /// Python 后端的网络库（requests / httpx 等）读取 HTTP_PROXY / HTTPS_PROXY / ALL_PROXY，
        /// 大小写同时注入以兼容不同库；NO_PROXY 排除本机回环与常规局域网网段（CIDR 写法，
        /// requests / httpx 等主流库均支持），避免后端访问局域网 IP / 本机服务时误走代理导致不通。
        private static void InjectProxyEnvironment(ProcessStartInfo psi)
        {
            string proxyUrl = EnvironmentSetup.BuildProxyUrl();
            if (proxyUrl == null)
            {
                return; // 未配置代理：不注入，走系统默认环境
            }

            // 本机回环（localhost / 127.0.0.1 / ::1）+ 私有网段（10/8、172.16/12、192.168/16）+ 链路本地（169.254/16）
            const string noProxy = "localhost,127.0.0.1,::1,10.0.0.0/8,172.16.0.0/12,192.168.0.0/16,169.254.0.0/16";

            psi.EnvironmentVariables["HTTP_PROXY"] = proxyUrl;
            psi.EnvironmentVariables["HTTPS_PROXY"] = proxyUrl;
            psi.EnvironmentVariables["http_proxy"] = proxyUrl;
            psi.EnvironmentVariables["https_proxy"] = proxyUrl;
            psi.EnvironmentVariables["ALL_PROXY"] = proxyUrl;
            psi.EnvironmentVariables["all_proxy"] = proxyUrl;
            psi.EnvironmentVariables["NO_PROXY"] = noProxy;
            psi.EnvironmentVariables["no_proxy"] = noProxy;
        }

        /// 停止进程：nginx 用官方 -s quit；python 后端先按命令行特征查询后端进程（平时用
        /// PowerShell，关机/重启清理场景 useWmi=true 时用 WMI 进程内查询），再附加到其控制台
        /// 发送 Ctrl+Break 停机信号，等待优雅退出（后端收到后触发正常关闭流程），首次宽限期
        /// 超时后再次发信号，最终仍不退出才强制结束。不按进程名匹配，避免误杀系统其他
        /// python 进程；未查询到进程或进程已退出时静默跳过。
        private static void RunTaskKill(string imageName, Action<string> log, bool useWmi = false)
        {
            // nginx：平时用官方优雅停止命令（-c 与启动保持一致，确保找到运行实例的 pid 文件）；
            // 关机/重启清理场景不启动子进程（关机序列中控制台子系统可能已不可用，此时启动
            // nginx.exe 会报 0xc0000142 DLL 初始化失败），直接按 PID 文件强制结束——
            // nginx 无数据库与状态需落盘，Kill 无副作用
            if (imageName.Equals("nginx.exe", StringComparison.OrdinalIgnoreCase))
            {
                string pidFile = Path.Combine(AppConfig.NGINX_DIR, "logs", "nginx.pid");
                if (File.Exists(pidFile))
                {
                    try
                    {
                        if (useWmi)
                        {
                            int pid;
                            if (int.TryParse(File.ReadAllText(pidFile).Trim(), out pid) && pid > 0)
                            {
                                using (Process nginxProc = Process.GetProcessById(pid))
                                {
                                    nginxProc.Kill();
                                    nginxProc.WaitForExit(3000);
                                }
                            }
                        }
                        else
                        {
                            string envPath = AppConfig.BuildEnvPath();
                            string nginxConf = Path.Combine(AppConfig.NGINX_CONFIG_DIR, "nginx.conf");
                            StartProcess(Path.Combine(AppConfig.NGINX_DIR, "nginx.exe"), "-c \"" + nginxConf + "\" -s quit", AppConfig.NGINX_DIR, envPath);
                            Thread.Sleep(500);
                        }
                    }
                    catch (Exception ex)
                    {
                        // nginx 停止失败不中断后续服务停止（关机场景尤其要保证 python 后端能优雅退出）
                        log("停止 Nginx 失败: " + ex.Message);
                    }
                }
                return;
            }

            // Python 后端：按命令行特征查询本面板的后端进程（可执行文件为运行版本的
            // python.exe 且命令行入口为对应版本目录的 app\main.py）；平时用 PowerShell 查询，
            // 关机/重启清理场景用 WMI 进程内查询（不启动子进程，避免关机序列中报错弹窗）
            List<int> pids = useWmi ? GetBackendPythonPidsWmi(log) : GetBackendPythonPids();
            if (pids.Count == 0)
            {
                log("未查询到后端进程，跳过优雅停止（进程将随系统关机强制结束）");
                return;
            }
            log("停止后端: 候选进程 " + pids.Count + " 个 (PID " + string.Join(",", pids) + ")");

            Process p = null;
            try
            {
                p = Process.GetProcessById(pids.Min());
            }
            catch (ArgumentException)
            {
                return; // PID 对应的进程不存在
            }
            catch (InvalidOperationException)
            {
                return; // 进程已退出
            }

            try
            {
                // 附加到目标进程的控制台并发送 Ctrl+Break，触发其优雅退出
                bool graceful = false;
                if (AttachConsole((uint)p.Id))
                {
                    log("停止后端: 已附加控制台 (PID " + p.Id + ")");
                    // 注册 Ctrl 处理程序（返回 true 表示已处理），防止面板自身被 Ctrl+Break 默认终止
                    SetConsoleCtrlHandler(ConsoleCtrlHandlerProc, true);

                    // 第一次信号：请求优雅退出（进程需时间处理存量请求、关闭连接等）
                    GenerateConsoleCtrlEvent(CTRL_BREAK_EVENT, 0);
                    graceful = p.WaitForExit(GracefulExitTimeoutMs);

                    // 仍未退出：再发一次信号（部分服务第一次信号开始清理、第二次强制），再给一段宽限期
                    if (!graceful)
                    {
                        GenerateConsoleCtrlEvent(CTRL_BREAK_EVENT, 0);
                        graceful = p.WaitForExit(GracefulExitRetryMs);
                    }

                    FreeConsole();
                    // 等待信号分发完全结束再注销处理程序
                    Thread.Sleep(500);
                    SetConsoleCtrlHandler(ConsoleCtrlHandlerProc, false);
                    log(graceful ? "停止后端: Ctrl+Break 后优雅退出完成" : "停止后端: 两次信号后仍未退出，转强制结束");
                }
                else
                {
                    log("停止后端: 附加控制台失败 (错误码 " + Marshal.GetLastWin32Error() + ")，转强制结束");
                }

                if (!graceful)
                {
                    // 无控制台（如 pythonw.exe）或两轮宽限期后仍未退出：强制结束
                    log("优雅退出超时，强制结束 " + imageName);
                    p.Kill();
                    p.WaitForExit(3000);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("停止进程失败 " + imageName + ": " + ex.Message);
            }
            finally
            {
                p.Dispose();
            }
        }
    }
}
