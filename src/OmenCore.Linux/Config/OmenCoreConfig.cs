using Tomlyn;
using Tomlyn.Model;

namespace OmenCore.Linux.Config;

/// <summary>
/// TOML configuration model for OmenCore Linux daemon.
/// </summary>
public class OmenCoreConfig
{
    public const int CurrentSchemaVersion = 2;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public GeneralConfig General { get; set; } = new();
    public FanConfig Fan { get; set; } = new();
    public BatteryConfig Battery { get; set; } = new();
    public PerformanceConfig Performance { get; set; } = new();
    public ThermalConfig Thermal { get; set; } = new();
    public KeyboardConfig Keyboard { get; set; } = new();
    public StartupConfig Startup { get; set; } = new();

    private static readonly string DefaultConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".config", "omencore");

    public static string DefaultConfigPath => Path.Combine(DefaultConfigDir, "config.toml");
    public static string SystemConfigPath => "/etc/omencore/config.toml";

    // Last load report is intentionally static so diagnostics can report parse/migration details.
    public static OmenCoreConfigLoadReport LastLoadReport { get; private set; } = new();

    /// <summary>
    /// Load configuration from TOML file.
    /// Looks in order: /etc/omencore/config.toml, ~/.config/omencore/config.toml
    /// and applies user-file values over system-file values.
    /// </summary>
    public static OmenCoreConfig Load(string? customPath = null)
    {
        var paths = new List<string>();

        if (!string.IsNullOrWhiteSpace(customPath))
        {
            paths.Add(customPath);
        }
        else
        {
            // System config first, then user config (user overrides system).
            paths.Add(SystemConfigPath);
            paths.Add(DefaultConfigPath);
        }

        var config = new OmenCoreConfig();
        var report = new OmenCoreConfigLoadReport();

        foreach (var path in paths)
        {
            if (!File.Exists(path))
            {
                continue;
            }

            report.LoadedPaths.Add(path);

            try
            {
                var toml = File.ReadAllText(path);
                var model = Toml.ToModel(toml) as TomlTable;
                if (model == null)
                {
                    report.Warnings.Add($"{path}: parsed model was not a TOML table; skipping.");
                    continue;
                }

                var fileSchemaVersion = GetInt(model, "schema_version") ?? 1;
                report.DetectedSchemaVersions[path] = fileSchemaVersion;

                ApplyLegacyAliases(model, report, path);
                ApplySchemaMigrations(model, fileSchemaVersion, report, path);
                ApplyTableToConfig(config, model, report, path);
            }
            catch (Exception ex)
            {
                report.Warnings.Add($"{path}: parse/load failed: {ex.Message}");
            }
        }

        config.SchemaVersion = CurrentSchemaVersion;
        Sanitize(config, report);
        LastLoadReport = report;

        return config;
    }

    /// <summary>
    /// Save configuration to TOML file.
    /// </summary>
    public void Save(string? customPath = null)
    {
        var path = customPath ?? DefaultConfigPath;
        var dir = Path.GetDirectoryName(path);

        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        SchemaVersion = CurrentSchemaVersion;
        var toml = Toml.FromModel(this);
        File.WriteAllText(path, toml);
    }

    /// <summary>
    /// Generate a default configuration file with comments.
    /// </summary>
    public static string GenerateDefaultToml()
    {
        return """
            # OmenCore Linux Configuration
            # Place this file at ~/.config/omencore/config.toml or /etc/omencore/config.toml

            # Schema version for migration compatibility.
            schema_version = 2

            [general]
            # Polling interval in milliseconds for daemon mode
            poll_interval_ms = 2000
            # Log level: debug, info, warn, error
            log_level = "info"

            [fan]
            # Fan profile: auto, silent, balanced, gaming, max, custom
            profile = "auto"
            # Enable fan boost mode (more aggressive cooling)
            boost = false
            # Smooth fan speed transitions to reduce noise spikes
            smooth_transition = true

            # Custom fan curve (only used when profile = "custom")
            [fan.curve]
            enabled = false
            # Hysteresis in degrees - prevents fan speed oscillation
            hysteresis = 3
            # Fan curve points: temperature (degrees C) -> fan speed (%)
            [[fan.curve.points]]
            temp = 40
            speed = 20

            [[fan.curve.points]]
            temp = 50
            speed = 30

            [[fan.curve.points]]
            temp = 60
            speed = 50

            [[fan.curve.points]]
            temp = 70
            speed = 70

            [[fan.curve.points]]
            temp = 80
            speed = 85

            [[fan.curve.points]]
            temp = 90
            speed = 100

            [battery]
            # Preferred runtime profile when on battery power.
            profile = "balanced"

            [performance]
            # Performance mode: default, balanced, performance, cool
            mode = "balanced"
            # Keep the selected performance mode applied from the daemon.
            # Useful on systems where firmware/kernel resets the profile after ~30s.
            hold_enabled = false
            # How often the daemon verifies/reapplies held performance state.
            hold_interval_seconds = 30
            # Optional thermal power limit multiplier (0-5) to reapply while hold is enabled.
            # Leave unset to avoid periodic power-limit writes.
            # thermal_power_limit = 5

            [thermal]
            # Re-apply configured performance mode after CPU thermal cooldown.
            restore_performance_after_throttle = false
            # CPU temperature (degrees C) above which throttling is considered active.
            throttle_temp_c = 95
            # CPU temperature (degrees C) below which the system is considered cooled-down.
            restore_temp_c = 80
            # Optional: force the CPU temperature source by hwmon driver name, e.g. "k10temp".
            # Leave empty for automatic selection (dedicated CPU drivers first, ACPI zones last).
            # cpu_sensor = "k10temp"

            [keyboard]
            # Enable keyboard lighting control
            enabled = true
            # RGB color in hex (without #)
            color = "FF0000"
            # Brightness 0-100
            brightness = 100

            [startup]
            # Apply saved configuration when daemon starts
            apply_on_boot = true
            # Restore previous fan/performance settings when daemon exits
            restore_on_exit = true
            """;
    }

    private static void ApplySchemaMigrations(
        TomlTable table,
        int fileSchemaVersion,
        OmenCoreConfigLoadReport report,
        string path)
    {
        if (fileSchemaVersion >= CurrentSchemaVersion)
        {
            return;
        }

        if (fileSchemaVersion <= 1)
        {
            // v1 -> v2: ensure nested [performance] table exists and migrate perf.mode alias.
            MoveTopLevelKeyToSection(table, "perf.mode", "performance", "mode", report, path);
            MoveTopLevelKeyToSection(table, "startup.apply", "startup", "apply_on_boot", report, path);
            MoveTopLevelKeyToSection(table, "keyboard.brightness_percent", "keyboard", "brightness", report, path);

            report.Migrations.Add(new OmenCoreConfigMigrationEntry
            {
                Path = path,
                FromVersion = fileSchemaVersion,
                ToVersion = 2,
                Note = "Applied compatibility aliases for perf/startup/keyboard keys."
            });
        }

        table["schema_version"] = CurrentSchemaVersion;
    }

    private static void ApplyLegacyAliases(TomlTable table, OmenCoreConfigLoadReport report, string path)
    {
        MoveTopLevelKeyToSection(table, "fan.profile", "fan", "profile", report, path);
        MoveTopLevelKeyToSection(table, "fan.boost", "fan", "boost", report, path);
        MoveTopLevelKeyToSection(table, "fan.smooth_transition", "fan", "smooth_transition", report, path);
        MoveTopLevelKeyToSection(table, "battery.profile", "battery", "profile", report, path);
        MoveTopLevelKeyToSection(table, "perf.mode", "performance", "mode", report, path);
        MoveTopLevelKeyToSection(table, "performance.power_limit", "performance", "thermal_power_limit", report, path);
        MoveTopLevelKeyToSection(table, "keyboard.color", "keyboard", "color", report, path);
        MoveTopLevelKeyToSection(table, "keyboard.brightness", "keyboard", "brightness", report, path);
        MoveTopLevelKeyToSection(table, "startup.apply", "startup", "apply_on_boot", report, path);
        MoveTopLevelKeyToSection(table, "general.polling_interval_ms", "general", "poll_interval_ms", report, path);
    }

    private static void MoveTopLevelKeyToSection(
        TomlTable root,
        string legacyKey,
        string sectionName,
        string fieldName,
        OmenCoreConfigLoadReport report,
        string path)
    {
        if (!root.TryGetValue(legacyKey, out var value))
        {
            return;
        }

        var section = GetOrCreateTable(root, sectionName);
        if (!section.ContainsKey(fieldName))
        {
            section[fieldName] = value;
        }

        root.Remove(legacyKey);
        report.Warnings.Add($"{path}: mapped legacy key '{legacyKey}' -> [{sectionName}].{fieldName}");
    }

    private static TomlTable GetOrCreateTable(TomlTable root, string key)
    {
        if (root.TryGetValue(key, out var existing) && existing is TomlTable existingTable)
        {
            return existingTable;
        }

        var table = new TomlTable();
        root[key] = table;
        return table;
    }

    private static void ApplyTableToConfig(
        OmenCoreConfig config,
        TomlTable root,
        OmenCoreConfigLoadReport report,
        string path)
    {
        config.SchemaVersion = GetInt(root, "schema_version") ?? config.SchemaVersion;

        if (TryGetTable(root, "general", out var general))
        {
            if (GetInt(general, "poll_interval_ms") is { } poll)
                config.General.PollIntervalMs = poll;
            if (GetString(general, "log_level") is { } log)
                config.General.LogLevel = log;

            if (TryGetTable(general, "low_overhead", out var lowOverhead))
            {
                if (GetBool(lowOverhead, "enable_on_battery") is { } enableOnBattery)
                    config.General.LowOverhead.EnableOnBattery = enableOnBattery;
                if (GetInt(lowOverhead, "poll_interval_ms") is { } lowPoll)
                    config.General.LowOverhead.PollIntervalMs = lowPoll;
                if (GetBool(lowOverhead, "disable_sensor_scanning") is { } disableScan)
                    config.General.LowOverhead.DisableSensorScanning = disableScan;
                if (GetBool(lowOverhead, "reduce_logging") is { } reduceLogging)
                    config.General.LowOverhead.ReduceLogging = reduceLogging;
            }
        }

        if (TryGetTable(root, "fan", out var fan))
        {
            if (GetString(fan, "profile") is { } profile)
                config.Fan.Profile = profile;
            if (GetBool(fan, "boost") is { } boost)
                config.Fan.Boost = boost;
            if (GetBool(fan, "smooth_transition") is { } smooth)
                config.Fan.SmoothTransition = smooth;

            if (TryGetTable(fan, "curve", out var curve))
            {
                if (GetBool(curve, "enabled") is { } enabled)
                    config.Fan.Curve.Enabled = enabled;
                if (GetInt(curve, "hysteresis") is { } hysteresis)
                    config.Fan.Curve.Hysteresis = hysteresis;
                if (curve.TryGetValue("points", out var pointsObj) && pointsObj is TomlTableArray pointsArray)
                {
                    var points = new List<FanCurvePoint>();
                    foreach (var item in pointsArray)
                    {
                        if (item is not TomlTable pointTable)
                        {
                            continue;
                        }

                        var temp = GetInt(pointTable, "temp");
                        var speed = GetInt(pointTable, "speed");
                        if (temp.HasValue && speed.HasValue)
                        {
                            points.Add(new FanCurvePoint { Temp = temp.Value, Speed = speed.Value });
                        }
                    }

                    if (points.Count > 0)
                    {
                        config.Fan.Curve.Points = points;
                    }
                    else
                    {
                        report.Warnings.Add($"{path}: [fan.curve].points was present but no valid temp/speed pairs were found.");
                    }
                }
            }
        }

        if (TryGetTable(root, "battery", out var battery))
        {
            if (GetString(battery, "profile") is { } profile)
                config.Battery.Profile = profile;
        }

        if (TryGetTable(root, "performance", out var performance))
        {
            if (GetString(performance, "mode") is { } mode)
                config.Performance.Mode = mode;
            if (GetBool(performance, "hold_enabled") is { } holdEnabled)
                config.Performance.HoldEnabled = holdEnabled;
            if (GetInt(performance, "hold_interval_seconds") is { } holdInterval)
                config.Performance.HoldIntervalSeconds = holdInterval;
            if (performance.TryGetValue("thermal_power_limit", out var tplObj))
                config.Performance.ThermalPowerLimit = TryCoerceInt(tplObj);
        }

        if (TryGetTable(root, "thermal", out var thermal))
        {
            if (GetBool(thermal, "restore_performance_after_throttle") is { } restore)
                config.Thermal.RestorePerformanceAfterThrottle = restore;
            if (GetInt(thermal, "throttle_temp_c") is { } throttle)
                config.Thermal.ThrottleTempC = throttle;
            if (GetInt(thermal, "restore_temp_c") is { } restoreTemp)
                config.Thermal.RestoreTempC = restoreTemp;
            if (GetString(thermal, "cpu_sensor") is { } cpuSensor)
                config.Thermal.CpuSensor = cpuSensor.Trim();
        }

        if (TryGetTable(root, "keyboard", out var keyboard))
        {
            if (GetBool(keyboard, "enabled") is { } kEnabled)
                config.Keyboard.Enabled = kEnabled;
            if (GetString(keyboard, "color") is { } color)
                config.Keyboard.Color = color;
            if (GetInt(keyboard, "brightness") is { } brightness)
                config.Keyboard.Brightness = brightness;
        }

        if (TryGetTable(root, "startup", out var startup))
        {
            if (GetBool(startup, "apply_on_boot") is { } apply)
                config.Startup.ApplyOnBoot = apply;
            if (GetBool(startup, "restore_on_exit") is { } restoreExit)
                config.Startup.RestoreOnExit = restoreExit;
        }
    }

    private static void Sanitize(OmenCoreConfig config, OmenCoreConfigLoadReport report)
    {
        config.General.PollIntervalMs = Math.Clamp(config.General.PollIntervalMs, 250, 60000);
        config.General.LogLevel = NormalizeLogLevel(config.General.LogLevel);
        config.General.LowOverhead.PollIntervalMs = Math.Clamp(config.General.LowOverhead.PollIntervalMs, 500, 120000);

        config.Fan.Profile = NormalizeFanProfile(config.Fan.Profile);
        config.Battery.Profile = NormalizePerformanceMode(config.Battery.Profile);
        config.Fan.Curve.Hysteresis = Math.Clamp(config.Fan.Curve.Hysteresis, 0, 20);
        foreach (var point in config.Fan.Curve.Points)
        {
            point.Temp = Math.Clamp(point.Temp, 20, 110);
            point.Speed = Math.Clamp(point.Speed, 0, 100);
        }

        config.Performance.Mode = NormalizePerformanceMode(config.Performance.Mode);
        config.Performance.HoldIntervalSeconds = Math.Clamp(config.Performance.HoldIntervalSeconds, 10, 300);
        if (config.Performance.ThermalPowerLimit.HasValue)
        {
            config.Performance.ThermalPowerLimit = Math.Clamp(config.Performance.ThermalPowerLimit.Value, 0, 5);
        }

        config.Keyboard.Brightness = Math.Clamp(config.Keyboard.Brightness, 0, 100);
        config.Keyboard.Color = NormalizeHexColor(config.Keyboard.Color);

        config.Thermal.ThrottleTempC = Math.Clamp(config.Thermal.ThrottleTempC, 70, 110);
        config.Thermal.RestoreTempC = Math.Clamp(config.Thermal.RestoreTempC, 50, 100);
        if (config.Thermal.RestoreTempC >= config.Thermal.ThrottleTempC)
        {
            config.Thermal.RestoreTempC = Math.Max(50, config.Thermal.ThrottleTempC - 10);
            report.Warnings.Add("thermal.restore_temp_c was not lower than thermal.throttle_temp_c; adjusted automatically.");
        }
    }

    private static bool TryGetTable(TomlTable root, string key, out TomlTable table)
    {
        if (root.TryGetValue(key, out var value) && value is TomlTable asTable)
        {
            table = asTable;
            return true;
        }

        table = null!;
        return false;
    }

    private static int? GetInt(TomlTable table, string key)
    {
        if (!table.TryGetValue(key, out var value))
        {
            return null;
        }

        return TryCoerceInt(value);
    }

    private static int? TryCoerceInt(object? value)
    {
        switch (value)
        {
            case null:
                return null;
            case int i:
                return i;
            case long l when l <= int.MaxValue && l >= int.MinValue:
                return (int)l;
            case short s:
                return s;
            case byte b:
                return b;
            case string str when int.TryParse(str, out var parsed):
                return parsed;
            default:
                return null;
        }
    }

    private static bool? GetBool(TomlTable table, string key)
    {
        if (!table.TryGetValue(key, out var value))
        {
            return null;
        }

        return value switch
        {
            bool b => b,
            string s when bool.TryParse(s, out var parsed) => parsed,
            _ => null
        };
    }

    private static string? GetString(TomlTable table, string key)
    {
        if (!table.TryGetValue(key, out var value))
        {
            return null;
        }

        return value switch
        {
            string s => s,
            _ => value?.ToString()
        };
    }

    private static string NormalizeFanProfile(string input)
    {
        var value = input.Trim().ToLowerInvariant();
        return value switch
        {
            "auto" or "silent" or "balanced" or "gaming" or "max" or "custom" => value,
            _ => "auto"
        };
    }

    private static string NormalizePerformanceMode(string input)
    {
        var value = input.Trim().ToLowerInvariant();
        return value switch
        {
            "default" or "balanced" or "performance" or "cool" => value,
            _ => "balanced"
        };
    }

    private static string NormalizeLogLevel(string input)
    {
        var value = input.Trim().ToLowerInvariant();
        return value switch
        {
            "trace" or "debug" or "info" or "warn" or "warning" or "error" or "fatal" => value,
            _ => "info"
        };
    }

    private static string NormalizeHexColor(string input)
    {
        var value = input.Trim().TrimStart('#').ToUpperInvariant();
        if (value.Length == 6 && value.All(Uri.IsHexDigit))
        {
            return value;
        }

        return "FF0000";
    }
}

