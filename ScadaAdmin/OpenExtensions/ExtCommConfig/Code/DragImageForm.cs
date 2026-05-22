// Copyright (c) Rapid Software LLC. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Drawing;
using System.Windows.Forms;

namespace Scada.Admin.Extensions.ExtCommConfig.Code
{
    /// <summary>
    /// Represents a translucent, click-through window that follows the cursor and shows the dragged items.
    /// <para>Представляет полупрозрачное сквозное окно, следующее за курсором и показывающее переносимые объекты.</para>
    /// </summary>
    internal class DragImageForm : Form
    {
        /// <summary>
        /// Specifies the badge shown next to the dragged items.
        /// </summary>
        public enum Badge { None, Plus, Minus }

        private const int CursorOffset = 16;
        private Image icon;
        private string text = "";
        private Badge badge = Badge.None;


        /// <summary>
        /// Initializes a new instance of the class.
        /// </summary>
        public DragImageForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            MinimizeBox = false;
            MaximizeBox = false;
            TopMost = true;
            DoubleBuffered = true;
            Opacity = 0.9;
            BackColor = SystemColors.Info;
            Font = SystemFonts.DefaultFont;
        }


        /// <summary>
        /// Gets a value indicating that the window should not be activated when shown.
        /// </summary>
        protected override bool ShowWithoutActivation => true;

        /// <summary>
        /// Adds the no-activate, tool-window and transparent (click-through) extended styles.
        /// </summary>
        protected override CreateParams CreateParams
        {
            get
            {
                const int WS_EX_TRANSPARENT = 0x00000020;
                const int WS_EX_TOOLWINDOW = 0x00000080;
                const int WS_EX_NOACTIVATE = 0x08000000;
                CreateParams createParams = base.CreateParams;
                createParams.ExStyle |= WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
                return createParams;
            }
        }


        /// <summary>
        /// Sets the icon and text shown for the dragged items.
        /// </summary>
        public void SetContent(Image icon, string text)
        {
            this.icon = icon;
            this.text = text ?? "";
            badge = Badge.None;
            AdjustSize();
            Invalidate();
        }

        /// <summary>
        /// Sets the badge shown next to the dragged items.
        /// </summary>
        public void SetBadge(Badge value)
        {
            if (badge != value)
            {
                badge = value;
                Invalidate();
            }
        }

        /// <summary>
        /// Moves the window so it sits next to the specified screen point.
        /// </summary>
        public void MoveTo(Point screenPoint)
        {
            Location = new Point(screenPoint.X + CursorOffset, screenPoint.Y + CursorOffset);
        }


        private void AdjustSize()
        {
            using Graphics graphics = CreateGraphics();
            int textWidth = (int)Math.Ceiling(graphics.MeasureString(text, Font).Width);
            int width = 6 + (icon != null ? 20 : 0) + textWidth + 22; // room for badge
            Height = Math.Max(22, Font.Height + 8);
            Width = Math.Min(Math.Max(width, 40), 360);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            graphics.Clear(BackColor);
            ControlPaint.DrawBorder(graphics, ClientRectangle, SystemColors.ActiveBorder, ButtonBorderStyle.Solid);

            int x = 4;

            if (icon != null)
            {
                graphics.DrawImage(icon, x, (Height - 16) / 2, 16, 16);
                x += 20;
            }

            TextRenderer.DrawText(graphics, text, Font,
                new Point(x, (Height - Font.Height) / 2), SystemColors.InfoText);

            if (badge != Badge.None)
                DrawBadge(graphics, Width - 16, (Height - 12) / 2);
        }

        private void DrawBadge(Graphics graphics, int x, int y)
        {
            Color color = badge == Badge.Plus ? Color.ForestGreen : Color.Firebrick;

            using SolidBrush brush = new(color);
            using Pen pen = new(Color.White, 2);
            graphics.FillEllipse(brush, x, y, 12, 12);
            graphics.DrawLine(pen, x + 3, y + 6, x + 9, y + 6);

            if (badge == Badge.Plus)
                graphics.DrawLine(pen, x + 6, y + 3, x + 6, y + 9);
        }
    }
}
