// Copyright (c) Rapid Software LLC. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Scada.Admin.Project;
using Scada.Data.Const;
using Scada.Data.Entities;
using Scada.Forms;
using Scada.Lang;
using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Forms;

namespace Scada.Admin.Extensions.ExtChannelSearch.Forms
{
    /// <summary>
    /// Represents a dialog that bulk-creates empty channels not bound to any device or communication line.
    /// <para>Представляет диалог для массового создания пустых каналов без привязки к устройству или линии связи.</para>
    /// </summary>
    public partial class FrmCnlCreateEmpty : Form
    {
        /// <summary>
        /// Represents a combo box item that carries an optional identifier.
        /// </summary>
        private class ListItem
        {
            public int? Id { get; init; }
            public string Text { get; init; }
            public override string ToString() => Text;
        }

        private const int MaxCount = 100000; // the safety cap on the number of channels created at once

        private readonly ScadaProject project; // the project under development
        private bool createdAny;                // at least one channel was created


        /// <summary>
        /// Initializes a new instance of the class.
        /// </summary>
        public FrmCnlCreateEmpty(ScadaProject project)
        {
            InitializeComponent();
            this.project = project ?? throw new ArgumentNullException(nameof(project));
            createdAny = false;
            Localize();
        }


        /// <summary>
        /// Gets a value indicating whether any channels were actually created.
        /// </summary>
        public bool CreatedAny => createdAny;


        /// <summary>
        /// Applies the interface language to the form.
        /// </summary>
        private void Localize()
        {
            bool ru = Locale.IsRussian;

            Text = ru ? "Создание пустых каналов" : "Create Empty Channels";
            gbNumbering.Text = ru ? "Нумерация" : "Numbering";
            lblStartNum.Text = ru ? "Начальный номер" : "Start number";
            lblCount.Text = ru ? "Количество" : "Count";
            lblStep.Text = ru ? "Шаг" : "Step";
            gbNaming.Text = ru ? "Имя и код" : "Name and code";
            lblNamePrefix.Text = ru ? "Шаблон имени" : "Name template";
            lblCodePrefix.Text = ru ? "Шаблон кода" : "Code template";
            lblTokenHint.Text = ru ? "{n} подставляет номер канала" : "{n} is replaced with the channel number";
            gbProps.Text = ru ? "Свойства" : "Properties";
            lblCnlType.Text = ru ? "Тип канала" : "Channel type";
            lblDataType.Text = ru ? "Тип данных" : "Data type";
            lblFormat.Text = ru ? "Формат" : "Format";
            chkActive.Text = ru ? "Активен" : "Active";
            btnCreate.Text = ru ? "Создать" : "Create";
            btnCancel.Text = ru ? "Отмена" : "Cancel";
        }

        /// <summary>
        /// Fills the channel type, data type and format combo boxes from the configuration database.
        /// </summary>
        private void FillCombos()
        {
            bool ru = Locale.IsRussian;
            string notSet = ru ? "(не задано)" : "(not set)";

            // channel type is required, so no empty option; default to the input type
            cbCnlType.Items.Clear();
            foreach (CnlType cnlType in project.ConfigDatabase.CnlTypeTable.Enumerate())
                cbCnlType.Items.Add(new ListItem { Id = cnlType.CnlTypeID, Text = $"{cnlType.CnlTypeID}. {cnlType.Name}" });
            SelectById(cbCnlType, CnlTypeID.Input);

            // data type is optional
            cbDataType.Items.Clear();
            cbDataType.Items.Add(new ListItem { Id = null, Text = notSet });
            foreach (DataType dataType in project.ConfigDatabase.DataTypeTable.Enumerate())
                cbDataType.Items.Add(new ListItem { Id = dataType.DataTypeID, Text = $"{dataType.DataTypeID}. {dataType.Name}" });
            cbDataType.SelectedIndex = 0;

            // format is optional
            cbFormat.Items.Clear();
            cbFormat.Items.Add(new ListItem { Id = null, Text = notSet });
            foreach (Format format in project.ConfigDatabase.FormatTable.Enumerate())
                cbFormat.Items.Add(new ListItem { Id = format.FormatID, Text = $"{format.FormatID}. {format.Name}" });
            cbFormat.SelectedIndex = 0;
        }

        /// <summary>
        /// Selects the combo box item whose identifier equals the specified value, or the first item otherwise.
        /// </summary>
        private static void SelectById(ComboBox comboBox, int id)
        {
            for (int i = 0; i < comboBox.Items.Count; i++)
            {
                if (comboBox.Items[i] is ListItem item && item.Id == id)
                {
                    comboBox.SelectedIndex = i;
                    return;
                }
            }

            if (comboBox.Items.Count > 0)
                comboBox.SelectedIndex = 0;
        }

