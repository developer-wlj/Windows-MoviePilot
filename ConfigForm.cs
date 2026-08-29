using System;
using System.Drawing;
using System.Windows.Forms;

namespace MoviePilot_V3
{
    /// 配置参数对话框：优雅退出超时、Nginx 端口、后端端口、启动开关。
    /// 确定后直接写入 AppSettings.Current 并持久化到 config\app.ini。
    public class ConfigForm : Form
    {
        private NumericUpDown numTimeout;
        private NumericUpDown numNginxPort;
        private NumericUpDown numBackendPort;
        private NumericUpDown numMonitorSec;
        private ComboBox cmbRunVersion;
        private TextBox txtToken;
        private ComboBox cmbProxyType;
        private TextBox txtProxyHost;
        private NumericUpDown numProxyPort;
        private CheckBox chkDebugLog;
        private CheckBox chkAutoUpdate;
        private CheckBox chkAutoStart;
        private CheckBox chkTrayStart;
        private Button btnOK;
        private Button btnCancel;
        private Button btnUpgradeNow;
        private Button btnFixConflict;
        private Icon windowIcon; // 窗口图标：Form.Icon 不会自动释放，Dispose 时显式释放（防 GDI 句柄泄漏）

        /// 点击"立即升级版本"时为 true（配置已保存，调用方据此触发升级流程）
        public bool UpgradeRequested { get; private set; }

        /// 点击"代码冲突时点我"时为 true（配置已保存，调用方据此触发冲突修复流程）
        public bool FixConflictRequested { get; private set; }

        public ConfigForm()
        {
            InitializeForm();
            LoadCurrentSettings();
        }

        /// 释放窗口图标：配置窗口每次打开都会新建图标（AppConfig.GetAppIcon），
        /// Form.Icon 属性不会自动释放，不显式释放会随开关配置窗口反复泄漏 GDI 句柄。
        protected override void Dispose(bool disposing)
        {
            if (disposing && windowIcon != null)
            {
                windowIcon.Dispose();
                windowIcon = null;
            }
            base.Dispose(disposing);
        }

        /// 居中于父窗口：在窗口首次可见之前定位（句柄已创建、尺寸已确定），
        /// 避免 CenterParent 先出现在默认位置再移动造成的一帧闪现。
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            if (Owner != null)
            {
                Location = new Point(Owner.Left + (Owner.Width - Width) / 2,
                                     Owner.Top + (Owner.Height - Height) / 2);
            }
        }

