using System;
using System.Drawing;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MoviePilot_V3.Services;

namespace MoviePilot_V3
{
    /// 主窗口：服务状态、运行日志、控制按钮，以及系统托盘管理。
    public partial class Form1 : Form
    {
        private NotifyIcon notifyIcon;
        private ContextMenuStrip trayMenu;
        private System.Windows.Forms.Timer statusTimer;
        private ToolStripMenuItem miRestart;
        private Icon appIcon;
        private bool allowClose; // 是否允许真正关闭（托盘"退出"菜单触发）
        private Thread pipeThread; // 命名管道监听线程：接收命令行模式（-c）发送的日志
        private readonly bool startInTray; // 启动时驻留托盘（构造时读取配置）
        private bool firstShowHandled; // 首次显示请求是否已处理（仅拦截一次，之后 Show() 正常显示）
        private bool loadDone; // Form1_Load 是否已执行（首次显示被吞掉时手动触发；用户 Show() 再次触发 Load 事件时跳过）
        private ConfigForm configForm; // 当前打开的配置窗口（模态）：托盘单击切换时随主窗口一起隐藏/显示
        private FormWindowState lastWindowState = FormWindowState.Normal; // 最小化前的窗口状态（托盘恢复时按此还原，避免最大化状态丢失）
        private readonly StringBuilder pendingLogs = new StringBuilder(); // 窗口隐藏期间的日志缓存（仅 UI 线程访问）：显示时一次性补发，避免隐藏期间逐行 AppendText 拖慢显示
        private const int MaxLogChars = 500 * 1024; // 日志区文本上限：超过后截断为一半，防止无限增长拖慢界面与占用内存
        private const int MaxPendingLogChars = 200 * 1024; // 隐藏期间日志缓存上限：超过后丢弃最旧一半

        // 阻止 Windows 空闲休眠/睡眠：SetThreadExecutionState（kernel32）——
        // ES_SYSTEM_REQUIRED 阻止系统空闲进入睡眠/休眠，ES_CONTINUOUS 保持该设置直到再次调用；
        // 设置由调用线程持有（UI 线程常驻面板全程有效），退出时单独调用 ES_CONTINUOUS 恢复
        [DllImport("kernel32.dll")]
        private static extern uint SetThreadExecutionState(uint esFlags);
        private const uint ES_CONTINUOUS = 0x80000000;
        private const uint ES_SYSTEM_REQUIRED = 0x00000001;

        /// 按配置应用“阻止休眠和睡眠”（面板启动 / 配置保存后调用，幂等）。
        private static void ApplySleepPrevention()
        {
            SetSleepPrevention(AppSettings.Current.PreventSleep);
        }

        /// 设置 / 解除“阻止休眠和睡眠”（面板退出时强制恢复系统自动睡眠）。
        private static void SetSleepPrevention(bool enabled)
        {
            SetThreadExecutionState(enabled ? (ES_CONTINUOUS | ES_SYSTEM_REQUIRED) : ES_CONTINUOUS);
        }

        /// 面板单例：Services 层静态日志入口（Form1.Debug / Form1.Error）使用；
        /// 面板实例创建前（命令行模式）为 null，静态入口静默丢弃
        public static Form1 Instance { get; private set; }

        public Form1()
        {
            Instance = this;
            startInTray = AppSettings.Current.StartMinimizedToTray;

            // 防白屏闪烁：所有绘制并入 WM_PAINT（WM_ERASEBKGND 不再用系统默认白背景擦除）；
            // 背景色由 OnPaintBackground 全客户区深色填充；
            // 子控件重绘的区域白屏由 CreateParams 的 WS_EX_COMPOSITED 整窗合成消除
            SetStyle(ControlStyles.AllPaintingInWmPaint, true);

            InitializeComponent();
            InitializeTray();

            // 主窗口图标与托盘一致（exe 内嵌清单图标优先，app.ico 文件回退）
            if (appIcon != null)
            {
                Icon = appIcon;
            }

            // 按配置的监控间隔刷新服务状态（默认 5 秒，可在配置菜单中调整；最小 3 秒）
            statusTimer = new System.Windows.Forms.Timer();
            statusTimer.Interval = Math.Max(3000, AppSettings.Current.StatusMonitorSec * 1000);
            statusTimer.Tick += (s, e) => RefreshStatus();
            statusTimer.Start();

            // 组件统一交由 components 容器管理（Designer 未初始化容器时兜底创建）
            if (components == null) components = new System.ComponentModel.Container();
            components.Add(statusTimer);
            components.Add(notifyIcon);
        }

