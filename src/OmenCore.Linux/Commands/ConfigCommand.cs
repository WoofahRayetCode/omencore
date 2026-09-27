using System.CommandLine;
using OmenCore.Linux.Config;

namespace OmenCore.Linux.Commands;

/// <summary>
/// Configuration management command.
///
/// Examples:
///   omencore-cli config --show
///   omencore-cli config --set fan.profile=gaming
///   omencore-cli config --set keyboard.color=FF0000
///   omencore-cli config --reset
/// </summary>
public static class ConfigCommand
{
    public static Command Create()
    {
        var command = new Command("config", "Manage OmenCore TOML configuration");

        var showOption = new Option<bool>(
            aliases: new[] { "--show", "-s" },
            description: "Show current effective configuration");

        var setOption = new Option<string?>(
            aliases: new[] { "--set" },
            description: "Set a configuration value (key=value)");

        var getOption = new Option<string?>(
            aliases: new[] { "--get" },
            description: "Get a configuration value by key");

        var resetOption = new Option<bool>(
            aliases: new[] { "--reset" },
            description: "Reset user configuration file to defaults");

        var applyOption = new Option<bool>(
            aliases: new[] { "--apply", "-a" },
            description: "Show values that would be applied by daemon/performance components");

        command.AddOption(showOption);
        command.AddOption(setOption);
        command.AddOption(getOption);
        command.AddOption(resetOption);
        command.AddOption(applyOption);

        command.SetHandler(async (show, set, get, reset, apply) =>
        {
            await HandleConfigCommandAsync(show, set, get, reset, apply);
        }, showOption, setOption, getOption, resetOption, applyOption);

        return command;
    }

    private static async Task HandleConfigCommandAsync(
        bool show, string? set, string? get, bool reset, bool apply)
    {
        if (reset)
        {
            var defaults = new OmenCoreConfig();
            defaults.Save(OmenCoreConfig.DefaultConfigPath);
            WriteSuccess($"Reset user config to defaults at {OmenCoreConfig.DefaultConfigPath}");
            return;
        }

        if (!string.IsNullOrWhiteSpace(set))
        {
            var parts = set.Split('=', 2);
            if (parts.Length != 2)
            {
                WriteError("Invalid format. Use --set key=value");
                return;
            }

            var key = NormalizeKey(parts[0].Trim());
            var value = parts[1].Trim();
            var config = OmenCoreConfig.Load();

            if (!TrySetValue(config, key, value, out var error))
            {
                WriteError(error ?? "Unsupported key.");
                return;
            }

            config.Save(OmenCoreConfig.DefaultConfigPath);
            WriteSuccess($"Set {key} = {value}");
            return;
        }

        if (!string.IsNullOrWhiteSpace(get))
        {
            var key = NormalizeKey(get.Trim());
            var config = OmenCoreConfig.Load();
            var value = GetValue(config, key);
            if (value != null)
            {
                Console.WriteLine(value);
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("(not set or unknown key)");
                Console.ResetColor();
            }

            return;
        }

        if (apply)
        {
            ShowApplyPreview();
            return;
        }

        ShowConfig();
        await Task.CompletedTask;
    }

