// Copyright (c) Rapid Software LLC. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Scada.Admin.Extensions.ExtCommConfig.Code
{
    /// <summary>
    /// Represents a non-activating popup that lists search matches as the user types.
    /// <para>Представляет всплывающее окно без активации, показывающее совпадения поиска при наборе.</para>
    /// </summary>
    internal class SearchPopupForm : Form
    {
        /// <summary>
        /// Represents a single entry of the suggestion list.
        /// </summary>
        public class Entry
        {
            public TreeNode Node { get; init; }
            public Image Image { get; init; }            // line or device icon, shown first
            public string Text { get; init; }            // "[num] name - kind"
            public Image InstanceImage { get; init; }    // optional instance icon, shown before the instance name
            public string InstanceText { get; init; }    // optional instance name, shown last
        }

        private const int RowHeight = 20;
        private const int MaxVisibleRows = 12;
        private readonly ListBox listBox;
        private int contentWidth;   // widest entry, computed when entries are set


        /// <summary>
        /// Initializes a new instance of the class.
        /// </summary>
        public SearchPopupForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            MinimizeBox = false;
            MaximizeBox = false;
            Padding = new Padding(1);
            BackColor = SystemColors.ActiveBorder;

            listBox = new ListBox
            {
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.None,
                DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = RowHeight,
                IntegralHeight = false,
                BackColor = SystemColors.Window
            };

            listBox.DrawItem += ListBox_DrawItem;
            listBox.MouseClick += ListBox_MouseClick;
            Controls.Add(listBox);
        }


        /// <summary>
        /// Gets a value indicating that the window should not be activated when shown.
        /// </summary>
        protected override bool ShowWithoutActivation => true;

        /// <summary>
        /// Adds the no-activate and tool-window extended styles so the window never takes focus.
        /// </summary>
        protected override CreateParams CreateParams
        {
            get
            {
                const int WS_EX_NOACTIVATE = 0x08000000;
                const int WS_EX_TOOLWINDOW = 0x00000080;
                CreateParams createParams = base.CreateParams;
                createParams.ExStyle |= WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
                return createParams;
            }
        }

        /// <summary>
        /// Gets the number of suggestions.
        /// </summary>
        public int Count => listBox.Items.Count;

        /// <summary>
        /// Occurs when an entry is chosen by click or by Enter.
        /// </summary>
        public event EventHandler<TreeNode> ItemChosen;


        /// <summary>
        /// Fills the list with the specified entries and selects the first one.
        /// </summary>
        public void SetEntries(IList<Entry> entries)
        {
            listBox.BeginUpdate();
            listBox.Items.Clear();
            contentWidth = 0;

            foreach (Entry entry in entries)
            {
                listBox.Items.Add(entry);
                contentWidth = Math.Max(contentWidth, MeasureEntryWidth(entry));
            }

            listBox.EndUpdate();

            if (listBox.Items.Count > 0)
                listBox.SelectedIndex = 0;
        }

        /// <summary>
        /// Measures the full pixel width of an entry row.
        /// </summary>
        private int MeasureEntryWidth(Entry entry)
        {
            int width = 4; // left padding

            if (entry.Image != null)
                width += 18;

            width += TextRenderer.MeasureText(entry.Text ?? "", Font).Width;

            if (entry.InstanceImage != null || !string.IsNullOrEmpty(entry.InstanceText))
            {
                width += 8;

                if (entry.InstanceImage != null)
                    width += 18;

                if (!string.IsNullOrEmpty(entry.InstanceText))
                    width += TextRenderer.MeasureText(entry.InstanceText, Font).Width;
            }

            return width + 8; // right padding
        }

        /// <summary>
        /// Positions and sizes the popup below the specified screen point, fitting the widest entry on screen.
        /// </summary>
        public void ShowAt(Point screenLocation, int minWidth)
        {
            int rows = Math.Min(Math.Max(listBox.Items.Count, 1), MaxVisibleRows);
            Rectangle area = Screen.FromPoint(screenLocation).WorkingArea;

            // grow to fit the widest entry; add the scrollbar width if the list scrolls
            int desired = Math.Max(minWidth, contentWidth);
            if (listBox.Items.Count > MaxVisibleRows)
                desired += SystemInformation.VerticalScrollBarWidth;

            int width = Math.Max(200, Math.Min(desired, area.Width - 8));

            // shift left so the popup never runs off the right edge of the screen
            int x = screenLocation.X;
            if (x + width > area.Right)
                x = Math.Max(area.Left + 4, area.Right - width - 4);

            Width = width;
            Height = rows * RowHeight + 2;
            Location = new Point(x, screenLocation.Y);

            if (!Visible)
                Show();
        }

        /// <summary>
        /// Moves the selection by the specified offset.
        /// </summary>
        public void MoveSelection(int offset)
        {
            if (listBox.Items.Count > 0)
            {
                int index = Math.Max(0, Math.Min(listBox.Items.Count - 1, listBox.SelectedIndex + offset));
                listBox.SelectedIndex = index;
            }
        }

        /// <summary>
        /// Raises the event for the currently selected entry.
        /// </summary>
        public void ChooseSelected()
        {
            if (listBox.SelectedItem is Entry entry)
                ItemChosen?.Invoke(this, entry.Node);
        }


        private void ListBox_MouseClick(object sender, MouseEventArgs e)
        {
            int index = listBox.IndexFromPoint(e.Location);

            if (index >= 0)
            {
                listBox.SelectedIndex = index;
                ChooseSelected();
            }
        }

        private void ListBox_DrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= listBox.Items.Count)
                return;

            Entry entry = (Entry)listBox.Items[e.Index];
            e.DrawBackground();
            int x = e.Bounds.Left + 4;
            int iconY = e.Bounds.Top + 2;
            int textY = e.Bounds.Top + (RowHeight - Font.Height) / 2;

            // draw text with GDI so widths match MeasureEntryWidth (TextRenderer), avoiding clipping
            if (entry.Image != null)
            {
                e.Graphics.DrawImage(entry.Image, x, iconY, 16, 16);
                x += 18;
            }

            TextRenderer.DrawText(e.Graphics, entry.Text, Font, new Point(x, textY), e.ForeColor, TextFormatFlags.NoPrefix);
            x += TextRenderer.MeasureText(entry.Text ?? "", Font).Width + 8;

            // instance icon placed right before the instance name
            if (entry.InstanceImage != null)
            {
                e.Graphics.DrawImage(entry.InstanceImage, x, iconY, 16, 16);
                x += 18;
            }

            if (!string.IsNullOrEmpty(entry.InstanceText))
                TextRenderer.DrawText(e.Graphics, entry.InstanceText, Font, new Point(x, textY), e.ForeColor, TextFormatFlags.NoPrefix);

            e.DrawFocusRectangle();
        }
    }
}