public class OmenCoreConfigLoadReport
{
    public List<string> LoadedPaths { get; } = new();
    public Dictionary<string, int> DetectedSchemaVersions { get; } = new();
    public List<OmenCoreConfigMigrationEntry> Migrations { get; } = new();
    public List<string> Warnings { get; } = new();
}

public class OmenCoreConfigMigrationEntry
{
    public string Path { get; set; } = string.Empty;
    public int FromVersion { get; set; }
    public int ToVersion { get; set; }
    public string Note { get; set; } = string.Empty;
}

public class GeneralConfig
{
    public int PollIntervalMs { get; set; } = 2000;
    public string LogLevel { get; set; } = "info";

    /// <summary>
    /// Low-overhead mode settings for battery and idle operation.
    /// </summary>
    public LowOverheadConfig LowOverhead { get; set; } = new();
}

/// <summary>
/// Low-overhead monitoring mode for reduced power consumption (#22).
/// </summary>
public class LowOverheadConfig
{
    /// <summary>
    /// Enable automatic low-overhead mode when on battery.
    /// </summary>
    public bool EnableOnBattery { get; set; } = true;

    /// <summary>
    /// Poll interval in low-overhead mode (ms) - longer = less CPU usage.
    /// </summary>
    public int PollIntervalMs { get; set; } = 5000;

