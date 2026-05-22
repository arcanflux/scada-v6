// Copyright (c) Rapid Software LLC. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Scada.Admin.Extensions.ExtCommConfig.Code;
using Scada.Admin.Extensions.ExtCommConfig.Forms;
using Scada.Admin.Extensions.ExtCommConfig.Properties;
using Scada.Admin.Lang;
using Scada.Admin.Project;
using Scada.Agent;
using Scada.Comm;
using Scada.Comm.Config;
using Scada.Data.Entities;
using Scada.Data.Models;
using Scada.Forms;
using Scada.Lang;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using WinControls;

namespace Scada.Admin.Extensions.ExtCommConfig.Controls
{
    /// <summary>
    /// Represents a control that provides extension menus.
    /// <para>Представляет элемент управления, предоставляющий меню расширения.</para>
    /// </summary>
    public partial class CtrlExtensionMenu : UserControl
    {
        private readonly IAdminContext adminContext;      // the Administrator context
        private readonly RecentSelection recentSelection; // the recently selected objects

        private readonly HashSet<TreeNode> selectedNodes; // the line or device nodes selected together
        private TreeNode selectionAnchor;             // the anchor node for range selection
        private TreeNode pendingSingleNode;               // a node to select alone if no dragging occurs
        private bool treeEventsWired;                     // the explorer tree events are attached
        private DateTime lastDragScrollUtc;               // the time of the last auto-scroll during a drag
        private TreeNode dropMarkerNode;                  // the node next to which the drop marker is drawn
        private bool dropMarkerAfter;                     // the drop marker is below the node
        private bool dropMarkerShown;                     // the drop marker is currently visible
        private bool treeDoubleBuffered;                  // native double buffering is enabled for the tree
        private ToolStripDropDown searchPopup;            // the dropdown listing search matches


        /// <summary>
        /// Initializes a new instance of the class.
        /// </summary>
        private CtrlExtensionMenu()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Initializes a new instance of the class.
        /// </summary>
        public CtrlExtensionMenu(IAdminContext adminContext)
            : this()
        {
            this.adminContext = adminContext ?? throw new ArgumentNullException(nameof(adminContext));
            recentSelection = new RecentSelection();
            selectedNodes = new HashSet<TreeNode>();
            selectionAnchor = null;
            pendingSingleNode = null;
            treeEventsWired = false;

            SetMenuItemsEnabled();
            WireExplorerTreeEvents();
            adminContext.CurrentProjectChanged += AdminContext_CurrentProjectChanged;
            adminContext.MessageToExtension += AdminContext_MessageToExtension;
        }


        /// <summary>
        /// Gets the explorer tree.
        /// </summary>
        private TreeView ExplorerTree => adminContext.MainForm.ExplorerTree;

        /// <summary>
        /// Gets the selected node of the explorer tree.
        /// </summary>
        private TreeNode SelectedNode => adminContext.MainForm.SelectedNode;

        /// <summary>
        /// Gets the communication line context menu.
        /// </summary>
        public ContextMenuStrip LineMenu => cmsLine;

        /// <summary>
        /// Gets the device context menu.
        /// </summary>
        public ContextMenuStrip DeviceMenu => cmsDevice;

        /// <summary>
        /// Gets all context menus.
        /// </summary>
        public ContextMenuStrip[] AllContextMenus => new ContextMenuStrip[] { cmsLine, cmsDevice };


        /// <summary>
        /// Enables or disables main menu items and toolbar buttons.
        /// </summary>
        private void SetMenuItemsEnabled()
        {
            bool projectIsOpen = adminContext.CurrentProject != null;
            miAddLine.Enabled = btnAddLine.Enabled = projectIsOpen;
            miAddDevice.Enabled = btnAddDevice.Enabled = projectIsOpen;
            miCreateChannels.Enabled = btnCreateChannels.Enabled = projectIsOpen;
            txtSearch.Enabled = btnSearch.Enabled = projectIsOpen;
        }

        /// <summary>
        /// Gets the Communicator application from the selected node, and validates the node type.
        /// </summary>
        private bool GetCommApp(out CommApp commApp, params string[] allowedNodeTypes)
        {
            if (SelectedNode?.Tag is CommNodeTag commNodeTag &&
                (allowedNodeTypes == null || allowedNodeTypes.Contains(commNodeTag.NodeType)))
            {
                commApp = commNodeTag.CommApp;
                return true;
            }
            else
            {
                commApp = null;
                return false;
            }
        }

        /// <summary>
        /// Saves the Communicator configuration.
        /// </summary>
        private void SaveCommConfig(CommApp commApp)
        {
            if (!commApp.SaveConfig(out string errMsg))
                adminContext.ErrLog.HandleError(errMsg);
        }

        /// <summary>
        /// Refreshes an open child form that shows the communication line configuration.
        /// </summary>
        private void RefreshLineConfigForm(TreeNode lineNode)
        {
            if (lineNode.FindFirst(CommNodeType.LineOptions) is TreeNode lineOptionsNode &&
                lineOptionsNode.Tag is TreeNodeTag tag && tag.ExistingForm is IChildForm childForm)
            {
                childForm.ChildFormTag.SendMessage(this, AdminMessage.RefreshData);
            }
        }

