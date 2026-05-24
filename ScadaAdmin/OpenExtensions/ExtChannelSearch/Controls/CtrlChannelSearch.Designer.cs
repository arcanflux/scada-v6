
namespace Scada.Admin.Extensions.ExtChannelSearch.Controls
{
    partial class CtrlChannelSearch
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                components?.Dispose();
                searchPopup?.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Component Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.toolStrip = new System.Windows.Forms.ToolStrip();
            this.tsSepChannelSearch = new System.Windows.Forms.ToolStripSeparator();
            this.btnCreateChannels = new System.Windows.Forms.ToolStripButton();
            this.lblChannelIcon = new System.Windows.Forms.ToolStripLabel();
            this.txtChannelSearch = new System.Windows.Forms.ToolStripTextBox();
            this.btnChannelSearch = new System.Windows.Forms.ToolStripButton();
            this.toolStrip.SuspendLayout();
            this.SuspendLayout();
            //
            // toolStrip
            //
            this.toolStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.tsSepChannelSearch,
            this.btnCreateChannels,
            this.lblChannelIcon,
            this.txtChannelSearch,
            this.btnChannelSearch});
            this.toolStrip.Location = new System.Drawing.Point(0, 0);
            this.toolStrip.Name = "toolStrip";
            this.toolStrip.Size = new System.Drawing.Size(300, 25);
            this.toolStrip.TabIndex = 0;
            //
            // tsSepChannelSearch
            //
            this.tsSepChannelSearch.Name = "tsSepChannelSearch";
            this.tsSepChannelSearch.Size = new System.Drawing.Size(6, 25);
            //
            // btnCreateChannels
            //
            this.btnCreateChannels.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.btnCreateChannels.Image = global::Scada.Admin.Extensions.ExtChannelSearch.Properties.Resources.add;
            this.btnCreateChannels.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.btnCreateChannels.Name = "btnCreateChannels";
            this.btnCreateChannels.Size = new System.Drawing.Size(23, 22);
            this.btnCreateChannels.ToolTipText = "Create empty channels";
            this.btnCreateChannels.Click += new System.EventHandler(this.btnCreateChannels_Click);
            //
            // lblChannelIcon
            //
            this.lblChannelIcon.Name = "lblChannelIcon";
            this.lblChannelIcon.Size = new System.Drawing.Size(16, 22);
            this.lblChannelIcon.ToolTipText = "Channels";
            //
            // txtChannelSearch
            //
            this.txtChannelSearch.Name = "txtChannelSearch";
            this.txtChannelSearch.Size = new System.Drawing.Size(150, 25);
            this.txtChannelSearch.ToolTipText = "Search channels by name, code or number";
            this.txtChannelSearch.KeyDown += new System.Windows.Forms.KeyEventHandler(this.txtChannelSearch_KeyDown);
            this.txtChannelSearch.TextChanged += new System.EventHandler(this.txtChannelSearch_TextChanged);
            //
            // btnChannelSearch
            //
            this.btnChannelSearch.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.btnChannelSearch.Image = global::Scada.Admin.Extensions.ExtChannelSearch.Properties.Resources._goto;
            this.btnChannelSearch.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.btnChannelSearch.Name = "btnChannelSearch";
            this.btnChannelSearch.Size = new System.Drawing.Size(23, 22);
            this.btnChannelSearch.ToolTipText = "Find channel";
            this.btnChannelSearch.Click += new System.EventHandler(this.btnChannelSearch_Click);
            //
            // CtrlChannelSearch
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.toolStrip);
            this.Name = "CtrlChannelSearch";
            this.Size = new System.Drawing.Size(300, 25);
            this.toolStrip.ResumeLayout(false);
            this.toolStrip.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        #endregion

        private System.Windows.Forms.ToolStrip toolStrip;
        private System.Windows.Forms.ToolStripSeparator tsSepChannelSearch;
        private System.Windows.Forms.ToolStripButton btnCreateChannels;
        private System.Windows.Forms.ToolStripLabel lblChannelIcon;
        private System.Windows.Forms.ToolStripTextBox txtChannelSearch;
        private System.Windows.Forms.ToolStripButton btnChannelSearch;
    }
}