    /// <summary>
    /// Disable hwmon scanning in low-overhead mode (use cached paths only).
    /// </summary>
    public bool DisableSensorScanning { get; set; } = true;

    /// <summary>
    /// Reduce logging verbosity in low-overhead mode.
    /// </summary>
    public bool ReduceLogging { get; set; } = true;
}

public class FanConfig
{
    public string Profile { get; set; } = "auto";
    public bool Boost { get; set; } = false;
    public bool SmoothTransition { get; set; } = true;
    public FanCurveConfig Curve { get; set; } = new();
}

public class FanCurveConfig
{
    public bool Enabled { get; set; } = false;
    public int Hysteresis { get; set; } = 3;
    public List<FanCurvePoint> Points { get; set; } = new()
    {
        new() { Temp = 40, Speed = 20 },
        new() { Temp = 50, Speed = 30 },
        new() { Temp = 60, Speed = 50 },
        new() { Temp = 70, Speed = 70 },
        new() { Temp = 80, Speed = 85 },
        new() { Temp = 90, Speed = 100 }
    };
}

public class FanCurvePoint
{
    public int Temp { get; set; }
    public int Speed { get; set; }
}

public class PerformanceConfig
{
    public string Mode { get; set; } = "balanced";
    public bool HoldEnabled { get; set; } = false;
    public int HoldIntervalSeconds { get; set; } = 30;
    public int? ThermalPowerLimit { get; set; }
}

