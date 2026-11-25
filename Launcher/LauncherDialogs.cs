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

        internal static void Message(Form owner, string message, string title) =>
            ShowMessage(owner, message, title);

        internal static bool OfferBrowser(Form owner, string message, string title) =>
            ShowMessage(owner, message, title, "&Open in browser", "Close") == DialogResult.Yes;

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