        /// <summary>
        /// Updates the specified communication line node.
        /// </summary>
        private void UpdateLineNode(string instanceName, int commLineNum)
        {
            if (adminContext.MainForm.FindInstanceNode(instanceName, out bool justPrepared) is TreeNode instanceNode &&
                !justPrepared && instanceNode.FindFirst(CommNodeType.Lines) is TreeNode linesNode)
            {
                foreach (TreeNode lineNode in linesNode.Nodes)
                {
                    if (lineNode.GetRelatedObject() is LineConfig lineConfig &&
                        lineConfig.CommLineNum == commLineNum)
                    {
                        adminContext.MainForm.CloseChildForms(lineNode, false);
                        new TreeViewBuilder(adminContext, this).UpdateLineNode(lineNode);
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Finds a tree node that contains the specified related object.
        /// </summary>
        private static TreeNode FindNode(TreeNode startNode, object relatedObject)
        {
            foreach (TreeNode node in startNode.IterateNodes())
            {
                if (node.GetRelatedObject() == relatedObject)
                    return node;
            }

            return null;
        }


        /// <summary>
        /// Gets menu items to add to the main menu.
        /// </summary>
        public ToolStripItem[] GetMainMenuItems()
        {
            return new ToolStripItem[] { miWizards };
        }

        /// <summary>
        /// Gets tool buttons to add to the toolbar.
        /// </summary>
        public ToolStripItem[] GetToobarButtons()
        {
            return new ToolStripItem[] { btnAddLine, btnAddDevice, btnCreateChannels,
                tsSepSearch, txtSearch, btnSearch };
        }


        private void AdminContext_CurrentProjectChanged(object sender, EventArgs e)
        {
            SetMenuItemsEnabled();
            recentSelection.Reset();
            ClearNodeSelection();
        }

        private void AdminContext_MessageToExtension(object sender, MessageEventArgs e)
        {
            if (e.Message == KnownExtensionMessage.UpdateLineNode)
            {
                UpdateLineNode(
                    (string)e.Arguments["InstanceName"],
                    (int)e.Arguments["CommLineNum"]);
            }
        }

        private void miAddLine_Click(object sender, EventArgs e)
        {
            // add communication line
            if (adminContext.CurrentProject != null)
            {
                FrmLineAdd frmLineAdd = new(adminContext.CurrentProject, recentSelection);

                if (frmLineAdd.ShowDialog() == DialogResult.OK)
                {
                    adminContext.MainForm.RefreshBaseTables(typeof(CommLine), true);

                    if (frmLineAdd.AddedToComm)
                    {
                        // update explorer
                        if (adminContext.MainForm.FindInstanceNode(frmLineAdd.Instance.Name, out bool justPrepared) is
                            TreeNode instanceNode)
                        {
                            if (justPrepared)
                            {
                                ExplorerTree.SelectedNode = FindNode(instanceNode, frmLineAdd.LineConfig);
                                SaveCommConfig(frmLineAdd.Instance.CommApp);
                            }
                            else if (instanceNode.FindFirst(CommNodeType.Lines) is TreeNode linesNode)
                            {
                                TreeNode lineNode = new TreeViewBuilder(adminContext, this)
                                    .CreateLineNode(frmLineAdd.Instance.CommApp, frmLineAdd.LineConfig);

                                // a new line goes to the root, ordered by line number among the root lines
                                InsertRootLineNodeSorted(linesNode, lineNode, frmLineAdd.LineConfig.CommLineNum);
                                ExplorerTree.SelectedNode = lineNode;
                                ApplyLineTreeChanges(linesNode, frmLineAdd.Instance.CommApp);
                            }
                            else
                            {
                                SaveCommConfig(frmLineAdd.Instance.CommApp);
                            }
                        }
                    }
                }
            }
        }

        private void miAddDevice_Click(object sender, EventArgs e)
        {
            // add device
            if (adminContext.CurrentProject != null)
            {
                FrmDeviceAdd frmDeviceAdd = new(adminContext, adminContext.CurrentProject, recentSelection);

                if (frmDeviceAdd.ShowDialog() == DialogResult.OK)
                {
                    adminContext.MainForm.RefreshBaseTables(typeof(Device), true);

                    if (frmDeviceAdd.AddedToComm)
                    {
                        // update explorer
                        if (adminContext.MainForm.FindInstanceNode(frmDeviceAdd.Instance.Name, out bool justPrepared) is
                            TreeNode instanceNode)
                        {
                            if (justPrepared)
                            {
                                ExplorerTree.SelectedNode = FindNode(instanceNode, frmDeviceAdd.DeviceConfig);
                            }
                            else if (FindNode(instanceNode, frmDeviceAdd.LineConfig) is TreeNode lineNode)
                            {
                                TreeNode deviceNode = new TreeViewBuilder(adminContext, this)
                                    .CreateDeviceNode(frmDeviceAdd.Instance.CommApp, frmDeviceAdd.DeviceConfig);

                                // insert after the leading option nodes at the ordered device position
                                int listIndex = frmDeviceAdd.LineConfig.DevicePolling.IndexOf(frmDeviceAdd.DeviceConfig);
                                int treeIndex = LineDeviceOffset(lineNode) + listIndex;

                                if (listIndex >= 0 && treeIndex < lineNode.Nodes.Count)
                                    lineNode.Nodes.Insert(treeIndex, deviceNode);
                                else
                                    lineNode.Nodes.Add(deviceNode);

                                ExplorerTree.SelectedNode = deviceNode;
                                RefreshLineConfigForm(lineNode);
                            }
                        }

                        // save configuration
                        SaveCommConfig(frmDeviceAdd.Instance.CommApp);
                    }
                }
            }
        }

        private void miCreateChannels_Click(object sender, EventArgs e)
        {
            // create channels
            if (adminContext.CurrentProject != null)
            {
                FrmCnlCreate frmCnlCreate = new(adminContext, adminContext.CurrentProject, recentSelection);

                if (frmCnlCreate.ShowDialog() == DialogResult.OK)
                    adminContext.MainForm.RefreshBaseTables(typeof(Cnl), true);
            }
        }

        private void btnSearch_Click(object sender, EventArgs e)
        {
            ShowSearchResults(txtSearch.Text);
        }

        private void txtSearch_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                ShowSearchResults(txtSearch.Text);
            }
        }

        /// <summary>
        /// Shows a dropdown list of communication lines and devices whose name contains the query.
        /// </summary>
        private void ShowSearchResults(string query)
        {
            const int MaxResults = 200;

            if (string.IsNullOrWhiteSpace(query) || ExplorerTree is not TreeView tree)
                return;

            query = query.Trim();
            List<TreeNode> matches = new();

            foreach (TreeNode node in tree.Nodes.IterateNodes())
            {
                if (NodeMatchesSearch(node, query))
                    matches.Add(node);
            }

            if (matches.Count == 0)
            {
                ScadaUiUtils.ShowInfo(ExtensionPhrases.NothingFound);
                return;
            }

            EnsureSearchPopup();
            searchPopup.Items.Clear();

            foreach (TreeNode node in matches.Take(MaxResults))
            {
                bool isLine = node.GetRelatedObject() is LineConfig;
                string kind = isLine ? ExtensionPhrases.LineKind : ExtensionPhrases.DeviceKind;
                Image image = isLine ? Resources.line : Resources.device;

                ToolStripMenuItem item = new($"{node.Text} — {kind}", image) { Tag = node };
                item.Click += SearchResultItem_Click;
                searchPopup.Items.Add(item);
            }

            // show the dropdown right below the search box
            if (txtSearch.Owner != null)
            {
                Point location = txtSearch.Owner.PointToScreen(
                    new Point(txtSearch.Bounds.Left, txtSearch.Bounds.Bottom));
                searchPopup.Show(location);
            }
        }

        /// <summary>
        /// Creates the search results dropdown if it does not exist yet.
        /// </summary>
        private void EnsureSearchPopup()
        {
            searchPopup ??= new ToolStripDropDown
            {
                AutoClose = true,
                DropShadowEnabled = true
            };
        }

        private void SearchResultItem_Click(object sender, EventArgs e)
        {
            if (sender is ToolStripMenuItem item && item.Tag is TreeNode node && ExplorerTree is TreeView tree)
            {
                tree.SelectedNode = node;
                node.EnsureVisible();
                tree.Focus();
            }
        }

        /// <summary>
        /// Checks whether a line or device node matches the search query by name.
        /// </summary>
        private static bool NodeMatchesSearch(TreeNode node, string query)
        {
            switch (node.GetRelatedObject())
            {
                case LineConfig lineConfig:
                    return ContainsText(lineConfig.Name, query) || ContainsText(lineConfig.Title, query);
                case DeviceConfig deviceConfig:
                    return ContainsText(deviceConfig.Name, query) || ContainsText(deviceConfig.Title, query);
                default:
                    return false;
            }
        }

        /// <summary>
        /// Checks whether the text contains the query, ignoring case.
        /// </summary>
        private static bool ContainsText(string text, string query)
        {
            return !string.IsNullOrEmpty(text) && text.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }


        /// <summary>
        /// Attaches drag-and-drop and multiple selection handlers to the explorer tree.
        /// </summary>
        private void WireExplorerTreeEvents()
        {
            if (treeEventsWired || ExplorerTree is not TreeView tree)
                return;

            tree.AllowDrop = true;
            tree.ItemDrag += ExplorerTree_ItemDrag;
            tree.DragEnter += ExplorerTree_DragEnter;
            tree.DragOver += ExplorerTree_DragOver;
            tree.DragDrop += ExplorerTree_DragDrop;
            tree.DragLeave += ExplorerTree_DragLeave;
            tree.MouseDown += ExplorerTree_MouseDown;
            tree.MouseUp += ExplorerTree_MouseUp;
            tree.KeyDown += ExplorerTree_KeyDown;
            tree.AfterSelect += ExplorerTree_AfterSelect;
            treeEventsWired = true;
        }

        /// <summary>
        /// Gets the parent node shared by the currently multi-selected nodes.
        /// </summary>
        private TreeNode SelectedNodesParent => selectedNodes.Count > 0
            ? selectedNodes.First().Parent
            : null;

        /// <summary>
        /// Checks whether the node represents a communication line or a device that can be reordered.
        /// </summary>
        private static bool IsReorderableNode(TreeNode node)
        {
            return node != null && (node.TagIs(CommNodeType.Line) || node.TagIs(CommNodeType.Device));
        }

        /// <summary>
        /// Sets or clears the highlight of the specified node.
        /// </summary>
        private static void SetNodeHighlight(TreeNode node, bool highlight)
        {
            node.BackColor = highlight ? SystemColors.Highlight : Color.Empty;
            node.ForeColor = highlight ? SystemColors.HighlightText : Color.Empty;
        }

        /// <summary>
        /// Highlights the selected nodes only when more than one node is selected.
        /// </summary>
        private void RefreshNodeHighlight()
        {
            bool highlight = selectedNodes.Count > 1;

            foreach (TreeNode node in selectedNodes)
                SetNodeHighlight(node, highlight);
        }

        /// <summary>
        /// Adds the specified node to the selection.
        /// </summary>
        private void AddNodeSelection(TreeNode node)
        {
            if (node != null && selectedNodes.Add(node))
                RefreshNodeHighlight();
        }

        /// <summary>
        /// Excludes the specified node from the selection.
        /// </summary>
        private void RemoveNodeSelection(TreeNode node)
        {
            if (node != null && selectedNodes.Remove(node))
            {
                SetNodeHighlight(node, false);
                RefreshNodeHighlight();
            }
        }

        /// <summary>
        /// Adds or removes the specified node from the selection.
        /// </summary>
        private void ToggleNodeSelection(TreeNode node)
        {
            if (selectedNodes.Contains(node))
                RemoveNodeSelection(node);
            else
                AddNodeSelection(node);
        }

        /// <summary>
        /// Gets the number of leading non-device child nodes (line option nodes) of a line node,
        /// which equals the tree index where device nodes begin.
        /// </summary>
        private static int LineDeviceOffset(TreeNode lineNode)
        {
            int offset = 0;

            foreach (TreeNode child in lineNode.Nodes)
            {
                if (child.TagIs(CommNodeType.Device))
                    break;

                offset++;
            }

            return offset;
        }

        /// <summary>
        /// Clears the multiple selection of nodes.
        /// </summary>
        private void ClearNodeSelection()
        {
            foreach (TreeNode node in selectedNodes)
                SetNodeHighlight(node, false);

            selectedNodes.Clear();
            selectionAnchor = null;
            pendingSingleNode = null;
        }

        /// <summary>
        /// Selects all reorderable nodes between the two specified sibling nodes, inclusive.
        /// </summary>
        private void SelectNodeRange(TreeNode fromNode, TreeNode toNode)
        {
            if (fromNode == null || toNode == null || fromNode.Parent != toNode.Parent)
                return;

            ClearNodeSelection();
            int loIndex = Math.Min(fromNode.Index, toNode.Index);
            int hiIndex = Math.Max(fromNode.Index, toNode.Index);
            TreeNodeCollection siblings = fromNode.Parent.Nodes;

            for (int i = loIndex; i <= hiIndex; i++)
            {
                if (IsReorderableNode(siblings[i]))
                    AddNodeSelection(siblings[i]);
            }

            selectionAnchor = fromNode;
        }


        private void ExplorerTree_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
                return;

            pendingSingleNode = null;
            TreeNode node = ExplorerTree.GetNodeAt(e.Location);

            if (!IsReorderableNode(node))
            {
                ClearNodeSelection();
                return;
            }

            bool ctrlPressed = (Control.ModifierKeys & Keys.Control) == Keys.Control;
            bool shiftPressed = (Control.ModifierKeys & Keys.Shift) == Keys.Shift;

            if (ctrlPressed)
            {
                if (SelectedNodesParent != null && SelectedNodesParent != node.Parent)
                    ClearNodeSelection();

                ToggleNodeSelection(node);
                selectionAnchor = node;
            }
            else if (shiftPressed && selectionAnchor != null && selectionAnchor.Parent == node.Parent)
            {
                SelectNodeRange(selectionAnchor, node);
            }
            else if (selectedNodes.Contains(node) && selectedNodes.Count > 1)
            {
                // keep the group so it can be dragged; collapse to a single node on mouse up if not dragged
                pendingSingleNode = node;
                selectionAnchor = node;
            }
            else
            {
                ClearNodeSelection();
                AddNodeSelection(node);
                selectionAnchor = node;
            }
        }

        private void ExplorerTree_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && pendingSingleNode != null)
            {
                TreeNode node = pendingSingleNode;
                ClearNodeSelection();
                AddNodeSelection(node);
                selectionAnchor = node;
            }