    private static void ShowConfig()
    {
        var config = OmenCoreConfig.Load();
        var report = OmenCoreConfig.LastLoadReport;

        Console.WriteLine();
        Console.WriteLine("OmenCore Linux - Configuration");
        Console.WriteLine("--------------------------------");
        Console.WriteLine($"Schema version : {config.SchemaVersion}");
        Console.WriteLine($"System config  : {OmenCoreConfig.SystemConfigPath}");
        Console.WriteLine($"User config    : {OmenCoreConfig.DefaultConfigPath}");
        Console.WriteLine();

        Console.WriteLine("[general]");
        Console.WriteLine($"poll_interval_ms = {config.General.PollIntervalMs}");
        Console.WriteLine($"log_level = \"{config.General.LogLevel}\"");

        Console.WriteLine();
        Console.WriteLine("[fan]");
        Console.WriteLine($"profile = \"{config.Fan.Profile}\"");
        Console.WriteLine($"boost = {config.Fan.Boost.ToString().ToLowerInvariant()}");
        Console.WriteLine($"smooth_transition = {config.Fan.SmoothTransition.ToString().ToLowerInvariant()}");

        Console.WriteLine();
        Console.WriteLine("[performance]");
        Console.WriteLine($"mode = \"{config.Performance.Mode}\"");
        Console.WriteLine($"hold_enabled = {config.Performance.HoldEnabled.ToString().ToLowerInvariant()}");
        Console.WriteLine($"hold_interval_seconds = {config.Performance.HoldIntervalSeconds}");
        Console.WriteLine($"thermal_power_limit = {(config.Performance.ThermalPowerLimit?.ToString() ?? "(unset)")}");

        Console.WriteLine();
        Console.WriteLine("[keyboard]");
        Console.WriteLine($"enabled = {config.Keyboard.Enabled.ToString().ToLowerInvariant()}");
        Console.WriteLine($"color = \"{config.Keyboard.Color}\"");
        Console.WriteLine($"brightness = {config.Keyboard.Brightness}");

        Console.WriteLine();
        Console.WriteLine("[startup]");
        Console.WriteLine($"apply_on_boot = {config.Startup.ApplyOnBoot.ToString().ToLowerInvariant()}");
        Console.WriteLine($"restore_on_exit = {config.Startup.RestoreOnExit.ToString().ToLowerInvariant()}");

        Console.WriteLine();
        Console.WriteLine("[thermal]");
        Console.WriteLine($"restore_performance_after_throttle = {config.Thermal.RestorePerformanceAfterThrottle.ToString().ToLowerInvariant()}");
        Console.WriteLine($"throttle_temp_c = {config.Thermal.ThrottleTempC}");
        Console.WriteLine($"restore_temp_c = {config.Thermal.RestoreTempC}");
        Console.WriteLine($"cpu_sensor = \"{config.Thermal.CpuSensor}\"");

        if (report.Migrations.Count > 0 || report.Warnings.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Load report");
            Console.WriteLine("-----------");
            foreach (var migration in report.Migrations)
            {
                Console.WriteLine($"migration: {migration.Path} v{migration.FromVersion} -> v{migration.ToVersion} ({migration.Note})");
            }

            foreach (var warning in report.Warnings.Take(8))
            {
                Console.WriteLine($"warning: {warning}");
            }
        }

        Console.WriteLine();
    }

    private static void ShowApplyPreview()
    {
        var config = OmenCoreConfig.Load();

        Console.WriteLine("Applying configuration preview...");
        Console.WriteLine($"Fan profile: {config.Fan.Profile}");
        Console.WriteLine($"Performance mode: {config.Performance.Mode}");
        Console.WriteLine($"Keyboard color: #{config.Keyboard.Color}");
        Console.WriteLine($"Keyboard brightness: {config.Keyboard.Brightness}");
        WriteSuccess("Configuration preview complete");
    }

