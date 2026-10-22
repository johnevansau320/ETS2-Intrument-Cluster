using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Ets2Cluster;

/// <summary>
/// The instrument cluster, drawn on a fixed 1280 x 720 canvas that is scaled to fit the window:
/// speedometer with needle, gear, rev strip, fuel and AdBlue gauges, consumption, range, cruise, ETA, clocks, odometer and trip,
/// indicator lamps and warnings.
/// </summary>
public sealed class ClusterControl : FrameworkElement
{
    public const double DW = 1280, DH = 720;
    static readonly Point C = new(640, 440);
    const double R = 330;   // radius of the speed dial

    public Truck T = new();
    public Settings Cfg = new();
    public double TripKm, TripLPer100 = -1;
    public string StatusText = "";

    // smoothed values (the needle glides between telemetry updates)
    double dispSpeed, dispRpm, dispFuel, dispAdBlue;

    static Brush Fz(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
    static readonly Brush BgBrush = Fz(Color.FromRgb(3, 6, 10));
    static readonly Brush White = Fz(Color.FromRgb(236, 241, 246)), Dim = Fz(Color.FromRgb(124, 136, 148)), Grey = Fz(Color.FromRgb(58, 68, 79));
    static readonly Brush Green = Fz(Color.FromRgb(56, 214, 108)), Amber = Fz(Color.FromRgb(255, 176, 46)), Red = Fz(Color.FromRgb(255, 59, 48)), Blue = Fz(Color.FromRgb(58, 160, 255));
    static readonly Typeface Face = new(new FontFamily("Bahnschrift, Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
    static readonly Typeface FaceLight = new(new FontFamily("Bahnschrift, Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    readonly Dictionary<string, FormattedText> texts = new();

    public double DebugSpeed => dispSpeed;
    public ClusterControl() { SnapsToDevicePixels = false; UseLayoutRounding = false; }

    // ------------------------------------------------------------------------------------------ helpers

    FormattedText Text(string t, double size, Brush b, bool light = false)
    {
        string key = t + "|" + size.ToString("0.#", CultureInfo.InvariantCulture) + "|" + (light ? "l" : "b") + "|" + b.GetHashCode();
        if (!texts.TryGetValue(key, out var ft))
        {
            if (texts.Count > 3000) texts.Clear();
            ft = new FormattedText(t, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, light ? FaceLight : Face, size, b, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            texts[key] = ft;
        }
        return ft;
    }

    /// <summary>align: 0 left, 1 centre, 2 right of x; y is the vertical centre.</summary>
    double Draw(DrawingContext dc, string t, double x, double y, double size, Brush b, int align = 1, bool light = false)
    {
        var ft = Text(t, size, b, light);
        double lx = align == 0 ? x : align == 1 ? x - ft.Width / 2 : x - ft.Width;
        dc.DrawText(ft, new Point(lx, y - ft.Height / 2));
        return ft.Width;
    }

    static Point P(double angleDeg, double r) { double a = angleDeg * Math.PI / 180; return new Point(C.X + r * Math.Cos(a), C.Y - r * Math.Sin(a)); }

    static StreamGeometry Arc(double r, double a0, double a1)
    {
        var g = new StreamGeometry();
        using var c = g.Open();
        c.BeginFigure(P(a0, r), false, false);
        c.ArcTo(P(a1, r), new Size(r, r), 0, Math.Abs(a1 - a0) > 180, a1 < a0 ? SweepDirection.Clockwise : SweepDirection.Counterclockwise, true, false);
        g.Freeze();
        return g;
    }

    static Pen PenOf(Brush b, double w, bool round = false) { var p = new Pen(b, w); if (round) { p.StartLineCap = PenLineCap.Round; p.EndLineCap = PenLineCap.Round; } p.Freeze(); return p; }

    // ------------------------------------------------------------------------------------------ animation

    /// <summary>Moves the smoothed values towards the telemetry; returns true while anything is still moving.</summary>
    public bool Step(double dt)
    {
        double k = 1 - Math.Exp(-dt * 12);
        bool moving = false;
        void Ease(ref double cur, double target, double eps)
        {
            double d = target - cur;
            if (Math.Abs(d) < eps) { if (d != 0) { cur = target; moving = true; } return; }
            cur += d * k; moving = true;
        }
        Ease(ref dispSpeed, Math.Max(0, T.SpeedKmh), 0.03);
        Ease(ref dispRpm, Math.Max(0, T.RpmValue), 2);
        Ease(ref dispFuel, T.FuelFraction, 0.001);
        Ease(ref dispAdBlue, T.AdBlueFraction, 0.001);
        return moving;
    }

    // ------------------------------------------------------------------------------------------ units

    double Spd(double kmh) => Cfg.Mph ? kmh * 0.621371 : kmh;
    double Dist(double km) => Cfg.Mph ? km * 0.621371 : km;
    string SpdUnit => Cfg.Mph ? "mph" : "km/h";
    string DistUnit => Cfg.Mph ? "mi" : "km";

    static string Clock(DateTime t) => t == DateTime.MinValue ? "--:--" : t.ToString("HH:mm", CultureInfo.InvariantCulture);
    static string HoursMinutes(float min) { if (min < 0) return "--:--"; int m = (int)Math.Round(min); return (m / 60).ToString(CultureInfo.InvariantCulture) + ":" + (m % 60).ToString("00", CultureInfo.InvariantCulture); }

    // ------------------------------------------------------------------------------------------ drawing

    protected override void OnRender(DrawingContext dc)
    {
        double W = ActualWidth, H = ActualHeight;
        dc.DrawRectangle(BgBrush, null, new Rect(0, 0, W, H));
        if (W < 10 || H < 10) return;
        double s = Math.Min(W / DW, H / DH);
        dc.PushTransform(new TranslateTransform((W - DW * s) / 2, (H - DH * s) / 2));
        dc.PushTransform(new ScaleTransform(s, s));
        dc.PushClip(new RectangleGeometry(new Rect(0, 0, DW, DH)));
        Cluster(dc);
        dc.Pop(); dc.Pop(); dc.Pop();
    }

    void Cluster(DrawingContext dc)
    {
        var t = T;
        double max = Cfg.Mph ? 80 : 120;
        double v = Spd(dispSpeed);
        double limit = t.SpeedLimit > 0 ? Spd(t.SpeedLimit) : 0;
        bool over = limit > 0 && v > limit + (Cfg.Mph ? 2 : 3);
        double Ang(double val) => 200 - 220 * Math.Clamp(val / max, 0, 1.03);

        // background glow
        var glow = new RadialGradientBrush { Center = new Point(0.5, 0.62), GradientOrigin = new Point(0.5, 0.62), RadiusX = 0.62, RadiusY = 0.9 };
        glow.GradientStops.Add(new GradientStop(Color.FromRgb(12, 28, 38), 0)); glow.GradientStops.Add(new GradientStop(Color.FromRgb(3, 6, 10), 1));
        dc.DrawRectangle(glow, null, new Rect(0, 0, DW, DH));

        // amber glow beside the dial centre: bright while over the speed limit
        byte a = (byte)(over ? 235 : 38);
        foreach (int side in new[] { 1, -1 })
        {
            double X(double x) => side == 1 ? x : DW - x;
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(new Point(X(452), 292), true, true);
                c.LineTo(new Point(X(528), 330), true, false); c.LineTo(new Point(X(506), 410), true, false); c.LineTo(new Point(X(448), 362), true, false);
            }
            var br = new LinearGradientBrush(Color.FromArgb(a, 255, 150, 30), Color.FromArgb(0, 255, 150, 30), new Point(side == 1 ? 0 : 1, 0.4), new Point(side == 1 ? 1 : 0, 0.6));
            dc.DrawGeometry(br, null, g);
        }

        // dial ring
        dc.DrawGeometry(null, PenOf(Fz(Color.FromRgb(20, 34, 44)), 8), Arc(R + 16, 204, -24));

        // fuel (left) and AdBlue (right) gauges hugging the dial
        Gauge(dc, 163.5, 200, dispFuel, t.FuelWarn ? Red : Green, true);
        Gauge(dc, 16.5, -20, dispAdBlue, t.AdBlueWarn ? Red : Blue, false);

        // ticks and numbers
        int labelFrom = Cfg.Mph ? 40 : 60;
        for (int i = 0; i <= (int)max; i += 2)
        {
            bool major = i % 10 == 0;
            double ang = Ang(i);
            bool lit = i <= v;
            var b = lit ? White : Grey;
            dc.DrawLine(PenOf(b, major ? 3.6 : 2), P(ang, R - (major ? 38 : 20)), P(ang, R));
            if (major && i >= labelFrom) { var p = P(ang, R - 72); Draw(dc, i.ToString(CultureInfo.InvariantCulture), p.X, p.Y, 30, lit ? White : Dim); }
        }

        // cruise-control setting and speed limit markers
        if (t.CruiseOn && t.CruiseKmh > 1)
            dc.DrawLine(PenOf(Green, 6, true), P(Ang(Spd(t.CruiseKmh)), R - 46), P(Ang(Spd(t.CruiseKmh)), R + 10));
        if (limit > 0 && limit <= max)
        {
            double la = Ang(limit);
            var tip = P(la, R + 2); var l1 = P(la - 1.6, R + 24); var l2 = P(la + 1.6, R + 24);
            var tg = new StreamGeometry();
            using (var c = tg.Open()) { c.BeginFigure(tip, true, true); c.LineTo(l1, true, false); c.LineTo(l2, true, false); }
            dc.DrawGeometry(over ? Red : Amber, null, tg);
        }

        // needle
        double na = Ang(v);
        dc.DrawLine(PenOf(Fz(Color.FromArgb(70, 255, 59, 48)), 16, true), P(na, R - 150), P(na, R + 2));
        dc.DrawLine(PenOf(Red, 6, true), P(na, R - 150), P(na, R + 2));

        // speed
        double sp = Math.Max(0, v);
        string speedText = Math.Round(sp).ToString(CultureInfo.InvariantCulture);
        double wNum = Draw(dc, speedText, 640, 268, 118, over ? Amber : White, 1);
        Draw(dc, SpdUnit, 640 + wNum / 2 + 8, 308, 26, Dim, 0, true);

        // make
        if (Cfg.ShowMake && t.Make.Length > 0)
        {
            string name = string.Join(" ", t.Make.ToUpperInvariant().ToCharArray());
            Draw(dc, "‹", 640 - 170, 372, 30, Dim, 1, true); Draw(dc, name, 640, 372, 26, Fz(Color.FromRgb(176, 184, 192)), 1, true); Draw(dc, "›", 640 + 170, 372, 30, Dim, 1, true);
        }

        // gear
        Draw(dc, GearText(t), 640, 430, 46, t.Gear < 0 ? Amber : White, 1);

        Revs(dc);
        Panels(dc);
        Lamps(dc);

        if (StatusText.Length > 0)
        {
            dc.DrawRectangle(Fz(Color.FromArgb(150, 3, 6, 10)), null, new Rect(0, 0, DW, DH));
            Draw(dc, StatusText, 640, 330, 44, Dim, 1, true);
        }
    }

    static string GearText(Truck t)
    {
        string prefix = t.Shifter == "automatic" || t.Shifter == "arcade" ? "A " : "";
        if (t.Gear > 0) return prefix + t.Gear.ToString(CultureInfo.InvariantCulture);
        if (t.Gear < 0) return "R" + (t.Gear < -1 ? Math.Abs(t.Gear).ToString(CultureInfo.InvariantCulture) : "");
        return prefix + "N";
    }

    void Gauge(DrawingContext dc, double a0, double a1, double fraction, Brush fill, bool left)
    {
        double r = R + 56;
        dc.DrawGeometry(null, PenOf(Fz(Color.FromRgb(24, 36, 46)), 12, true), Arc(r, a0, a1));
        double fa = a1 + (a0 - a1) * Math.Clamp(fraction, 0, 1);   // fills from the bottom end (a1) upwards
        if (fraction > 0.004) dc.DrawGeometry(null, PenOf(fill, 12, true), Arc(r, a1, fa));
        // labels: the level and the name above the top end, 1/2 in the middle, E at the bottom end
        var top = P(a0, r + 40); var mid = P((a0 + a1) / 2, r + 36); var bot = P(a1, r + 34);
        Draw(dc, left ? "DIESEL" : "ADBLUE", top.X, top.Y - 40, 16, Dim, 1, true);
        Draw(dc, Math.Round(fraction * 100).ToString(CultureInfo.InvariantCulture) + "%", top.X, top.Y - 14, 28, fill, 1);
        Draw(dc, "1/2", mid.X, mid.Y, 18, Dim, 1, true);
        Draw(dc, "E", bot.X, bot.Y, 18, Dim, 1, true);
    }

    void Revs(DrawingContext dc)
    {
        double rmax = T.RpmMax > 0 ? Math.Max(2500, Math.Ceiling(T.RpmMax / 500) * 500) : 2500;
        double x0 = 440, x1 = 840, y = 514;
        double X(double rpm) => x0 + (x1 - x0) * Math.Clamp(rpm / rmax, 0, 1);
        dc.DrawLine(PenOf(Grey, 2), new Point(x0, y), new Point(x1, y));
        for (int rpm = 0; rpm <= rmax; rpm += 100)
        {
            bool major = rpm % 500 == 0 && rpm > 0;
            dc.DrawLine(PenOf(major ? White : Grey, major ? 2.6 : 1.6), new Point(X(rpm), y), new Point(X(rpm), y - (major ? 16 : 8)));
            if (major) Draw(dc, (rpm / 100).ToString(CultureInfo.InvariantCulture), X(rpm), y - 34, 24, Dim);
        }
        bool high = T.RpmMax > 0 && dispRpm > T.RpmMax * 0.92;
        double mx = X(dispRpm);
        dc.DrawRectangle(Fz(Color.FromArgb(70, 255, 59, 48)), null, new Rect(mx - 6, y - 24, 12, 38));
        dc.DrawRectangle(high ? Amber : Red, null, new Rect(mx - 2.5, y - 22, 5, 34));
        Draw(dc, "rpm x100", 640, y + 26, 16, Dim, 1, true);
    }

    void Panels(DrawingContext dc)
    {
        var t = T;
        // top corners: coolant and oil temperature
        Draw(dc, "COOLANT", 50, 40, 17, Dim, 0, true);
        Draw(dc, t.WaterC > 0 ? Math.Round(t.WaterC).ToString(CultureInfo.InvariantCulture) + "°C" : "--", 50, 72, 34, t.WaterWarn ? Red : White, 0);
        Draw(dc, "OIL", DW - 50, 40, 17, Dim, 2, true);
        Draw(dc, t.OilC > 0 ? Math.Round(t.OilC).ToString(CultureInfo.InvariantCulture) + "°C" : "--", DW - 50, 72, 34, t.OilWarn ? Red : White, 2);

        // left: consumption, trip consumption, range
        Row(dc, 50, 190, "AVERAGE CONSUMPTION", t.FuelAvgLPerKm > 0 ? (t.FuelAvgLPerKm * 100).ToString("0.0", CultureInfo.InvariantCulture) : "--", "l/100km", White, false);
        Row(dc, 50, 300, "THIS TRIP", TripLPer100 > 0 ? TripLPer100.ToString("0.0", CultureInfo.InvariantCulture) : "--", "l/100km", White, false);
        Row(dc, 50, 410, "RANGE", t.RangeKm > 0 ? Math.Round(Dist(t.RangeKm)).ToString(CultureInfo.InvariantCulture) : "--", DistUnit, t.FuelWarn ? Amber : White, false);

        // right: cruise, ETA, speed limit
        bool cc = t.CruiseOn && t.CruiseKmh > 1;
        Row(dc, DW - 50, 190, "CRUISE CONTROL", cc ? Math.Round(Spd(t.CruiseKmh)).ToString(CultureInfo.InvariantCulture) : "--", SpdUnit, cc ? Green : Dim, true);
        string eta = "--:--";
        if (t.EtaMin >= 0 && t.GameTime != DateTime.MinValue) eta = Clock(t.GameTime.AddMinutes(t.EtaMin));
        Row(dc, DW - 50, 300, "ARRIVAL (ETA)", eta, "", White, true);
        Row(dc, DW - 50, 410, "SPEED LIMIT", t.SpeedLimit > 0 ? Math.Round(Spd(t.SpeedLimit)).ToString(CultureInfo.InvariantCulture) : "--", SpdUnit, t.SpeedLimit > 0 && Spd(dispSpeed) > Spd(t.SpeedLimit) + 3 ? Amber : White, true);

        // bottom row: game clock and rest, trip and odometer
        dc.DrawLine(PenOf(Fz(Color.FromRgb(24, 36, 46)), 2), new Point(40, 596), new Point(DW - 40, 596));
        Draw(dc, "GAME TIME", 50, 622, 16, Dim, 0, true); Draw(dc, Clock(t.GameTime), 50, 656, 34, White, 0);
        Draw(dc, "REST IN", 250, 622, 16, Dim, 0, true); Draw(dc, HoursMinutes(t.RestMin), 250, 656, 34, t.RestMin >= 0 && t.RestMin < 30 ? Amber : White, 0);
        Draw(dc, "TRIP", DW - 330, 622, 16, Dim, 2, true); Draw(dc, Math.Round(Dist(TripKm)).ToString(CultureInfo.InvariantCulture) + " " + DistUnit, DW - 330, 656, 34, White, 2);
        Draw(dc, "ODOMETER", DW - 50, 622, 16, Dim, 2, true); Draw(dc, Math.Round(Dist(t.OdoKm)).ToString(CultureInfo.InvariantCulture) + " " + DistUnit, DW - 50, 656, 34, White, 2);
    }

    void Row(DrawingContext dc, double x, double y, string caption, string value, string unit, Brush b, bool right)
    {
        Draw(dc, caption, x, y, 16, Dim, right ? 2 : 0, true);
        double size = 46;
        var vf = Text(value, size, b); var uf = unit.Length > 0 ? Text(unit, 20, Dim, true) : null;
        double total = vf.Width + (uf != null ? uf.Width + 8 : 0);
        double sx = right ? x - total : x;
        dc.DrawText(vf, new Point(sx, y + 30 - vf.Height / 2));
        if (uf != null) dc.DrawText(uf, new Point(sx + vf.Width + 8, y + 40 - uf.Height / 2));
    }

    // ------------------------------------------------------------------------------------------ lamps

    void Lamps(DrawingContext dc)
    {
        var t = T;
        double y = 50, x = 640;
        // turn signals either side of the middle
        Arrow(dc, x - 96, y, true, t.BlinkerL ? Green : Fz(Color.FromRgb(26, 40, 32)));
        Arrow(dc, x + 96, y, false, t.BlinkerR ? Green : Fz(Color.FromRgb(26, 40, 32)));
        // lights
        Beam(dc, x - 48, y, false, t.LowBeam ? Green : Fz(Color.FromRgb(26, 40, 32)));
        Beam(dc, x + 48, y, true, t.HighBeam ? Blue : Fz(Color.FromRgb(24, 34, 46)));
        // status tags below
        var tags = new List<(string, Brush)>();
        if (t.ParkBrake) tags.Add(("P BRAKE", Red));
        if (t.MotorBrake) tags.Add(("ENGINE BRAKE", Amber));
        if (t.Retarder > 0) tags.Add(("RETARDER " + t.Retarder.ToString(CultureInfo.InvariantCulture), Amber));
        if (t.Wipers) tags.Add(("WIPERS", Dim));
        if (t.Beacon) tags.Add(("BEACON", Amber));
        if (t.AirWarn) tags.Add(("AIR", Red));
        if (t.OilWarn) tags.Add(("OIL", Red));
        if (t.WaterWarn) tags.Add(("COOLANT", Red));
        if (t.BatteryWarn) tags.Add(("BATTERY", Red));
        if (t.FuelWarn) tags.Add(("FUEL", Amber));
        if (t.AdBlueWarn) tags.Add(("ADBLUE", Amber));
        if (tags.Count == 0) return;
        double total = 0; var widths = new List<double>();
        foreach (var (s, _) in tags) { double w = Text(s, 18, Dim).Width + 26; widths.Add(w); total += w + 8; }
        double tx = 640 - (total - 8) / 2;
        for (int i = 0; i < tags.Count; i++)
        {
            var (s, b) = tags[i];
            var rect = new Rect(tx, 88, widths[i], 30);
            dc.DrawRoundedRectangle(Fz(Color.FromRgb(14, 22, 30)), PenOf(b, 1.6), rect, 6, 6);
            Draw(dc, s, rect.X + rect.Width / 2, rect.Y + 15, 18, b, 1);
            tx += widths[i] + 8;
        }
    }

    void Arrow(DrawingContext dc, double x, double y, bool left, Brush b)
    {
        double d = left ? -1 : 1;
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(x + d * 18, y), true, true);
            c.LineTo(new Point(x - d * 2, y - 16), true, false); c.LineTo(new Point(x - d * 2, y - 7), true, false);
            c.LineTo(new Point(x - d * 18, y - 7), true, false); c.LineTo(new Point(x - d * 18, y + 7), true, false);
            c.LineTo(new Point(x - d * 2, y + 7), true, false); c.LineTo(new Point(x - d * 2, y + 16), true, false);
        }
        g.Freeze();
        dc.DrawGeometry(b, null, g);
    }

    void Beam(DrawingContext dc, double x, double y, bool high, Brush b)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(x - 8, y - 14), false, false);
            c.ArcTo(new Point(x - 8, y + 14), new Size(14, 14), 0, false, SweepDirection.Counterclockwise, true, false);
        }
        g.Freeze();
        dc.DrawGeometry(null, PenOf(b, 3.4, true), g);
        for (int i = -2; i <= 2; i++)
        {
            double dy = high ? i * 6 : i * 5;
            var p1 = new Point(x + 2, y + dy); var p2 = new Point(x + 22, y + dy + (high ? 0 : 3 + Math.Abs(i) * 0));
            dc.DrawLine(PenOf(b, 3, true), p1, p2);
        }
    }
}