            pendingSingleNode = null;
        }

        private void ExplorerTree_KeyDown(object sender, KeyEventArgs e)
        {
            // keyboard navigation collapses the multiple selection
            if (selectedNodes.Count > 1 &&
                (e.KeyCode == Keys.Up || e.KeyCode == Keys.Down ||
                e.KeyCode == Keys.Left || e.KeyCode == Keys.Right ||
                e.KeyCode == Keys.Home || e.KeyCode == Keys.End))
            {
                ClearNodeSelection();
            }
        }

        private void ExplorerTree_AfterSelect(object sender, TreeViewEventArgs e)
        {
            // a selection moved away from the reorderable nodes resets the multiple selection
            if (!IsReorderableNode(e.Node))
                ClearNodeSelection();
        }

        private void ExplorerTree_ItemDrag(object sender, ItemDragEventArgs e)
        {
            if (e.Button != MouseButtons.Left || e.Item is not TreeNode node || !IsReorderableNode(node))
                return;

            // remove flicker of the insertion marker and auto-scroll during the drag
            EnableTreeDoubleBuffering();

            // a drag is starting, so do not collapse the group selection
            pendingSingleNode = null;

            if (!selectedNodes.Contains(node))
            {
                ClearNodeSelection();
                AddNodeSelection(node);
                selectionAnchor = node;
            }

            ExplorerTree.DoDragDrop(node, DragDropEffects.Move);
        }