        /// 初始化托盘图标与托盘菜单。
        private void InitializeTray()
        {
            // 优先使用 exe 内嵌图标（清单图标）；提取失败时回退脚本目录下的 app.ico，再回退系统默认图标
            appIcon = AppConfig.GetAppIcon();

            notifyIcon = new NotifyIcon
            {
                Icon = appIcon ?? SystemIcons.Application,
                Text = AppConfig.APP_NAME + " v" + AppConfig.APP_VERSION + " - 运行中",
                Visible = true
            };
            // 左键单击切换窗口显示/隐藏；右键单击仅弹出托盘菜单（ContextMenuStrip 默认行为，不处理）
            notifyIcon.MouseUp += (s, e) =>
            {
                if (e.Button == MouseButtons.Left)
                {
                    ToggleWindow();
                }
            };

            // 托盘菜单（不显示左侧对勾/选中标记区域，菜单项不带键盘助记符）
            trayMenu = new ContextMenuStrip();
            trayMenu.ShowCheckMargin = true;
            trayMenu.ShowImageMargin = false;
            trayMenu.Items.Add(new ToolStripMenuItem(AppConfig.APP_NAME + " 服务管理器") { Enabled = false });
            trayMenu.Items.Add(new ToolStripMenuItem("版本: " + AppConfig.APP_VERSION) { Enabled = false });
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add(new ToolStripMenuItem("显示主窗口", null, (s, e) => ShowWindow()));
            trayMenu.Items.Add(new ToolStripMenuItem("打开站点", null, (s, e) => OpenSite()));
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add(new ToolStripMenuItem("打开MP日志目录", null, (s, e) => OpenDirectory(AppConfig.CurrentMpLogDir, "已打开MP_Log目录")));
            trayMenu.Items.Add(new ToolStripMenuItem("打开MP配置目录", null, (s, e) => OpenDirectory(AppConfig.CurrentMpConfDir, "已打开MP_Config目录")));
            trayMenu.Items.Add(new ToolStripMenuItem("打开面板目录", null, (s, e) => OpenDirectory(AppConfig.BASE_DIR, "已打开面板目录")));
            trayMenu.Items.Add(new ToolStripSeparator());
            miRestart = new ToolStripMenuItem("重启服务", null, (s, e) => RestartServices());
            trayMenu.Items.Add(miRestart);
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add(new ToolStripMenuItem("退出", null, (s, e) => ExitApplication()));

            notifyIcon.ContextMenuStrip = trayMenu;
        }

