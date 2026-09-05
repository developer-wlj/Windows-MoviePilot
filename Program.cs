using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using MoviePilot_V3.Services;

namespace MoviePilot_V3
{
    internal static class Program
    {
        /// 单实例互斥锁（仅面板模式使用），防止重复启动多个面板
        private static Mutex instanceMutex;

        /// 应用程序的主入口点。
        [STAThread]
        static void Main(string[] args)
        {
            // 命令行模式：-c <start|stop|restart|update>，执行后退出，不显示窗口
            // （日志通过命名管道发送到面板运行日志区；面板未运行时静默执行）
            if (args != null && args.Length > 0)
            {
                // 命令行模式不占用面板单实例锁，且启动服务需要 config 模板，先释放内置资源
                BundledResources.Deploy();

                string arg0 = args[0].ToLowerInvariant();
                if (arg0 == "-c")
                {
                    if (args.Length >= 2)
                    {
                        RunCommandLine(args[1]);
                    }
                    else
                    {
                        SendLinesToPanel("缺少命令参数，用法: MoviePilot-V3 -c <start|stop|restart|update>");
                    }
                    return;
                }
                // 未知参数：提示后退出，不启动面板
                SendLinesToPanel("未知参数: " + args[0] + "（支持: -c start / stop / restart / update）");
                return;
            }

            // 单实例检查：已运行的面板则提示并退出。检查通过后才释放内置资源，
            // 避免在不同目录启动的第二个实例误释放 ico / config（互斥锁被拒时直接退出）
            bool createdNew;
            instanceMutex = new Mutex(true, "Local\\MoviePilot-V3-Panel", out createdNew);
            if (!createdNew)
            {
                MessageBox.Show("MoviePilot 服务管理面板已在运行，请勿重复启动。", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // 首次运行：把内置的 app.ico 与 config 模板（nginx.conf / common.conf）释放到 exe 同级目录
            // （平层布局，已存在不覆盖）
            BundledResources.Deploy();

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            try
            {
                Application.Run(new Form1());
            }
            finally
            {
                instanceMutex.ReleaseMutex();
            }
        }

        /// 将多行日志发送到面板运行日志区；面板未运行时静默（不阻塞、不报错）。
        private static void SendLinesToPanel(params string[] lines)
        {
            if (lines == null || lines.Length == 0) return;
            try
            {
                using (var client = new NamedPipeClientStream(".", AppConfig.PANEL_LOG_PIPE, PipeDirection.Out))
                {
                    client.Connect(500);
                    using (var writer = new StreamWriter(client, Encoding.UTF8) { AutoFlush = true })
                    {
                        foreach (string line in lines)
                        {
                            writer.WriteLine(line);
                        }
                    }
                }
            }
            catch
            {
                // 面板未运行：静默
            }
        }

        /// 命令行模式：执行服务操作（update 与面板"检查MP更新"确认后的升级流程一致），日志实时发送到面板运行日志区。
        private static void RunCommandLine(string command)
        {
            // 尝试连接面板日志管道；面板未运行时连接失败，日志转写文件（logs\cmd.log）
            StreamWriter pipeWriter = null;
            StreamWriter fileWriter = null;
            try
            {
                var client = new NamedPipeClientStream(".", AppConfig.PANEL_LOG_PIPE, PipeDirection.Out);
                client.Connect(500);
                pipeWriter = new StreamWriter(client, Encoding.UTF8) { AutoFlush = true };
            }
            catch
            {
                // 面板未运行：日志写入文件，便于排查命令行模式问题
                try
                {
                    Directory.CreateDirectory(AppConfig.LOGS_DIR);
                    fileWriter = new StreamWriter(Path.Combine(AppConfig.LOGS_DIR, "cmd.log"), true, Encoding.UTF8) { AutoFlush = true };
                }
                catch
                {
                }
            }

            Action<string> log = msg =>
            {
                try
                {
                    if (pipeWriter != null)
                    {
                        pipeWriter.WriteLine(msg);
                    }
                    else if (fileWriter != null)
                    {
                        fileWriter.WriteLine(DateTime.Now.ToString("HH:mm:ss") + " " + msg);
                    }
                }
                catch
                {
                    // 管道中断：后续日志静默丢弃
                }
            };

            try
            {
                switch (command.ToLowerInvariant())
                {
                    case "start":
                        // 环境就绪检查在 StartServices 内完成（缺失的便携版自动下载）
                        ServiceManager.StartServices(log);
                        break;
                    case "stop":
                        ServiceManager.StopServices(log);
                        break;
                    case "restart":
                        // 特例：app.env 中 MOVIEPILOT_AUTO_UPDATE 为 dev/release 时，执行启动检查更新并启动
                        if (IsAutoUpdateEnabled())
                        {
                            UpgradeService.Upgrade(log, (success, message) => log(message));
                            break;
                        }
                        // 与面板"重启服务"一致：先确保环境就绪，再停止服务，最后启动服务（纯重启，不检查更新）
                        EnvironmentSetup.EnsureEnvironment(log);
                        ServiceManager.StopServices(log);
                        ServiceManager.StartServices(log);
                        break;
                    case "update":
                        // 与面板"检查MP更新"确认后的升级流程一致：升级流程内部自行停止服务、更新代码、安装依赖并重启服务
                        UpgradeService.Upgrade(log, (success, message) => log(message));
                        break;
                    default:
                        log("未知命令: " + command + "（支持: start / stop / restart / update）");
                        break;
                }
            }
            catch (Exception ex)
            {
                log("执行失败: " + ex.Message);
            }
            finally
            {
                if (pipeWriter != null) pipeWriter.Dispose();
                if (fileWriter != null) fileWriter.Dispose();
            }
        }

        /// 读取后端配置 app.env 的 MOVIEPILOT_AUTO_UPDATE 值，判断是否为 dev/release 模式（均可升级）
        private static bool IsAutoUpdateEnabled()
        {
            try
            {
                string envFile = Path.Combine(AppConfig.CurrentMpConfDir, "app.env");
                if (!File.Exists(envFile)) return false;
                foreach (string line in File.ReadAllLines(envFile))
                {
                    string trimmed = line.Trim();
                    if (!trimmed.StartsWith("MOVIEPILOT_AUTO_UPDATE=", StringComparison.OrdinalIgnoreCase)) continue;
                    string value = trimmed.Substring("MOVIEPILOT_AUTO_UPDATE=".Length).Trim().Trim('\'', '"');
                    return string.Equals(value, "dev", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(value, "release", StringComparison.OrdinalIgnoreCase);
                }
            }
            catch
            {
                // 读取失败按非 dev/release 处理，走正常重启流程
            }
            return false;
        }
    }
}
