// Copyright (c) Rapid Software LLC. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Scada.Admin.Extensions.ExtCommConfig.Code;
using Scada.Admin.Extensions.ExtCommConfig.Forms;
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

        private readonly HashSet<TreeNode> selectedLineNodes; // the line nodes selected together
        private TreeNode lineSelectionAnchor;             // the anchor node for range selection
        private TreeNode pendingSingleNode;               // a node to select alone if no dragging occurs
        private bool treeEventsWired;                     // the explorer tree events are attached


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
            selectedLineNodes = new HashSet<TreeNode>();
            lineSelectionAnchor = null;
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
            return new ToolStripItem[] { btnAddLine, btnAddDevice, btnCreateChannels };
        }


        private void AdminContext_CurrentProjectChanged(object sender, EventArgs e)
        {
            SetMenuItemsEnabled();
            recentSelection.Reset();
            ClearLineSelection();
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
                            }
                            else if (instanceNode.FindFirst(CommNodeType.Lines) is TreeNode linesNode)
                            {
                                TreeNode lineNode = new TreeViewBuilder(adminContext, this)
                                    .CreateLineNode(frmLineAdd.Instance.CommApp, frmLineAdd.LineConfig);
                                linesNode.Nodes.Add(lineNode);
                                ExplorerTree.SelectedNode = lineNode;
                            }
                        }

                        // save configuration
                        SaveCommConfig(frmLineAdd.Instance.CommApp);
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
            tree.MouseDown += ExplorerTree_MouseDown;
            tree.MouseUp += ExplorerTree_MouseUp;
            tree.KeyDown += ExplorerTree_KeyDown;
            tree.AfterSelect += ExplorerTree_AfterSelect;
            treeEventsWired = true;
        }

        /// <summary>
        /// Gets the parent node shared by the currently multi-selected line nodes.
        /// </summary>
        private TreeNode SelectedLinesParent => selectedLineNodes.Count > 0
            ? selectedLineNodes.First().Parent
            : null;

        /// <summary>
        /// Sets or clears the highlight of the specified line node.
        /// </summary>
        private static void SetNodeHighlight(TreeNode node, bool highlight)
        {
            node.BackColor = highlight ? SystemColors.Highlight : Color.Empty;
            node.ForeColor = highlight ? SystemColors.HighlightText : Color.Empty;
        }

        /// <summary>
        /// Highlights the selected line nodes only when more than one node is selected.
        /// </summary>
        private void RefreshLineHighlight()
        {
            bool highlight = selectedLineNodes.Count > 1;

            foreach (TreeNode node in selectedLineNodes)
                SetNodeHighlight(node, highlight);
        }

        /// <summary>
        /// Adds the specified line node to the selection.
        /// </summary>
        private void AddLineSelection(TreeNode node)
        {
            if (node != null && selectedLineNodes.Add(node))
                RefreshLineHighlight();
        }

        /// <summary>
        /// Excludes the specified line node from the selection.
        /// </summary>
        private void RemoveLineSelection(TreeNode node)
        {
            if (node != null && selectedLineNodes.Remove(node))
            {
                SetNodeHighlight(node, false);
                RefreshLineHighlight();
            }
        }

        /// <summary>
        /// Adds or removes the specified line node from the selection.
        /// </summary>
        private void ToggleLineSelection(TreeNode node)
        {
            if (selectedLineNodes.Contains(node))
                RemoveLineSelection(node);
            else
                AddLineSelection(node);
        }

        /// <summary>
        /// Clears the multiple selection of line nodes.
        /// </summary>
        private void ClearLineSelection()
        {
            foreach (TreeNode node in selectedLineNodes)
                SetNodeHighlight(node, false);

            selectedLineNodes.Clear();
            lineSelectionAnchor = null;
            pendingSingleNode = null;
        }

        /// <summary>
        /// Selects all line nodes between the two specified sibling nodes, inclusive.
        /// </summary>
        private void SelectLineRange(TreeNode fromNode, TreeNode toNode)
        {
            if (fromNode == null || toNode == null || fromNode.Parent != toNode.Parent)
                return;

            ClearLineSelection();
            int loIndex = Math.Min(fromNode.Index, toNode.Index);
            int hiIndex = Math.Max(fromNode.Index, toNode.Index);
            TreeNodeCollection siblings = fromNode.Parent.Nodes;

            for (int i = loIndex; i <= hiIndex; i++)
            {
                if (siblings[i].TagIs(CommNodeType.Line))
                    AddLineSelection(siblings[i]);
            }

            lineSelectionAnchor = fromNode;
        }


        private void ExplorerTree_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
                return;

            pendingSingleNode = null;
            TreeNode node = ExplorerTree.GetNodeAt(e.Location);

            if (node == null || !node.TagIs(CommNodeType.Line))
            {
                ClearLineSelection();
                return;
            }

            bool ctrlPressed = (Control.ModifierKeys & Keys.Control) == Keys.Control;
            bool shiftPressed = (Control.ModifierKeys & Keys.Shift) == Keys.Shift;

            if (ctrlPressed)
            {
                if (SelectedLinesParent != null && SelectedLinesParent != node.Parent)
                    ClearLineSelection();

                ToggleLineSelection(node);
                lineSelectionAnchor = node;
            }
            else if (shiftPressed && lineSelectionAnchor != null && lineSelectionAnchor.Parent == node.Parent)
            {
                SelectLineRange(lineSelectionAnchor, node);
            }
            else if (selectedLineNodes.Contains(node) && selectedLineNodes.Count > 1)
            {
                // keep the group so it can be dragged; collapse to a single node on mouse up if not dragged
                pendingSingleNode = node;
                lineSelectionAnchor = node;
            }
            else
            {
                ClearLineSelection();
                AddLineSelection(node);
                lineSelectionAnchor = node;
            }
        }

        private void ExplorerTree_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && pendingSingleNode != null)
            {
                TreeNode node = pendingSingleNode;
                ClearLineSelection();
                AddLineSelection(node);
                lineSelectionAnchor = node;
            }

            pendingSingleNode = null;
        }

        private void ExplorerTree_KeyDown(object sender, KeyEventArgs e)
        {
            // keyboard navigation collapses the multiple selection
            if (selectedLineNodes.Count > 1 &&
                (e.KeyCode == Keys.Up || e.KeyCode == Keys.Down ||
                e.KeyCode == Keys.Left || e.KeyCode == Keys.Right ||
                e.KeyCode == Keys.Home || e.KeyCode == Keys.End))
            {
                ClearLineSelection();
            }
        }

        private void ExplorerTree_AfterSelect(object sender, TreeViewEventArgs e)
        {
            // a selection moved away from the line nodes resets the multiple selection
            if (e.Node == null || !e.Node.TagIs(CommNodeType.Line))
                ClearLineSelection();
        }

        private void ExplorerTree_ItemDrag(object sender, ItemDragEventArgs e)
        {
            if (e.Button != MouseButtons.Left || e.Item is not TreeNode node || !node.TagIs(CommNodeType.Line))
                return;

            // a drag is starting, so do not collapse the group selection
            pendingSingleNode = null;

            if (!selectedLineNodes.Contains(node))
            {
                ClearLineSelection();
                AddLineSelection(node);
                lineSelectionAnchor = node;
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

            if (selectedLineNodes.Count == 0)
                return;

            TreeNode target = ExplorerTree.GetNodeAt(ExplorerTree.PointToClient(new Point(e.X, e.Y)));

            if (target != null && target.TagIs(CommNodeType.Line) &&
                target.Parent == SelectedLinesParent && !selectedLineNodes.Contains(target))
            {
                e.Effect = DragDropEffects.Move;
            }
        }

        private void ExplorerTree_DragDrop(object sender, DragEventArgs e)
        {
            Point point = ExplorerTree.PointToClient(new Point(e.X, e.Y));
            TreeNode target = ExplorerTree.GetNodeAt(point);

            if (target == null || !target.TagIs(CommNodeType.Line) ||
                target.Tag is not CommNodeTag targetTag ||
                selectedLineNodes.Count == 0 || selectedLineNodes.Contains(target))
            {
                return;
            }

            TreeNode parentNode = target.Parent;
            List<TreeNode> draggedNodes = selectedLineNodes
                .Where(n => n.Parent == parentNode)
                .OrderBy(n => n.Index)
                .ToList();

            if (draggedNodes.Count == 0)
                return;

            CommApp commApp = targetTag.CommApp;
            IList lineList = commApp.AppConfig.Lines;
            bool insertAfter = point.Y > target.Bounds.Top + target.Bounds.Height / 2;

            try
            {
                ExplorerTree.BeginUpdate();
                List<LineConfig> draggedConfigs = draggedNodes
                    .Select(n => (LineConfig)n.GetRelatedObject())
                    .ToList();

                // remove the dragged lines from the tree and the configuration
                foreach (TreeNode node in draggedNodes)
                    node.Remove();

                foreach (LineConfig lineConfig in draggedConfigs)
                    lineList.Remove(lineConfig);

                // the target node keeps its identity, so its index reflects the new position
                int insertIndex = target.Index + (insertAfter ? 1 : 0);

                for (int i = 0; i < draggedNodes.Count; i++)
                {
                    parentNode.Nodes.Insert(insertIndex + i, draggedNodes[i]);
                    lineList.Insert(insertIndex + i, draggedConfigs[i]);
                }

                ExplorerTree.SelectedNode = draggedNodes[0];
            }
            finally
            {
                ExplorerTree.EndUpdate();
            }

            SaveCommConfig(commApp);
        }

        private void cmsLine_Opening(object sender, CancelEventArgs e)
        {
            // enable or disable menu items
            bool isLineNode = SelectedNode != null && SelectedNode.TagIs(CommNodeType.Line);
            miLineMoveUp.Enabled = isLineNode && SelectedNode.PrevNode != null;
            miLineMoveDown.Enabled = isLineNode && SelectedNode.NextNode != null;
            miLineDelete.Enabled = isLineNode;

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
                            foreach (TreeNode lineNode in linesNode.Nodes)
                            {
                                TreeViewBuilder.UpdateLineNodeText(lineNode);
                                RefreshLineConfigForm(lineNode);

                                foreach (TreeNode lineSubnode in lineNode.Nodes)
                                {
                                    if (lineSubnode.TagIs(CommNodeType.Device))
                                        TreeViewBuilder.UpdateDeviceNodeText(lineSubnode);
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
            if (GetCommApp(out CommApp commApp, CommNodeType.Lines, CommNodeType.Line))
            {
                TreeNode linesNode = SelectedNode.FindClosest(CommNodeType.Lines);
                TreeNode lineNode = new TreeViewBuilder(adminContext, this).CreateLineNode(commApp, new LineConfig());
                lineNode.Expand();
                ExplorerTree.Insert(linesNode, lineNode);
                SaveCommConfig(commApp);
            }
        }

        private void miLineMoveUp_Click(object sender, EventArgs e)
        {
            // move up selected line
            if (GetCommApp(out CommApp commApp, CommNodeType.Line))
            {
                ExplorerTree.MoveUpSelectedNode(TreeNodeBehavior.WithinParent);
                SaveCommConfig(commApp);
            }
        }

        private void miLineMoveDown_Click(object sender, EventArgs e)
        {
            // move up selected line
            if (GetCommApp(out CommApp commApp, CommNodeType.Line))
            {
                ExplorerTree.MoveDownSelectedNode(TreeNodeBehavior.WithinParent);
                SaveCommConfig(commApp);
            }
        }

        private void miLineDelete_Click(object sender, EventArgs e)
        {
            // delete selected line
            if (GetCommApp(out CommApp commApp, CommNodeType.Line) &&
                MessageBox.Show(ExtensionPhrases.ConfirmDeleteLine, CommonPhrases.QuestionCaption,
                    MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                adminContext.MainForm.CloseChildForms(SelectedNode, false);
                ExplorerTree.RemoveSelectedNode();
                SaveCommConfig(commApp);
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
