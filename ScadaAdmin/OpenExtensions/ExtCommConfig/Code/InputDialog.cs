// Copyright (c) Rapid Software LLC. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Drawing;
using System.Windows.Forms;

namespace Scada.Admin.Extensions.ExtCommConfig.Code
{
    /// <summary>
    /// Provides a simple modal dialog for entering a single line of text.
    /// <para>Предоставляет простое модальное окно для ввода одной строки текста.</para>
    /// </summary>
    internal static class InputDialog
    {
        /// <summary>
        /// Shows the dialog and returns the entered non-empty text, or null if cancelled.
        /// </summary>
        public static string Show(string title, string prompt, string initialValue)
        {
            using Form form = new()
            {
                Text = title,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent,
                ClientSize = new Size(360, 130),
                MaximizeBox = false,
                MinimizeBox = false,
                ShowInTaskbar = false
            };

            Label lblPrompt = new()
            {
                Text = prompt,
                AutoSize = true,
                MaximumSize = new Size(336, 0),
                Location = new Point(12, 14)
            };

            TextBox txtValue = new()
            {
                Text = initialValue ?? "",
                Location = new Point(12, 48),
                Width = 336,
                MaxLength = 100,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };

            Button btnOk = new()
            {
                Text = "OK",
                DialogResult = DialogResult.OK,
                Location = new Point(184, 90),
                Width = 80,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right
            };

            Button btnCancel = new()
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Location = new Point(268, 90),
                Width = 80,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right
            };

            form.Controls.Add(lblPrompt);
            form.Controls.Add(txtValue);
            form.Controls.Add(btnOk);
            form.Controls.Add(btnCancel);
            form.AcceptButton = btnOk;
            form.CancelButton = btnCancel;
            txtValue.SelectAll();

            if (form.ShowDialog() == DialogResult.OK)
            {
                string value = txtValue.Text.Trim();
                return value.Length > 0 ? value : null;
            }

            return null;
        }
    }
}