        private void ExplorerTree_DragEnter(object sender, DragEventArgs e)
        {
            e.Effect = e.Data.GetDataPresent(typeof(TreeNode)) ? DragDropEffects.Move : DragDropEffects.None;
        }

        private void ExplorerTree_DragOver(object sender, DragEventArgs e)
        {
            e.Effect = DragDropEffects.None;

            if (selectedNodes.Count == 0)
                return;

            Point point = ExplorerTree.PointToClient(new Point(e.X, e.Y));
            AutoScrollDuringDrag(point);
            TreeNode target = ExplorerTree.GetNodeAt(point);

            if (DraggingDevices())
            {
                // a device can be reordered within its line or moved to another line
                if (IsDeviceDropTarget(target))
                    e.Effect = DragDropEffects.Move;
            }
            else if (DraggingLines() && IsLineDropTarget(target))
            {
                e.Effect = DragDropEffects.Move;
            }

            // show an insertion marker only between items of the dragged kind
            if (e.Effect == DragDropEffects.Move && target != null &&
                ((DraggingDevices() && target.TagIs(CommNodeType.Device)) ||
                (DraggingLines() && target.TagIs(CommNodeType.Line))))
            {
                UpdateDropMarker(target, point.Y > target.Bounds.Top + target.Bounds.Height / 2);
            }
            else
            {
                UpdateDropMarker(null, false);
            }
        }

        /// <summary>
        /// Updates the insertion marker position and redraws it if it changed.
        /// </summary>
        private void UpdateDropMarker(TreeNode node, bool after)
        {
            if (dropMarkerNode == node && dropMarkerAfter == after && dropMarkerShown == (node != null))
                return;

            dropMarkerNode = node;
            dropMarkerAfter = after;
            dropMarkerShown = node != null;

            // repaint to erase the previous marker, then draw the new one on top
            ExplorerTree.Invalidate();
            ExplorerTree.Update();

            if (dropMarkerShown)
                DrawDropMarker();
        }

        /// <summary>
        /// Hides the insertion marker.
        /// </summary>
        private void ClearDropMarker()
        {
            if (dropMarkerShown || dropMarkerNode != null)
            {
                dropMarkerNode = null;
                dropMarkerShown = false;
                ExplorerTree.Invalidate();
                ExplorerTree.Update();
            }
        }

        /// <summary>
        /// Draws a horizontal insertion line with arrow heads at the marked position.
        /// </summary>
        private void DrawDropMarker()
        {
            if (!dropMarkerShown || dropMarkerNode == null)
                return;

            Rectangle bounds = dropMarkerNode.Bounds;
            int y = dropMarkerAfter ? bounds.Bottom : bounds.Top;
            int left = bounds.Left;
            int right = Math.Max(bounds.Right + 8, ExplorerTree.ClientSize.Width - 2);

            using Graphics graphics = ExplorerTree.CreateGraphics();
            using Pen pen = new(SystemColors.ControlText, 2);
            graphics.DrawLine(pen, left, y, right, y);
        }

