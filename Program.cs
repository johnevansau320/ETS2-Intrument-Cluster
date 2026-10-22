using System.Windows;

namespace Ets2Cluster;

public static class Program
{
    // Ets2Cluster.exe [--config file.json] [--host 127.0.0.1:31377] [--demo] [--snapshot out.png]
    //   --demo      shows made-up values instead of reading the game (to try the layout); --demo-state over|warn picks another made-up state
    //   --snapshot  draws the cluster to a picture and quits (used for the manual)
    [STAThread]
    public static int Main(string[] args)
    {
        string? Arg(string name) { int i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
        Settings.PathOverride = Arg("--config");
        var cfg = Settings.Load();
        if (Arg("--host") is { } host) cfg.Host = host;
        UpdateCheck.FakeVersion = Arg("--fake-version");   // test only: pretend to be this version

        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        app.DispatcherUnhandledException += (_, e) =>
        {
            MessageBox.Show(e.Exception.ToString(), "ETS2 Instrument Cluster - unexpected error");
            e.Handled = true;
        };
        return app.Run(new ClusterWindow(cfg, args.Contains("--demo"), Arg("--snapshot"), Arg("--demo-state")));
    }
}
