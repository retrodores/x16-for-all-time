using System;
using System.Windows.Forms;

namespace CommanderX16
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try
            {
                using (var store = new SettingsStore(SettingsStore.Home))
                    Application.Run(new MainForm(store));
            }
            catch (Exception error)
            {
                MessageBox.Show(error.Message, "Commander X16 For Right Now", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