        private void ExplorerTree_DragLeave(object sender, EventArgs e)
        {
            ClearDropMarker();
        }

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        /// <summary>
        /// Enables native double buffering for the explorer tree to remove flicker during a drag.
        /// </summary>
        private void EnableTreeDoubleBuffering()
        {
            const int TVM_SETEXTENDEDSTYLE = 0x112C;
            const int TVS_EX_DOUBLEBUFFER = 0x0004;

            if (!treeDoubleBuffered && ExplorerTree.IsHandleCreated)
            {
                SendMessage(ExplorerTree.Handle, TVM_SETEXTENDEDSTYLE,
                    (IntPtr)TVS_EX_DOUBLEBUFFER, (IntPtr)TVS_EX_DOUBLEBUFFER);
                treeDoubleBuffered = true;
            }
        }

        /// <summary>
        /// Checks whether a device drag is in progress.
        /// </summary>
        private bool DraggingDevices()
        {
            return selectedNodes.Count > 0 && selectedNodes.First().TagIs(CommNodeType.Device);
        }

        /// <summary>
        /// Checks whether a communication line drag is in progress.
        /// </summary>
        private bool DraggingLines()
        {
            return selectedNodes.Count > 0 && selectedNodes.First().TagIs(CommNodeType.Line);
        }

        /// <summary>
        /// Gets the communication lines node that contains the dragged line nodes.
        /// </summary>
        private TreeNode DraggedLinesRoot()
        {
            return selectedNodes.Count > 0 ? selectedNodes.First().FindClosest(CommNodeType.Lines) : null;
        }

        /// <summary>
        /// Checks whether a line drag can be dropped on the specified target node.
        /// </summary>
        private bool IsLineDropTarget(TreeNode target)
        {
            return target != null && !selectedNodes.Contains(target) &&
                (target.TagIs(CommNodeType.Line) || target.TagIs(CommNodeType.LineFolder) ||
                target.TagIs(CommNodeType.Lines)) &&
                target.FindClosest(CommNodeType.Lines) == DraggedLinesRoot();
        }

        /// <summary>
        /// Scrolls the explorer tree when the dragged cursor approaches its top or bottom edge.
        /// </summary>
        private void AutoScrollDuringDrag(Point clientPoint)
        {
            const int EdgeZone = 24;       // distance from the edge that triggers scrolling
            const int ScrollDelayMs = 50;  // minimum interval between scroll steps

            if ((DateTime.UtcNow - lastDragScrollUtc).TotalMilliseconds < ScrollDelayMs)
                return;

            TreeNode topNode = ExplorerTree.TopNode;

            if (clientPoint.Y < EdgeZone && topNode?.PrevVisibleNode is TreeNode prevNode)
            {
                ExplorerTree.TopNode = prevNode;
                lastDragScrollUtc = DateTime.UtcNow;
            }
            else if (clientPoint.Y > ExplorerTree.ClientSize.Height - EdgeZone &&
                topNode?.NextVisibleNode is TreeNode nextNode)
            {
                ExplorerTree.TopNode = nextNode;
                lastDragScrollUtc = DateTime.UtcNow;
            }
        }

        private void ExplorerTree_DragDrop(object sender, DragEventArgs e)
        {
            ClearDropMarker();

            if (selectedNodes.Count == 0)
                return;

            Point point = ExplorerTree.PointToClient(new Point(e.X, e.Y));
            TreeNode target = ExplorerTree.GetNodeAt(point);

            if (DraggingDevices())
                DropDevices(target, point);
            else if (DraggingLines())
                DropLines(target, point);
        }

        /// <summary>
        /// Gets the communication lines node that contains the dragged device nodes.
        /// </summary>
        private TreeNode DraggedDevicesRoot()
        {
            return selectedNodes.Count > 0 ? selectedNodes.First().FindClosest(CommNodeType.Lines) : null;
        }

        /// <summary>
        /// Checks whether a device drag can be dropped on the specified target node.
        /// </summary>
        private bool IsDeviceDropTarget(TreeNode target)
        {
            return target != null && !selectedNodes.Contains(target) &&
                (target.TagIs(CommNodeType.Device) || target.TagIs(CommNodeType.Line)) &&
                target.FindClosest(CommNodeType.Lines) == DraggedDevicesRoot();
        }

        /// <summary>
        /// Rebuilds the device polling list of a line from its device child nodes.
        /// </summary>
        private static void RebuildDevicePolling(TreeNode lineNode)
        {
            if (lineNode?.GetRelatedObject() is not LineConfig lineConfig)
                return;

            List<DeviceConfig> devices = lineConfig.DevicePolling;
            devices.Clear();

            foreach (TreeNode child in lineNode.Nodes)
            {
                if (child.GetRelatedObject() is DeviceConfig deviceConfig)
                {
                    deviceConfig.Parent = lineConfig;
                    devices.Add(deviceConfig);
                }
            }
        }

        /// <summary>
        /// Reorders the dragged device nodes within their line or moves them to another line.
        /// </summary>
        private void DropDevices(TreeNode target, Point point)
        {
            if (!IsDeviceDropTarget(target) || target.Tag is not CommNodeTag targetTag)
                return;

            TreeNode sourceLineNode = SelectedNodesParent;
            List<TreeNode> draggedNodes = selectedNodes
                .Where(n => n.TagIs(CommNodeType.Device))
                .OrderBy(n => n.Index)
                .ToList();

            if (draggedNodes.Count == 0 || sourceLineNode == null)
                return;

            bool targetIsDevice = target.TagIs(CommNodeType.Device);
            TreeNode targetLineNode = targetIsDevice ? target.Parent : target;
            bool insertAfter = targetIsDevice && point.Y > target.Bounds.Top + target.Bounds.Height / 2;

            try
            {
                ExplorerTree.BeginUpdate();

                foreach (TreeNode node in draggedNodes)
                    node.Remove();

                int insertIndex = targetIsDevice
                    ? target.Index + (insertAfter ? 1 : 0)
                    : targetLineNode.Nodes.Count;

                for (int i = 0; i < draggedNodes.Count; i++)
                    targetLineNode.Nodes.Insert(insertIndex + i, draggedNodes[i]);

                targetLineNode.Expand();
                ExplorerTree.SelectedNode = draggedNodes[0];
            }
            finally
            {
                ExplorerTree.EndUpdate();
            }

            // rebuild the device lists of the affected lines from the tree
            RebuildDevicePolling(sourceLineNode);
            RefreshLineConfigForm(sourceLineNode);

            if (targetLineNode != sourceLineNode)
            {
                RebuildDevicePolling(targetLineNode);
                RefreshLineConfigForm(targetLineNode);
            }

            SaveCommConfig(targetTag.CommApp);
        }