public class BatteryConfig
{
    public string Profile { get; set; } = "balanced";
}

public class KeyboardConfig
{
    public bool Enabled { get; set; } = true;
    public string Color { get; set; } = "FF0000";
    public int Brightness { get; set; } = 100;
}

public class StartupConfig
{
    public bool ApplyOnBoot { get; set; } = true;
    public bool RestoreOnExit { get; set; } = true;
}

public class ThermalConfig
{
    /// <summary>
    /// Re-apply configured performance mode after the CPU cools down from a thermal throttle event.
    /// </summary>
    public bool RestorePerformanceAfterThrottle { get; set; } = false;

    /// <summary>CPU C above which the system is considered thermally throttling. Default 95.</summary>
    public int ThrottleTempC { get; set; } = 95;

    /// <summary>
    /// CPU C below which the system is considered cooled-down and the performance mode is re-applied.
    /// </summary>
    public int RestoreTempC { get; set; } = 80;

    /// <summary>
    /// Optional hwmon driver name to use for CPU temperature (e.g. "k10temp", "coretemp",
    /// "zenpower"). Matched by driver name, not hwmon index, since indexes reshuffle between boots.
    /// Empty = automatic ranking. If the named driver has no readable sensor, automatic ranking is used.
    /// </summary>
    public string CpuSensor { get; set; } = string.Empty;
}
