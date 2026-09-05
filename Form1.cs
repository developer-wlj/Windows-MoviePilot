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
        private bool ignoreNextTrayUp; // 双击后待忽略的下一次 MouseUp（第二次点击的弹起，避免 toggle 再隐藏）
        private readonly bool startInTray; // 启动时驻留托盘（构造时读取配置）
        private bool firstShowHandled; // 首次显示请求是否已处理（仅拦截一次，之后 Show() 正常显示）
        private bool loadDone; // Form1_Load 是否已执行（首次显示被吞掉时手动触发；用户 Show() 再次触发 Load 事件时跳过）
        private ConfigForm configForm; // 当前打开的配置窗口（模态）：托盘单击切换时随主窗口一起隐藏/显示
        private FormWindowState lastWindowState = FormWindowState.Normal; // 最小化前的窗口状态（托盘恢复时按此还原，避免最大化状态丢失）
        private readonly StringBuilder pendingLogs = new StringBuilder(); // 窗口隐藏期间的日志缓存（仅 UI 线程访问）：显示时一次性补发，避免隐藏期间逐行 AppendText 拖慢显示
        private const int MaxLogChars = 500 * 1024; // 日志区文本上限：超过后截断为一半，防止无限增长拖慢界面与占用内存
        private const int MaxPendingLogChars = 200 * 1024; // 隐藏期间日志缓存上限：超过后丢弃最旧一半
        private string pendingPanelTag; // 右上角面板提示对应的面板有新版本（点击更新时使用）
        private bool panelUpdateRunning; // 面板自更新流程进行中（防重复触发，退出前拦截）
        private bool updateTipsStarted; // 启动版本提示检测是否已发起（防重复）

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

        // Windows 关机/重启拦截：WM_QUERYENDSESSION 返回 FALSE 阻止本次关机请求，
        // 同时 ShutdownBlockReasonCreate 在系统关机界面（“正在阻止关机”对话框）显示友好原因；
        // 转入后台停止服务并退出进程后，系统检测到无阻止者自动继续关机/重启流程
        private const int WM_QUERYENDSESSION = 0x0011;
        private const int WM_ENDSESSION = 0x0016;
        private const int ENDSESSION_LOGOFF = unchecked((int)0x80000000);
        private bool shutdownCleanupStarted; // 防重复：系统超时后会重发 WM_QUERYENDSESSION

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool ShutdownBlockReasonCreate(IntPtr hWnd, string reason);
        [DllImport("user32.dll")]
        private static extern bool ShutdownBlockReasonDestroy(IntPtr hWnd);

        // 关机优先级：SetProcessShutdownParameters 把面板提到应用最高档 0x300（所有进程默认 0x280）。
        // 系统关机/重启时 csrss 按优先级从高到低向各进程发通知：控制台进程（nginx/python）收到
        // CTRL_SHUTDOWN_EVENT 默认直接退出，且其通知先于面板的 WM_QUERYENDSESSION，导致面板拦截时
        // 服务已被系统提前结束；面板提到 0x300 后最先收到 WM_QUERYENDSESSION，此时服务进程仍存活，
        // 优雅停止链路（WMI 查询 + AttachConsole + Ctrl+Break）才能正常生效
        [DllImport("kernel32.dll")]
        private static extern bool SetProcessShutdownParameters(uint dwLevel, uint dwFlags);
        private const uint SHUTDOWN_NORETRY = 0x00000001; // 终止失败时不弹重试对话框
        private const uint SHUTDOWN_LEVEL_FIRST = 0x300; // 0x300-0x3FF：应用保留的第一关闭范围

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_QUERYENDSESSION)
            {
                // lParam 高位 ENDSESSION_LOGOFF 表示注销登录：不拦截，放行
                bool isLogoff = (unchecked((int)m.LParam) & ENDSESSION_LOGOFF) != 0;
                if (!isLogoff && !shutdownCleanupStarted)
                {
                    shutdownCleanupStarted = true;
                    // 在系统关机界面显示友好提示（必须配合本消息返回 FALSE 才生效）
                    try { ShutdownBlockReasonCreate(Handle, "正在停止 MoviePilot 服务并保存配置，请稍候..."); } catch { }
                    Log("检测到 Windows 关机/重启，正在停止服务并退出...");
                    HandleShutdownCleanup();
                }
                // 返回 FALSE：阻止本次关机请求（后台清理完成后进程退出，系统自动继续关机）
                m.Result = IntPtr.Zero;
                return;
            }
            if (m.Msg == WM_ENDSESSION)
            {
                // 系统已确认关机（未走阻止路径的兜底）：立即退出，避免拖慢系统关机
                if (m.WParam != IntPtr.Zero)
                {
                    Environment.Exit(0);
                }
            }
            base.WndProc(ref m);
        }

        /// 关机/重启后台清理：停止服务（Ctrl+Break 优雅停止）→ 终止下载/命令子进程 →
        /// 恢复睡眠设置 → 移除阻止原因 → 退出进程，让系统自动继续关机流程。
        private void HandleShutdownCleanup()
        {
            // 停止状态刷新定时器：避免清理期间并发启动 PowerShell 进程查询
            try { statusTimer.Stop(); } catch { }
            // 关机清理日志同时写入 logs\shutdown.log：面板马上退出，日志区内容会丢失，
            // 写文件便于重启后排查（每次关机/重启追加，保留最近一次完整链路）
            Action<string> shutdownLog = msg =>
            {
                try
                {
                    // 目录可能不存在（如首次运行/日志被清理）：先创建保证可写
                    Directory.CreateDirectory(AppConfig.LOGS_DIR);
                    File.AppendAllText(Path.Combine(AppConfig.LOGS_DIR, "shutdown.log"),
                        "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] " + msg + Environment.NewLine);
                }
                catch { }
                Log(msg);
            };
            Task.Run(() =>
            {
                // 关机/重启清理：WMI 进程内查询替代 PowerShell，避免关机序列中启动子进程报错弹窗
                try { ServiceManager.StopServices(shutdownLog, useWmi: true); } catch (Exception ex) { shutdownLog("关机停止服务异常: " + ex.Message); }
                try { EnvironmentSetup.KillActiveProcesses(); } catch { }
                try { SetSleepPrevention(false); } catch { }
                try { ShutdownBlockReasonDestroy(Handle); } catch { }
                Environment.Exit(0);
            });
        }

        /// 面板单例：Services 层静态日志入口（Form1.Debug / Form1.Error）使用；
        /// 面板实例创建前（命令行模式）为 null，静态入口静默丢弃
        public static Form1 Instance { get; private set; }

        public Form1()
        {
            Instance = this;
            startInTray = AppSettings.Current.StartMinimizedToTray;

            // 关机优先级提到应用最高档：确保关机/重启时面板先于 nginx/python 收到系统通知，
            // 服务进程仍存活时执行优雅停止（机制详见 P/Invoke 声明处注释）
            try { SetProcessShutdownParameters(SHUTDOWN_LEVEL_FIRST, SHUTDOWN_NORETRY); } catch { }

            // 防白屏闪烁：所有绘制并入 WM_PAINT（WM_ERASEBKGND 不再用系统默认白背景擦除）；
            // 背景色由 OnPaintBackground 全客户区深色填充；
            // 子控件重绘的区域白屏由 CreateParams 的 WS_EX_COMPOSITED 整窗合成消除
            SetStyle(ControlStyles.AllPaintingInWmPaint, true);

            InitializeComponent();
            InitializeTray();

            // 主窗口图标与托盘同源（app.ico 大图标，标题栏/任务栏高 DPI 下保持清晰）
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
            // 优先使用 app.ico 大图标（窗口与托盘同源）；托盘固定小尺寸渲染，
            // 从大图派生 32x32 供系统缩放，比直接缩小 128x128 更清晰
            appIcon = AppConfig.GetAppIcon();

            Icon trayIcon = null;
            if (appIcon != null)
            {
                try
                {
                    trayIcon = new Icon(appIcon, 32, 32);
                }
                catch
                {
                    // 派生失败：直接用原图标
                }
            }

            notifyIcon = new NotifyIcon
            {
                Icon = trayIcon ?? appIcon ?? SystemIcons.Application,
                Text = AppConfig.APP_NAME + " v" + AppConfig.APP_VERSION + " - 运行中",
                Visible = true
            };
            // 左键单击切换窗口显示/隐藏；右键单击仅弹出托盘菜单（ContextMenuStrip 默认行为，不处理）。
            // 单击立即响应（不加延迟）；双击由 WM_LBUTTONDBLCLK 触发 DoubleClick（先于第二次点击的 MouseUp）：
            // 置 ignoreNextTrayUp 吞掉该次 MouseUp，双击收敛为显示——窗口隐藏时双击：第一次点击已显示、
            // 第二次被吞掉，无闪烁；窗口显示时双击会先隐藏再恢复（罕见场景，避免拖慢单击响应）
            notifyIcon.MouseUp += (s, e) =>
            {
                if (e.Button != MouseButtons.Left) return;
                if (ignoreNextTrayUp)
                {
                    ignoreNextTrayUp = false;
                    return;
                }
                ToggleWindow();
            };
            // 双击语义：直接显示主窗口（不 toggle），并吞掉第二次点击的 MouseUp
            notifyIcon.DoubleClick += (s, e) =>
            {
                ignoreNextTrayUp = true;
                ShowWindow();
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

            // 右上角新版本提示：后台检测面板自身 Release 与当前 MP 的官方新标签（见 StartUpdateTipsCheck）
            LayoutUpdateTips();
            StartUpdateTipsCheck();

            // 面板自更新遗留的旧版 exe（MoviePilot-V3-old.exe，更新重启时旧进程退出后文件锁才释放）：
            // 延迟数秒后台尽力删除，不阻塞启动；删除失败留待下次启动重试
            Task.Run(() =>
            {
                try { Thread.Sleep(3000); PanelUpdateService.TryDeleteOldExe(); }
                catch { }
            });
        }

        /// 启动后的新版本提示检测（后台执行，不阻塞界面）：
        /// 勾选了配置"启动时更新版本"时延迟 3 分钟再执行——启动更新可能正在拉取代码 / 装依赖，
        /// 此时检测会与更新流程抢网络，且刚更新完又提示新版本没有意义；未勾选则立即检测。
        /// 检测两项（独立并行）：① 面板仓库 GitHub Release 新版本 → 右上角"面板有vX新版本"；
        /// ② 当前运行版本 MP 的官方新标签 → 右上角"MP有新版本"。
        /// 线程与子进程管理：检测任务跑在线程池后台线程（进程退出自动结束），期间启动的
        /// curl / git 子进程注册到活动进程表，面板退出 / 关机时由 KillActiveProcesses 统一终止，
        /// 不会在应用退出后遗留运行。
        private void StartUpdateTipsCheck()
        {
            if (updateTipsStarted) return;
            updateTipsStarted = true;
            bool delayed = AppSettings.Current.AutoUpdateOnStart;
            Task.Run(() =>
            {
                try
                {
                    if (delayed)
                    {
                        Thread.Sleep(3 * 60 * 1000);
                    }
                    CheckUpdateTips();
                }
                catch
                {
                    // 检测失败静默：不打扰用户，下次启动面板自动重试
                }
            });
        }

        /// 并行执行两项检测（相互独立，避免 git ls-remote 卡顿时拖累 GitHub API 检测），
        /// 完成后在 UI 线程更新右上角提示（窗口可能隐藏/已销毁，均需容错）。
        private void CheckUpdateTips()
        {
            Task<string> tPanel = Task.Run(() => PanelUpdateService.FetchLatestTag(Log));
            Task<bool> tMp = Task.Run(() => UpgradeService.HasNewMpVersion(Log));
            try { Task.WaitAll(tPanel, tMp); } catch { } // 任一项异常不影响另一项结果（下方按 IsFaulted 取值）
            if (IsDisposed) return;
            string tag = tPanel.IsFaulted ? null : tPanel.Result;
            bool panelNew = tag != null && PanelUpdateService.IsNewerThanCurrent(tag);
            bool mpNew = tMp.IsFaulted ? false : tMp.Result;
            try
            {
                BeginInvoke(new Action(() =>
                {
                    if (IsDisposed) return;
                    pendingPanelTag = panelNew ? tag : null;
                    if (lblPanelUpdateTip != null)
                    {
                        // 完整提示文案："面板有v1.0.4新版本"（tag 为 null 时退化为"面板有新版本"占位，不可见无影响）
                        lblPanelUpdateTip.Text = "面板有新版本";
                        lblPanelUpdateTip.Visible = panelNew;
                    }
                    if (lblMpUpdateTip != null)
                    {
                        lblMpUpdateTip.Visible = mpNew;
                    }
                    LayoutUpdateTips();
                }));
            }
            catch
            {
                // 窗口句柄已销毁：放弃更新提示
            }
        }

        /// 右上角更新提示右对齐标题栏同一行：面板提示与 MP 提示水平并排（面板提示贴右缘、
        /// MP 提示在其左），两者都存在时不再上下排列——标题栏高度有限，上下排列占两行会让
        /// 文字显示不全；距右缘 12px、两提示间距 10px。AutoSize=false，宽高用 PreferredSize
        /// 每次实时量度（按当前字体与文本，不依赖 AutoSize 缓存尺寸），避免缩放/字体变化后
        /// 控件尺寸与文字不一致导致文字裁切或不可见；位置在显示 / 窗口尺寸变化时重算。
        private void LayoutUpdateTips()
        {
            if (lblPanelUpdateTip == null || lblMpUpdateTip == null) return;
            // 固定 AutoSize=false：AutoSize 的缓存尺寸可能在字体缩放/窗口布局后与当前字体
            // 量度不一致（表现为文字只显示一半甚至不可见），改用 PreferredSize 实时量度宽高
            lblPanelUpdateTip.AutoSize = false;
            lblMpUpdateTip.AutoSize = false;
            lblPanelUpdateTip.Size = lblPanelUpdateTip.PreferredSize;
            lblMpUpdateTip.Size = lblMpUpdateTip.PreferredSize;
            // 置最前层，防止与铺满标题栏的标题 Label 等兄弟控件重叠时被盖住
            lblPanelUpdateTip.BringToFront();
            lblMpUpdateTip.BringToFront();
            const int margin = 12;
            const int gap = 10;
            const int topY = 15;
            int right = ClientSize.Width - margin;
            if (lblPanelUpdateTip.Visible)
            {
                // 面板提示贴最右，MP 提示随后排在其左（并排一行，不再上下排列）
                lblPanelUpdateTip.Location = new Point(right - lblPanelUpdateTip.Width, topY);
                right = lblPanelUpdateTip.Left - gap;
            }
            if (lblMpUpdateTip.Visible)
            {
                lblMpUpdateTip.Location = new Point(right - lblMpUpdateTip.Width, topY);
            }
        }

        /// 点击"面板有vX新版本"：确认后下载新版 exe → 替换当前 exe → 重启面板（后台执行）。
        private void LblPanelUpdateTip_Click(object sender, EventArgs e)
        {
            string tag = pendingPanelTag;
            if (string.IsNullOrEmpty(tag) || panelUpdateRunning)
            {
                return;
            }
            if (MessageBox.Show(this,
                "发现面板新版本 " + tag + "（当前 v" + AppConfig.APP_VERSION + "）。\n" +
                "是否立即下载并更新？\n\n更新完成后面板将自动退出并重启，正在运行的 MoviePilot 服务不受影响。",
                "面板更新", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }
            RunPanelSelfUpdate(tag);
        }

        /// 点击"MP有新版本"：确认后走与配置窗口"检查MP更新"确认后相同的升级流程
        /// （UpgradeService.Upgrade：内部自行停止服务、更新代码、装依赖并重启服务）。
        private void LblMpUpdateTip_Click(object sender, EventArgs e)
        {
            if (panelUpdateRunning)
            {
                return; // 面板自更新进行中：不与面板重启流程并发
            }
            if (MessageBox.Show(this,
                "检测到选择的运行版本（" + AppSettings.Current.RunVersion + "）有新版本。\n" +
                "是否立即升级？\n\n升级将停止并重启 MoviePilot 服务（含依赖安装与资源同步），耗时可能较长。",
                "升级 MoviePilot", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }
            // 进入升级流程前先撤销提示：防重复触发；升级结束后（无论成败）后台重新检测并恢复提示
            if (lblMpUpdateTip != null)
            {
                lblMpUpdateTip.Visible = false;
            }
            LayoutUpdateTips();
            RunUpgrade();
        }

        /// 后台执行面板自更新：下载（支持 GitHub Token / 代理）→ 校验 exe → 改名旧 exe 为
        /// MoviePilot-V3-old.exe → 新 exe 移入运行目录 → 启动新版并退出当前进程。
        /// 下载 curl 注册活动进程表：中途退出面板会被 KillActiveProcesses 终止，不遗留；
        /// 任何失败自动回滚并恢复界面（成功后进程已退出，无需恢复）。
        private void RunPanelSelfUpdate(string tag)
        {
            if (panelUpdateRunning) return;
            panelUpdateRunning = true;
            SetBusy(true);
            Task.Run(() =>
            {
                try
                {
                    Log("开始更新面板到 " + tag + " ...");
                    string downloaded = PanelUpdateService.DownloadAsset(tag, Log);
                    if (downloaded == null)
                    {
                        LogError("面板更新失败：新版 exe 下载未完成");
                        NotifyPanelUpdateError("面板新版本下载失败，请检查网络 / 代理 / GitHub Token 后重试。");
                        return;
                    }
                    string error = PanelUpdateService.InstallUpdate(downloaded, Log);
                    if (error != null)
                    {
                        LogError("面板更新失败: " + error);
                        NotifyPanelUpdateError(error);
                        return;
                    }
                    Log("面板已更新到 " + tag + "，正在重启...");
                    try
                    {
                        // 当前 exe 路径已被新版占据：直接启动新实例
                        System.Diagnostics.Process.Start(Application.ExecutablePath);
                    }
                    catch (Exception ex)
                    {
                        LogError("启动新版面板失败: " + ex.Message);
                    }
                    // 退出前终止仍在运行的检测子进程（curl / git 等已注册活动进程表），
                    // 避免面板重启后遗留后台命令；新启动的面板进程未注册，不受影响
                    EnvironmentSetup.KillActiveProcesses();
                    // 立即退出本进程（不停止 MoviePilot 服务：它们是独立进程，新版面板会自动接管状态监控）
                    Environment.Exit(0);
                }
                catch (Exception ex)
                {
                    LogError("面板自更新异常: " + ex.Message);
                    NotifyPanelUpdateError("面板自更新过程出现异常:\n" + ex.Message);
                }
            });
        }

        /// 面板更新失败提示（UI 线程弹窗并恢复界面）。
        private void NotifyPanelUpdateError(string message)
        {
            if (IsDisposed) return;
            try
            {
                BeginInvoke(new Action(() =>
                {
                    if (IsDisposed) return;
                    SetBusy(false);
                    panelUpdateRunning = false;
                    MessageBox.Show(this, message, "面板更新失败",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }));
            }
            catch
            {
                // 窗口已销毁：静默
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

        // 后台线程日志合并缓冲（静态）：Debug 开启时子进程输出（ls-remote / pip / git 等）
        // 逐行到达这里；先合并、再由 UI 线程按批消费。逐行 BeginInvoke 在海量输出
        // （如拉取大量标签）时会灌满 UI 消息队列，窗口长时间无法处理消息而显示"无响应"
        private static readonly object LogBatchLock = new object();
        private static readonly StringBuilder LogBatch = new StringBuilder();
        private static bool logFlushPending; // 是否已有批量刷新消息挂起（单飞防抖：任意时刻最多一条）
        private const int MaxFlushChars = 32 * 1024; // 单批刷新上限：防止单次 AppendText 过大阻塞 UI

        /// 日志核心：时间戳 + 追加 + 截断（线程安全，后台线程可直接调用）。
        private void AppendLog(string content)
        {
            string line = "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] " + content + "\r\n";
            if (InvokeRequired)
            {
                // 后台线程：行先合并进缓冲，仅在没有挂起刷新时排一条封送消息；
                // 面板退出过程中句柄可能已销毁：封送失败静默丢弃
                bool shouldFlush = false;
                
                lock (LogBatchLock)
                {
                    LogBatch.Append(line);
                    if (!logFlushPending)
                    {
                        logFlushPending = true;
                        shouldFlush = true;
                    }
                }
                if (shouldFlush)
                {
                    try { BeginInvoke(new Action(FlushLogBatch)); }
                    catch { logFlushPending = false; }
                }
                return;
            }
            AppendLogCore(line);
        }

        /// 批量刷新合并日志（仅 UI 线程执行）：一次封送可携带多行，避免消息风暴。
        private void FlushLogBatch()
        {
            string batch = null;
            bool more = false;
            lock (LogBatchLock)
            {
                logFlushPending = false;
                if (LogBatch.Length == 0) return;
                if (LogBatch.Length > MaxFlushChars)
                {
                    batch = LogBatch.ToString(0, MaxFlushChars);
                    LogBatch.Remove(0, MaxFlushChars);
                    more = true; // 还有剩余：继续排下一次刷新，保证数据最终显示
                }
                else
                {
                    batch = LogBatch.ToString();
                    LogBatch.Clear();
                }
            }
            if (more)
            {
                try { BeginInvoke(new Action(FlushLogBatch)); } catch { }
            }
            AppendLogCore(batch);
        }

        /// 追加文本（仅 UI 线程调用）：先补发隐藏期间缓存，再追加当前批，最后按上限截断。
        private void AppendLogCore(string text)
        {
            if (Visible)
            {
                // 窗口可见：先补发隐藏期间缓存的日志，再追加当前批（隐藏期间零控件操作）
                FlushPendingLogs();
                txtLog.AppendText(text);
                txtLog.SelectionStart = txtLog.TextLength;
                txtLog.ScrollToCaret();
            }
            else
            {
                // 窗口隐藏：只缓存不操作控件，显示时一次性补发（见上）
                pendingLogs.Append(text);
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

        /// 补发窗口隐藏期间缓存的日志（仅 UI 线程调用）。
        private void FlushPendingLogs()
        {
            if (pendingLogs.Length == 0) return;
            txtLog.AppendText(pendingLogs.ToString());
            pendingLogs.Clear();
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

        /// 后台执行"检查MP更新"（配置对话框触发）：git 检查当前运行版本是否有官方新标签。
        /// 无更新 / 检查失败（Git 缺失、代码未克隆、网络异常）时弹窗提示结果；
        /// 检测到新版本时弹窗询问用户，确认后复用 RunUpgrade 升级流程（停止服务 + 更新 + 重启）。
        private void RunCheckMpUpdate()
        {
            SetBusy(true);
            Task.Run(() =>
            {
                string error = null;
                bool hasNew;
                try
                {
                    hasNew = UpgradeService.CheckNewMpVersion(Log);
                }
                catch (Exception ex)
                {
                    hasNew = false;
                    error = "检查更新过程出现异常: " + ex.Message;
                }
                if (IsDisposed) return;
                BeginInvoke(new Action(() =>
                {
                    if (IsDisposed) return;
                    SetBusy(false);
                    if (!hasNew)
                    {
                        // 检查失败给出原因；确认无更新时提示已是最新
                        MessageBox.Show(this, error != null
                            ? "检查失败: " + error + "\n\n当前运行的 " + AppSettings.Current.RunVersion + " 保持不变。"
                            : "当前运行的 " + AppSettings.Current.RunVersion + " 已是最新版本，无需更新。",
                            "检查MP更新", MessageBoxButtons.OK,
                            error != null ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
                        return;
                    }
                    // 检测到官方新版本：询问用户确认后复用升级流程
                    if (MessageBox.Show(this,
                        "检测到运行的 " + AppSettings.Current.RunVersion + " 有官方新版本。\n" +
                        "是否立即升级？\n\n升级将停止并重启 MoviePilot 服务（含依赖安装与资源同步），耗时可能较长。",
                        "检查MP更新", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                    {
                        Log("已取消升级，保持当前版本");
                        return;
                    }
                    RunUpgrade();
                }));
            });
        }

        /// 后台执行升级流程（"MP有新版本"提示 / 配置对话框"检查MP更新"确认后触发），结束后弹窗提示结果。
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
                            if (success)
                            {
                                // 升级成功：撤销右上角"MP有新版本"提示（下次启动重新检测）
                                if (lblMpUpdateTip != null)
                                {
                                    lblMpUpdateTip.Visible = false;
                                }
                                LayoutUpdateTips();
                            }
                            else
                            {
                                // 升级失败：后台重新检测，仍检测到新版本时恢复右上角提示
                                Task.Run(() => { try { CheckUpdateTips(); } catch { } });
                            }
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
                            if (success)
                            {
                                // 修复后已重建到官方最新：撤销右上角"MP有新版本"提示
                                if (lblMpUpdateTip != null)
                                {
                                    lblMpUpdateTip.Visible = false;
                                }
                                LayoutUpdateTips();
                            }
                            else
                            {
                                // 修复失败：后台重新检测，仍检测到新版本时恢复右上角提示
                                Task.Run(() => { try { CheckUpdateTips(); } catch { } });
                            }
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
            LayoutUpdateTips();
            Show();
            // 补发窗口隐藏期间缓存的日志：仅靠日志到达时补发的话，显示后若无新日志，
            // 隐藏期间的日志会一直躺在缓存里，日志区看似空白
            FlushPendingLogs();
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
            if (panelUpdateRunning)
            {
                MessageBox.Show(this, "面板更新正在进行中，请稍候完成后再退出。", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
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
        /// 纯重启，不做版本检查/升级（升级只走配置窗口"检查MP更新"确认后的升级流程与"启动时更新版本"配置项）。
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

        /// 打开配置对话框；"检查MP更新"触发检查与确认升级，端口变化时同步 nginx 配置并重载。
        private void BtnConfig_Click(object sender, EventArgs e)
        {
            int oldNginx = AppSettings.Current.NginxPort;
            int oldBackend = AppSettings.Current.BackendPort;
            string oldRunVersion = AppSettings.Current.RunVersion;

            bool checkUpdateRequested;
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
                    checkUpdateRequested = f.CheckUpdateRequested;
                    fixConflictRequested = f.FixConflictRequested;
                }
                finally
                {
                    configForm = null;
                }
            }

            // 阻止休眠开关变更立即生效（勾选阻止 / 取消恢复），无需重启面板
            ApplySleepPrevention();

            // 运行版本切换：共用端口一次只运行一个版本，重启服务后生效（下次启动自动停旧起新）
            if (oldRunVersion != AppSettings.Current.RunVersion)
            {
                Log("运行版本已切换为 " + AppSettings.Current.RunVersion + "，重启服务后生效");
            }

            statusTimer.Interval = Math.Max(3000, AppSettings.Current.StatusMonitorSec * 1000);

            // 点击"检查MP更新"：配置已在对话框内保存，先按刚保存的运行版本检查官方新标签，
            // 有更新时弹窗询问用户，确认后走升级流程（官方标签更新 + 依赖 + 重启服务）
            if (checkUpdateRequested)
            {
                RunCheckMpUpdate();
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
            LayoutUpdateTips();
        }

        /// 首次显示后（尺寸已稳定）再执行一次按钮行布局。
        private void Form1_Shown(object sender, EventArgs e)
        {
            LayoutButtonRow();
            LayoutUpdateTips();
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