        /// <summary>
        /// Moves the dragged line nodes within a folder, into a folder, or to the root.
        /// </summary>
        private void DropLines(TreeNode target, Point point)
        {
            if (!IsLineDropTarget(target) || target.Tag is not CommNodeTag targetTag)
                return;

            List<TreeNode> draggedNodes = selectedNodes
                .Where(n => n.TagIs(CommNodeType.Line))
                .OrderBy(n => n.Index)
                .ToList();

            TreeNode linesNode = DraggedLinesRoot();

            if (draggedNodes.Count == 0 || linesNode == null)
                return;

            bool targetIsLine = target.TagIs(CommNodeType.Line);
            TreeNode container = targetIsLine ? target.Parent : target; // folder or lines node
            bool insertAfter = targetIsLine && point.Y > target.Bounds.Top + target.Bounds.Height / 2;

            try
            {
                ExplorerTree.BeginUpdate();

                foreach (TreeNode node in draggedNodes)
                    node.Remove();

                int insertIndex = targetIsLine
                    ? target.Index + (insertAfter ? 1 : 0)
                    : container.Nodes.Count;

                for (int i = 0; i < draggedNodes.Count; i++)
                    container.Nodes.Insert(insertIndex + i, draggedNodes[i]);

                if (container.TagIs(CommNodeType.LineFolder))
                    container.Expand();

                ExplorerTree.SelectedNode = draggedNodes[0];
            }
            finally
            {
                ExplorerTree.EndUpdate();
            }

            ApplyLineTreeChanges(linesNode, targetTag.CommApp);
        }

        /// <summary>
        /// Rebuilds the line order and the folder configuration from the current tree, then saves both.
        /// </summary>
        private void ApplyLineTreeChanges(TreeNode linesNode, CommApp commApp)
        {
            if (linesNode == null)
                return;

            LineGroupConfig groupConfig = new();
            List<LineConfig> newLines = new();

            foreach (TreeNode child in linesNode.Nodes)
            {
                if (child.TagIs(CommNodeType.LineFolder))
                {
                    string folder = child.GetRelatedObject() as string ?? child.Text;
                    groupConfig.AddFolder(folder);

                    foreach (TreeNode lineNode in child.Nodes)
                    {
                        if (lineNode.GetRelatedObject() is LineConfig lineConfig)
                        {
                            newLines.Add(lineConfig);
                            groupConfig.SetFolder(lineConfig.CommLineNum, folder);
                        }
                    }
                }
                else if (child.GetRelatedObject() is LineConfig lineConfig)
                {
                    newLines.Add(lineConfig);
                }
            }

            List<LineConfig> lines = commApp.AppConfig.Lines;
            lines.Clear();

            foreach (LineConfig lineConfig in newLines)
            {
                lineConfig.Parent = commApp.AppConfig;
                lines.Add(lineConfig);
            }

            groupConfig.Save(commApp.ConfigDir, out _);
            SaveCommConfig(commApp);
        }

        /// <summary>
        /// Inserts a root-level line node ordered by line number, after the folder nodes.
        /// </summary>
        private static void InsertRootLineNodeSorted(TreeNode linesNode, TreeNode lineNode, int commLineNum)
        {
            int insertIndex = linesNode.Nodes.Count;

            for (int i = 0; i < linesNode.Nodes.Count; i++)
            {
                TreeNode child = linesNode.Nodes[i];

                if (child.TagIs(CommNodeType.LineFolder))
                    continue;

                if (child.GetRelatedObject() is LineConfig lineConfig && lineConfig.CommLineNum > commLineNum)
                {
                    insertIndex = i;
                    break;
                }
            }

            linesNode.Nodes.Insert(insertIndex, lineNode);
        }

        /// <summary>
        /// Checks whether a folder with the specified name exists under the lines node.
        /// </summary>
        private static bool FolderNodeExists(TreeNode linesNode, string folderName)
        {
            foreach (TreeNode child in linesNode.Nodes)
            {
                if (child.TagIs(CommNodeType.LineFolder) &&
                    string.Equals(child.GetRelatedObject() as string, folderName))
                {
                    return true;
                }
            }

            return false;
        }

        private void cmsLine_Opening(object sender, CancelEventArgs e)
        {
            // enable or disable menu items
            bool isLinesNode = SelectedNode != null && SelectedNode.TagIs(CommNodeType.Lines);
            bool isLineNode = SelectedNode != null && SelectedNode.TagIs(CommNodeType.Line);
            bool isFolderNode = SelectedNode != null && SelectedNode.TagIs(CommNodeType.LineFolder);

            miLineSync.Enabled = isLinesNode || isLineNode;
            miLineAdd.Enabled = isLinesNode || isLineNode || isFolderNode;

            // moving is limited to neighbouring lines, so folder boundaries are respected
            miLineMoveUp.Enabled = isLineNode && SelectedNode.PrevNode != null &&
                SelectedNode.PrevNode.TagIs(CommNodeType.Line);
            miLineMoveDown.Enabled = isLineNode && SelectedNode.NextNode != null &&
                SelectedNode.NextNode.TagIs(CommNodeType.Line);
            miLineDelete.Enabled = isLineNode;

            miLineCreateFolder.Enabled = isLinesNode || isLineNode || isFolderNode;
            miLineRenameFolder.Enabled = isFolderNode;
            miLineDeleteFolder.Enabled = isFolderNode;

            miLineStart.Enabled = isLineNode;
            miLineStop.Enabled = isLineNode;
            miLineRestart.Enabled = isLineNode;
        }

