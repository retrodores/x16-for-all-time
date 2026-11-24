using System;
using System.Drawing;
using System.Windows.Forms;

namespace CommanderX16
{
    internal static class Program
    {
        internal const string Title = "Commander X16 For Now";
        internal const string Tagline = "For Now, not Forever";

        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try
            {
                using (var store = new SettingsStore(SettingsStore.Home))
                using (var icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath))
                using (var window = new MainForm(store))
                {
                    window.Icon = icon;
                    Application.Run(window);
                }
            }
            catch (Exception error)
            {
                LauncherDialogs.Message(null, error.Message, Title);
            }
        }
    }
}
