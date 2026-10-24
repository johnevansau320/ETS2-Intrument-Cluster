using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace Ets2Cluster;

/// <summary>Everything the cluster shows, read from the TruckSim GPS Telemetry Server.</summary>
public sealed class Truck
{
    public bool Connected, Paused;
    public string Make = "";
    public float SpeedKmh, RpmValue, RpmMax;
    public int SpeedLimit;
    public bool CruiseOn; public float CruiseKmh;
    public int Gear, ForwardGears; public string Shifter = "";
    public float FuelL, FuelCapL, FuelAvgLPerKm, RangeKm, AdBlueL, AdBlueCapL, OdoKm;
    public float OilC, WaterC, BrakeC, AirPsi, BatteryV;
    public bool ParkBrake, MotorBrake, EngineOn, Wipers, LowBeam, HighBeam, Beacon, BlinkerL, BlinkerR;
    public int Retarder, RetarderSteps;
    public bool AirWarn, OilWarn, WaterWarn, BatteryWarn, FuelWarn, AdBlueWarn;
    public DateTime GameTime = DateTime.MinValue;
    public float EtaMin = -1f, RestMin = -1f;   // game minutes until arrival / until a rest is needed (-1 unknown)

    /// <summary>Changes only when something visible changes, so the cluster is redrawn only when needed.</summary>
    public string Signature() => string.Join("|", Connected, Paused, Make, SpeedKmh.ToString("0.0"), SpeedLimit, CruiseOn, CruiseKmh.ToString("0"), (int)(RpmValue / 5), RpmMax.ToString("0"), Gear, Shifter,
        FuelL.ToString("0.0"), AdBlueL.ToString("0.0"), FuelAvgLPerKm.ToString("0.000"), RangeKm.ToString("0"), OdoKm.ToString("0.0"), WaterC.ToString("0"), OilC.ToString("0"),
        ParkBrake, MotorBrake, Retarder, Wipers, LowBeam, HighBeam, Beacon, BlinkerL, BlinkerR, AirWarn, OilWarn, WaterWarn, BatteryWarn, FuelWarn, AdBlueWarn,
        GameTime.ToString("yyyyMMddHHmm"), EtaMin.ToString("0"), RestMin.ToString("0"));

    public float FuelFraction => FuelCapL > 0 ? Math.Clamp(FuelL / FuelCapL, 0, 1) : 0;
    public float AdBlueFraction => AdBlueCapL > 0 ? Math.Clamp(AdBlueL / AdBlueCapL, 0, 1) : 0;
}