        private void miLineSync_Click(object sender, EventArgs e)
        {
            // sync communication lines and devices
            if (GetCommApp(out CommApp commApp, CommNodeType.Lines, CommNodeType.Line))
            {
                FrmSync frmSync = new(adminContext, adminContext.CurrentProject, commApp);

                if (SelectedNode.GetRelatedObject() is LineConfig lineConfig)
                    frmSync.SelectedLineNum = lineConfig.CommLineNum;

                if (frmSync.ShowDialog() == DialogResult.OK)
                {
                    if (frmSync.BaseToComm)
                    {
                        // update explorer and open forms
                        TreeNode linesNode = SelectedNode.FindClosest(CommNodeType.Lines);

                        if (frmSync.AddedToComm)
                        {
                            adminContext.MainForm.CloseChildForms(linesNode, false);
                            new TreeViewBuilder(adminContext, this).UpdateLinesNode(linesNode);
                        }
                        else
                        {
                            // lines may be nested in folders, so iterate all descendants
                            foreach (TreeNode node in linesNode.IterateNodes())
                            {
                                if (node.TagIs(CommNodeType.Line))
                                {
                                    TreeViewBuilder.UpdateLineNodeText(node);
                                    RefreshLineConfigForm(node);
                                }
                                else if (node.TagIs(CommNodeType.Device))
                                {
                                    TreeViewBuilder.UpdateDeviceNodeText(node);
                                }
                            }

                            adminContext.MainForm.UpdateChildFormHints(linesNode);
                        }

                        // save configuration
                        SaveCommConfig(commApp);
                    }
                    else
                    {
                        // refresh open tables
                        adminContext.MainForm.RefreshBaseTables(typeof(CommLine), true);
                        adminContext.MainForm.RefreshBaseTables(typeof(Device), true);
                    }
                };
            }
        }

        private void miLineAdd_Click(object sender, EventArgs e)
        {
            // add new line
            if (GetCommApp(out CommApp commApp, CommNodeType.Lines, CommNodeType.Line, CommNodeType.LineFolder))
            {
                TreeNode linesNode = SelectedNode.FindClosest(CommNodeType.Lines);
                TreeNode lineNode = new TreeViewBuilder(adminContext, this).CreateLineNode(commApp, new LineConfig());
                lineNode.Expand();

                if (SelectedNode.TagIs(CommNodeType.LineFolder))
                {
                    SelectedNode.Nodes.Add(lineNode);
                    SelectedNode.Expand();
                }
                else if (SelectedNode.TagIs(CommNodeType.Line))
                {
                    SelectedNode.Parent.Nodes.Insert(SelectedNode.Index + 1, lineNode);
                }
                else
                {
                    linesNode.Nodes.Add(lineNode);
                }

                ExplorerTree.SelectedNode = lineNode;
                ApplyLineTreeChanges(linesNode, commApp);
            }
        }

        private void miLineMoveUp_Click(object sender, EventArgs e)
        {
            // move up selected line within its container
            if (GetCommApp(out CommApp commApp, CommNodeType.Line) &&
                SelectedNode.PrevNode is TreeNode prevNode && prevNode.TagIs(CommNodeType.Line))
            {
                MoveLineNode(SelectedNode, -1, commApp);
            }
        }

        private void miLineMoveDown_Click(object sender, EventArgs e)
        {
            // move down selected line within its container
            if (GetCommApp(out CommApp commApp, CommNodeType.Line) &&
                SelectedNode.NextNode is TreeNode nextNode && nextNode.TagIs(CommNodeType.Line))
            {
                MoveLineNode(SelectedNode, 1, commApp);
            }
        }

        /// <summary>
        /// Moves a line node by the specified offset among its siblings and saves the configuration.
        /// </summary>
        private void MoveLineNode(TreeNode lineNode, int offset, CommApp commApp)
        {
            TreeNode parentNode = lineNode.Parent;
            TreeNode linesNode = lineNode.FindClosest(CommNodeType.Lines);
            int newIndex = lineNode.Index + offset;

            try
            {
                ExplorerTree.BeginUpdate();
                parentNode.Nodes.RemoveAt(lineNode.Index);
                parentNode.Nodes.Insert(newIndex, lineNode);
                ExplorerTree.SelectedNode = lineNode;
            }
            finally
            {
                ExplorerTree.EndUpdate();
            }

            ApplyLineTreeChanges(linesNode, commApp);
        }

