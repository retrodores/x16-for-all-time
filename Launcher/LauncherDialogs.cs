using System;
using System.Drawing;
using System.Windows.Forms;
using static CommanderX16.MainForm;

namespace CommanderX16
{
    internal static class LauncherDialogs
    {
        internal static DialogResult Show(Form dialog, Form owner)
        {
            dialog.Icon = owner.Icon;
            return dialog.ShowDialog(owner);
        }

        internal static bool Confirm(Form owner, string message, string title) =>
            ShowMessage(owner, message, title, "&Yes", "&No") == DialogResult.Yes;

        internal static bool ConfirmProgramDeletion(Form owner, string summary, string[] paths)
        {
            using (var dialog = new Form
            {
                Text = "Delete programs from disk", Font = owner.Font, AutoScaleMode = AutoScaleMode.Dpi,
                StartPosition = FormStartPosition.CenterParent, ClientSize = new Size(570, 310),
                MinimumSize = new Size(440, 260), ShowInTaskbar = false, MinimizeBox = false
            })
            {
                var body = Table();
                body.AutoSize = false;
                body.Dock = DockStyle.Fill;
                body.Padding = new Padding(16);
                dialog.Controls.Add(body);
                AddRow(body, new Label { Text = summary, AutoSize = true, MaximumSize = new Size(390, 0) });
                AddRow(body, new TextBox
                {
                    Text = string.Join(Environment.NewLine, paths), Multiline = true, ReadOnly = true,
                    Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical, AccessibleName = "Files to delete"
                }, SizeType.Percent, 100);
                var cancel = Button("Cancel");
                cancel.DialogResult = DialogResult.Cancel;
                var delete = Button("&Delete files");
                delete.DialogResult = DialogResult.Yes;
                AddRow(body, Flow(cancel, delete));
                dialog.AcceptButton = dialog.CancelButton = cancel;
                dialog.ActiveControl = cancel;
                return Show(dialog, owner) == DialogResult.Yes;
            }
        }

        internal static void Message(Form owner, string message, string title) =>
            ShowMessage(owner, message, title);

        internal static bool OfferBrowser(Form owner, string message, string title) =>
            ShowMessage(owner, message, title, "&Open in browser", "Close") == DialogResult.Yes;

        internal static int SelectItem(Form owner, string title, string[] items)
        {
            using (var dialog = new Form
            {
                Text = title, Font = owner.Font, ClientSize = new Size(640, 300), AutoScaleMode = AutoScaleMode.Dpi,
                StartPosition = FormStartPosition.CenterParent, ShowInTaskbar = false,
                MinimizeBox = false
            })
            {
                var body = Table();
                body.Dock = DockStyle.Fill;
                body.AutoSize = false;
                body.Padding = new Padding(12);
                dialog.Controls.Add(body);
                var list = new ListBox
                {
                    Dock = DockStyle.Fill, HorizontalScrollbar = true, IntegralHeight = false,
                    AccessibleName = title
                };
                list.Items.AddRange(items);
                list.SelectedIndex = 0;
                var select = Button("Select");
                select.DialogResult = DialogResult.OK;
                var cancel = Button("Cancel");
                cancel.DialogResult = DialogResult.Cancel;
                dialog.AcceptButton = select;
                dialog.CancelButton = cancel;
                list.DoubleClick += (sender, args) =>
                {
                    if (list.IndexFromPoint(list.PointToClient(Cursor.Position)) >= 0)
                        dialog.DialogResult = DialogResult.OK;
                };
                AddRow(body, list, SizeType.Percent, 100);
                AddRow(body, Flow(select, cancel));
                dialog.ActiveControl = list;
                return Show(dialog, owner) == DialogResult.OK ? list.SelectedIndex : -1;
            }
        }

        private static DialogResult ShowMessage(Form owner, string message, string title,
            string acceptText = null, string closeText = "OK")
        {
            var font = owner?.Font ?? SystemFonts.MessageBoxFont;
            using (var icon = owner == null ? Icon.ExtractAssociatedIcon(Application.ExecutablePath) : null)
            using (var dialog = new Form
            {
                Text = title, Font = font, AutoScaleMode = AutoScaleMode.Dpi,
                StartPosition = owner == null ? FormStartPosition.CenterScreen : FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog, ShowInTaskbar = false,
                MinimizeBox = false, MaximizeBox = false
            })
            {
                int textHeight = TextRenderer.MeasureText(message, font, new Size(480, int.MaxValue),
                    TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.TextBoxControl).Height;
                dialog.ClientSize = new Size(540, Math.Max(140, Math.Min(420, textHeight + 88)));
                var body = Table();
                body.Dock = DockStyle.Fill;
                body.AutoSize = false;
                body.Padding = new Padding(16);
                dialog.Controls.Add(body);
                AddRow(body, new TextBox
                {
                    Text = message, Dock = DockStyle.Fill, Multiline = true, ReadOnly = true,
                    BorderStyle = BorderStyle.None, BackColor = dialog.BackColor, ScrollBars = ScrollBars.Vertical
                }, SizeType.Percent, 100);
                var close = Button(closeText);
                close.DialogResult = acceptText == null ? DialogResult.OK : DialogResult.No;
                var actions = Flow(close);
                actions.FlowDirection = FlowDirection.RightToLeft;
                if (acceptText != null)
                {
                    var yes = Button(acceptText);
                    yes.DialogResult = DialogResult.Yes;
                    actions.Controls.Add(yes);
                }
                AddRow(body, actions);
                dialog.AcceptButton = dialog.CancelButton = close;
                dialog.ActiveControl = close;
                if (owner != null) return Show(dialog, owner);
                dialog.Icon = icon;
                return dialog.ShowDialog();
            }
        }
    }
}
