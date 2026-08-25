using System;
using System.Threading;
using System.Windows;

namespace CopyRecord
{
    internal static class Program
    {
        private static Mutex _mutex;

        [STAThread]
        public static void Main(string[] args)
        {
            bool created;
            _mutex = new Mutex(true, "Local\\CopyRecord.SingleInstance", out created);
            if (!created)
            {
                MessageBox.Show("CopyRecord 已经在运行，请按 Ctrl+Shift+V 呼出。", "CopyRecord");
                return;
            }

            Application application = new Application
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown
            };
            MainWindow window = new MainWindow();
            window.StartBackground();
            bool firstRun = window.ConsumeFirstRun();
            bool showArg = args != null && Array.IndexOf(args, "--show") >= 0;
            if (firstRun || showArg)
            {
                application.Dispatcher.BeginInvoke(new Action(window.ShowPalette));
            }
            if (args != null && Array.IndexOf(args, "--settings") >= 0)
            {
                application.Dispatcher.BeginInvoke(new Action(window.ShowSettings));
            }
            application.Run();

            _mutex.ReleaseMutex();
            _mutex.Dispose();
        }
    }
}