        private void miLineDelete_Click(object sender, EventArgs e)
        {
            // delete selected line
            if (GetCommApp(out CommApp commApp, CommNodeType.Line) &&
                MessageBox.Show(ExtensionPhrases.ConfirmDeleteLine, CommonPhrases.QuestionCaption,
                    MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                TreeNode lineNode = SelectedNode;
                TreeNode linesNode = lineNode.FindClosest(CommNodeType.Lines);
                adminContext.MainForm.CloseChildForms(lineNode, false);
                lineNode.Remove();
                ApplyLineTreeChanges(linesNode, commApp);
            }
        }

        private void miLineCreateFolder_Click(object sender, EventArgs e)
        {
            // create a folder for communication lines
            if (GetCommApp(out CommApp commApp, CommNodeType.Lines, CommNodeType.Line, CommNodeType.LineFolder))
            {
                TreeNode linesNode = SelectedNode.FindClosest(CommNodeType.Lines);
                string folderName = InputDialog.Show(ExtensionPhrases.CreateFolderTitle,
                    ExtensionPhrases.FolderNamePrompt, ExtensionPhrases.NewFolderName);

                if (string.IsNullOrEmpty(folderName))
                    return;

                if (FolderNodeExists(linesNode, folderName))
                {
                    ScadaUiUtils.ShowError(ExtensionPhrases.FolderAlreadyExists);
                    return;
                }

                TreeNode folderNode = new TreeViewBuilder(adminContext, this)
                    .CreateLineFolderNode(commApp, folderName);

                // place the new folder after the existing folders, before the root lines
                int insertIndex = 0;
                foreach (TreeNode child in linesNode.Nodes)
                {
                    if (child.TagIs(CommNodeType.LineFolder))
                        insertIndex = child.Index + 1;
                    else
                        break;
                }

                linesNode.Nodes.Insert(insertIndex, folderNode);
                ExplorerTree.SelectedNode = folderNode;
                ApplyLineTreeChanges(linesNode, commApp);
            }
        }

        private void miLineRenameFolder_Click(object sender, EventArgs e)
        {
            // rename the selected folder
            if (GetCommApp(out CommApp commApp, CommNodeType.LineFolder))
            {
                TreeNode folderNode = SelectedNode;
                TreeNode linesNode = folderNode.FindClosest(CommNodeType.Lines);
                string oldName = folderNode.GetRelatedObject() as string ?? folderNode.Text;
                string newName = InputDialog.Show(ExtensionPhrases.RenameFolderTitle,
                    ExtensionPhrases.FolderNamePrompt, oldName);

                if (string.IsNullOrEmpty(newName) || newName == oldName)
                    return;

                if (FolderNodeExists(linesNode, newName))
                {
                    ScadaUiUtils.ShowError(ExtensionPhrases.FolderAlreadyExists);
                    return;
                }

                folderNode.Text = newName;
                folderNode.Tag = new CommNodeTag(commApp, newName, CommNodeType.LineFolder);
                ApplyLineTreeChanges(linesNode, commApp);
            }
        }

        private void miLineDeleteFolder_Click(object sender, EventArgs e)
        {
            // delete the selected folder, moving its lines to the root
            if (GetCommApp(out CommApp commApp, CommNodeType.LineFolder) &&
                MessageBox.Show(ExtensionPhrases.ConfirmDeleteFolder, CommonPhrases.QuestionCaption,
                    MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                TreeNode folderNode = SelectedNode;
                TreeNode linesNode = folderNode.FindClosest(CommNodeType.Lines);

                try
                {
                    ExplorerTree.BeginUpdate();

                    foreach (TreeNode lineNode in new ArrayList(folderNode.Nodes))
                    {
                        lineNode.Remove();
                        linesNode.Nodes.Add(lineNode);
                    }

                    folderNode.Remove();
                }
                finally
                {
                    ExplorerTree.EndUpdate();
                }

                ApplyLineTreeChanges(linesNode, commApp);
            }
        }

        private void miLineStartStop_Click(object sender, EventArgs e)
        {
            // start, stop or restart communication line
            if (SelectedNode?.GetRelatedObject() is LineConfig lineConfig)
            {
                if (adminContext.MainForm.GetAgentClient(SelectedNode, false) is IAgentClient agentClient)
                {
                    TeleCommand cmd = new()
                    {
                        CmdVal = lineConfig.CommLineNum
                    };

                    if (sender == miLineStart)
                        cmd.CmdCode = CommCmdCode.StartLine;
                    else if (sender == miLineStop)
                        cmd.CmdCode = CommCmdCode.StopLine;
                    else
                        cmd.CmdCode = CommCmdCode.RestartLine;

                    if (ExtensionUtils.SendCommand(adminContext, agentClient, cmd))
                        ScadaUiUtils.ShowInfo(CommonPhrases.CommandSent);
                }
                else
                {
                    ScadaUiUtils.ShowWarning(AdminPhrases.AgentNotEnabled);
                }
            }
        }


        private void miDeviceChannels_Click(object sender, EventArgs e)
        {
            // select channel table node
            if (SelectedNode?.GetRelatedObject() is DeviceConfig deviceConfig)
            {
                if (adminContext.MainForm.FindBaseTableNode(typeof(Cnl), deviceConfig.DeviceNum) is
                    TreeNode treeNode)
                {
                    ExplorerTree.SelectedNode = treeNode;
                }
                else
                {
                    ScadaUiUtils.ShowWarning(ExtensionPhrases.CnlNodeNotFound);
                }
            }
        }

        private void miDeviceCommand_Click(object sender, EventArgs e)
        {
            // show device command form
            if (SelectedNode?.GetRelatedObject() is DeviceConfig deviceConfig)
            {
                if (adminContext.MainForm.GetAgentClient(SelectedNode, false) is IAgentClient agentClient)
                {
                    FrmDeviceCommand frmDeviceCommand = new(adminContext, deviceConfig);
                    frmDeviceCommand.AgentClient = agentClient;
                    frmDeviceCommand.ShowDialog();
                }
                else
                {
                    ScadaUiUtils.ShowWarning(AdminPhrases.AgentNotEnabled);
                }
            }
        }

        private void miDevicePoll_Click(object sender, EventArgs e)
        {
            // send command to poll device
            if (SelectedNode?.GetRelatedObject() is DeviceConfig deviceConfig)
            {
                if (adminContext.MainForm.GetAgentClient(SelectedNode, false) is IAgentClient agentClient)
                {
                    if (ExtensionUtils.SendCommand(adminContext, agentClient, new TeleCommand
                    {
                        CreationTime = DateTime.UtcNow,
                        DeviceNum = deviceConfig.DeviceNum,
                        CmdCode = CommCmdCode.PollDevice
                    }))
                    {
                        ScadaUiUtils.ShowInfo(CommonPhrases.CommandSent);
                    }
                }
                else
                {
                    ScadaUiUtils.ShowWarning(AdminPhrases.AgentNotEnabled);
                }
            }
        }

        private void miDeviceProperties_Click(object sender, EventArgs e)
        {
            // show device properties
            if (GetCommApp(out CommApp commApp, CommNodeType.Device) &&
                SelectedNode?.GetRelatedObject() is DeviceConfig deviceConfig)
            {
                ExtensionUtils.ShowDeviceProperties(adminContext, commApp, deviceConfig, SelectedNode);
            }
        }
    }
}
