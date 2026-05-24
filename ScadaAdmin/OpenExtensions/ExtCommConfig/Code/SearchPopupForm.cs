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

            foreach (Entry entry in entries)
                listBox.Items.Add(entry);

            listBox.EndUpdate();

            if (listBox.Items.Count > 0)
                listBox.SelectedIndex = 0;
        }

        /// <summary>
        /// Positions and sizes the popup below the specified screen point.
        /// </summary>
        public void ShowAt(Point screenLocation, int width)
        {
            int rows = Math.Min(Math.Max(listBox.Items.Count, 1), MaxVisibleRows);
            Width = Math.Max(width, 200);
            Height = rows * RowHeight + 2;
            Location = screenLocation;

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
            int x = e.Bounds.Left + 2;
            int iconY = e.Bounds.Top + 2;
            int textY = e.Bounds.Top + 2;

            using SolidBrush brush = new(e.ForeColor);

            // line/device icon, then the main text
            if (entry.Image != null)
            {
                e.Graphics.DrawImage(entry.Image, x, iconY, 16, 16);
                x += 18;
            }

            e.Graphics.DrawString(entry.Text, listBox.Font, brush, x, textY);
            x += (int)Math.Ceiling(e.Graphics.MeasureString(entry.Text, listBox.Font).Width) + 8;

            // instance icon placed right before the instance name
            if (entry.InstanceImage != null)
            {
                e.Graphics.DrawImage(entry.InstanceImage, x, iconY, 16, 16);
                x += 18;
            }

            if (!string.IsNullOrEmpty(entry.InstanceText))
                e.Graphics.DrawString(entry.InstanceText, listBox.Font, brush, x, textY);

            e.DrawFocusRectangle();
        }
    }
}