    private static bool TrySetValue(OmenCoreConfig config, string key, string rawValue, out string? error)
    {
        error = null;

        switch (key)
        {
            case "general.poll_interval_ms":
                return TrySetInt(rawValue, 250, 60000, v => config.General.PollIntervalMs = v, out error);
            case "general.log_level":
                config.General.LogLevel = rawValue.Trim().ToLowerInvariant();
                return true;

            case "fan.profile":
                config.Fan.Profile = rawValue.Trim().ToLowerInvariant();
                return true;
            case "fan.boost":
                return TrySetBool(rawValue, v => config.Fan.Boost = v, out error);
            case "fan.smooth_transition":
                return TrySetBool(rawValue, v => config.Fan.SmoothTransition = v, out error);

            case "performance.mode":
                config.Performance.Mode = rawValue.Trim().ToLowerInvariant();
                return true;
            case "performance.hold_enabled":
                return TrySetBool(rawValue, v => config.Performance.HoldEnabled = v, out error);
            case "performance.hold_interval_seconds":
                return TrySetInt(rawValue, 10, 300, v => config.Performance.HoldIntervalSeconds = v, out error);
            case "performance.thermal_power_limit":
                if (string.Equals(rawValue, "unset", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(rawValue, "null", StringComparison.OrdinalIgnoreCase))
                {
                    config.Performance.ThermalPowerLimit = null;
                    return true;
                }
                return TrySetInt(rawValue, 0, 5, v => config.Performance.ThermalPowerLimit = v, out error);

            case "keyboard.enabled":
                return TrySetBool(rawValue, v => config.Keyboard.Enabled = v, out error);
            case "keyboard.color":
                var value = rawValue.Trim().TrimStart('#').ToUpperInvariant();
                if (value.Length != 6 || !value.All(Uri.IsHexDigit))
                {
                    error = "keyboard.color must be a 6-digit hex value (example: FF0000).";
                    return false;
                }
                config.Keyboard.Color = value;
                return true;
            case "keyboard.brightness":
                return TrySetInt(rawValue, 0, 100, v => config.Keyboard.Brightness = v, out error);

            case "startup.apply_on_boot":
                return TrySetBool(rawValue, v => config.Startup.ApplyOnBoot = v, out error);
            case "startup.restore_on_exit":
                return TrySetBool(rawValue, v => config.Startup.RestoreOnExit = v, out error);

            case "thermal.restore_performance_after_throttle":
                return TrySetBool(rawValue, v => config.Thermal.RestorePerformanceAfterThrottle = v, out error);
            case "thermal.throttle_temp_c":
                return TrySetInt(rawValue, 70, 110, v => config.Thermal.ThrottleTempC = v, out error);
            case "thermal.restore_temp_c":
                return TrySetInt(rawValue, 50, 100, v => config.Thermal.RestoreTempC = v, out error);
            case "thermal.cpu_sensor":
                config.Thermal.CpuSensor = rawValue.Trim().Trim('"');
                error = string.Empty;
                return true;

            default:
                error = "Unknown key. Run --show to see supported keys.";
                return false;
        }
    }

    private static string? GetValue(OmenCoreConfig config, string key)
    {
        return key switch
        {
            "schema_version" => config.SchemaVersion.ToString(),
            "general.poll_interval_ms" => config.General.PollIntervalMs.ToString(),
            "general.log_level" => config.General.LogLevel,
            "fan.profile" => config.Fan.Profile,
            "fan.boost" => config.Fan.Boost.ToString().ToLowerInvariant(),
            "fan.smooth_transition" => config.Fan.SmoothTransition.ToString().ToLowerInvariant(),
            "performance.mode" => config.Performance.Mode,
            "performance.hold_enabled" => config.Performance.HoldEnabled.ToString().ToLowerInvariant(),
            "performance.hold_interval_seconds" => config.Performance.HoldIntervalSeconds.ToString(),
            "performance.thermal_power_limit" => config.Performance.ThermalPowerLimit?.ToString(),
            "keyboard.enabled" => config.Keyboard.Enabled.ToString().ToLowerInvariant(),
            "keyboard.color" => config.Keyboard.Color,
            "keyboard.brightness" => config.Keyboard.Brightness.ToString(),
            "startup.apply_on_boot" => config.Startup.ApplyOnBoot.ToString().ToLowerInvariant(),
            "startup.restore_on_exit" => config.Startup.RestoreOnExit.ToString().ToLowerInvariant(),
            "thermal.restore_performance_after_throttle" => config.Thermal.RestorePerformanceAfterThrottle.ToString().ToLowerInvariant(),
            "thermal.throttle_temp_c" => config.Thermal.ThrottleTempC.ToString(),
            "thermal.restore_temp_c" => config.Thermal.RestoreTempC.ToString(),
            "thermal.cpu_sensor" => config.Thermal.CpuSensor,
            _ => null
        };
    }

    private static string NormalizeKey(string key)
    {
        var normalized = key.Trim().ToLowerInvariant();
        return normalized switch
        {
            "perf.mode" => "performance.mode",
            "startup.apply" => "startup.apply_on_boot",
            "keyboard.brightness_percent" => "keyboard.brightness",
            _ => normalized
        };
    }

    private static bool TrySetBool(string raw, Action<bool> setter, out string? error)
    {
        if (!bool.TryParse(raw, out var value))
        {
            error = "Expected true or false.";
            return false;
        }

        setter(value);
        error = null;
        return true;
    }

    private static bool TrySetInt(string raw, int min, int max, Action<int> setter, out string? error)
    {
        if (!int.TryParse(raw, out var value))
        {
            error = "Expected an integer value.";
            return false;
        }

        if (value < min || value > max)
        {
            error = $"Value must be in range {min}-{max}.";
            return false;
        }

        setter(value);
        error = null;
        return true;
    }

    private static void WriteSuccess(string message)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"OK: {message}");
        Console.ResetColor();
    }

    private static void WriteError(string message)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"Error: {message}");
        Console.ResetColor();
    }
}
