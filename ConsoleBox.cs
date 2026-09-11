using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Text;
using System.Windows.Forms;

namespace MoviePilot_V3
{
    /// <summary>运行日志级别：决定控制台行的显示配色。</summary>
    public enum LogLevel
    {
        /// <summary>主流程状态与进度（常规灰）。</summary>
        Info,
        /// <summary>可恢复异常 / 自动重试 / 跳过（琥珀）。</summary>
        Warn,
        /// <summary>终结性失败（红）。</summary>
        Error,
        /// <summary>子进程原始输出，仅"打印Debug日志"开启时出现（暗灰）。</summary>
        Debug
    }

    /// <summary>一条待显示日志（正文 + 级别）。批量追加时使用，避免逐条封送。</summary>
    public struct LogItem
    {
        public string Text;
        public LogLevel Level;

        public LogItem(string text, LogLevel level)
        {
            Text = text;
            Level = level;
        }
    }

    /// <summary>
    /// 控制台风格的运行日志显示控件（整个客户区自绘 + 双缓冲）。
    ///
    /// 取代原多行 TextBox 的原因：TextBox 的 AppendText + ScrollToCaret 每次追加都会触发
    /// 整段文本重新排版与滚动条重绘，深色主题下（叠加上窗体 WS_EX_COMPOSITED 合成层）
    /// 运行日志区表现为持续闪烁。本控件把日志按"条目"保存在内存中，绘制时只画可视区域内的
    /// 行，并在 AllPaintingInWmPaint + OptimizedDoubleBuffer 下一次性合成上屏，追加日志零闪烁。
    ///
    /// 特性：
    ///  - 按级别着色：INFO 常规灰 / WARN 琥珀 / ERROR 红 / DEBUG 暗灰，一眼定位问题；
    ///  - 自动换行：中英文混排按字符宽度自算折行（见 WrapEntry），绘制逐行输出，位置严格一致；
    ///  - 折行廉价：全部日志按新宽度重新折算只需十几毫秒（4000 条），拖拽窗口尺寸不卡顿；
    ///  - 自动滚屏：视图停在底部时跟随最新日志，用户上滚后不再打断阅读；
    ///  - 内存上限：只保留最近 MaxLines 条（默认 4000），长期运行不增长；
    ///  - 右键菜单 / Ctrl+C 复制全部日志（便于贴 issue），右键可清空。
    ///
    /// 已知取舍：为保持自绘的简洁与零闪烁，未实现鼠标拖拽选择文本（原 TextBox 的能力），
    /// 需要局部内容时用"复制全部"。
    /// </summary>
    public sealed class ConsoleBox : Control
    {
        // ---- 布局参数 ----
        private const int PadLeft = 6;
        private const int PadTop = 4;
        private const int PadRight = 4;
        private const int PadBottom = 4;
        private const int MaxEntryChars = 4096; // 单条日志字符上限：pip/uv 的超长行截断，避免单行量度与绘制退化
        private const int WheelScrollLines = 3; // 一格滚轮滚动的行数

        // 逐行绘制用的 flags：断行位置由 WrapEntry 自算（见其注释），这里关闭自动折行，
        // 保证"算出来的行"与"画出来的行"严格一致；
        // NoPrefix 必须保留——日志里常有 & (URL 参数) 等字符，不关闭前缀解析会被 GDI 当助记符吃掉；
        // NoPadding 去掉内边距，使行高与绘制位置严格对齐
        private const TextFormatFlags LineRenderFlags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix
            | TextFormatFlags.SingleLine | TextFormatFlags.PreserveGraphicsClipping;

        private const int BorderColorArgb = unchecked((int)0xFF464646);
        private const int ErrorColorArgb = unchecked((int)0xFFFF5C5C);
        private const int WarnColorArgb = unchecked((int)0xFFFFC107);
        private const int DebugColorArgb = unchecked((int)0xFF808080);

        /// <summary>内存中的一条日志（正文已含时间戳与级别前缀）。</summary>
        private sealed class Entry
        {
            public string Text;
            public LogLevel Level;
            public int[] LineStarts;    // 每行的起始字符索引（长度即显示行数），由 WrapEntry 折算
            public int Height;          // = 显示行数 * lineHeight（整数行高，保证逐行对齐不漂移）

