using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BarRaider.SdTools;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

// ReSharper disable StringLiteralTypo

namespace Elite.Buttons
{
    /// <summary>
    /// Keypad version of the Exploration Dial's touch strip: the body you just resolved in the
    /// FSS, with its biological and geological signal counts.
    ///
    /// The dial pushed this through SetFeedbackAsync, which needs a Stream Deck + touch strip.
    /// Hosts without one - Stream Dock (Ajazz/Mirabox) among them, which implements setFeedback
    /// but not setFeedbackLayout, on hardware that has no strip at all - could never show it, and
    /// nothing else on a key does: NavTarget reports signals for the nav target, not for the body
    /// the FSS just resolved. So the same readout is drawn as a key image instead.
    ///
    /// Counts are drawn as BIO / GEO text rather than the dial's leaf and volcano emoji, which
    /// GDI+ renders monochrome at best and as tofu boxes at worst.
    /// </summary>
    [PluginActionId("com.mhwlng.elite.explorationsignals")]
    public class ExplorationSignals : EliteKeypadBase
    {
        // How long a body resolved in the FSS holds the display before it falls back to
        // whatever body we are actually at. Matches the Exploration Dial.
        private const int FssFlashMs = 5000;

        private const int Width = 256;
        private const int Height = 256;

        private const int NameTop = 8;
        private const int NameHeight = 84;
        private const int RowHeight = 76;
        private const int BioTop = 96;
        private const int GeoTop = 172;

        private const int SideMargin = 18;
        private const int LabelFontPt = 26;

        protected class PluginSettings
        {
            public static PluginSettings CreateDefaultSettings() => new PluginSettings
            {
                BackgroundImageFilename = string.Empty,
                NameColor = "#ffffff",
                BioColor = "#4ce04c",
                GeoColor = "#ffa040",
                HideWhenEmpty = false
            };

            [FilenameProperty]
            [JsonProperty(PropertyName = "backgroundImage")]
            public string BackgroundImageFilename { get; set; }

            [JsonProperty(PropertyName = "nameColor")]
            public string NameColor { get; set; }

            [JsonProperty(PropertyName = "bioColor")]
            public string BioColor { get; set; }

            [JsonProperty(PropertyName = "geoColor")]
            public string GeoColor { get; set; }

            [JsonProperty(PropertyName = "hideWhenEmpty")]
            public bool HideWhenEmpty { get; set; }
        }

        private readonly SemaphoreSlim _displayLock = new SemaphoreSlim(1, 1);

        private PluginSettings settings;
        private Image _backgroundImage;
        private string _lastDrawn;

        public ExplorationSignals(SDConnection connection, InitialPayload payload) : base(connection, payload)
        {
            if (payload.Settings == null || payload.Settings.Count == 0)
            {
                settings = PluginSettings.CreateDefaultSettings();

                // A button dropped for the first time arrives with no settings at all, so seed the
                // default artwork here too - otherwise it stays blank until something else saves.
                DefaultImages.Apply(settings, "explorationsignals");

                Connection.SetSettingsAsync(JObject.FromObject(settings)).Wait();
            }
            else
            {
                settings = payload.Settings.ToObject<PluginSettings>();

                if (DefaultImages.Apply(settings, "explorationsignals"))
                {
                    Connection.SetSettingsAsync(JObject.FromObject(settings)).Wait();
                }
            }

            HandleFileNames();

            AsyncHelper.RunSync(HandleDisplay);
        }

        public override void KeyPressed(KeyPayload payload) { }

        public override void KeyReleased(KeyPayload payload) { }

        public override async void OnTick()
        {
            base.OnTick();

            await HandleDisplay();
        }

        public override void ReceivedSettings(ReceivedSettingsPayload payload)
        {
            BarRaider.SdTools.Tools.AutoPopulateSettings(settings, payload.Settings);

            if (DefaultImages.Apply(settings, "explorationsignals"))
            {
                Connection.SetSettingsAsync(JObject.FromObject(settings)).Wait();
            }

            HandleFileNames();

            // The picture changed underneath us, so the change check must not suppress the redraw.
            _lastDrawn = null;

            AsyncHelper.RunSync(HandleDisplay);
        }

        public override void Dispose()
        {
            _backgroundImage?.Dispose();
            _backgroundImage = null;

            _displayLock?.Dispose();

            base.Dispose();
        }

        private void HandleFileNames()
        {
            _backgroundImage?.Dispose();
            _backgroundImage = null;

            if (string.IsNullOrWhiteSpace(settings.BackgroundImageFilename)) return;

            try
            {
                if (File.Exists(settings.BackgroundImageFilename))
                {
                    _backgroundImage = Image.FromFile(settings.BackgroundImageFilename);
                }
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.ERROR,
                    $"ExplorationSignals background {settings.BackgroundImageFilename}: {ex}");
            }
        }

        /// <summary>
        /// Which body to describe: one just resolved in the FSS wins for a few seconds,
        /// otherwise the body we are actually at.
        /// </summary>
        private static string DisplayBody()
        {
            if (!string.IsNullOrEmpty(EliteData.LastSignalBody) &&
                (DateTime.UtcNow - EliteData.LastSignalUtc).TotalMilliseconds < FssFlashMs)
            {
                return EliteData.LastSignalBody;
            }

            return EliteData.StatusData?.BodyName;
        }

