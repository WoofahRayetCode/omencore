using System;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using OmenCore.Hardware;

namespace OmenCore.Services.KeyboardLighting
{
    /// <summary>
    /// Keyboard backend for the Primax per-key keyboards on 2021-2024 OMEN 16/17 per-key boards
    /// (see <see cref="PrimaxKeyboardMcu"/>). On those boards the BIOS reports a per-key keyboard
    /// and the four-zone ColorTable path is accepted but inert, so this is the only route that
    /// lights anything.
    ///
    /// UNIFORM COLOUR ONLY, for now. The per-key index map isn't in this project yet, so zone
    /// colours can't be placed on the right keys. When the requested zones differ, zone 0 is
    /// applied to the whole keyboard and the result says so rather than claiming a zone layout.
    /// </summary>
    public sealed class PrimaxPerKeyBackend : IKeyboardBackend
    {
        private readonly LoggingService _logging;
        private PrimaxKeyboardMcu? _mcu;
        private Color[] _lastZoneColors = Enumerable.Repeat(Color.White, 4).ToArray();
        private int _brightness = 100;

        public PrimaxPerKeyBackend(LoggingService logging) => _logging = logging;

        public string Name => _mcu == null
            ? "OMEN per-key (Primax MCU)"
            : $"OMEN per-key (Primax {PrimaxKeyboardMcu.PrimaxVid:X4}:{_mcu.ProductId:X4})";
        public KeyboardMethod Method => KeyboardMethod.HidPerKey;
        public bool IsAvailable => _mcu != null;
        public bool SupportsReadback => false;
        public int ZoneCount => 4;
        public bool IsPerKey => true;

        public Task<bool> InitializeAsync()
        {
            try
            {
                _mcu = PrimaxKeyboardMcu.Open();
                if (_mcu == null)
                {
                    foreach (var line in PrimaxKeyboardMcu.DescribeCandidates())
                        _logging.Info($"[PrimaxPerKey] {line}");
                    return Task.FromResult(false);
                }

                _logging.Info($"[PrimaxPerKey] Opened {_mcu.ProductName} ({PrimaxKeyboardMcu.PrimaxVid:X4}:{_mcu.ProductId:X4}), " +
                              $"firmware {_mcu.FirmwareVersion}, {_mcu.LedCount} LEDs. Uniform colour only; not yet confirmed on hardware by OmenCore.");
                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                _logging.Warn($"[PrimaxPerKey] Open failed: {ex.Message}");
                _mcu?.Dispose();
                _mcu = null;
                return Task.FromResult(false);
            }
        }

        /// <summary>Colour actually sent for a zone request, and whether zones had to be collapsed.</summary>
        internal static (Color Color, bool Collapsed) ResolveUniformColor(Color[] zoneColors, int brightnessPercent)
        {
            var first = zoneColors.Length > 0 ? zoneColors[0] : Color.White;
            bool collapsed = zoneColors.Skip(1).Any(c => c.ToArgb() != first.ToArgb());
            double scale = Math.Clamp(brightnessPercent, 0, 100) / 100.0;
            var scaled = Color.FromArgb(
                (int)Math.Round(first.R * scale),
                (int)Math.Round(first.G * scale),
                (int)Math.Round(first.B * scale));
            return (scaled, collapsed);
        }

        public Task<RgbApplyResult> SetZoneColorsAsync(Color[] zoneColors)
        {
            _lastZoneColors = zoneColors.ToArray();
            return Task.FromResult(ApplyCurrent());
        }

        public Task<RgbApplyResult> SetZoneColorAsync(int zone, Color color)
        {
            if (zone >= 0 && zone < _lastZoneColors.Length) _lastZoneColors[zone] = color;
            return Task.FromResult(ApplyCurrent());
        }

        private RgbApplyResult ApplyCurrent()
        {
            var sw = Stopwatch.StartNew();
            var result = new RgbApplyResult();
            if (_mcu == null)
            {
                result.FailureReason = "Primax keyboard not open";
                return result;
            }

            var (color, collapsed) = ResolveUniformColor(_lastZoneColors, _brightness);
            try
            {
                result.BackendReportedSuccess = _mcu.SetStaticColor(color.R, color.G, color.B);
            }
            catch (Exception ex)
            {
                result.FailureReason = ex.Message;
            }

            if (!result.BackendReportedSuccess && result.FailureReason == null)
                result.FailureReason = "Keyboard MCU did not acknowledge the colour map";

            // Deliberately no VerificationAdvisory: any advisory marks the result unverified, and
            // the service would then "fall back" to the ColorTable path, which on these boards is
            // accepted but inert. The limitation is logged instead.
            if (collapsed)
                _logging.Info("[PrimaxPerKey] Per-zone colours aren't mapped on this keyboard yet; zone 1's colour was applied to every key.");
            result.DurationMs = (int)sw.ElapsedMilliseconds;
            return result;
        }

        public Task<Color[]?> ReadZoneColorsAsync() => Task.FromResult<Color[]?>(null);

        public Task<bool> SetBrightnessAsync(int brightness)
        {
            _brightness = Math.Clamp(brightness, 0, 100);
            return Task.FromResult(ApplyCurrent().BackendReportedSuccess);
        }

        public Task<bool> SetBacklightEnabledAsync(bool enabled)
        {
            try { return Task.FromResult(_mcu?.SetLightingEnabled(enabled) ?? false); }
            catch (Exception ex)
            {
                _logging.Warn($"[PrimaxPerKey] Backlight toggle failed: {ex.Message}");
                return Task.FromResult(false);
            }
        }

        public Task<RgbApplyResult> SetEffectAsync(KeyboardEffect effect, Color primaryColor, Color secondaryColor, int speed)
        {
            if (effect == KeyboardEffect.Off)
            {
                bool off;
                try { off = _mcu?.SetLightingEnabled(false) ?? false; }
                catch (Exception ex) { _logging.Warn($"[PrimaxPerKey] Lighting off failed: {ex.Message}"); off = false; }
                return Task.FromResult(new RgbApplyResult { BackendReportedSuccess = off });
            }

            if (effect == KeyboardEffect.Static)
                return SetZoneColorsAsync(new[] { primaryColor, primaryColor, primaryColor, primaryColor });

            return Task.FromResult(new RgbApplyResult
            {
                FailureReason = $"Effect '{effect}' is not supported on the Primax per-key keyboard yet (static colour only)."
            });
        }

        public void Dispose()
        {
            _mcu?.Dispose();
            _mcu = null;
        }
    }
}