            /// <summary>按当前折行宽度折算出的显示行数。</summary>
            public int VisualLines { get { return LineStarts == null ? 1 : LineStarts.Length; } }
        }

        private readonly List<Entry> entries = new List<Entry>();
        private readonly List<int> wrapScratch = new List<int>(); // WrapEntry 的临时缓冲，复用以避免逐条分配列表
        private readonly VScrollBar vScroll;
        private readonly ContextMenuStrip menu;

        private int lineHeight = 16; // 单行像素高（按当前字体量度）
        private int wrapWidth;       // 上次折算换行时使用的正文宽度，0 表示尚未折算
        private float halfCharWidth = 10f; // 半角字符宽度（按当前字体实测）
        private float fullCharWidth = 20f; // 全角字符宽度（汉字 / 全角标点，Consolas 下恰为半角 2 倍）
        private int totalLines;      // 全部条目的显示行数合计（滚动条与绘制都基于它）
        private int lastVisibleLines = 1; // 最近一次绘制时的可见行数：尺寸不可信期间（最小化后系统给的伪尺寸）用它判断是否贴底
        private int maxLines = 4000; // 内存中保留的最大日志行数
        private bool inScrollSet;    // 内部改写 vScroll.Value 期间抑制 ValueChanged 重入

        public ConsoleBox()
        {
            // 双缓冲 + 全绘制并入 WM_PAINT：追加日志时只重绘一次即可合成上屏，不产生擦除闪烁
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
                | ControlStyles.Selectable, true);

            // 沿用原 TextBox 日志区底色 #2D2D2D：比 grpLog(#1E1E1E) 亮一档形成层次，
            // 与窗体其余部分同色系；近黑底(#101010)在深灰界面里对比过强、显得突兀
            BackColor = Color.FromArgb(unchecked((int)0xFF2D2D2D));
            ForeColor = Color.FromArgb(unchecked((int)0xFFCCCCCC)); // INFO 行颜色，改 ForeColor 即可整体换色

            // 右侧竖向滚动条：交给系统原生控件自绘滚动条，行为与手感与系统一致
            vScroll = new VScrollBar
            {
                Dock = DockStyle.Right,
                TabStop = false,
                SmallChange = WheelScrollLines
            };
            vScroll.ValueChanged += (s, e) =>
            {
                if (!inScrollSet)
                {
                    Invalidate();
                }
            };
            Controls.Add(vScroll);