public static class TelemetryParser
{
    static JsonElement? Obj(JsonElement? p, string n) => p is { ValueKind: JsonValueKind.Object } e && e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Object ? v : null;
    static double Num(JsonElement? p, string n) => p is { ValueKind: JsonValueKind.Object } e && e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;
    static bool Bool(JsonElement? p, string n) => p is { ValueKind: JsonValueKind.Object } e && e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.True;
    static string Str(JsonElement? p, string n) => p is { ValueKind: JsonValueKind.Object } e && e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    static DateTime Time(string s) => DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var t) ? t : DateTime.MinValue;
    static float Minutes(string s) { var t = Time(s); return t == DateTime.MinValue ? -1f : (float)(t - DateTime.MinValue).TotalMinutes; }

    public static Truck Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        JsonElement? root = doc.RootElement;
        var game = Obj(root, "game"); var t = Obj(root, "truck"); var nav = Obj(root, "navigation");
        var tr = new Truck
        {
            Connected = Bool(game, "connected"), Paused = Bool(game, "paused"),
            Make = Str(t, "make"),
            SpeedKmh = (float)Num(t, "speed"), RpmValue = (float)Num(t, "engineRpm"), RpmMax = (float)Num(t, "engineRpmMax"),
            SpeedLimit = (int)Math.Round(Num(nav, "speedLimit")),
            CruiseOn = Bool(t, "cruiseControlOn"), CruiseKmh = (float)Num(t, "cruiseControlSpeed"),
            Gear = (int)Num(t, "displayedGear"), ForwardGears = (int)Num(t, "forwardGears"), Shifter = Str(t, "shifterType"),
            FuelL = (float)Num(t, "fuel"), FuelCapL = (float)Num(t, "fuelCapacity"), FuelAvgLPerKm = (float)Num(t, "fuelAverageConsumption"), RangeKm = (float)Num(t, "fuelRange"),
            AdBlueL = (float)Num(t, "adblue"), AdBlueCapL = (float)Num(t, "adblueCapacity"), OdoKm = (float)Num(t, "odometer"),
            OilC = (float)Num(t, "oilTemperature"), WaterC = (float)Num(t, "waterTemperature"), BrakeC = (float)Num(t, "brakeTemperature"),
            AirPsi = (float)Num(t, "airPressure"), BatteryV = (float)Num(t, "batteryVoltage"),
            ParkBrake = Bool(t, "parkBrakeOn"), MotorBrake = Bool(t, "motorBrakeOn"), EngineOn = Bool(t, "engineOn"), Wipers = Bool(t, "wipersOn"),
            LowBeam = Bool(t, "lightsBeamLowOn"), HighBeam = Bool(t, "lightsBeamHighOn"), Beacon = Bool(t, "lightsBeaconOn"),
            BlinkerL = Bool(t, "blinkerLeftOn"), BlinkerR = Bool(t, "blinkerRightOn"),
            Retarder = (int)Num(t, "retarderBrake"), RetarderSteps = (int)Num(t, "retarderStepCount"),
            AirWarn = Bool(t, "airPressureWarningOn") || Bool(t, "airPressureEmergencyOn"), OilWarn = Bool(t, "oilPressureWarningOn"),
            WaterWarn = Bool(t, "waterTemperatureWarningOn"), BatteryWarn = Bool(t, "batteryVoltageWarningOn"),
            FuelWarn = Bool(t, "fuelWarningOn"), AdBlueWarn = Bool(t, "adblueWarningOn"),
            GameTime = Time(Str(game, "time")),
            EtaMin = Minutes(Str(nav, "estimatedTime")), RestMin = Minutes(Str(game, "nextRestStopTime")),
        };
        // the telemetry server never sends the fuel / AdBlue warning lamps: light them from the levels, as the game does (at 15 %)
        if (tr.Connected && tr.FuelCapL > 0 && tr.FuelFraction <= 0.15f) tr.FuelWarn = true;
        if (tr.Connected && tr.AdBlueCapL > 0 && tr.AdBlueFraction <= 0.15f) tr.AdBlueWarn = true;
        return tr;
    }
}

/// <summary>Polls the telemetry server a few times a second and raises <see cref="Updated"/> from a background thread.</summary>
public sealed class TelemetryClient : IDisposable
{
    // no proxy lookup: it can stall a request to the local server for seconds
    readonly HttpClient http = new(new SocketsHttpHandler { UseProxy = false, ConnectTimeout = TimeSpan.FromSeconds(2) }) { Timeout = TimeSpan.FromSeconds(3) };
    readonly CancellationTokenSource stop = new();
    public Func<string> Host = () => "127.0.0.1:31377";
    public event Action<Truck>? Updated;
    public event Action<string>? Status;

    public void Start() => _ = Task.Run(Loop);

    async Task Loop()
    {
        while (!stop.IsCancellationRequested)
        {
            int delay = 140;
            try
            {
                string host = Host().Trim();
                if (host.Length == 0) host = "127.0.0.1:31377";
                if (!host.Contains(':')) host += ":31377";
                if (!host.StartsWith("http", StringComparison.OrdinalIgnoreCase)) host = "http://" + host;
                string json = await http.GetStringAsync(host.TrimEnd('/') + "/api/ets2/telemetry", stop.Token);
                var tr = TelemetryParser.Parse(json);
                Status?.Invoke(tr.Connected ? "" : "Waiting for the game");
                Updated?.Invoke(tr);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { return; }
            catch (Exception)
            {
                Status?.Invoke("Telemetry server not reachable");
                Updated?.Invoke(new Truck());
                delay = 1000;
            }
            try { await Task.Delay(delay, stop.Token); } catch { return; }
        }
    }

    public void Dispose() { stop.Cancel(); http.Dispose(); }
}
