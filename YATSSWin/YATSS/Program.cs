namespace YATSS
{
    internal static class Program
    {
        internal const string InstallerMutexName = @"Local\YATSS.Desktop.Running";
        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            // Setup/uninstall must wait for the director to close YATSS normally.
            using Mutex runningMarker = new(false, InstallerMutexName);
            // To customize application configuration such as set high DPI settings or default font,
            // see https://aka.ms/applicationconfiguration.
            ApplicationConfiguration.Initialize();
            Application.Run(new YATSS());
            GC.KeepAlive(runningMarker);
        }
    }
}