            // 自绘控件没有文本框的"选择-复制"能力，右键菜单补上复制全部 / 清空
            menu = new ContextMenuStrip();
            menu.Items.Add(new ToolStripMenuItem("复制全部日志", null, (s, e) => CopyAll()));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("清空日志", null, (s, e) => Clear()));
            ContextMenuStrip = menu;
        }

        /// <summary>
        /// WS_CLIPCHILDREN：父窗口绘制时由系统裁掉子窗口（滚动条）所在矩形，
        /// 避免自绘背景反复盖住滚动条再让其重绘造成的边缘闪烁。
        /// </summary>
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.Style |= 0x02000000; // WS_CLIPCHILDREN
                return cp;
            }
        }

        // ---------- 对外接口 ----------

        /// <summary>内存中保留的最大日志条数，超出后从最旧开始丢弃。</summary>
        public int MaxLines
        {
            get { return maxLines; }
            set
            {
                maxLines = Math.Max(1, value);
                TrimEntries();
                EnsureLayout(false); // 仅保证已折算过，不因尺寸变化重算
                InvalidateIfVisible();
            }
        }

        /// <summary>当前保留的日志条数。</summary>
        public int LineCount
        {
            get { return entries.Count; }
        }

        /// <summary>追加一条日志（仅 UI 线程调用）。</summary>
        public void Append(string text, LogLevel level)
        {
            EnsureLayout(false); // 追加路径不重算：窗口最小化期间照常追加，不会因此把历史日志按伪尺寸重算
            bool follow = IsAtBottom(); // 追加前视图在底部才跟随，用户上滚时不打断阅读
            Entry entry = new Entry { Text = Normalize(text), Level = level };
            WrapEntry(entry);
            entries.Add(entry);
            totalLines += entry.VisualLines;
            TrimEntries();
            SyncScroll(follow);
            InvalidateIfVisible();
        }

        /// <summary>批量追加日志（仅 UI 线程调用）：整批只折算一次滚动条、只失效一次，避免消息风暴。</summary>
        public void AppendBatch(IList<LogItem> items)
        {
            if (items == null || items.Count == 0)
            {
                return;
            }
            EnsureLayout(false); // 同上：批量追加同样不触发全量重算
            bool follow = IsAtBottom();
            for (int i = 0; i < items.Count; i++)
            {
                Entry entry = new Entry { Text = Normalize(items[i].Text), Level = items[i].Level };
                WrapEntry(entry);
                entries.Add(entry);
                totalLines += entry.VisualLines;
            }
            TrimEntries();
            SyncScroll(follow);
            InvalidateIfVisible();
        }

        /// <summary>清空全部日志。</summary>
        public void Clear()
        {
            entries.Clear();
            totalLines = 0;
            SyncScroll(true);
            InvalidateIfVisible();
        }

        /// <summary>取出全部日志文本（复制到剪贴板用）。</summary>
        public string GetAllText()
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < entries.Count; i++)
            {
                sb.Append(entries[i].Text).Append("\r\n");
            }
            return sb.ToString();
        }

        // ---------- 布局与滚动 ----------

        /// <summary>正文区（排除右侧滚动条与四周内边距）。</summary>
        private Rectangle ContentRect
        {
            get
            {
                int w = ClientSize.Width - vScroll.Width - PadLeft - PadRight;
                int h = ClientSize.Height - PadTop - PadBottom;
                return new Rectangle(PadLeft, PadTop, Math.Max(1, w), Math.Max(1, h));
            }
        }

        /// <summary>正文折行宽度（与绘制矩形同宽，保证换行位置一致）。</summary>
        private int TextWidth
        {
            get { return ContentRect.Width; }
        }

        /// <summary>当前视口可显示的整行数。</summary>
        private int VisibleLines
        {
            get { return Math.Max(1, ContentRect.Height / Math.Max(1, lineHeight)); }
        }

        /// <summary>
        /// 确保行高 / 折行宽度可用，并在允许时按当前控件尺寸重算全部条目的折行。
        ///
        /// allowResize 区分两条路径：只有绘制路径（OnPaint）才允许按当前控件尺寸重新折算；
        /// 追加、属性变更等路径只保证"已折算过"（wrapWidth 有效），不因尺寸变化触发重算。
        /// 自 WrapEntry 改为按字符宽度自算后，全量重算由 4.3 秒降到十几毫秒，
        /// 因此尺寸真正变化时（含拖拽调整窗口大小）可以放心重算，不必再靠冻结来规避。
        /// </summary>
        /// <param name="allowResize">true 表示允许按当前控件尺寸重新折算折行宽度（仅绘制时传 true）。</param>
        private void EnsureLayout(bool allowResize)
        {
            bool follow = IsAtBottom(); // 重算会改变总行数，先记录是否在底部
            int h = Math.Max(1, TextRenderer.MeasureText("Wg", Font, new Size(1000, 1000),
                TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Height);
            int w = allowResize || wrapWidth <= 0 ? TextWidth : wrapWidth;
            if (h != lineHeight || w != wrapWidth)
            {
                lineHeight = h;
                wrapWidth = w;
                MeasureCharWidths();
                totalLines = 0;
                for (int i = 0; i < entries.Count; i++)
                {
                    WrapEntry(entries[i]);
                    totalLines += entries[i].VisualLines;
                }
            }
            SyncScroll(follow); // 视口尺寸变化同样要更新滚动条范围
        }

        /// <summary>实测当前字体下的半角 / 全角字符宽度（等宽字体下这两个值即可精确折算折行）。</summary>
        private void MeasureCharWidths()
        {
            TextFormatFlags f = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;
            halfCharWidth = TextRenderer.MeasureText("00000000000000000000", Font, new Size(10000, 1000), f).Width / 20f;
            fullCharWidth = TextRenderer.MeasureText("中中中中中中中中中中", Font, new Size(10000, 1000), f).Width / 10f;
            if (halfCharWidth <= 0f)
            {
                halfCharWidth = 10f; // 极端字体下的兜底，避免宽度恒为 0 导致每字符一行
            }
            if (fullCharWidth < halfCharWidth)
            {
                fullCharWidth = halfCharWidth * 2f;
            }
        }

        /// <summary>
        /// 折算单个条目的折行位置（按当前 wrapWidth）。
        ///
        /// 用字符宽度累加自算，而不是逐条调用 GDI 量度：单条 GDI 量度约 1ms，自算约 3μs，
        /// 4000 条全量重算由 4.3 秒降到十几毫秒 —— 这是拖拽窗口尺寸 / 最小化还原不再卡的关键。
        /// 断行规则对齐 GDI 的 DT_WORDBREAK：优先在空格后断行，CJK 字符之间可断行，
        /// 超长无空格 token（URL 等）在字符处强制断行；实测与 GDI 量度结果逐条一致。
        /// 前提是等宽字体（本控件默认 Consolas）；换成非等宽字体时宽度估算会有偏差。
        /// </summary>
        private void WrapEntry(Entry entry)
        {
            string text = entry.Text;
            List<int> starts = wrapScratch;
            starts.Clear();
            starts.Add(0);

            int lineStart = 0;
            float w = 0f;
            int lastBreak = -1; // 新行可从该索引开始（空格后 / CJK 字符后）

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                float cw = IsWideChar(c) ? fullCharWidth : halfCharWidth;

                if (w + cw > wrapWidth && i > lineStart)
                {
                    // 优先退到最近的断点，避免把英文单词 / URL 拦腰截断
                    int breakAt = (lastBreak > lineStart && lastBreak <= i) ? lastBreak : i;
                    starts.Add(breakAt);
                    lineStart = breakAt;
                    w = 0f;
                    for (int j = lineStart; j <= i; j++)
                    {
                        w += IsWideChar(text[j]) ? fullCharWidth : halfCharWidth;
                    }
                    lastBreak = -1;
                }
                else
                {
                    w += cw;
                }

                if (c == ' ' || c == '\t' || IsWideChar(c))
                {
                    lastBreak = i + 1;
                }
            }

            entry.LineStarts = starts.ToArray();
            entry.Height = entry.LineStarts.Length * lineHeight;
        }

        /// <summary>
        /// East Asian Width 的简化判定：这些区间的字符按全角宽度折算
        /// （CJK 汉字 / 假名 / 全角标点 / 韩文等）。
        /// </summary>
        private static bool IsWideChar(char c)
        {
            return c >= 0x1100 && (
                c <= 0x115F ||                                  // 韩文字母
                c == 0x2329 || c == 0x232A ||
                (c >= 0x2E80 && c <= 0xA4CF && c != 0x303F) ||  // CJK 部首 / 假名 / 汉字
                (c >= 0xAC00 && c <= 0xD7A3) ||                 // 韩文音节
                (c >= 0xF900 && c <= 0xFAFF) ||                 // CJK 兼容表意
                (c >= 0xFE30 && c <= 0xFE6F) ||                 // CJK 兼容形式
                (c >= 0xFF00 && c <= 0xFF60) ||                 // 全角形式
                (c >= 0xFFE0 && c <= 0xFFE6));
        }

        /// <summary>同步滚动条范围；followBottom 为 true 时贴到最新一行。</summary>
        private void SyncScroll(bool followBottom)
        {
            int visible = VisibleLines;
            int maxValue = Math.Max(0, totalLines - visible);
            inScrollSet = true;
            try
            {
                vScroll.LargeChange = Math.Max(1, Math.Min(visible, Math.Max(1, totalLines)));
                vScroll.SmallChange = WheelScrollLines;
                vScroll.Maximum = Math.Max(0, totalLines - 1);
                int want = followBottom ? maxValue : Math.Min(Math.Max(vScroll.Value, 0), maxValue);
                if (vScroll.Value != want)
                {
                    vScroll.Value = want;
                }
            }
            finally
            {
                inScrollSet = false;
            }
        }

        /// <summary>视图是否停在最底部（决定追加时是否跟随）。</summary>
        private bool IsAtBottom()
        {
            // 用最近一次绘制的可见行数而非当前即时值：窗口最小化后系统给的伪尺寸会让
            // VisibleLines 退化成 1，据此判断会误导出"不在底部"，还原后不跟随最新日志
            return vScroll.Value >= Math.Max(0, totalLines - lastVisibleLines);
        }

        /// <summary>按行滚动（负值向上），越界自动收敛。</summary>
        private void ScrollBy(int lines)
        {
            int maxValue = Math.Max(0, totalLines - VisibleLines);
            int target = vScroll.Value + lines;
            if (target < 0) target = 0;
            if (target > maxValue) target = maxValue;
            if (vScroll.Value != target)
            {
                vScroll.Value = target;
            }
        }

        /// <summary>超过上限时从最旧开始丢弃并同步总行数。</summary>
        private void TrimEntries()
        {
            int remove = entries.Count - maxLines;
            if (remove <= 0)
            {
                return;
            }
            for (int i = 0; i < remove; i++)
            {
                totalLines -= entries[i].VisualLines;
            }
            entries.RemoveRange(0, remove);
        }

        /// <summary>规范化单条日志：折叠换行与制表符、超长截断（保留尾部提示）。</summary>
        private static string Normalize(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }
            if (text.IndexOf('\r') >= 0 || text.IndexOf('\n') >= 0 || text.IndexOf('\t') >= 0)
            {
                text = text.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ').Replace("\t", "    ");
            }
            if (text.Length > MaxEntryChars)
            {
                text = text.Substring(0, MaxEntryChars) + " …（超长已截断）";
            }
            return text;
        }

        private void InvalidateIfVisible()
        {
            // 窗口隐藏时（含面板驻留托盘）不触发重绘：Control.Visible 已含父级可见性
            if (Visible && IsHandleCreated)
            {
                Invalidate();
            }
        }

        private void CopyAll()
        {
            if (entries.Count == 0)
            {
                return;
            }
            try
            {
                Clipboard.SetText(GetAllText());
            }
            catch
            {
                // 剪贴板被其他进程占用：忽略，用户可稍后重试
            }
        }

        private Color LevelColor(LogLevel level)
        {
            switch (level)
            {
                case LogLevel.Error: return Color.FromArgb(ErrorColorArgb);
                case LogLevel.Warn: return Color.FromArgb(WarnColorArgb);
                case LogLevel.Debug: return Color.FromArgb(DebugColorArgb);
                default: return ForeColor;
            }
        }

        // ---------- 绘制 ----------

        /// <summary>
        /// 背景直接填满整个客户区（不调用 base 的默认擦除），与 Form1 的防白屏策略一致：
        /// 双缓冲位图若只填无效区域，子控件区域会残留初始白色。
        /// </summary>
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            using (SolidBrush brush = new SolidBrush(BackColor))
            {
                e.Graphics.FillRectangle(brush, ClientRectangle);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            // 唯一允许"按当前尺寸重新折算折行"的入口：只有真要绘制时才会触发全量重算
            EnsureLayout(true);
            lastVisibleLines = VisibleLines;
            Graphics g = e.Graphics;
            Rectangle content = ContentRect;
            int topLine = vScroll.Value;
            int line = 0;

            // 裁到正文区：视口顶部被滚出半行的条目只显示下半部分，不会糊到上边框外
            Region savedClip = g.Clip;
            g.SetClip(content, CombineMode.Intersect);
            try
            {
                for (int i = 0; i < entries.Count; i++)
                {
                    Entry entry = entries[i];
                    int[] starts = entry.LineStarts;
                    int lineCount = starts.Length;
                    int startLine = line;
                    line += lineCount;
                    if (line <= topLine)
                    {
                        continue; // 整条在视口上方
                    }
                    int y = PadTop + (startLine - topLine) * lineHeight; // 部分滚出时为负，由裁剪区截断
                    if (y >= content.Bottom)
                    {
                        break; // 整条在视口下方，后面的更靠下，无需继续
                    }

                    // 逐行绘制：折行位置已由 WrapEntry 自算，这里关闭自动折行（SingleLine），
                    // 保证"算出来的行"与"画出来的行"严格一致；行高为整数，逐行累加不漂移
                    Color color = LevelColor(entry.Level);
                    string text = entry.Text;
                    for (int k = 0; k < lineCount; k++)
                    {
                        int lineTop = y + k * lineHeight;
                        if (lineTop >= content.Bottom)
                        {
                            break; // 该条后面的行已落到视口下方
                        }
                        if (lineTop + lineHeight <= content.Top)
                        {
                            continue; // 该行完全在视口上方
                        }
                        int from = starts[k];
                        int to = (k + 1 < lineCount) ? starts[k + 1] : text.Length;
                        TextRenderer.DrawText(g, text.Substring(from, to - from), Font,
                            new Rectangle(content.Left, lineTop, content.Width, lineHeight),
                            color, LineRenderFlags);
                    }
                }
            }
            finally
            {
                g.SetClip(savedClip, CombineMode.Replace);
                savedClip.Dispose();
            }

            // 自绘 1px 细边框：深色主题下比 BorderStyle 的系统边框更协调，且不参与子控件擦除
            using (Pen pen = new Pen(Color.FromArgb(BorderColorArgb)))
            {
                g.DrawRectangle(pen, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
            }
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            wrapWidth = 0; // 强制下次 EnsureLayout 重算行高与折行
            InvalidateIfVisible();
        }

        protected override void OnForeColorChanged(EventArgs e)
        {
            base.OnForeColorChanged(e);
            InvalidateIfVisible();
        }

        // 这里刻意不重写 OnResize：窗口最小化时系统会把窗体连同子控件重设成伪尺寸并触发 Resize，
        // 若在 Resize 中折算折行，全部日志会按伪宽度重算一遍（4000 条目实测约 4 秒），
        // 还原时尺寸回跳再算一遍 —— 这正是"日志多时最小化/还原窗口极卡"的根源。
        // 折行与滚动条范围改由 OnPaint 统一处理（ResizeRedraw 样式保证尺寸变化后会重绘）。

        // ---------- 交互 ----------

        protected override bool IsInputKey(Keys keyData)
        {
            switch (keyData & Keys.KeyCode)
            {
                case Keys.Up:
                case Keys.Down:
                case Keys.PageUp:
                case Keys.PageDown:
                case Keys.Home:
                case Keys.End:
                    return true;
            }
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.C)
            {
                CopyAll(); // 自绘控件无文本选择，Ctrl+C 语义为复制全部
                e.Handled = true;
                return;
            }
            switch (e.KeyCode)
            {
                case Keys.Up:
                    ScrollBy(-1);
                    e.Handled = true;
                    break;
                case Keys.Down:
                    ScrollBy(1);
                    e.Handled = true;
                    break;
                case Keys.PageUp:
                    ScrollBy(-(VisibleLines - 1));
                    e.Handled = true;
                    break;
                case Keys.PageDown:
                    ScrollBy(VisibleLines - 1);
                    e.Handled = true;
                    break;
                case Keys.Home:
                    ScrollBy(-totalLines);
                    e.Handled = true;
                    break;
                case Keys.End:
                    ScrollBy(totalLines);
                    e.Handled = true;
                    break;
            }
            if (!e.Handled)
            {
                base.OnKeyDown(e);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            // 点击聚焦：焦点在本控件上滚轮才作用于日志区
            if (!Focused && CanFocus)
            {
                Focus();
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            int notches = -e.Delta / 120; // 上滚为正：行号减小（向上翻阅）
            if (notches != 0)
            {
                ScrollBy(notches * WheelScrollLines);
            }
            base.OnMouseWheel(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && menu != null)
            {
                menu.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
