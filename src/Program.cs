using System;
using System.Globalization;
using System.Threading;
using System.Windows;

namespace HardwareDeck
{
    public static class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.GetCultureInfo("en-US");
            CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo("en-US");
            string root = AppDomain.CurrentDomain.BaseDirectory;
            bool worker = args.Length == 1 && args[0] == "--preset-worker";
            bool probe = args.Length == 2 && args[0] == "--probe" && !string.IsNullOrWhiteSpace(args[1]);
            bool preview = (args.Length == 2 || (args.Length == 3 && args[2] == "--stay")) && args[0] == "--preview" && !string.IsNullOrWhiteSpace(args[1]);
            if (args.Length != 0 && !probe && !preview && !worker)
                return 2;
            try
            {
                if (worker)
                    return PresetWorker.Run();
                if (probe)
                {
                    using (var collector = new Collector(root))
                    {
                        collector.Sample();
                        Thread.Sleep(1000);
                        collector.Sample();
                        Thread.Sleep(1000);
                        SnapshotFile.Write(args[1], collector.Sample());
                    }

                    return 0;
                }

                new DeckWindow(root, preview ? args[1] : null, preview && args.Length == 3).Run();
                return 0;
            }
            catch (Exception ex)
            {
                try
                {
                    SnapshotFile.Write(System.IO.Path.Combine(SnapshotFile.DataDirectory, "error.json"), new { Error = ex.ToString(), At = DateTime.UtcNow.ToString("o") });
                }
                catch
                {
                }

                if (args.Length == 0)
                    MessageBox.Show("HardwareDeck could not start.\n" + ex.Message, "HardwareDeck", MessageBoxButton.OK, MessageBoxImage.Error);
                return 1;
            }
        }
    }
}
