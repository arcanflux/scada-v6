// Copyright (c) Rapid Software LLC. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Scada.Admin.Extensions.ExtCommConfig.Code
{
    /// <summary>
    /// Represents a translucent, click-through window that follows the cursor and shows what is being dragged
    /// and where it will move.
    /// <para>Полупрозрачное сквозное окно у курсора, показывающее переносимые объекты и куда они переместятся.</para>
    /// </summary>
    internal class DragImageForm : Form
    {
        private sealed class Item
        {
            public Image Icon { get; init; }
            public string Text { get; init; }
        }

        private const int CursorOffset = 18;
        private const int Pad = 5;
        private const int RowHeight = 18;
        private const int BadgeSize = 12;
        private const int ArrowWidth = 22;
        private const int MaxNameLength = 22;
        private const int MaxExpandedRows = 15;
        private const int MaxWidth = 380;

        private readonly List<Item> items = new();
        private bool expanded;
        private string sourceText;
        private string targetText;
        private bool hasRoute;  // moving between containers (source -> target)
        private bool reorder;   // in-place reorder (gray arrow only)


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
        /// Sets the dragged items shown in the window.
        /// </summary>
        public void SetItems(IEnumerable<(Image Icon, string Text)> entries)
        {
            items.Clear();

            foreach ((Image icon, string text) in entries)
                items.Add(new Item { Icon = icon, Text = text ?? "" });

            UpdateLayout();
        }

        /// <summary>
        /// Shows a route from the source container to the target container.
        /// </summary>
        public void SetRoute(string source, string target)
        {
            sourceText = Shorten(source);
            targetText = Shorten(target);
            hasRoute = true;
            reorder = false;
            UpdateLayout();
        }

        /// <summary>
        /// Shows the in-place reorder indication (a gray arrow).
        /// </summary>
        public void SetReorder()
        {
            hasRoute = false;
            reorder = true;
            UpdateLayout();
        }

        /// <summary>
        /// Clears the route indication.
        /// </summary>
        public void ClearRoute()
        {
            hasRoute = false;
            reorder = false;
            UpdateLayout();
        }

        /// <summary>
        /// Expands or collapses the full list of dragged items.
        /// </summary>
        public void SetExpanded(bool value)
        {
            if (expanded != value)
            {
                expanded = value;
                UpdateLayout();
            }
        }

        /// <summary>
        /// Moves the window so it sits next to the specified screen point.
        /// </summary>
        public void MoveTo(Point screenPoint)
        {
            Location = new Point(screenPoint.X + CursorOffset, screenPoint.Y + CursorOffset);
        }


        private static string Shorten(string text)
        {
            if (string.IsNullOrEmpty(text))
                return "";

            return text.Length > MaxNameLength ? text.Substring(0, MaxNameLength - 1) + "…" : text;
        }

        private IEnumerable<Item> VisibleItems()
        {
            if (expanded)
                return items.Count > MaxExpandedRows ? items.Take(MaxExpandedRows) : items;

            if (items.Count == 0)
                return items;

            // collapsed: a single summary row
            Item first = items[0];
            string text = items.Count > 1 ? $"{first.Text} (+{items.Count - 1})" : first.Text;
            return new[] { new Item { Icon = first.Icon, Text = text } };
        }

        private void UpdateLayout()
        {
            int rows = 0;
            int contentWidth = 0;

            foreach (Item item in VisibleItems())
            {
                rows++;
                int width = (item.Icon != null ? 20 : 0) + TextRenderer.MeasureText(item.Text, Font).Width;
                contentWidth = Math.Max(contentWidth, width);
            }

            if (expanded && items.Count > MaxExpandedRows)
            {
                rows++;
                contentWidth = Math.Max(contentWidth, TextRenderer.MeasureText("…", Font).Width);
            }

            if (hasRoute)
            {
                rows++;
                int routeWidth = BadgeSize + 2 + TextRenderer.MeasureText(sourceText, Font).Width +
                    ArrowWidth + BadgeSize + 2 + TextRenderer.MeasureText(targetText, Font).Width;
                contentWidth = Math.Max(contentWidth, routeWidth);
            }
            else if (reorder)
            {
                rows++;
                contentWidth = Math.Max(contentWidth, ArrowWidth + 4);
            }

            Width = Math.Min(Math.Max(contentWidth + Pad * 2, 50), MaxWidth);
            Height = Math.Max(rows, 1) * RowHeight + Pad * 2;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            graphics.Clear(BackColor);
            ControlPaint.DrawBorder(graphics, ClientRectangle, SystemColors.ActiveBorder, ButtonBorderStyle.Solid);

            int y = Pad;
            int textOffset = (RowHeight - Font.Height) / 2;
            int iconOffset = (RowHeight - 16) / 2;

            foreach (Item item in VisibleItems())
            {
                int x = Pad;

                if (item.Icon != null)
                {
                    graphics.DrawImage(item.Icon, x, y + iconOffset, 16, 16);
                    x += 20;
                }

                TextRenderer.DrawText(graphics, item.Text, Font, new Point(x, y + textOffset), SystemColors.InfoText);
                y += RowHeight;
            }

            if (expanded && items.Count > MaxExpandedRows)
            {
                TextRenderer.DrawText(graphics, "…", Font, new Point(Pad, y + textOffset), SystemColors.InfoText);
                y += RowHeight;
            }

            if (hasRoute)
                DrawRouteRow(graphics, y, textOffset);
            else if (reorder)
                DrawArrow(graphics, Pad, y + RowHeight / 2, ArrowWidth, SystemColors.GrayText);
        }

        private void DrawRouteRow(Graphics graphics, int y, int textOffset)
        {
            int centerY = y + RowHeight / 2;
            int x = Pad;

            DrawBadge(graphics, x, centerY - BadgeSize / 2, false);
            x += BadgeSize + 2;
            TextRenderer.DrawText(graphics, sourceText, Font, new Point(x, y + textOffset), SystemColors.InfoText);
            x += TextRenderer.MeasureText(sourceText, Font).Width;

            DrawArrow(graphics, x, centerY, ArrowWidth, SystemColors.ControlText);
            x += ArrowWidth;

            DrawBadge(graphics, x, centerY - BadgeSize / 2, true);
            x += BadgeSize + 2;
            TextRenderer.DrawText(graphics, targetText, Font, new Point(x, y + textOffset), SystemColors.InfoText);
        }

        private static void DrawArrow(Graphics graphics, int x, int centerY, int width, Color color)
        {
            using Pen pen = new(color, 2);
            int left = x + 3;
            int right = x + width - 4;
            graphics.DrawLine(pen, left, centerY, right, centerY);
            graphics.DrawLine(pen, right - 4, centerY - 4, right, centerY);
            graphics.DrawLine(pen, right - 4, centerY + 4, right, centerY);
        }

        private static void DrawBadge(Graphics graphics, int x, int y, bool plus)
        {
            Color color = plus ? Color.ForestGreen : Color.Firebrick;
            using SolidBrush brush = new(color);
            using Pen pen = new(Color.White, 2);
            graphics.FillEllipse(brush, x, y, BadgeSize, BadgeSize);
            graphics.DrawLine(pen, x + 3, y + BadgeSize / 2, x + BadgeSize - 3, y + BadgeSize / 2);

            if (plus)
                graphics.DrawLine(pen, x + BadgeSize / 2, y + 3, x + BadgeSize / 2, y + BadgeSize - 3);
        }
    }
}
