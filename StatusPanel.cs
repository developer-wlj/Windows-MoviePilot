using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MoviePilot_V3
{
    /// <summary>一项服务状态（名称 + 状态文本 + 是否正常），由 Services 层采集、面板显示。</summary>
    public struct StatusItem
    {
        public string Label;
        public string Value;
        public bool Ok;

        public StatusItem(string label, string value, bool ok)
        {
            Label = label;
            Value = value;
            Ok = ok;
        }
    }

    /// <summary>
    /// 服务状态显示面板（整个客户区自绘 + 双缓冲）。
    ///
    /// 取代原多行只读 TextBox 的原因：状态每 5 秒由 txtStatus.Text = ... 整体替换一次，
    /// TextBox 赋值会触发全文重排 + 背景擦除 + 重绘 + 光标/滚动条处理，深色主题下表现为
    /// 周期性闪烁。本控件把状态行保存在内存里，内容未变化时直接跳过重绘（状态长时间不变
    /// 就完全不画），变化时也只重绘一次并双缓冲合成上屏。
    ///
    /// 显示形态：每行 = 状态圆点 + 服务名 + 状态文本；运行中绿色、已停止红色；
    /// 整体在客户区内垂直居中，适配 grpStatus 的固定高度。
    /// </summary>
    public sealed class StatusPanel : Control
    {
        // ---- 布局参数 ----
        private const int PadLeft = 14;
        private const int PadTop = 8;
        private const int PadBottom = 8;
        private const int RowGap = 14;   // 行间距（不含行高）
        private const int DotSize = 10;  // 状态圆点直径
        private const int DotGap = 10;   // 圆点与文字间距
        private const int LabelGap = 20; // 服务名与状态文本间距

        private const int OkColorArgb = unchecked((int)0xFF66E06B);   // 运行中：绿
        private const int FailColorArgb = unchecked((int)0xFFFF5C5C); // 已停止：红
        private const int PlaceholderColorArgb = unchecked((int)0xFF808080);

        // NoPrefix：服务名/状态文本理论上有 & 时不被当助记符吃掉；NoPadding 保证量度与绘制一致
        private const TextFormatFlags TextFlags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix
            | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.Left;

        private List<StatusItem> items; // 当前显示的状态行；null / 空表示尚未取到状态

        public StatusPanel()
        {
            // 双缓冲 + 全绘制并入 WM_PAINT：状态刷新时一次合成上屏，不产生擦除闪烁
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

            BackColor = Color.FromArgb(unchecked((int)0xFF1E1E1E)); // 与 grpStatus 同色，无边框时浑然一体
            ForeColor = Color.FromArgb(unchecked((int)0xFFCCCCCC)); // 服务名颜色，改 ForeColor 即可整体换色
        }

        /// <summary>
        /// 更新状态行（仅 UI 线程调用）：内容无变化时不重绘，
        /// 因此定时轮询在状态稳定期间几乎不产生绘制开销。
        /// 传入 null 或空列表表示"尚未取到状态"，显示等待提示。
        /// </summary>
        public void SetItems(IList<StatusItem> newItems)
        {
            if (SameAs(newItems))
            {
                return;
            }
            items = (newItems == null || newItems.Count == 0) ? null : new List<StatusItem>(newItems);
            if (IsHandleCreated)
            {
                Invalidate();
            }
        }

        /// <summary>与新状态逐项比较，完全相同则无需重绘。</summary>
        private bool SameAs(IList<StatusItem> other)
        {
            int curCount = items == null ? 0 : items.Count;
            int newCount = (other == null) ? 0 : other.Count;
            if (curCount != newCount)
            {
                return false;
            }
            for (int i = 0; i < curCount; i++)
            {
                StatusItem a = items[i];
                StatusItem b = other[i];
                if (a.Label != b.Label || a.Value != b.Value || a.Ok != b.Ok)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>单行像素高（按当前字体量度）。</summary>
        private int LineHeight
        {
            get
            {
                int h = TextRenderer.MeasureText("Ag", Font, new Size(1000, 1000),
                    TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Height;
                return Math.Max(1, h);
            }
        }

        /// <summary>
        /// 背景直接填满整个客户区（不调用 base 的默认擦除）：双缓冲位图若只填无效区域，
        /// 其余部分会残留初始白色，提交后形成块状白屏（与 Form1 的防白屏策略一致）。
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
            Graphics g = e.Graphics;
            Rectangle cr = ClientRectangle;
            int lh = LineHeight;

            // 尚未取到状态：居中提示，避免状态区显示为空白让人以为卡住
            if (items == null || items.Count == 0)
            {
                int y0 = Math.Max(0, (cr.Height - lh) / 2);
                TextRenderer.DrawText(g, "正在检测服务状态...", Font,
                    new Rectangle(PadLeft, y0, Math.Max(1, cr.Width - PadLeft * 2), lh),
                    Color.FromArgb(PlaceholderColorArgb), TextFlags);
                return;
            }

            // 服务名列宽按最长名称动态计算，保证各行状态文本左边缘对齐
            int labelWidth = 0;
            for (int i = 0; i < items.Count; i++)
            {
                int w = TextRenderer.MeasureText(items[i].Label, Font, new Size(1000, 1000), TextFlags).Width;
                if (w > labelWidth)
                {
                    labelWidth = w;
                }
            }

            int rowH = lh + RowGap;
            int blockH = items.Count * rowH - RowGap; // 末行不计行距
            int y = PadTop + Math.Max(0, (cr.Height - PadTop - PadBottom - blockH) / 2);
            int labelX = PadLeft + DotSize + DotGap;
            int valueX = labelX + labelWidth + LabelGap;
            int valueW = Math.Max(1, cr.Width - valueX - PadLeft);

            SmoothingMode savedMode = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias; // 圆点抗锯齿
            try
            {
                for (int i = 0; i < items.Count; i++)
                {
                    StatusItem item = items[i];
                    Color color = item.Ok ? Color.FromArgb(OkColorArgb) : Color.FromArgb(FailColorArgb);

                    int dotY = y + (lh - DotSize) / 2;
                    using (SolidBrush dotBrush = new SolidBrush(color))
                    {
                        g.FillEllipse(dotBrush, PadLeft, dotY, DotSize, DotSize);
                    }

                    TextRenderer.DrawText(g, item.Label, Font,
                        new Rectangle(labelX, y, labelWidth + 4, lh), ForeColor, TextFlags);
                    TextRenderer.DrawText(g, item.Value, Font,
                        new Rectangle(valueX, y, valueW, lh), color, TextFlags);

                    y += rowH;
                }
            }
            finally
            {
                g.SmoothingMode = savedMode;
            }
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            if (IsHandleCreated)
            {
                Invalidate(); // 行高与列宽按新字体重量
            }
        }

        protected override void OnForeColorChanged(EventArgs e)
        {
            base.OnForeColorChanged(e);
            if (IsHandleCreated)
            {
                Invalidate();
            }
        }
    }
}
