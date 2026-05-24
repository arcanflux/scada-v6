// Copyright (c) Rapid Software LLC. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Scada.Admin.Extensions.ExtChannelSearch.Code;
using Scada.Admin.Extensions.ExtChannelSearch.Properties;
using Scada.Data.Entities;
using Scada.Data.Tables;
using Scada.Forms;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace Scada.Admin.Extensions.ExtChannelSearch.Controls
{
    /// <summary>
    /// Represents a control that provides a toolbar field for searching channels.
    /// <para>Представляет элемент управления, предоставляющий поле поиска каналов в панели инструментов.</para>
    /// </summary>
    public partial class CtrlChannelSearch : UserControl
    {
        private const int MaxResults = 200;

        private readonly IAdminContext adminContext; // the Administrator context
        private SearchPopupForm searchPopup;          // the popup listing live channel search matches
        private bool treeEventsWired;                 // the explorer tree events are attached


        /// <summary>
        /// Initializes a new instance of the class.
        /// </summary>
        private CtrlChannelSearch()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Initializes a new instance of the class.
        /// </summary>
        public CtrlChannelSearch(IAdminContext adminContext)
            : this()
        {
            this.adminContext = adminContext ?? throw new ArgumentNullException(nameof(adminContext));
            searchPopup = null;
            treeEventsWired = false;

            lblChannelIcon.Image = Resources.channel;
            SetEnabled();
            WireExplorerTreeEvents();
            adminContext.CurrentProjectChanged += (s, e) => SetEnabled();
        }


        /// <summary>
        /// Gets the explorer tree.
        /// </summary>
        private TreeView ExplorerTree => adminContext.MainForm.ExplorerTree;


        /// <summary>
        /// Gets the tool buttons that make up the channel search field.
        /// </summary>
        public ToolStripItem[] GetToolbarButtons()
        {
            return new ToolStripItem[] { tsSepChannelSearch, lblChannelIcon, txtChannelSearch, btnChannelSearch };
        }


        /// <summary>
        /// Enables or disables the search field depending on whether a project is open.
        /// </summary>
        private void SetEnabled()
        {
            bool projectIsOpen = adminContext.CurrentProject != null;
            txtChannelSearch.Enabled = btnChannelSearch.Enabled = projectIsOpen;

            if (!projectIsOpen)
                HideSearchPopup();
        }

        /// <summary>
        /// Attaches handlers that hide the popup when the user interacts with the explorer tree.
        /// </summary>
        private void WireExplorerTreeEvents()
        {
            if (treeEventsWired || ExplorerTree is not TreeView tree)
                return;

            tree.Enter += (s, e) => HideSearchPopup();
            tree.MouseDown += (s, e) => HideSearchPopup();
            treeEventsWired = true;
        }

        /// <summary>
        /// Checks whether the text contains the query, ignoring case.
        /// </summary>
        private static bool ContainsText(string text, string query)
        {
            return !string.IsNullOrEmpty(text) && text.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Rebuilds the live suggestion list of channels whose name, code or number contains the query.
        /// </summary>
        private void UpdateChannelSuggestions()
        {
            // the tree events may not have been available during construction
            WireExplorerTreeEvents();

            string query = txtChannelSearch.Text;

            if (string.IsNullOrWhiteSpace(query) || adminContext.CurrentProject == null)
            {
                HideSearchPopup();
                return;
            }

            query = query.Trim();
            List<SearchPopupForm.Entry> entries = new();

            foreach (Cnl cnl in adminContext.CurrentProject.ConfigDatabase.CnlTable.Enumerate())
            {
                if (ContainsText(cnl.Name, query) || ContainsText(cnl.Code, query) ||
                    cnl.CnlNum.ToString().Contains(query))
                {
                    entries.Add(new SearchPopupForm.Entry
                    {
                        Tag = cnl,
                        Image = Resources.channel,
                        Text = $"[{cnl.CnlNum}] {cnl.Name} - {ExtensionPhrases.ChannelKind}"
                    });

                    if (entries.Count >= MaxResults)
                        break;
                }
            }

            if (entries.Count == 0)
            {
                HideSearchPopup();
                return;
            }

            if (searchPopup == null)
            {
                searchPopup = new SearchPopupForm();
                searchPopup.ItemChosen += SearchPopup_ItemChosen;

                // own it by the main window so it stays above ScadaAdmin only, not over other apps
                if (adminContext.MainForm is Form mainForm)
                    searchPopup.Owner = mainForm;
            }

            searchPopup.SetEntries(entries);

            if (txtChannelSearch.Owner != null)
            {
                Point location = txtChannelSearch.Owner.PointToScreen(
                    new Point(txtChannelSearch.Bounds.Left, txtChannelSearch.Bounds.Bottom));
                searchPopup.ShowAt(location, Math.Max(txtChannelSearch.Width + 40, 280));

                // keep the caret in the search box so the user can continue typing
                txtChannelSearch.TextBox?.Focus();
            }
        }

        /// <summary>
        /// Hides the suggestion popup if it is shown.
        /// </summary>
        private void HideSearchPopup()
        {
            if (searchPopup != null && searchPopup.Visible)
                searchPopup.Hide();
        }

        private void SearchPopup_ItemChosen(object sender, object tag)
        {
            HideSearchPopup();

            if (tag is not Cnl cnl)
                return;

            // find the explorer node of the device channel table (or the empty-device table)
            TreeNode node = FindDeviceChannelNode(cnl.DeviceNum);

            // open the table exactly like the explorer node does, so the caption and tracking match
            OpenChannelTable(node);

            // highlight the channel group in the explorer tree
            if (node != null && ExplorerTree is TreeView tree)
            {
                tree.SelectedNode = node;
                node.EnsureVisible();
            }

            // select the exact channel row after the form is shown and laid out by the docking manager
            int cnlNum = cnl.CnlNum;
            if (ExplorerTree is TreeView treeView && treeView.IsHandleCreated)
                treeView.BeginInvoke(new Action(() => SelectChannelRow(cnlNum)));
            else
                SelectChannelRow(cnlNum);
        }

        /// <summary>
        /// Finds the explorer node of the channel table filtered by the specified device number.
        /// </summary>
        private TreeNode FindDeviceChannelNode(int? deviceNum)
        {
            // the root Channels node holds one child table node per device, plus the empty-device node
            if (adminContext.MainForm.FindBaseTableNode(typeof(Cnl), null) is not TreeNode cnlRootNode)
                return null;

            string deviceKey = deviceNum?.ToString();

            foreach (TreeNode child in cnlRootNode.Nodes)
            {
                if (child.Tag is TreeNodeTag tag && tag.FormArgs is { Length: >= 2 } &&
                    tag.FormArgs[1] is TableFilter filter && filter.Argument?.ToString() == deviceKey)
                {
                    return child;
                }
            }

            return null;
        }

        /// <summary>
        /// Opens or activates the channel table represented by the specified explorer node,
        /// mirroring the action performed when the node is double-clicked.
        /// </summary>
        private void OpenChannelTable(TreeNode node)
        {
            if (node?.Tag is not TreeNodeTag nodeTag || nodeTag.FormType == null)
                return;

            if (nodeTag.ExistingForm == null)
            {
                // create and show the same form the explorer would open (correct caption, node-tracked)
                Form form = (Form)Activator.CreateInstance(nodeTag.FormType, nodeTag.FormArgs);
                nodeTag.ExistingForm = form;
                adminContext.MainForm.AddChildForm(form);
            }
            else if (nodeTag.FormArgs is { Length: >= 2 } && nodeTag.FormArgs[1] is TableFilter nodeFilter)
            {
                // the table is already open: activate it
                adminContext.MainForm.OpenBaseTable(typeof(Cnl), nodeFilter);
            }
        }

        /// <summary>
        /// Selects and scrolls to the row of the active channel table that has the specified channel number.
        /// </summary>
        private void SelectChannelRow(int cnlNum)
        {
            try
            {
                if (adminContext.MainForm.ActiveBaseTable != typeof(Cnl) ||
                    adminContext.MainForm.ActiveChildForm is not Form baseTableForm ||
                    FindDataGridView(baseTableForm) is not DataGridView grid)
                {
                    return;
                }

                int pkIndex = ColumnIndex(grid, "CnlNum");
                int firstVisibleIndex = FirstVisibleColumnIndex(grid);

                if (pkIndex < 0)
                    return;

                foreach (DataGridViewRow row in grid.Rows)
                {
                    if (row.IsNewRow)
                        continue;

                    if (row.Cells[pkIndex].Value is int value && value == cnlNum)
                    {
                        grid.ClearSelection();
                        row.Selected = true;

                        // move the current cell to the first visible cell of the row, then scroll to it
                        if (firstVisibleIndex >= 0)
                            grid.CurrentCell = row.Cells[firstVisibleIndex];

                        grid.FirstDisplayedScrollingRowIndex = row.Index;
                        grid.Focus();
                        break;
                    }
                }
            }
            catch
            {
                // navigation to the device table already succeeded; row selection is best effort
            }
        }

        /// <summary>
        /// Gets the data grid view of the base table form via reflection on its private field.
        /// </summary>
        private static DataGridView FindDataGridView(Form baseTableForm)
        {
            FieldInfo field = baseTableForm.GetType().GetField("dataGridView",
                BindingFlags.Instance | BindingFlags.NonPublic);
            return field?.GetValue(baseTableForm) as DataGridView;
        }

        /// <summary>
        /// Gets the index of the first visible column, or -1 if none.
        /// </summary>
        private static int FirstVisibleColumnIndex(DataGridView grid)
        {
            foreach (DataGridViewColumn column in grid.Columns)
            {
                if (column.Visible)
                    return column.Index;
            }

            return -1;
        }

        /// <summary>
        /// Gets the index of the column with the specified name, or -1 if not found.
        /// </summary>
        private static int ColumnIndex(DataGridView grid, string columnName)
        {
            foreach (DataGridViewColumn column in grid.Columns)
            {
                if (column.Name == columnName)
                    return column.Index;
            }

            return -1;
        }


        private void btnChannelSearch_Click(object sender, EventArgs e)
        {
            UpdateChannelSuggestions();
        }

        private void txtChannelSearch_TextChanged(object sender, EventArgs e)
        {
            UpdateChannelSuggestions();
        }

        private void txtChannelSearch_KeyDown(object sender, KeyEventArgs e)
        {
            bool popupVisible = searchPopup != null && searchPopup.Visible && searchPopup.Count > 0;

            switch (e.KeyCode)
            {
                case Keys.Down when popupVisible:
                    searchPopup.MoveSelection(1);
                    e.Handled = e.SuppressKeyPress = true;
                    break;
                case Keys.Up when popupVisible:
                    searchPopup.MoveSelection(-1);
                    e.Handled = e.SuppressKeyPress = true;
                    break;
                case Keys.Enter:
                    if (popupVisible)
                        searchPopup.ChooseSelected();
                    else
                        UpdateChannelSuggestions();
                    e.Handled = e.SuppressKeyPress = true;
                    break;
                case Keys.Escape when popupVisible:
                    HideSearchPopup();
                    e.Handled = e.SuppressKeyPress = true;
                    break;
            }
        }
    }
}