        /// <summary>
        /// Gets the identifier selected in the specified combo box.
        /// </summary>
        private static int? GetSelectedId(ComboBox comboBox)
        {
            return comboBox.SelectedItem is ListItem item ? item.Id : null;
        }

        /// <summary>
        /// Builds the channel name or code from a template, substituting {n} with the channel number.
        /// </summary>
        private static string BuildText(string template, int cnlNum)
        {
            if (string.IsNullOrEmpty(template))
                return "";

            return template.Contains("{n}", StringComparison.Ordinal)
                ? template.Replace("{n}", cnlNum.ToString())
                : template + cnlNum;
        }

        /// <summary>
        /// Computes the channel numbers for the requested range.
        /// </summary>
        private List<int> GetCnlNums()
        {
            int start = (int)numStartNum.Value;
            int count = (int)numCount.Value;
            int step = (int)numStep.Value;
            List<int> cnlNums = new(count);
            long num = start;

            for (int i = 0; i < count; i++)
            {
                if (num > ConfigDatabase.MaxID)
                    break;

                cnlNums.Add((int)num);
                num += step;
            }

            return cnlNums;
        }

        /// <summary>
        /// Updates the preview label that summarizes the channel numbers to be created.
        /// </summary>
        private void UpdatePreview()
        {
            bool ru = Locale.IsRussian;
            List<int> cnlNums = GetCnlNums();

            if (cnlNums.Count == 0)
            {
                lblPreview.Text = ru ? "Нет каналов для создания" : "No channels to create";
            }
            else
            {
                string range = cnlNums.Count == 1
                    ? cnlNums[0].ToString()
                    : $"{cnlNums[0]}..{cnlNums[^1]}";
                lblPreview.Text = ru
                    ? $"Будет создано каналов: {cnlNums.Count} ({range})"
                    : $"Channels to create: {cnlNums.Count} ({range})";
            }
        }

        /// <summary>
        /// Checks the requested numbers against the channel table and reports a conflict if any already exists.
        /// </summary>
        private bool CheckNoConflicts(List<int> cnlNums)
        {
            List<int> taken = new();

            foreach (int cnlNum in cnlNums)
            {
                if (project.ConfigDatabase.CnlTable.PkExists(cnlNum))
                {
                    taken.Add(cnlNum);

                    if (taken.Count >= 10)
                        break;
                }
            }

            if (taken.Count == 0)
                return true;

            bool ru = Locale.IsRussian;
            StringBuilder sb = new();
            sb.Append(ru
                ? "Указанные номера каналов уже заняты, создание отменено:"
                : "The specified channel numbers are already in use, creation cancelled:");
            sb.AppendLine().Append(string.Join(", ", taken));

            if (taken.Count >= 10)
                sb.Append(ru ? " и др." : " etc.");

            ScadaUiUtils.ShowError(sb.ToString());
            return false;
        }

        /// <summary>
        /// Creates the channels and adds them to the configuration database.
        /// </summary>
        private void CreateChannels()
        {
            List<int> cnlNums = GetCnlNums();

            if (cnlNums.Count == 0)
            {
                ScadaUiUtils.ShowWarning(Locale.IsRussian
                    ? "Нет каналов для создания."
                    : "There are no channels to create.");
                return;
            }

            if (!CheckNoConflicts(cnlNums))
                return;

            string namePrefix = txtNamePrefix.Text.Trim();
            string codePrefix = txtCodePrefix.Text.Trim();
            int cnlTypeID = GetSelectedId(cbCnlType) ?? CnlTypeID.Input;
            int? dataTypeID = GetSelectedId(cbDataType);
            int? formatID = GetSelectedId(cbFormat);
            bool active = chkActive.Checked;

            foreach (int cnlNum in cnlNums)
            {
                project.ConfigDatabase.CnlTable.AddItem(new Cnl
                {
                    CnlNum = cnlNum,
                    Active = active,
                    Name = BuildText(namePrefix, cnlNum),
                    Code = BuildText(codePrefix, cnlNum),
                    CnlTypeID = cnlTypeID,
                    DataTypeID = dataTypeID,
                    FormatID = formatID,
                    DeviceNum = null,
                    ObjNum = null
                });
            }

            project.ConfigDatabase.CnlTable.Modified = true;
            createdAny = true;

            ScadaUiUtils.ShowInfo(Locale.IsRussian
                ? $"Создано каналов: {cnlNums.Count}."
                : $"Channels created: {cnlNums.Count}.");

            DialogResult = DialogResult.OK;
        }


        private void FrmCnlCreateEmpty_Load(object sender, EventArgs e)
        {
            FillCombos();
            UpdatePreview();
        }

        private void Numbering_ValueChanged(object sender, EventArgs e)
        {
            UpdatePreview();
        }

        private void btnCreate_Click(object sender, EventArgs e)
        {
            CreateChannels();
        }
    }
}