        /// <summary>
        /// Bodies are named "&lt;system&gt; &lt;designator&gt;"; the system part is noise on a key.
        /// </summary>
        private static string ShortBodyName(string bodyName)
        {
            if (string.IsNullOrEmpty(bodyName)) return string.Empty;

            var system = EliteData.StarSystem;

            if (!string.IsNullOrEmpty(system) &&
                bodyName.StartsWith(system, StringComparison.OrdinalIgnoreCase) &&
                bodyName.Length > system.Length)
            {
                return bodyName.Substring(system.Length).Trim();
            }

            return bodyName;
        }

        private async Task HandleDisplay()
        {
            if (!await _displayLock.WaitAsync(0)) return;

            try
            {
                var body = DisplayBody();

                (int BiologyCount, int GeologyCount) signals = (0, 0);

                var hasSignals = !string.IsNullOrEmpty(body) &&
                                 EliteData.SignalCache.TryGetValue(body, out signals) &&
                                 (signals.BiologyCount > 0 || signals.GeologyCount > 0);

                if (!hasSignals) signals = (0, 0);

                var name = hasSignals ? ShortBodyName(body) : string.Empty;

                // The tick runs every second and the key almost never changes, so skip the
                // redraw when nothing did - SetImageAsync would drop it anyway, but rendering
                // a bitmap a second for every placed key is wasted work.
                var state = $"{name}|{signals.BiologyCount}|{signals.GeologyCount}|{hasSignals}";

                if (state == _lastDrawn) return;

                _lastDrawn = state;

                if (!hasSignals && settings.HideWhenEmpty)
                {
                    await Connection.SetDefaultImageAsync();
                    return;
                }

                using (var bitmap = CreateBitmap())
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

                    Draw(graphics, name, signals.BiologyCount, signals.GeologyCount, hasSignals);

                    await Connection.SetImageAsync(BarRaider.SdTools.Tools.ImageToBase64(bitmap, true));
                }
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.ERROR, "ExplorationSignals HandleDisplay: " + ex);
            }
            finally
            {
                _displayLock.Release();
            }
        }

        /// <summary>
        /// Falls back to solid black rather than nothing, so the key works with no image set.
        /// </summary>
        private Bitmap CreateBitmap()
        {
            var bitmap = new Bitmap(Width, Height);

            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.Black);

                if (_backgroundImage != null)
                {
                    graphics.DrawImage(_backgroundImage, 0, 0, Width, Height);
                }
            }

            return bitmap;
        }

        private void Draw(Graphics graphics, string name, int bio, int geo, bool hasSignals)
        {
            DrawCentered(graphics, hasSignals ? name : "NO SIGNALS",
                ParseColor(settings.NameColor, Color.White), NameTop, NameHeight, 40);

            DrawRow(graphics, "BIO", hasSignals ? bio.ToString() : "-",
                ParseColor(settings.BioColor, Color.FromArgb(76, 224, 76)), BioTop);

            DrawRow(graphics, "GEO", hasSignals ? geo.ToString() : "-",
                ParseColor(settings.GeoColor, Color.FromArgb(255, 160, 64)), GeoTop);
        }

        /// <summary>
        /// Label on the left, value on the right, both vertically centred in the row.
        /// </summary>
        private static void DrawRow(Graphics graphics, string label, string value, Color color, int rowTop)
        {
            using (var labelFont = new Font("Arial", LabelFontPt, FontStyle.Bold))
            using (var labelBrush = new SolidBrush(Color.FromArgb(190, 190, 190)))
            {
                var labelSize = graphics.MeasureString(label, labelFont);

                graphics.DrawString(label, labelFont, labelBrush,
                    SideMargin, rowTop + (RowHeight - labelSize.Height) / 2f);
            }

            var valueSize = FitFont(graphics, value, Width / 2f - SideMargin, RowHeight, 54, out var size);

            if (size <= 0) return;

            using (var valueFont = new Font("Arial", size, FontStyle.Bold))
            using (var valueBrush = new SolidBrush(color))
            {
                graphics.DrawString(value, valueFont, valueBrush,
                    Width - SideMargin - valueSize.Width, rowTop + (RowHeight - valueSize.Height) / 2f);
            }
        }

        private static void DrawCentered(Graphics graphics, string text, Color color, int top, int height, int maxSize)
        {
            if (string.IsNullOrEmpty(text)) return;

            var measured = FitFont(graphics, text, Width - SideMargin * 2, height, maxSize, out var size);

            if (size <= 0) return;

            using (var font = new Font("Arial", size, FontStyle.Bold))
            using (var brush = new SolidBrush(color))
            {
                graphics.DrawString(text, font, brush,
                    (Width - measured.Width) / 2f, top + (height - measured.Height) / 2f);
            }
        }

        /// <summary>
        /// Largest Arial size at which the text fits the box, and its measured size there.
        /// </summary>
        private static SizeF FitFont(Graphics graphics, string text, float maxWidth, float maxHeight, int maxSize, out int size)
        {
            size = 0;

            if (string.IsNullOrEmpty(text)) return SizeF.Empty;

            for (var candidate = maxSize; candidate >= 8; candidate--)
            {
                using (var font = new Font("Arial", candidate, FontStyle.Bold))
                {
                    var measured = graphics.MeasureString(text, font);

                    if (measured.Width > maxWidth || measured.Height > maxHeight) continue;

                    size = candidate;

                    return measured;
                }
            }

            return SizeF.Empty;
        }

        private static Color ParseColor(string hex, Color fallback)
        {
            try { return (Color)new ColorConverter().ConvertFromString(hex); }
            catch { return fallback; }
        }
    }
}
