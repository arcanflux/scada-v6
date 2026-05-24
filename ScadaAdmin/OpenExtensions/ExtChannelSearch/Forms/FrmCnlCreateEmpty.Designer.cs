namespace Scada.Admin.Extensions.ExtChannelSearch.Forms
{
    partial class FrmCnlCreateEmpty
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.gbNumbering = new System.Windows.Forms.GroupBox();
            this.lblStep = new System.Windows.Forms.Label();
            this.numStep = new System.Windows.Forms.NumericUpDown();
            this.lblCount = new System.Windows.Forms.Label();
            this.numCount = new System.Windows.Forms.NumericUpDown();
            this.lblStartNum = new System.Windows.Forms.Label();
            this.numStartNum = new System.Windows.Forms.NumericUpDown();
            this.gbNaming = new System.Windows.Forms.GroupBox();
            this.lblTokenHint = new System.Windows.Forms.Label();
            this.txtCodePrefix = new System.Windows.Forms.TextBox();
            this.lblCodePrefix = new System.Windows.Forms.Label();
            this.txtNamePrefix = new System.Windows.Forms.TextBox();
            this.lblNamePrefix = new System.Windows.Forms.Label();
            this.gbProps = new System.Windows.Forms.GroupBox();
            this.chkActive = new System.Windows.Forms.CheckBox();
            this.cbFormat = new System.Windows.Forms.ComboBox();
            this.lblFormat = new System.Windows.Forms.Label();
            this.cbDataType = new System.Windows.Forms.ComboBox();
            this.lblDataType = new System.Windows.Forms.Label();
            this.cbCnlType = new System.Windows.Forms.ComboBox();
            this.lblCnlType = new System.Windows.Forms.Label();
            this.lblPreview = new System.Windows.Forms.Label();
            this.btnCreate = new System.Windows.Forms.Button();
            this.btnCancel = new System.Windows.Forms.Button();
            this.gbNumbering.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.numStep)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numCount)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numStartNum)).BeginInit();
            this.gbNaming.SuspendLayout();
            this.gbProps.SuspendLayout();
            this.SuspendLayout();
            //
            // gbNumbering
            //
            this.gbNumbering.Controls.Add(this.lblStep);
            this.gbNumbering.Controls.Add(this.numStep);
            this.gbNumbering.Controls.Add(this.lblCount);
            this.gbNumbering.Controls.Add(this.numCount);
            this.gbNumbering.Controls.Add(this.lblStartNum);
            this.gbNumbering.Controls.Add(this.numStartNum);
            this.gbNumbering.Location = new System.Drawing.Point(12, 12);
            this.gbNumbering.Name = "gbNumbering";
            this.gbNumbering.Size = new System.Drawing.Size(390, 80);
            this.gbNumbering.TabIndex = 0;
            this.gbNumbering.TabStop = false;
            this.gbNumbering.Text = "Numbering";
            //
            // lblStep
            //
            this.lblStep.AutoSize = true;
            this.lblStep.Location = new System.Drawing.Point(264, 19);
            this.lblStep.Name = "lblStep";
            this.lblStep.Size = new System.Drawing.Size(30, 15);
            this.lblStep.TabIndex = 4;
            this.lblStep.Text = "Step";
            //
            // numStep
            //
            this.numStep.Location = new System.Drawing.Point(267, 37);
            this.numStep.Maximum = new decimal(new int[] { 1000, 0, 0, 0 });
            this.numStep.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            this.numStep.Name = "numStep";
            this.numStep.Size = new System.Drawing.Size(110, 23);
            this.numStep.TabIndex = 5;
            this.numStep.Value = new decimal(new int[] { 1, 0, 0, 0 });
            this.numStep.ValueChanged += new System.EventHandler(this.Numbering_ValueChanged);
            //
            // lblCount
            //
            this.lblCount.AutoSize = true;
            this.lblCount.Location = new System.Drawing.Point(138, 19);
            this.lblCount.Name = "lblCount";
            this.lblCount.Size = new System.Drawing.Size(39, 15);
            this.lblCount.TabIndex = 2;
            this.lblCount.Text = "Count";
            //
            // numCount
            //
            this.numCount.Location = new System.Drawing.Point(141, 37);
            this.numCount.Maximum = new decimal(new int[] { 100000, 0, 0, 0 });
            this.numCount.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            this.numCount.Name = "numCount";
            this.numCount.Size = new System.Drawing.Size(110, 23);
            this.numCount.TabIndex = 3;
            this.numCount.Value = new decimal(new int[] { 10, 0, 0, 0 });
            this.numCount.ValueChanged += new System.EventHandler(this.Numbering_ValueChanged);
            //
            // lblStartNum
            //
            this.lblStartNum.AutoSize = true;
            this.lblStartNum.Location = new System.Drawing.Point(12, 19);
            this.lblStartNum.Name = "lblStartNum";
            this.lblStartNum.Size = new System.Drawing.Size(72, 15);
            this.lblStartNum.TabIndex = 0;
            this.lblStartNum.Text = "Start number";
            //
            // numStartNum
            //
            this.numStartNum.Location = new System.Drawing.Point(15, 37);
            this.numStartNum.Maximum = new decimal(new int[] { 2147483647, 0, 0, 0 });
            this.numStartNum.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            this.numStartNum.Name = "numStartNum";
            this.numStartNum.Size = new System.Drawing.Size(110, 23);
            this.numStartNum.TabIndex = 1;
            this.numStartNum.Value = new decimal(new int[] { 101, 0, 0, 0 });
            this.numStartNum.ValueChanged += new System.EventHandler(this.Numbering_ValueChanged);
            //
            // gbNaming
            //
            this.gbNaming.Controls.Add(this.lblTokenHint);
            this.gbNaming.Controls.Add(this.txtCodePrefix);
            this.gbNaming.Controls.Add(this.lblCodePrefix);
            this.gbNaming.Controls.Add(this.txtNamePrefix);
            this.gbNaming.Controls.Add(this.lblNamePrefix);
            this.gbNaming.Location = new System.Drawing.Point(12, 98);
            this.gbNaming.Name = "gbNaming";
            this.gbNaming.Size = new System.Drawing.Size(390, 110);
            this.gbNaming.TabIndex = 1;
            this.gbNaming.TabStop = false;
            this.gbNaming.Text = "Name and code";
            //
            // lblTokenHint
            //
            this.lblTokenHint.AutoSize = true;
            this.lblTokenHint.ForeColor = System.Drawing.SystemColors.GrayText;
            this.lblTokenHint.Location = new System.Drawing.Point(12, 85);
            this.lblTokenHint.Name = "lblTokenHint";
            this.lblTokenHint.Size = new System.Drawing.Size(200, 15);
            this.lblTokenHint.TabIndex = 4;
            this.lblTokenHint.Text = "{n} is replaced with the channel number";
            //
            // txtCodePrefix
            //
            this.txtCodePrefix.Location = new System.Drawing.Point(110, 52);
            this.txtCodePrefix.Name = "txtCodePrefix";
            this.txtCodePrefix.Size = new System.Drawing.Size(267, 23);
            this.txtCodePrefix.TabIndex = 3;
            //
            // lblCodePrefix
            //
            this.lblCodePrefix.AutoSize = true;
            this.lblCodePrefix.Location = new System.Drawing.Point(12, 55);
            this.lblCodePrefix.Name = "lblCodePrefix";
            this.lblCodePrefix.Size = new System.Drawing.Size(86, 15);
            this.lblCodePrefix.TabIndex = 2;
            this.lblCodePrefix.Text = "Code template";
            //
            // txtNamePrefix
            //
            this.txtNamePrefix.Location = new System.Drawing.Point(110, 22);
            this.txtNamePrefix.Name = "txtNamePrefix";
            this.txtNamePrefix.Size = new System.Drawing.Size(267, 23);
            this.txtNamePrefix.TabIndex = 1;
            //
            // lblNamePrefix
            //
            this.lblNamePrefix.AutoSize = true;
            this.lblNamePrefix.Location = new System.Drawing.Point(12, 25);
            this.lblNamePrefix.Name = "lblNamePrefix";
            this.lblNamePrefix.Size = new System.Drawing.Size(88, 15);
            this.lblNamePrefix.TabIndex = 0;
            this.lblNamePrefix.Text = "Name template";
            //
            // gbProps
            //
            this.gbProps.Controls.Add(this.chkActive);
            this.gbProps.Controls.Add(this.cbFormat);
            this.gbProps.Controls.Add(this.lblFormat);
            this.gbProps.Controls.Add(this.cbDataType);
            this.gbProps.Controls.Add(this.lblDataType);
            this.gbProps.Controls.Add(this.cbCnlType);
            this.gbProps.Controls.Add(this.lblCnlType);
            this.gbProps.Location = new System.Drawing.Point(12, 214);
            this.gbProps.Name = "gbProps";
            this.gbProps.Size = new System.Drawing.Size(390, 140);
            this.gbProps.TabIndex = 2;
            this.gbProps.TabStop = false;
            this.gbProps.Text = "Properties";
            //
            // chkActive
            //
            this.chkActive.AutoSize = true;
            this.chkActive.Checked = true;
            this.chkActive.CheckState = System.Windows.Forms.CheckState.Checked;
            this.chkActive.Location = new System.Drawing.Point(110, 112);
            this.chkActive.Name = "chkActive";
            this.chkActive.Size = new System.Drawing.Size(60, 19);
            this.chkActive.TabIndex = 6;
            this.chkActive.Text = "Active";
            this.chkActive.UseVisualStyleBackColor = true;
            //
            // cbFormat
            //
            this.cbFormat.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cbFormat.FormattingEnabled = true;
            this.cbFormat.Location = new System.Drawing.Point(110, 80);
            this.cbFormat.Name = "cbFormat";
            this.cbFormat.Size = new System.Drawing.Size(267, 23);
            this.cbFormat.TabIndex = 5;
            //
            // lblFormat
            //
            this.lblFormat.AutoSize = true;
            this.lblFormat.Location = new System.Drawing.Point(12, 83);
            this.lblFormat.Name = "lblFormat";
            this.lblFormat.Size = new System.Drawing.Size(45, 15);
            this.lblFormat.TabIndex = 4;
            this.lblFormat.Text = "Format";
            //
            // cbDataType
            //
            this.cbDataType.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cbDataType.FormattingEnabled = true;
            this.cbDataType.Location = new System.Drawing.Point(110, 51);
            this.cbDataType.Name = "cbDataType";
            this.cbDataType.Size = new System.Drawing.Size(267, 23);
            this.cbDataType.TabIndex = 3;
            //
            // lblDataType
            //
            this.lblDataType.AutoSize = true;
            this.lblDataType.Location = new System.Drawing.Point(12, 54);
            this.lblDataType.Name = "lblDataType";
            this.lblDataType.Size = new System.Drawing.Size(58, 15);
            this.lblDataType.TabIndex = 2;
            this.lblDataType.Text = "Data type";
            //
            // cbCnlType
            //
            this.cbCnlType.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cbCnlType.FormattingEnabled = true;
            this.cbCnlType.Location = new System.Drawing.Point(110, 22);
            this.cbCnlType.Name = "cbCnlType";
            this.cbCnlType.Size = new System.Drawing.Size(267, 23);
            this.cbCnlType.TabIndex = 1;
            //
            // lblCnlType
            //
            this.lblCnlType.AutoSize = true;
            this.lblCnlType.Location = new System.Drawing.Point(12, 25);
            this.lblCnlType.Name = "lblCnlType";
            this.lblCnlType.Size = new System.Drawing.Size(75, 15);
            this.lblCnlType.TabIndex = 0;
            this.lblCnlType.Text = "Channel type";
            //
            // lblPreview
            //
            this.lblPreview.AutoSize = true;
            this.lblPreview.Location = new System.Drawing.Point(12, 366);
            this.lblPreview.Name = "lblPreview";
            this.lblPreview.Size = new System.Drawing.Size(0, 15);
            this.lblPreview.TabIndex = 3;
            //
            // btnCreate
            //
            this.btnCreate.Location = new System.Drawing.Point(221, 390);
            this.btnCreate.Name = "btnCreate";
            this.btnCreate.Size = new System.Drawing.Size(85, 25);
            this.btnCreate.TabIndex = 4;
            this.btnCreate.Text = "Create";
            this.btnCreate.UseVisualStyleBackColor = true;
            this.btnCreate.Click += new System.EventHandler(this.btnCreate_Click);
            //
            // btnCancel
            //
            this.btnCancel.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.btnCancel.Location = new System.Drawing.Point(312, 390);
            this.btnCancel.Name = "btnCancel";
            this.btnCancel.Size = new System.Drawing.Size(85, 25);
            this.btnCancel.TabIndex = 5;
            this.btnCancel.Text = "Cancel";
            this.btnCancel.UseVisualStyleBackColor = true;
            //
            // FrmCnlCreateEmpty
            //
            this.AcceptButton = this.btnCreate;
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.CancelButton = this.btnCancel;
            this.ClientSize = new System.Drawing.Size(414, 427);
            this.Controls.Add(this.btnCancel);
            this.Controls.Add(this.btnCreate);
            this.Controls.Add(this.lblPreview);
            this.Controls.Add(this.gbProps);
            this.Controls.Add(this.gbNaming);
            this.Controls.Add(this.gbNumbering);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "FrmCnlCreateEmpty";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Create Empty Channels";
            this.Load += new System.EventHandler(this.FrmCnlCreateEmpty_Load);
            this.gbNumbering.ResumeLayout(false);
            this.gbNumbering.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.numStep)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numCount)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numStartNum)).EndInit();
            this.gbNaming.ResumeLayout(false);
            this.gbNaming.PerformLayout();
            this.gbProps.ResumeLayout(false);
            this.gbProps.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        #endregion

        private System.Windows.Forms.GroupBox gbNumbering;
        private System.Windows.Forms.Label lblStartNum;
        private System.Windows.Forms.NumericUpDown numStartNum;
        private System.Windows.Forms.Label lblStep;
        private System.Windows.Forms.NumericUpDown numStep;
        private System.Windows.Forms.Label lblCount;
        private System.Windows.Forms.NumericUpDown numCount;
        private System.Windows.Forms.GroupBox gbNaming;
        private System.Windows.Forms.Label lblTokenHint;
        private System.Windows.Forms.TextBox txtCodePrefix;
        private System.Windows.Forms.Label lblCodePrefix;
        private System.Windows.Forms.TextBox txtNamePrefix;
        private System.Windows.Forms.Label lblNamePrefix;
        private System.Windows.Forms.GroupBox gbProps;
        private System.Windows.Forms.CheckBox chkActive;
        private System.Windows.Forms.ComboBox cbFormat;
        private System.Windows.Forms.Label lblFormat;
        private System.Windows.Forms.ComboBox cbDataType;
        private System.Windows.Forms.Label lblDataType;
        private System.Windows.Forms.ComboBox cbCnlType;
        private System.Windows.Forms.Label lblCnlType;
        private System.Windows.Forms.Label lblPreview;
        private System.Windows.Forms.Button btnCreate;
        private System.Windows.Forms.Button btnCancel;
    }
}
