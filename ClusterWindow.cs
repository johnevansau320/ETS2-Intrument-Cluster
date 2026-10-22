using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Ets2Cluster;

/// <summary>The borderless, movable, resizable window around the cluster.</summary>
public sealed class ClusterWindow : Window
{
    readonly ClusterControl view = new();
    readonly Settings cfg;
    readonly TelemetryClient tel = new();
    readonly DispatcherTimer saveTimer = new() { Interval = TimeSpan.FromMilliseconds(700) };
    readonly bool demo;
    long lastTick;
    bool dirty = true;

    // trip tracking
    float lastFuel = -1, lastOdo = -1;
    double tripFuel, tripDist;

    public ClusterWindow(Settings settings, bool demo, string? snapshot, string? demoState = null)
    {
        cfg = settings; this.demo = demo;
        Title = "ETS2 Instrument Cluster";
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        MinWidth = 320; MinHeight = 180;
        Width = Math.Max(MinWidth, cfg.Width); Height = Math.Max(MinHeight, cfg.Height);
        if (!double.IsNaN(cfg.Left) && !double.IsNaN(cfg.Top) && Fits(cfg.Left, cfg.Top)) { WindowStartupLocation = WindowStartupLocation.Manual; Left = cfg.Left; Top = cfg.Top; }
        else WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Topmost = cfg.Topmost; Opacity = Math.Clamp(cfg.Opacity, 0.2, 1);

        view.Cfg = cfg;
        Content = view;
        ContextMenu = BuildMenu();

        MouseLeftButtonDown += (_, e) => { if (e.ClickCount == 2) ToggleMax(); else { try { DragMove(); } catch { /* released too early */ } } };
        MouseWheel += OnWheel;
        KeyDown += (_, e) => { if (e.Key == Key.F11) ToggleMax(); else if (e.Key == Key.Escape && WindowState == WindowState.Maximized) ToggleMax(); };
        LocationChanged += (_, _) => { saveTimer.Stop(); saveTimer.Start(); };
        SizeChanged += OnSizeChanged;
        saveTimer.Tick += (_, _) => { saveTimer.Stop(); PersistWindow(); };
        Closed += (_, _) => { PersistWindow(); tel.Dispose(); };

        CompositionTarget.Rendering += OnFrame;
        if (demo) { Apply(DemoTruck(demoState)); }
        else
        {
            tel.Host = () => cfg.Host;
            tel.Updated += tr => Dispatcher.BeginInvoke(new Action(() => Apply(tr)));
            tel.Status += s => Dispatcher.BeginInvoke(new Action(() => { if (view.StatusText != s) { view.StatusText = s; dirty = true; } }));
            tel.Start();
        }

        if (snapshot != null)
        {
            var st = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.2) };
            st.Tick += (_, _) => { st.Stop(); SaveSnapshot(snapshot); Close(); };
            st.Start();
        }
    }

    static bool Fits(double l, double t) =>
        l > SystemParameters.VirtualScreenLeft - 200 && t > SystemParameters.VirtualScreenTop - 50 &&
        l < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 200 && t < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 100;

    // ------------------------------------------------------------------------------------------ data

    string lastSig = "";

    void Apply(Truck tr)
    {
        view.T = tr;
        string sig = tr.Signature();
        if (tr.Connected && tr.OdoKm > 0)
        {
            if (double.IsNaN(cfg.TripBaseKm) || cfg.TripBaseKm > tr.OdoKm) { cfg.TripBaseKm = tr.OdoKm; cfg.Save(); }
            view.TripKm = Math.Max(0, tr.OdoKm - cfg.TripBaseKm);
            if (lastOdo >= 0 && tr.OdoKm >= lastOdo && tr.OdoKm - lastOdo < 5) tripDist += tr.OdoKm - lastOdo;
            lastOdo = tr.OdoKm;
            if (lastFuel >= 0 && tr.FuelL < lastFuel && lastFuel - tr.FuelL < 20) tripFuel += lastFuel - tr.FuelL;   // refuelling (a rise) is ignored
            lastFuel = tr.FuelL;
            view.TripLPer100 = tripDist > 0.5 ? tripFuel / tripDist * 100 : -1;
        }
        if (sig != lastSig) { lastSig = sig; dirty = true; }
    }

    void ResetTrip()
    {
        cfg.TripBaseKm = view.T.OdoKm > 0 ? view.T.OdoKm : double.NaN; cfg.Save();
        tripFuel = 0; tripDist = 0; view.TripKm = 0; view.TripLPer100 = -1; dirty = true;
        view.InvalidateVisual();
    }

    static Truck DemoTruck(string? state)
    {
        var t = DemoBase();
        if (state == "over") { t.SpeedKmh = 98; t.SpeedLimit = 90; t.RpmValue = 1480; t.Gear = 12; t.BlinkerR = false; t.HighBeam = true; }
        else if (state == "warn") { t.SpeedKmh = 42; t.SpeedLimit = 50; t.CruiseOn = false; t.RpmValue = 1750; t.Gear = 7; t.FuelL = 150; t.RangeKm = 410; t.ParkBrake = false; t.MotorBrake = true; t.Retarder = 2; t.FuelWarn = true; t.AirWarn = true; t.Wipers = true; t.Beacon = true; t.BlinkerL = true; t.BlinkerR = false; t.WaterC = 101; t.AdBlueL = 6; t.AdBlueWarn = true; }
        return t;
    }

    static Truck DemoBase() => new()
    {
        Connected = true, Make = "Volvo", SpeedKmh = 87, SpeedLimit = 90, CruiseOn = true, CruiseKmh = 90, RpmValue = 1180, RpmMax = 2200, Gear = 12, ForwardGears = 12, Shifter = "automatic",
        FuelL = 1110, FuelCapL = 1500, FuelAvgLPerKm = 0.277f, RangeKm = 3020, AdBlueL = 51, AdBlueCapL = 60, OdoKm = 2369, WaterC = 87, OilC = 96, LowBeam = true, BlinkerR = true,
        GameTime = new DateTime(1, 1, 3, 6, 50, 0), EtaMin = 72, RestMin = 8 * 60 + 12, Wipers = false,
    };

    void OnFrame(object? sender, EventArgs e)
    {
        long now = Environment.TickCount64;
        double dt = Math.Min(0.1, Math.Max(0.001, (now - lastTick) / 1000.0)); lastTick = now;
        bool moving = view.Step(dt);
        if (moving || dirty) { dirty = false; view.InvalidateVisual(); }
    }

    // ------------------------------------------------------------------------------------------ window behaviour

    bool fixingSize;
    void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        saveTimer.Stop(); saveTimer.Start();
        if (WindowState != WindowState.Normal || fixingSize) return;
        // keep the 16:9 shape of the cluster
        fixingSize = true;
        try
        {
            if (e.WidthChanged && Math.Abs(Height - Width * 9 / 16) > 2) Height = Width * 9 / 16;
            else if (e.HeightChanged && Math.Abs(Width - Height * 16 / 9) > 2) Width = Height * 16 / 9;
        }
        finally { fixingSize = false; }
    }

    void ToggleMax() => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    void OnWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            SetOpacity(Math.Clamp(Opacity + (e.Delta > 0 ? 0.05 : -0.05), 0.2, 1));
        }
        else if (WindowState == WindowState.Normal)
        {
            double f = e.Delta > 0 ? 1.06 : 1 / 1.06;
            Width = Math.Clamp(Width * f, MinWidth, 3840);
        }
    }

    void SetOpacity(double o) { Opacity = o; cfg.Opacity = o; cfg.Save(); }

    void PersistWindow()
    {
        if (WindowState == WindowState.Normal) { cfg.Left = Left; cfg.Top = Top; cfg.Width = Width; cfg.Height = Height; }
        cfg.Save();
    }

    // ------------------------------------------------------------------------------------------ menu

    ContextMenu BuildMenu()
    {
        var m = new ContextMenu();
        MenuItem Item(string text, Action a, bool check = false, bool on = false) { var i = new MenuItem { Header = text, IsCheckable = check, IsChecked = on }; i.Click += (_, _) => a(); return i; }

        var top = Item("Always on top", () => { }, true, cfg.Topmost);
        top.Click += (_, _) => { cfg.Topmost = top.IsChecked; Topmost = cfg.Topmost; cfg.Save(); };
        m.Items.Add(top);

        var op = new MenuItem { Header = "Opacity" };
        foreach (int pct in new[] { 100, 90, 80, 70, 60, 50, 40 }) { int p = pct; op.Items.Add(Item(p + " %", () => SetOpacity(p / 100.0))); }
        m.Items.Add(op);

        var size = new MenuItem { Header = "Size" };
        foreach (var (name, w) in new[] { ("Small (640 x 360)", 640.0), ("Medium (960 x 540)", 960.0), ("Large (1280 x 720)", 1280.0), ("Extra large (1600 x 900)", 1600.0) })
        { double ww = w; size.Items.Add(Item(name, () => { WindowState = WindowState.Normal; Width = ww; Height = ww * 9 / 16; })); }
        size.Items.Add(Item("Full screen (F11)", ToggleMax));
        m.Items.Add(size);

        var unit = new MenuItem { Header = "Units" };
        var kmh = Item("km/h and km", () => { }, true, !cfg.Mph); var mph = Item("mph and miles", () => { }, true, cfg.Mph);
        kmh.Click += (_, _) => { cfg.Mph = false; kmh.IsChecked = true; mph.IsChecked = false; cfg.Save(); dirty = true; view.InvalidateVisual(); };
        mph.Click += (_, _) => { cfg.Mph = true; mph.IsChecked = true; kmh.IsChecked = false; cfg.Save(); dirty = true; view.InvalidateVisual(); };
        unit.Items.Add(kmh); unit.Items.Add(mph);
        m.Items.Add(unit);

        var make = Item("Show the truck make", () => { }, true, cfg.ShowMake);
        make.Click += (_, _) => { cfg.ShowMake = make.IsChecked; cfg.Save(); dirty = true; view.InvalidateVisual(); };
        m.Items.Add(make);

        m.Items.Add(new Separator());
        m.Items.Add(Item("Reset trip", ResetTrip));
        m.Items.Add(Item("Telemetry server address...", AskHost));
        m.Items.Add(Item("Reset window position and size", () => { WindowState = WindowState.Normal; Width = 960; Height = 540; WindowStartupLocation = WindowStartupLocation.CenterScreen; Left = (SystemParameters.PrimaryScreenWidth - Width) / 2; Top = (SystemParameters.PrimaryScreenHeight - Height) / 2; }));
        m.Items.Add(new Separator());
        m.Items.Add(Item("Close", Close));
        return m;
    }

    void AskHost()
    {
        var box = new TextBox { Text = cfg.Host, Margin = new Thickness(0, 6, 0, 10), Padding = new Thickness(4), MinWidth = 280 };
        var ok = new Button { Content = "Save", IsDefault = true, Padding = new Thickness(18, 4, 18, 4), Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, Padding = new Thickness(18, 4, 18, 4) };
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right }; row.Children.Add(ok); row.Children.Add(cancel);
        var sp = new StackPanel { Margin = new Thickness(16) };
        sp.Children.Add(new TextBlock { Text = "Address of the TruckSim GPS Telemetry Server (host:port, usually 127.0.0.1:31377):", TextWrapping = TextWrapping.Wrap, MaxWidth = 360 });
        sp.Children.Add(box); sp.Children.Add(row);
        var w = new Window { Title = "Telemetry server", Content = sp, SizeToContent = SizeToContent.WidthAndHeight, WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = this, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false };
        ok.Click += (_, _) => { cfg.Host = box.Text.Trim().Length > 0 ? box.Text.Trim() : "127.0.0.1:31377"; cfg.Save(); w.DialogResult = true; };
        w.ShowDialog();
    }

    // ------------------------------------------------------------------------------------------ test hook

    void SaveSnapshot(string path)
    {
        var tmp = new ClusterControl { T = view.T, Cfg = cfg, TripKm = view.TripKm, TripLPer100 = view.TripLPer100, StatusText = view.StatusText };
        tmp.Measure(new Size(1280, 720)); tmp.Arrange(new Rect(0, 0, 1280, 720));
        int steps = 0; while (steps < 400 && tmp.Step(0.1)) steps++;
        tmp.InvalidateVisual(); tmp.UpdateLayout();
        var rtb = new RenderTargetBitmap(1280, 720, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(tmp);
        var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = System.IO.File.Create(path); enc.Save(fs);
    }
}
