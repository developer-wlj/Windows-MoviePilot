namespace MoviePilot_V3
{
    partial class Form1
    {
        /// 必需的设计器变量。
        private System.ComponentModel.IContainer components = null;

        private System.Windows.Forms.Label lblTitle;
        // 右上角新版本提示（启动后台检测到新版时显示，点击触发更新；两者都存在时并排一行）
        private System.Windows.Forms.Label lblPanelUpdateTip;
        private System.Windows.Forms.Label lblMpUpdateTip;
        private System.Windows.Forms.GroupBox grpStatus;
        // 服务状态区：自绘状态面板（原多行 TextBox 每次整体赋值刷新时会闪烁，见 StatusPanel.cs 类注释）
        private StatusPanel statusPanel;
        private System.Windows.Forms.GroupBox grpLog;
        // 运行日志区：控制台风格自绘控件（原多行 TextBox 追加日志时会闪烁，见 ConsoleBox.cs 类注释）
        private ConsoleBox logBox;
        private System.Windows.Forms.Button btnStart;
        private System.Windows.Forms.Button btnStop;
        private System.Windows.Forms.Button btnRestart;
        private System.Windows.Forms.Button btnSite;
        private System.Windows.Forms.Button btnConfig;
        private System.Windows.Forms.Button btnHide;

        /// 清理所有正在使用的资源。
        /// <param name="disposing">如果应释放托管资源，为 true；否则为 false。</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                if (appIcon != null)
                {
                    appIcon.Dispose();
                }
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows 窗体设计器生成的代码
        /// 设计器支持所需的方法 - 不要修改
        /// 使用代码编辑器修改此方法的内容。
        private void InitializeComponent()
        {
            this.lblTitle = new System.Windows.Forms.Label();
            this.grpStatus = new System.Windows.Forms.GroupBox();
            this.statusPanel = new StatusPanel();
            this.grpLog = new System.Windows.Forms.GroupBox();
            this.logBox = new ConsoleBox();
            this.btnStart = new System.Windows.Forms.Button();
            this.btnStop = new System.Windows.Forms.Button();
            this.btnRestart = new System.Windows.Forms.Button();
            this.btnSite = new System.Windows.Forms.Button();
            this.btnConfig = new System.Windows.Forms.Button();
            this.btnHide = new System.Windows.Forms.Button();
            this.lblPanelUpdateTip = new System.Windows.Forms.Label();
            this.lblMpUpdateTip = new System.Windows.Forms.Label();
            this.grpStatus.SuspendLayout();
            this.grpLog.SuspendLayout();
            this.SuspendLayout();
            // 
            // lblTitle
            // 
            this.lblTitle.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.lblTitle.Font = new System.Drawing.Font("微软雅黑", 14F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.lblTitle.ForeColor = System.Drawing.Color.White;
            // 标题背景显式设为与窗体一致的深色（Label 默认 Transparent 依赖父窗体背景抓取，
            // 双缓冲合成时序下可能闪现白底，显式设置后无此依赖）
            this.lblTitle.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(30)))), ((int)(((byte)(30)))), ((int)(((byte)(30)))));
            this.lblTitle.Location = new System.Drawing.Point(15, 15);
            this.lblTitle.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.lblTitle.Name = "lblTitle";
            this.lblTitle.Size = new System.Drawing.Size(1098, 45);
            this.lblTitle.TabIndex = 0;
            this.lblTitle.Text = "MoviePilot 服务管理控制台 v" + AppConfig.APP_VERSION;
            this.lblTitle.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // grpStatus
            // 
            this.grpStatus.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.grpStatus.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(30)))), ((int)(((byte)(30)))), ((int)(((byte)(30)))));
            this.grpStatus.Controls.Add(this.statusPanel);
            this.grpStatus.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(170)))), ((int)(((byte)(170)))), ((int)(((byte)(170)))));
            this.grpStatus.Location = new System.Drawing.Point(15, 75);
            this.grpStatus.Margin = new System.Windows.Forms.Padding(4);
            this.grpStatus.Name = "grpStatus";
            this.grpStatus.Padding = new System.Windows.Forms.Padding(4);
            this.grpStatus.Size = new System.Drawing.Size(1098, 180);
            this.grpStatus.TabIndex = 1;
            this.grpStatus.TabStop = false;
            this.grpStatus.Text = "服务状态";
            // 
            // statusPanel
            // 
            // 自绘状态面板：状态圆点 + 服务名 + 状态文本（运行中绿 / 已停止红），
            // BackColor 与 grpStatus 一致故无边框也浑然一体（原 BorderStyle/Multiline/ReadOnly 等 TextBox 专有属性已不适用）
            this.statusPanel.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.statusPanel.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(30)))), ((int)(((byte)(30)))), ((int)(((byte)(30)))));
            this.statusPanel.Font = new System.Drawing.Font("Consolas", 11F);
            this.statusPanel.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(204)))), ((int)(((byte)(204)))), ((int)(((byte)(204)))));
            this.statusPanel.Location = new System.Drawing.Point(23, 37);
            this.statusPanel.Margin = new System.Windows.Forms.Padding(4);
            this.statusPanel.Name = "statusPanel";
            this.statusPanel.Size = new System.Drawing.Size(1053, 120);
            this.statusPanel.TabIndex = 2;
            this.statusPanel.TabStop = false;
            // 
            // grpLog
            // 
            this.grpLog.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.grpLog.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(30)))), ((int)(((byte)(30)))), ((int)(((byte)(30)))));
            this.grpLog.Controls.Add(this.logBox);
            this.grpLog.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(170)))), ((int)(((byte)(170)))), ((int)(((byte)(170)))));
            this.grpLog.Location = new System.Drawing.Point(15, 270);
            this.grpLog.Margin = new System.Windows.Forms.Padding(4);
            this.grpLog.Name = "grpLog";
            this.grpLog.Padding = new System.Windows.Forms.Padding(4);
            this.grpLog.Size = new System.Drawing.Size(1098, 343);
            this.grpLog.TabIndex = 3;
            this.grpLog.TabStop = false;
            this.grpLog.Text = "运行日志";
            // 
            // logBox
            // 
            // 控制台风格自绘日志区：深灰底(#2D2D2D，沿用原 TextBox 底色) + 等宽字体 + 按级别着色，
            // 边框与滚动条均由控件内部绘制/承载
            // （原 BorderStyle/ScrollBars/ReadOnly 等 TextBox 专有属性已不适用）
            this.logBox.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.logBox.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(45)))), ((int)(((byte)(45)))));
            this.logBox.Font = new System.Drawing.Font("Consolas", 9F);
            this.logBox.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(204)))), ((int)(((byte)(204)))), ((int)(((byte)(204)))));
            this.logBox.Location = new System.Drawing.Point(15, 38);
            this.logBox.Margin = new System.Windows.Forms.Padding(4);
            this.logBox.Name = "logBox";
            this.logBox.Size = new System.Drawing.Size(1066, 289);
            this.logBox.TabIndex = 4;
            this.logBox.TabStop = false;
            // 
            // btnStart
            // 
            this.btnStart.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(60)))), ((int)(((byte)(60)))), ((int)(((byte)(60)))));
            this.btnStart.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnStart.ForeColor = System.Drawing.Color.White;
            this.btnStart.Location = new System.Drawing.Point(105, 630);
            this.btnStart.Margin = new System.Windows.Forms.Padding(4);
            this.btnStart.Name = "btnStart";
            this.btnStart.Size = new System.Drawing.Size(150, 45);
            this.btnStart.TabIndex = 5;
            this.btnStart.Text = "启动服务";
            this.btnStart.UseVisualStyleBackColor = false;
            this.btnStart.Click += new System.EventHandler(this.BtnStart_Click);
            // 
            // btnStop
            // 
            this.btnStop.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(60)))), ((int)(((byte)(60)))), ((int)(((byte)(60)))));
            this.btnStop.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnStop.ForeColor = System.Drawing.Color.White;
            this.btnStop.Location = new System.Drawing.Point(263, 630);
            this.btnStop.Margin = new System.Windows.Forms.Padding(4);
            this.btnStop.Name = "btnStop";
            this.btnStop.Size = new System.Drawing.Size(150, 45);
            this.btnStop.TabIndex = 6;
            this.btnStop.Text = "停止服务";
            this.btnStop.UseVisualStyleBackColor = false;
            this.btnStop.Click += new System.EventHandler(this.BtnStop_Click);
            // 
            // btnRestart
            // 
            this.btnRestart.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(60)))), ((int)(((byte)(60)))), ((int)(((byte)(60)))));
            this.btnRestart.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnRestart.ForeColor = System.Drawing.Color.White;
            this.btnRestart.Location = new System.Drawing.Point(421, 630);
            this.btnRestart.Margin = new System.Windows.Forms.Padding(4);
            this.btnRestart.Name = "btnRestart";
            this.btnRestart.Size = new System.Drawing.Size(150, 45);
            this.btnRestart.TabIndex = 7;
            this.btnRestart.Text = "重启服务";
            this.btnRestart.UseVisualStyleBackColor = false;
            this.btnRestart.Click += new System.EventHandler(this.BtnRestart_Click);
            // 
            // btnSite
            // 
            this.btnSite.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(60)))), ((int)(((byte)(60)))), ((int)(((byte)(60)))));
            this.btnSite.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnSite.ForeColor = System.Drawing.Color.White;
            this.btnSite.Location = new System.Drawing.Point(579, 630);
            this.btnSite.Margin = new System.Windows.Forms.Padding(4);
            this.btnSite.Name = "btnSite";
            this.btnSite.Size = new System.Drawing.Size(150, 45);
            this.btnSite.TabIndex = 8;
            this.btnSite.Text = "打开站点";
            this.btnSite.UseVisualStyleBackColor = false;
            this.btnSite.Click += new System.EventHandler(this.BtnSite_Click);
            // 
            // btnConfig
            // 
            this.btnConfig.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(60)))), ((int)(((byte)(60)))), ((int)(((byte)(60)))));
            this.btnConfig.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnConfig.ForeColor = System.Drawing.Color.White;
            this.btnConfig.Location = new System.Drawing.Point(737, 630);
            this.btnConfig.Margin = new System.Windows.Forms.Padding(4);
            this.btnConfig.Name = "btnConfig";
            this.btnConfig.Size = new System.Drawing.Size(150, 45);
            this.btnConfig.TabIndex = 9;
            this.btnConfig.Text = "配置";
            this.btnConfig.UseVisualStyleBackColor = false;
            this.btnConfig.Click += new System.EventHandler(this.BtnConfig_Click);
            // 
            // btnHide
            // 
            this.btnHide.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(60)))), ((int)(((byte)(60)))), ((int)(((byte)(60)))));
            this.btnHide.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnHide.ForeColor = System.Drawing.Color.White;
            this.btnHide.Location = new System.Drawing.Point(895, 630);
            this.btnHide.Margin = new System.Windows.Forms.Padding(4);
            this.btnHide.Name = "btnHide";
            this.btnHide.Size = new System.Drawing.Size(45, 45);
            this.btnHide.TabIndex = 10;
            this.btnHide.Text = "X";
            this.btnHide.UseVisualStyleBackColor = false;
            this.btnHide.Click += new System.EventHandler(this.BtnHide_Click);
            // 
            // lblPanelUpdateTip
            // 
            // 面板自身有新 Release：右对齐标题栏右上角，金黄色，点击弹窗确认后自更新（下载 exe → 替换 → 重启）
            this.lblPanelUpdateTip.AutoSize = true;
            this.lblPanelUpdateTip.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(30)))), ((int)(((byte)(30)))), ((int)(((byte)(30)))));
            this.lblPanelUpdateTip.Cursor = System.Windows.Forms.Cursors.Hand;
            this.lblPanelUpdateTip.Font = new System.Drawing.Font("微软雅黑", 10F, System.Drawing.FontStyle.Bold);
            this.lblPanelUpdateTip.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(255)))), ((int)(((byte)(193)))), ((int)(((byte)(7)))));
            this.lblPanelUpdateTip.Location = new System.Drawing.Point(900, 16);
            this.lblPanelUpdateTip.Name = "lblPanelUpdateTip";
            this.lblPanelUpdateTip.TabIndex = 11;
            this.lblPanelUpdateTip.Text = "面板有新版本";
            this.lblPanelUpdateTip.Visible = false;
            this.lblPanelUpdateTip.Click += new System.EventHandler(this.LblPanelUpdateTip_Click);
            // 
            // lblMpUpdateTip
            // 
            // MoviePilot 后端有新版本标签：与面板提示并排（位于其左），浅绿色，点击弹窗确认后走与"检查MP更新"相同的升级流程
            this.lblMpUpdateTip.AutoSize = true;
            this.lblMpUpdateTip.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(30)))), ((int)(((byte)(30)))), ((int)(((byte)(30)))));
            this.lblMpUpdateTip.Cursor = System.Windows.Forms.Cursors.Hand;
            this.lblMpUpdateTip.Font = new System.Drawing.Font("微软雅黑", 10F, System.Drawing.FontStyle.Bold);
            this.lblMpUpdateTip.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(129)))), ((int)(((byte)(199)))), ((int)(((byte)(132)))));
            this.lblMpUpdateTip.Location = new System.Drawing.Point(790, 16);
            this.lblMpUpdateTip.Name = "lblMpUpdateTip";
            this.lblMpUpdateTip.TabIndex = 12;
            this.lblMpUpdateTip.Text = "MP有新版本";
            this.lblMpUpdateTip.Visible = false;
            this.lblMpUpdateTip.Click += new System.EventHandler(this.LblMpUpdateTip_Click);
            // 
            // Form1
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(9F, 18F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(30)))), ((int)(((byte)(30)))), ((int)(((byte)(30)))));
            this.ClientSize = new System.Drawing.Size(1128, 688);
            this.Controls.Add(this.btnHide);
            this.Controls.Add(this.btnConfig);
            this.Controls.Add(this.btnSite);
            this.Controls.Add(this.btnRestart);
            this.Controls.Add(this.btnStop);
            this.Controls.Add(this.btnStart);
            this.Controls.Add(this.grpLog);
            this.Controls.Add(this.grpStatus);
            this.Controls.Add(this.lblTitle);
            // 更新提示置于最上层（z 序末尾），避免被横向铺满的标题 Label 覆盖
            this.Controls.Add(this.lblPanelUpdateTip);
            this.Controls.Add(this.lblMpUpdateTip);
            this.Margin = new System.Windows.Forms.Padding(4);
            this.MinimumSize = new System.Drawing.Size(900, 572);
            this.Name = "Form1";
            // Manual 而非 CenterScreen：CenterScreen 在句柄创建时先按默认位置 (0,0) 创建窗口、再居中移动，
            // 合成层（WS_EX_COMPOSITED）窗口首帧在 Windows 11 24H2 上表现为左上角黑色细框闪现后瞬移到中心；
            // 居中改由 Form1_Load 在窗口可见前手动计算（与 ConfigForm 的 OnLoad 居中同一模式）
            this.StartPosition = System.Windows.Forms.FormStartPosition.Manual;
            this.Text = "MoviePilot-V3 服务管理控制台 v" + AppConfig.APP_VERSION;
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.Form1_FormClosing);
            this.Load += new System.EventHandler(this.Form1_Load);
            this.Shown += new System.EventHandler(this.Form1_Shown);
            this.Resize += new System.EventHandler(this.Form1_Resize);
            this.grpStatus.ResumeLayout(false);
            this.grpStatus.PerformLayout();
            this.grpLog.ResumeLayout(false);
            this.grpLog.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion
    }
}