        /// 构建深色主题对话框（纯代码布局，与主窗口风格一致）。
        private void InitializeForm()
        {
            // 防白屏闪烁：绘制并入 WM_PAINT；背景由 OnPaintBackground 深色填充；
            // 子控件重绘的区域白屏由 CreateParams 的 WS_EX_COMPOSITED 整窗合成消除
            SetStyle(ControlStyles.AllPaintingInWmPaint, true);

            Font uiFont = new Font("微软雅黑", 10F);
            Color bg = Color.FromArgb(30, 30, 30);
            Color fg = Color.White;
            Color labelGray = Color.FromArgb(170, 170, 170);

            Text = "配置参数";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            // Manual 而非 CenterParent：CenterParent 会让窗口先在默认位置 (0,0) 显示一帧再居中，
            // 合成层（WS_EX_COMPOSITED）窗口首帧表现为左上角黑色细框闪现；居中改由 OnLoad 中
            // 在窗口可见前手动计算（见 OnLoad），窗口直接出现在最终位置
            StartPosition = FormStartPosition.Manual;
            // 窗口图标与主窗口一致（exe 内嵌清单图标优先，app.ico 文件回退）；
            // 存字段而非局部变量：Dispose 时显式释放
            windowIcon = AppConfig.GetAppIcon();
            if (windowIcon != null)
            {
                Icon = windowIcon;
            }
            // 高度 560：运行版本 + 四行数值配置 + GitHub Token + 代理（类型/地址/端口）+ 提示行 + 三个启动开关 + 操作按钮行 + 确定/取消行
            ClientSize = new Size(480, 560);
            BackColor = bg;
            ForeColor = fg;
            Font = uiFont;

            // 运行版本：标准版 MoviePilot-V3（默认）/ freethreaded 版 MoviePilot-V3-T；
            // 决定启动服务时使用哪套 Python / venv / 后端代码目录（共用端口与前端，一次只运行一个版本）
            Label lblRunVersion = new Label
            {
                Text = "运行版本",
                AutoSize = true,
                ForeColor = labelGray,
                Location = new Point(20, 20)
            };
            cmbRunVersion = new ComboBox
            {
                Location = new Point(230, 16),
                Width = 200,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            cmbRunVersion.Items.AddRange(new object[] { "MoviePilot-V3", "MoviePilot-V3-T" });

            // 优雅退出超时
            Label lblTimeout = new Label
            {
                Text = "优雅退出超时（秒）",
                AutoSize = true,
                ForeColor = labelGray,
                Location = new Point(20, 60)
            };
            numTimeout = new NumericUpDown
            {
                Location = new Point(230, 60),
                Width = 120,
                Minimum = 1,
                Maximum = 600,
                TextAlign = HorizontalAlignment.Right
            };

            // Nginx 端口
            Label lblNginx = new Label
            {
                Text = "Nginx 监听端口",
                AutoSize = true,
                ForeColor = labelGray,
                Location = new Point(20, 100)
            };
            numNginxPort = new NumericUpDown
            {
                Location = new Point(230, 100),
                Width = 120,
                Minimum = 1,
                Maximum = 65535,
                TextAlign = HorizontalAlignment.Right
            };

            // 后端端口
            Label lblBackend = new Label
            {
                Text = "后端监听端口",
                AutoSize = true,
                ForeColor = labelGray,
                Location = new Point(20, 140)
            };
            numBackendPort = new NumericUpDown
            {
                Location = new Point(230, 140),
                Width = 120,
                Minimum = 1,
                Maximum = 65535,
                TextAlign = HorizontalAlignment.Right
            };

            // 服务状态监控间隔（面板定时刷新 nginx / python 存活状态，默认 5 秒）
            Label lblMonitor = new Label
            {
                Text = "状态监控间隔（秒）",
                AutoSize = true,
                ForeColor = labelGray,
                Location = new Point(20, 180)
            };
            numMonitorSec = new NumericUpDown
            {
                Location = new Point(230, 180),
                Width = 120,
                // 最小 3 秒：状态查询实时拉取 PowerShell，低于 3 秒会使查询开销占比过高
                Minimum = 3,
                Maximum = 600,
                TextAlign = HorizontalAlignment.Right
            };

            // 打印 Debug 日志：勾选后 uv / pip / curl / git 等子进程命令输出以 DEBUG 级别
            // 实时显示到面板日志（未勾选时只显示 INFO / ERROR 级别的主流程日志）
            chkDebugLog = new CheckBox
            {
                Text = "打印Debug日志（显示 uv / pip / curl / git 命令输出）",
                AutoSize = true,
                ForeColor = fg,
                Location = new Point(20, 340)
            };

            // GitHub Token（下载 GitHub 资源文件时携带 Authorization 请求头）
            Label lblToken = new Label
            {
                Text = "GitHub Token",
                AutoSize = true,
                ForeColor = labelGray,
                Location = new Point(20, 224)
            };
            txtToken = new TextBox
            {
                Location = new Point(230, 220),
                Width = 230,
                BackColor = Color.White,
                ForeColor = Color.Black,
                BorderStyle = BorderStyle.FixedSingle
            };

            // 代理类型（关闭 / http / socks5）
            Label lblProxyType = new Label
            {
                Text = "代理类型",
                AutoSize = true,
                ForeColor = labelGray,
                Location = new Point(20, 264)
            };
            cmbProxyType = new ComboBox
            {
                Location = new Point(230, 260),
                Width = 90,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            cmbProxyType.Items.AddRange(new object[] { "关闭", "http", "socks5" });

            // 代理地址
            Label lblProxyHost = new Label
            {
                Text = "代理地址",
                AutoSize = true,
                ForeColor = labelGray,
                Location = new Point(20, 304)
            };
            txtProxyHost = new TextBox
            {
                Location = new Point(230, 300),
                Width = 160,
                BackColor = Color.White,
                ForeColor = Color.Black,
                BorderStyle = BorderStyle.FixedSingle
            };

            // 代理端口
            Label lblProxyPort = new Label
            {
                Text = "代理端口",
                AutoSize = true,
                ForeColor = labelGray,
                Location = new Point(330, 304)
            };
            numProxyPort = new NumericUpDown
            {
                Location = new Point(410, 300),
                Width = 50,
                Minimum = 0,
                Maximum = 65535,
                TextAlign = HorizontalAlignment.Right
            };

            // 启动时更新版本（对比官方最新标签，发现新版本自动更新 v3 分支）
            chkAutoUpdate = new CheckBox
            {
                Text = "启动时更新版本（对比官方最新标签，重建 v3 分支）",
                AutoSize = true,
                ForeColor = fg,
                Location = new Point(20, 370)
            };

            // 启动时自动启动 Nginx 和 Python
            chkAutoStart = new CheckBox
            {
                Text = "打开应用自动启动 Nginx 和 Python",
                AutoSize = true,
                ForeColor = fg,
                Location = new Point(20, 400)
            };

            // 启动时驻留系统托盘（不显示主窗口）
            chkTrayStart = new CheckBox
            {
                Text = "启动时驻留托盘（不显示主窗口）",
                AutoSize = true,
                ForeColor = fg,
                Location = new Point(20, 430)
            };

            // 立即升级版本（保存配置后触发 git pull 升级流程）
            btnUpgradeNow = new Button
            {
                Text = "立即升级版本",
                BackColor = Color.FromArgb(60, 60, 60),
                FlatStyle = FlatStyle.Flat,
                ForeColor = fg,
                Location = new Point(20, 464),
                Size = new Size(120, 38)
            };
            btnUpgradeNow.Click += BtnUpgradeNow_Click;

            // 代码冲突修复：靠右对齐窗口右边框（补丁 cherry-pick 与本地旧补丁冲突时：强制重建官方最新 v3，不再并入补丁）
            btnFixConflict = new Button
            {
                Text = "代码冲突时点我",
                BackColor = Color.FromArgb(60, 60, 60),
                FlatStyle = FlatStyle.Flat,
                ForeColor = fg,
                Location = new Point(ClientSize.Width - 160 - 20, 464),
                Size = new Size(160, 38)
            };
            btnFixConflict.Click += BtnFixConflict_Click;

            // 冲突修复说明：操作按钮上方，文字右对齐窗口右边框
            Label lblFixHint = new Label
            {
                Text = "源码运行，不再打入补丁",
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleRight,
                ForeColor = labelGray,
                Location = new Point(20, 434),
                Size = new Size(ClientSize.Width - 40, 20)
            };

            // 确定 / 取消：最底部水平居中
            btnOK = new Button
            {
                Text = "确定",
                DialogResult = DialogResult.OK,
                BackColor = Color.FromArgb(60, 60, 60),
                FlatStyle = FlatStyle.Flat,
                ForeColor = fg,
                Location = new Point(132, 510),
                Size = new Size(100, 38)
            };
            btnOK.Click += BtnOK_Click;

            btnCancel = new Button
            {
                Text = "取消",
                DialogResult = DialogResult.Cancel,
                BackColor = Color.FromArgb(60, 60, 60),
                FlatStyle = FlatStyle.Flat,
                ForeColor = fg,
                Location = new Point(247, 510),
                Size = new Size(100, 38)
            };
            CancelButton = btnCancel;

            Controls.Add(lblRunVersion);
            Controls.Add(cmbRunVersion);
            Controls.Add(lblTimeout);
            Controls.Add(numTimeout);
            Controls.Add(lblNginx);
            Controls.Add(numNginxPort);
            Controls.Add(lblBackend);
            Controls.Add(numBackendPort);
            Controls.Add(lblMonitor);
            Controls.Add(numMonitorSec);
            Controls.Add(lblToken);
            Controls.Add(txtToken);
            Controls.Add(lblProxyType);
            Controls.Add(cmbProxyType);
            Controls.Add(lblProxyHost);
            Controls.Add(txtProxyHost);
            Controls.Add(lblProxyPort);
            Controls.Add(numProxyPort);
            Controls.Add(chkDebugLog);
            Controls.Add(chkAutoUpdate);
            Controls.Add(chkAutoStart);
            Controls.Add(chkTrayStart);
            Controls.Add(btnUpgradeNow);
            Controls.Add(btnFixConflict);
            Controls.Add(lblFixHint);
            Controls.Add(btnOK);
            Controls.Add(btnCancel);
        }

        /// <summary>
        /// WS_EX_COMPOSITED：子控件与窗体统一合成，消除深色对话框子控件重绘瞬间的区域白屏。
        /// 注意：本对话框含文本输入框，若发现输入光标（caret）显示异常，可移除本重写。
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

        /// 背景绘制：直接用窗体背景色填充，避免模态对话框首次显示时的白屏闪烁。
        /// 注意必须填充整个 ClientRectangle 而非 e.ClipRectangle：双缓冲位图中子控件
        /// 区域不在无效区域内，只填 ClipRectangle 会使位图中子控件区域呈白色块。
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            using (SolidBrush brush = new SolidBrush(BackColor))
            {
                e.Graphics.FillRectangle(brush, ClientRectangle);
            }
        }

        /// 将当前配置值回填到控件。
        private void LoadCurrentSettings()
        {
            AppSettings s = AppSettings.Current;

            numTimeout.Value = Clamp(s.ShutdownTimeoutSec, (int)numTimeout.Minimum, (int)numTimeout.Maximum);
            numNginxPort.Value = Clamp(s.NginxPort, (int)numNginxPort.Minimum, (int)numNginxPort.Maximum);
            numBackendPort.Value = Clamp(s.BackendPort, (int)numBackendPort.Minimum, (int)numBackendPort.Maximum);
            numMonitorSec.Value = Clamp(s.StatusMonitorSec, (int)numMonitorSec.Minimum, (int)numMonitorSec.Maximum);
            cmbRunVersion.SelectedItem = s.RunVersion;
            chkDebugLog.Checked = s.DebugLog;
            chkAutoUpdate.Checked = s.AutoUpdateOnStart;
            chkAutoStart.Checked = s.AutoStartServices;
            chkTrayStart.Checked = s.StartMinimizedToTray;
            txtToken.Text = s.GitHubToken;
            cmbProxyType.SelectedIndex = s.ProxyType == "http" ? 1 : (s.ProxyType == "socks5" ? 2 : 0);
            txtProxyHost.Text = s.ProxyHost;
            numProxyPort.Value = Clamp(s.ProxyPort, (int)numProxyPort.Minimum, (int)numProxyPort.Maximum);
        }

        private static decimal Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        /// 代理地址格式校验：IPv4（四段各 0-255）或主机名（localhost / 域名，段内字母数字与连字符）；
        /// 纯数字但不是合法 IPv4（如把端口误填进地址框）拒绝。
        private static bool IsValidProxyHost(string host)
        {
            string[] parts = host.Split('.');
            bool allNumeric = true;
            foreach (string part in parts)
            {
                if (part.Length == 0 || !IsDigits(part))
                {
                    allNumeric = false;
                    break;
                }
            }
            // IPv4：四段全数字，每段 0-255（byte.TryParse 天然拒绝 256 等越界值）
            if (parts.Length == 4 && allNumeric)
            {
                foreach (string part in parts)
                {
                    byte b;
                    if (!byte.TryParse(part, out b)) return false;
                }
                return true;
            }
            // 纯数字但不是合法 IPv4（如 10828）：判定为端口误填，拒绝
            if (allNumeric) return false;
            // 主机名（localhost / 域名）：段内仅字母数字与连字符，不以连字符开头或结尾
            foreach (string part in parts)
            {
                if (part.Length == 0 || part[0] == '-' || part[part.Length - 1] == '-') return false;
                foreach (char c in part)
                {
                    if (!(char.IsLetterOrDigit(c) || c == '-')) return false;
                }
            }
            return true;
        }

        /// 字符串是否全为数字。
        private static bool IsDigits(string s)
        {
            foreach (char c in s)
            {
                if (!char.IsDigit(c)) return false;
            }
            return true;
        }

        /// 立即升级：先保存当前配置，再标记升级请求（对话框以 OK 关闭）。
        private void BtnUpgradeNow_Click(object sender, EventArgs e)
        {
            BtnOK_Click(sender, e);
            if (DialogResult == DialogResult.OK)
            {
                UpgradeRequested = true;
            }
        }

        /// 修复代码冲突：先保存当前配置，再标记冲突修复请求（对话框以 OK 关闭）。
        private void BtnFixConflict_Click(object sender, EventArgs e)
        {
            BtnOK_Click(sender, e);
            if (DialogResult == DialogResult.OK)
            {
                FixConflictRequested = true;
            }
        }

        /// 确定：校验后写回配置并持久化。
        private void BtnOK_Click(object sender, EventArgs e)
        {
            try
            {
                AppSettings s = AppSettings.Current;

                // 代理完整性校验：选了类型但地址/端口不完整或格式错误时提示，
                // 避免静默保存无效配置（BuildProxyUrl 返回 null，表现为日志“已清空 git 全局代理”且下载不走代理）
                if (cmbProxyType.SelectedIndex != 0)
                {
                    string proxyHost = txtProxyHost.Text.Trim();
                    int proxyPort = (int)numProxyPort.Value;
                    if (proxyHost.Length == 0 || proxyPort <= 0)
                    {
                        MessageBox.Show(this, "已选择代理类型，请填写完整的代理地址与端口。", "配置提示",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                    if (!IsValidProxyHost(proxyHost))
                    {
                        MessageBox.Show(this, "代理地址格式无效，请填写 IPv4 地址（如 127.0.0.1）或主机名（如 localhost）。", "配置提示",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                }

                s.ShutdownTimeoutSec = (int)numTimeout.Value;
                s.NginxPort = (int)numNginxPort.Value;
                s.BackendPort = (int)numBackendPort.Value;
                s.StatusMonitorSec = (int)numMonitorSec.Value;
                s.RunVersion = (string)cmbRunVersion.SelectedItem;
                s.DebugLog = chkDebugLog.Checked;
                s.AutoUpdateOnStart = chkAutoUpdate.Checked;
                s.AutoStartServices = chkAutoStart.Checked;
                s.StartMinimizedToTray = chkTrayStart.Checked;
                s.GitHubToken = txtToken.Text.Trim();
                s.ProxyType = cmbProxyType.SelectedIndex == 1 ? "http" : (cmbProxyType.SelectedIndex == 2 ? "socks5" : "");
                s.ProxyHost = txtProxyHost.Text.Trim();
                s.ProxyPort = (int)numProxyPort.Value;
                s.Save();
                DialogResult = DialogResult.OK;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "配置参数无效:\n" + ex.Message, "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