        /// <summary>
        /// WS_EX_COMPOSITED：子控件与窗体在同一合成层统一呈现，子控件各自重绘不再单独上屏，
        /// 消除深色窗体上子控件（标题 / 状态块 / 日志块）重绘瞬间的区域白屏（内容已绘制、
        /// 边缘被系统默认色重绘形成的“白色 margin”）。
        /// 注意：本窗体所有 TextBox 均为 ReadOnly（无文本输入光标），不受合成层 caret 兼容问题影响。
        /// </summary>
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x02000000; // WS_EX_COMPOSITED
                return cp;
            }
        }

        /// 背景绘制：直接用窗体背景色填充（不调用 base 的默认背景擦除），
        /// 避免窗口显示 / 从托盘恢复瞬间系统用默认白背景擦除造成的白屏闪烁。
        /// 注意必须填充整个 ClientRectangle 而非 e.ClipRectangle：双缓冲位图中子控件
        /// 区域不在无效区域内（父窗口无需绘制子控件遮挡部分），只填 ClipRectangle 会
        /// 使位图中子控件区域保持初始白色，提交后子控件尚未绘制，形成块状白屏。
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            using (SolidBrush brush = new SolidBrush(BackColor))
            {
                e.Graphics.FillRectangle(brush, ClientRectangle);
            }
        }

        /// 启动驻留托盘：吞掉 Application.Run 的首次显示请求——窗口从未进入可见状态（不调用 ShowWindow），
        /// 因此不会有任何窗口闪烁。
        /// 关键时序：必须先由 UI 线程创建窗体句柄再执行初始化。CreateControl() 在窗体不可见时
        /// 不会创建句柄，须用 CreateHandle() 强制创建；句柄归属 UI 线程后，后台线程 Log 的
        /// InvokeRequired/BeginInvoke 才能正确封送，否则句柄可能在后台线程创建，窗口消息无人
        /// 处理，表现为界面卡死、日志不显示。
        /// 注意不能改为 Load 中 Hide()：SetVisibleCore(true) 触发 OnLoad 后仍会把可见状态置回 true，窗口会闪一下再隐藏。
        protected override void SetVisibleCore(bool value)
        {
            if (startInTray && value && !firstShowHandled)
            {
                firstShowHandled = true;
                // 由 UI 线程强制创建窗口句柄（不显示）：后续后台线程 Log 的
                // InvokeRequired / BeginInvoke 依赖句柄归属本线程，才能正确封送
                if (!IsDisposed && !IsHandleCreated)
                {
                    CreateHandle();
                }
                // 手动触发 Form1_Load 完成面板初始化（此时句柄已创建，布局尺寸正确）
                OnLoad(EventArgs.Empty);
                return; // 保持隐藏状态
            }
            base.SetVisibleCore(value);
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            // 首次显示被吞掉时已手动执行过 Load；用户 Show() 会再次触发 Load 事件，此处跳过避免重复初始化
            if (loadDone)
            {
                return;
            }
            loadDone = true;

            // 监听命名管道：命令行模式（MoviePilot-V3 -c xxx）的日志实时显示到运行日志区
            StartLogPipeServer();

            // 按配置阻止 Windows 空闲休眠/睡眠（面板运行期间生效，退出时在 FormClosing 恢复）
            ApplySleepPrevention();

            // AutoScale 已完成，先按实际尺寸布局一次按钮行
            LayoutButtonRow();

            // 按配置开关执行启动任务：先检查更新（可选），再自动启动服务（可选）。
            // 注意：不在此处做环境下载（首次运行环境在点击"启动服务"时才准备，避免打开面板即下载）
            bool autoUpdate = AppSettings.Current.AutoUpdateOnStart;
            bool autoStart = AppSettings.Current.AutoStartServices;
            if (autoUpdate || autoStart)
            {
                RunTask(() =>
                {
                    if (autoUpdate)
                    {
                        UpgradeService.CheckUpdateOnStart(Log);
                    }
                    if (autoStart)
                    {
                        ServiceManager.StartServices(Log);
                    }
                });
            }
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            // 系统关机或任务管理器结束时放行
            if (e.CloseReason == CloseReason.WindowsShutDown ||
                e.CloseReason == CloseReason.TaskManagerClosing)
            {
                return;
            }

            // 点击 X 时最小化到托盘，不退出
            if (!allowClose)
            {
                e.Cancel = true;
                MinimizeToTray();
            }
            else
            {
                // 退出放行前：终止面板启动的下载/命令子进程（curl/tar/git/pip），
                // 防止下载等长任务在面板退出后遗留运行
                EnvironmentSetup.KillActiveProcesses();
                // 退出前恢复系统自动睡眠/休眠（若配置了阻止）
                SetSleepPrevention(false);
            }
        }

        /// 追加一行带时间戳的运行日志（线程安全，后台线程可直接调用）。INFO 级别：主流程状态。
        public void Log(string msg)
        {
            AppendLog("[INFO] " + msg);
        }

        /// ERROR 级别：失败 / 异常等需要关注的信息。
        public void LogError(string msg)
        {
            AppendLog("[ERROR] " + msg);
        }

        /// DEBUG 级别：仅配置“打印Debug日志”开启时输出（uv / pip / curl / git 等子进程命令输出），
        /// 关闭时直接丢弃，不占用日志区。
        public void LogDebug(string msg)
        {
            if (!AppSettings.Current.DebugLog)
            {
                return;
            }
            AppendLog("[DEBUG] " + msg);
        }

        /// DEBUG 级别静态入口：Services 层子进程（uv / pip / curl / git）输出逐行转发，
        /// 未开启 Debug 日志时静默丢弃（面板单例未创建时同样丢弃）。
        public static void Debug(string msg)
        {
            Form1 f = Instance;
            if (f != null) f.LogDebug(msg);
        }

        /// ERROR 级别静态入口：Services 层失败提示。
        public static void Error(string msg)
        {
            Form1 f = Instance;
            if (f != null) f.LogError(msg);
        }

        /// 日志核心：时间戳 + 追加 + 截断（线程安全，后台线程可直接调用）。
        private void AppendLog(string content)
        {
            if (InvokeRequired)
            {
                // 面板退出过程中句柄可能已销毁：封送失败静默丢弃（进程本就要退出，避免后台线程未处理异常）
                try { BeginInvoke(new Action<string>(AppendLog), content); } catch { }
                return;
            }
            string line = "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] " + content + "\r\n";
            if (Visible)
            {
                // 窗口可见：先补发隐藏期间缓存的日志，再追加当前行（隐藏期间零控件操作）
                if (pendingLogs.Length > 0)
                {
                    txtLog.AppendText(pendingLogs.ToString());
                    pendingLogs.Clear();
                }
                txtLog.AppendText(line);
                txtLog.SelectionStart = txtLog.TextLength;
                txtLog.ScrollToCaret();
            }
            else
            {
                // 窗口隐藏：只缓存不操作控件，显示时一次性补发（见上）
                pendingLogs.Append(line);
                if (pendingLogs.Length > MaxPendingLogChars)
                {
                    pendingLogs.Remove(0, pendingLogs.Length - MaxPendingLogChars / 2);
                }
            }
            // 日志区上限：超过后截断为一半（低频触发，全量替换可接受）
            if (txtLog.TextLength > MaxLogChars)
            {
                txtLog.Text = txtLog.Text.Substring(txtLog.TextLength - MaxLogChars / 2);
            }
        }

        /// 启动命名管道服务器：命令行模式（-c）连接后把日志实时显示到运行日志区。
        private void StartLogPipeServer()
        {
            pipeThread = new Thread(PipeServerLoop);
            pipeThread.IsBackground = true;
            pipeThread.Start();
        }

        /// 命名管道监听循环：接收一行日志显示到运行日志区，断开后继续等待下一个连接。
        private void PipeServerLoop()
        {
            while (true)
            {
                try
                {
                    using (var server = new NamedPipeServerStream(AppConfig.PANEL_LOG_PIPE, PipeDirection.In, 1))
                    {
                        server.WaitForConnection();
                        using (var reader = new StreamReader(server, Encoding.UTF8))
                        {
                            string line;
                            while ((line = reader.ReadLine()) != null)
                            {
                                Log(line);
                            }
                        }
                    }
                }
                catch
                {
                    // 连接异常：短暂等待后继续监听
                    Thread.Sleep(100);
                }
            }
        }

        /// 在后台线程执行耗时操作，期间禁用控制按钮，异常写入日志。
        private void RunTask(Action action)
        {
            SetBusy(true);
            Task.Run(action).ContinueWith(t =>
            {
                if (t.IsFaulted)
                {
                    Log("错误: " + (t.Exception != null ? t.Exception.GetBaseException().Message : "未知异常"));
                }
                if (!IsDisposed)
                {
                    BeginInvoke(new Action(() => SetBusy(false)));
                }
            });
        }

        /// 切换忙碌状态：禁用/启用服务控制按钮与托盘菜单项。
        private void SetBusy(bool busy)
        {
            btnStart.Enabled = !busy;
            btnStop.Enabled = !busy;
            btnRestart.Enabled = !busy;
            btnConfig.Enabled = !busy;
            if (miRestart != null) miRestart.Enabled = !busy;
        }

        /// 后台执行升级流程（配置对话框"立即升级版本"触发），结束后弹窗提示结果。
        private void RunUpgrade()
        {
            SetBusy(true);
            Task.Run(() =>
            {
                try
                {
                    UpgradeService.Upgrade(Log, (success, message) =>
                    {
                        if (IsDisposed) return;
                        if (success) Log("升级成功: " + message);
                        else LogError("升级失败: " + message);
                        BeginInvoke(new Action(() =>
                        {
                            SetBusy(false);
                            MessageBox.Show(this, message, success ? "升级成功" : "错误",
                                MessageBoxButtons.OK, success ? MessageBoxIcon.Information : MessageBoxIcon.Error);
                        }));
                    });
                }
                catch (Exception ex)
                {
                    LogError("升级过程异常: " + ex.Message);
                    if (!IsDisposed)
                    {
                        BeginInvoke(new Action(() =>
                        {
                            SetBusy(false);
                            MessageBox.Show(this, "升级过程出现异常:\n" + ex.Message, "错误",
                                MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }));
                    }
                }
            });
        }

        /// 后台执行代码冲突修复流程（配置对话框"代码冲突时点我"触发），结束后弹窗提示结果。
        private void RunFixConflict()
        {
            SetBusy(true);
            Task.Run(() =>
            {
                try
                {
                    UpgradeService.FixCodeConflict(Log, (success, message) =>
                    {
                        if (IsDisposed) return;
                        if (success) Log("冲突修复成功: " + message);
                        else LogError("冲突修复失败: " + message);
                        BeginInvoke(new Action(() =>
                        {
                            SetBusy(false);
                            MessageBox.Show(this, message, success ? "修复成功" : "错误",
                                MessageBoxButtons.OK, success ? MessageBoxIcon.Information : MessageBoxIcon.Error);
                        }));
                    });
                }
                catch (Exception ex)
                {
                    LogError("冲突修复过程异常: " + ex.Message);
                    if (!IsDisposed)
                    {
                        BeginInvoke(new Action(() =>
                        {
                            SetBusy(false);
                            MessageBox.Show(this, "冲突修复过程出现异常:\n" + ex.Message, "错误",
                                MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }));
                    }
                }
            });
        }

        /// 单击托盘图标：窗口隐藏或最小化在任务栏时显示主窗口，正常显示（含最大化）时隐藏到托盘；
        /// 配置窗口（模态对话框）打开时随主窗口一起隐藏/显示。
        private void ToggleWindow()
        {
            if (Visible && WindowState != FormWindowState.Minimized)
            {
                MinimizeToTray();
                // 配置窗口跟随主窗口一起隐藏，避免只隐藏主窗口而对话框仍悬浮
                if (configForm != null && configForm.Visible)
                {
                    configForm.Hide();
                }
            }
            else
            {
                ShowWindow();
                // 恢复时一起显示配置窗口（Show 会激活对话框，便于继续配置操作）
                if (configForm != null && !configForm.Visible)
                {
                    configForm.Show();
                }
            }
        }

        /// 显示主窗口并刷新状态。
        private void ShowWindow()
        {
            // 显示前重新布局，确保按钮行位置正确
            LayoutButtonRow();
            Show();
            // 仅当窗口处于最小化状态（点最小化按钮到任务栏后从托盘恢复）时按最小化前的状态还原：
            // 普通窗口还原普通、最大化窗口还原最大化；无条件设置 Normal 会把最大化窗口还原为
            // 最大化前的尺寸，且 Show() 先以最大化显示再缩小，产生一闪而过的跳动
            if (WindowState == FormWindowState.Minimized)
            {
                WindowState = lastWindowState;
            }
            Activate();
            // 状态查询走 PowerShell 子进程（约 0.5 秒）：异步刷新避免拖慢窗口显示
            // （原同步调用在 UI 线程执行，每次点击托盘图标显示窗口都会卡顿半秒以上）
            RefreshStatus();
        }

        /// 最小化到系统托盘。
        private void MinimizeToTray()
        {
            Hide();
            notifyIcon.ShowBalloonTip(1000, AppConfig.APP_NAME, "已最小化到系统托盘", ToolTipIcon.Info);
        }

        /// 打开站点。
        private void OpenSite()
        {
            try
            {
                System.Diagnostics.Process.Start(AppConfig.SITE_URL);
                Log("已打开站点: " + AppConfig.SITE_URL);
            }
            catch (Exception ex)
            {
                Log("打开站点失败: " + ex.Message);
            }
        }

        /// 打开指定目录（存在时）。
        private void OpenDirectory(string dir, string message)
        {
            if (Directory.Exists(dir))
            {
                System.Diagnostics.Process.Start("explorer.exe", "\"" + dir + "\"");
                Log(message);
            }
            else
            {
                Log("目录不存在: " + dir);
            }
        }

        private bool statusQuerying; // 状态查询进行中（防止定时器重叠触发并发查询）

        /// 刷新服务状态面板。状态查询通过 PowerShell 子进程完成（约 0.5 秒），
        /// 放后台线程执行避免冻结界面；查询期间再次触发则跳过本次（下次定时器到点自然补上）。
        private void RefreshStatus()
        {
            if (!txtStatus.IsHandleCreated || statusQuerying)
            {
                return;
            }
            statusQuerying = true;
            Task.Run(() =>
            {
                string text = ServiceManager.GetStatusText();
                try
                {
                    BeginInvoke(new Action(() =>
                    {
                        statusQuerying = false;
                        if (txtStatus.IsHandleCreated)
                        {
                            txtStatus.Text = text;
                        }
                    }));
                }
                catch (InvalidOperationException)
                {
                    statusQuerying = false; // 窗口句柄已销毁，放弃本次刷新
                }
            });
        }

        /// 退出应用：确认后停止服务再退出。
        private void ExitApplication()
        {
            DialogResult result = MessageBox.Show(this, "确定要退出并停止所有服务吗？", "确认退出",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (result != DialogResult.Yes)
            {
                return;
            }

            allowClose = true;
            SetBusy(true);
            // 立即隐藏窗口并提示，避免停止服务（优雅退出最长可达数十秒）期间界面停留无反馈
            Hide();
            notifyIcon.ShowBalloonTip(1000, AppConfig.APP_NAME, "正在停止服务并退出...", ToolTipIcon.Info);
            Task.Run(() => ServiceManager.StopServices(Log)).ContinueWith(t =>
            {
                if (IsDisposed) return;
                BeginInvoke(new Action(() =>
                {
                    notifyIcon.Visible = false;
                    statusTimer.Stop();
                    Close();
                }));
            });
        }

        // ---------- 按钮事件 ----------

        private void BtnStart_Click(object sender, EventArgs e) => RunTask(() => ServiceManager.StartServices(Log));

        private void BtnStop_Click(object sender, EventArgs e) => RunTask(() => ServiceManager.StopServices(Log));

        private void BtnRestart_Click(object sender, EventArgs e) => RestartServices();

        /// 重启服务：先确保运行环境就绪（缺失的便携版自动下载），再停止服务，最后启动服务。
        /// 纯重启，不做版本检查/升级（升级只走配置窗口"立即升级版本"与"启动时更新版本"配置项）。
        /// 主窗口按钮与托盘右键菜单共用入口，统一在此做二次确认。
        private void RestartServices()
        {
            if (MessageBox.Show(this, "确定要重启服务吗？",
                "确认重启", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
            {
                return;
            }
            RunTask(() =>
            {
                EnvironmentSetup.EnsureEnvironment(Log);
                ServiceManager.StopServices(Log);
                ServiceManager.StartServices(Log);
            });
        }

        private void BtnSite_Click(object sender, EventArgs e) => OpenSite();

        /// 打开配置对话框；"立即升级版本"触发升级，端口变化时同步 nginx 配置并重载。
        private void BtnConfig_Click(object sender, EventArgs e)
        {
            int oldNginx = AppSettings.Current.NginxPort;
            int oldBackend = AppSettings.Current.BackendPort;
            string oldRunVersion = AppSettings.Current.RunVersion;

            bool upgradeRequested;
            bool fixConflictRequested;
            using (ConfigForm f = new ConfigForm())
            {
                configForm = f; // 记录当前打开的配置窗口：托盘单击隐藏/显示主窗口时跟随处理
                try
                {
                    if (f.ShowDialog(this) != DialogResult.OK)
                    {
                        return;
                    }
                    upgradeRequested = f.UpgradeRequested;
                    fixConflictRequested = f.FixConflictRequested;
                }
                finally
                {
                    configForm = null;
                }
            }

            Log("配置已保存: " + AppSettings.ConfigPath);

            // 阻止休眠开关变更立即生效（勾选阻止 / 取消恢复），无需重启面板
            ApplySleepPrevention();

            // 运行版本切换：共用端口一次只运行一个版本，重启服务后生效（下次启动自动停旧起新）
            if (oldRunVersion != AppSettings.Current.RunVersion)
            {
                Log("运行版本已切换为 " + AppSettings.Current.RunVersion + "，重启服务后生效");
            }

            statusTimer.Interval = Math.Max(3000, AppSettings.Current.StatusMonitorSec * 1000);

            // 点击"立即升级版本"：配置已在对话框内保存，直接执行升级（官方标签更新 + 依赖 + 重启服务）
            if (upgradeRequested)
            {
                RunUpgrade();
                return;
            }

            // 点击"代码冲突时点我"：强制签出官方最新 v3，不再并入补丁（丢弃本地冲突的 cherry-pick）
            if (fixConflictRequested)
            {
                RunFixConflict();
                return;
            }

            bool portChanged = oldNginx != AppSettings.Current.NginxPort || oldBackend != AppSettings.Current.BackendPort;

            // 后台串行执行：先应用/清空 git 全局代理（代理配置变更时），端口变化时同步 nginx 端口
            RunTask(() =>
            {
                EnvironmentSetup.ApplyGitProxy(Log);
                if (!portChanged)
                {
                    return;
                }
                if (portChanged)
                {
                    NginxConfigService.ApplyPorts(AppSettings.Current.NginxPort, AppSettings.Current.BackendPort, Log);
                }
            });
        }

        private void BtnHide_Click(object sender, EventArgs e) => MinimizeToTray();

        /// 窗口尺寸变化时重新布局按钮行，并记录最小化前的窗口状态。
        private void Form1_Resize(object sender, EventArgs e)
        {
            // 排除 Minimized：最小化瞬间不覆盖记录，保留最小化前的 Normal / Maximized 状态
            if (WindowState != FormWindowState.Minimized)
            {
                lastWindowState = WindowState;
            }
            LayoutButtonRow();
        }

        /// 首次显示后（尺寸已稳定）再执行一次按钮行布局。
        private void Form1_Shown(object sender, EventArgs e)
        {
            LayoutButtonRow();
        }

        /// 重新计算按钮行位置：水平居中、固定距窗体底部。
        /// 使用控件实际尺寸与间距比例计算，不依赖 AutoScaleFactor，
        /// 避免窗口创建阶段缩放时序导致的尺寸误判（按钮重叠、压住日志框）。
        private void LayoutButtonRow()
        {
            if (btnStart == null || btnHide == null)
            {
                return;
            }

            int btnW = btnStart.Width;
            int hideW = btnHide.Width;
            int gap = Math.Max(10, btnW / 10); // 保持 15:150 的设计间距比例
            int totalWidth = btnW * 5 + hideW + gap * 5;
            int startX = Math.Max(0, (ClientSize.Width - totalWidth) / 2);
            int y = Math.Max(0, ClientSize.Height - btnStart.Height - btnStart.Height / 3);

            btnStart.Location = new Point(startX, y);
            btnStop.Location = new Point(startX + (btnW + gap), y);
            btnRestart.Location = new Point(startX + 2 * (btnW + gap), y);
            btnSite.Location = new Point(startX + 3 * (btnW + gap), y);
            btnConfig.Location = new Point(startX + 4 * (btnW + gap), y);
            btnHide.Location = new Point(startX + 5 * (btnW + gap), y);
        }
    }
}
