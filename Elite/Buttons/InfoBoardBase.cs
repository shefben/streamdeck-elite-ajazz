using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using BarRaider.SdTools;
using EliteJournalReader;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

// ReSharper disable StringLiteralTypo

namespace Elite.Buttons
{
    /// <summary>
    /// A read-only key that draws a few lines of live text over a background. Three states,
    /// each with its own image so the key reads at a glance: Inactive (nothing to show - the
    /// image is drawn as is), Active (normal readout) and Alert (something needs attention,
    /// e.g. wanted, a mission about to expire, docking denied).
    ///
    /// Subclasses only build the text; drawing, image loading, default artwork and redraw
    /// throttling live here.
    /// </summary>
    public abstract class InfoBoardBase : EliteKeypadBase
    {
        public enum BoardState
        {
            Inactive,
            Active,
            Alert
        }

        /// <summary>What a board wants on the key right now.</summary>
        public class BoardContent
        {
            public BoardState State { get; set; }

            /// <summary>First line is drawn larger, as the headline.</summary>
            public List<string> Lines { get; set; } = new List<string>();

            /// <summary>Optional 0-1 progress bar drawn along the bottom.</summary>
            public double? Progress { get; set; }

            public static BoardContent Inactive(params string[] lines) =>
                new BoardContent { State = BoardState.Inactive, Lines = lines.ToList() };
        }

        protected class PluginSettings
        {
            public static PluginSettings CreateDefaultSettings()
            {
                return new PluginSettings
                {
                    PrimaryImageFilename = string.Empty,
                    DefaultImageFilename = string.Empty,
                    AlertImageFilename = string.Empty,
                    PrimaryColor = "#ffffff",
                    AlertColor = "#ffb000",
                    Option = string.Empty
                };
            }

            [FilenameProperty]
            [JsonProperty(PropertyName = "primaryImage")]
            public string PrimaryImageFilename { get; set; }

            [FilenameProperty]
            [JsonProperty(PropertyName = "defaultImage")]
            public string DefaultImageFilename { get; set; }

            [FilenameProperty]
            [JsonProperty(PropertyName = "alertImage")]
            public string AlertImageFilename { get; set; }

            [JsonProperty(PropertyName = "primaryColor")]
            public string PrimaryColor { get; set; }

            [JsonProperty(PropertyName = "alertColor")]
            public string AlertColor { get; set; }

            /// <summary>Board-specific choice (e.g. a threshold), interpreted by the subclass.</summary>
            [JsonProperty(PropertyName = "option")]
            public string Option { get; set; }
        }

        protected PluginSettings settings;

        private byte[] _activeImage;
        private byte[] _inactiveImage;
        private byte[] _alertImage;
        private Color _primaryColor = Color.White;
        private Color _alertColor = Color.Orange;
        private string _lastKey;
        private DateTime _lastSent = DateTime.MinValue;
        private readonly object _drawLock = new object();

        /// <summary>Short action key used for default artwork, e.g. "missions".</summary>
        protected abstract string ActionKey { get; }

        /// <summary>Builds the current content. Called on every tick and journal event.</summary>
        protected abstract BoardContent BuildContent();

        protected InfoBoardBase(SDConnection connection, InitialPayload payload) : base(connection, payload)
        {
            if (payload.Settings == null || payload.Settings.Count == 0)
            {
                settings = PluginSettings.CreateDefaultSettings();
            }
            else
            {
                settings = payload.Settings.ToObject<PluginSettings>() ?? PluginSettings.CreateDefaultSettings();
            }

            DefaultImages.Apply(settings, ActionKey);
            LoadSettings();
            Draw(force: true);

            Program.JournalWatcher.MessageReceived += HandleEliteEvents;
        }

        private void HandleEliteEvents(object sender, MessageReceivedEventArgs args) => Draw();

        public override void KeyPressed(KeyPayload payload)
        {
            // Pressing a board forces a fresh draw - handy after the game was restarted.
            Draw(force: true);
        }

        public override void OnTick()
        {
            base.OnTick();
            Draw();
        }

        public override void Dispose()
        {
            base.Dispose();
            Program.JournalWatcher.MessageReceived -= HandleEliteEvents;
        }

        public override void ReceivedSettings(ReceivedSettingsPayload payload)
        {
            BarRaider.SdTools.Tools.AutoPopulateSettings(settings, payload.Settings);

            DefaultImages.Apply(settings, ActionKey);
            LoadSettings();
            Draw(force: true);
        }

        private static byte[] ReadImage(string path)
        {
            try
            {
                return File.Exists(path) ? File.ReadAllBytes(path) : null;
            }
            catch
            {
                return null;
            }
        }

        private static Color ParseColor(string value, Color fallback)
        {
            try
            {
                return string.IsNullOrEmpty(value) ? fallback : ColorTranslator.FromHtml(value);
            }
            catch
            {
                return fallback;
            }
        }

        private void LoadSettings()
        {
            if (string.IsNullOrEmpty(settings.PrimaryColor)) settings.PrimaryColor = "#ffffff";
            if (string.IsNullOrEmpty(settings.AlertColor)) settings.AlertColor = "#ffb000";

            lock (_drawLock)
            {
                _activeImage = ReadImage(settings.PrimaryImageFilename);
                _inactiveImage = ReadImage(settings.DefaultImageFilename) ?? _activeImage;
                _alertImage = ReadImage(settings.AlertImageFilename) ?? _activeImage;
                _primaryColor = ParseColor(settings.PrimaryColor, Color.White);
                _alertColor = ParseColor(settings.AlertColor, Color.Orange);
                _lastKey = null;
            }

            Connection.SetSettingsAsync(JObject.FromObject(settings)).Wait();
        }

        private void Draw(bool force = false)
        {
            BoardContent content;

            try
            {
                content = BuildContent() ?? BoardContent.Inactive();
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.ERROR, $"{GetType().Name} BuildContent: {ex.Message}");
                content = BoardContent.Inactive("--");
            }

            var key = content.State + "|" + string.Join("|", content.Lines) + "|" +
                      (content.Progress.HasValue ? content.Progress.Value.ToString("F3") : "");

            lock (_drawLock)
            {
                // The library also skips identical images, but rendering is the expensive part;
                // resend now and then anyway in case the host dropped the image on a page change.
                if (!force && key == _lastKey && (DateTime.UtcNow - _lastSent).TotalSeconds < 10) return;

                try
                {
                    var image = Render(content);

                    Connection.SetImageAsync(image).Wait();

                    _lastKey = key;
                    _lastSent = DateTime.UtcNow;
                }
                catch (Exception ex)
                {
                    Logger.Instance.LogMessage(TracingLevel.ERROR, $"{GetType().Name} Draw: {ex.Message}");
                }
            }
        }

        private string Render(BoardContent content)
        {
            byte[] background;
            switch (content.State)
            {
                case BoardState.Alert:
                    background = _alertImage;
                    break;
                case BoardState.Inactive:
                    background = _inactiveImage;
                    break;
                default:
                    background = _activeImage;
                    break;
            }

            const int size = 256;

            // Each draw decodes its own copy of the background. Sharing one Bitmap between the
            // tick thread and the journal thread is what throws "Object is currently in use
            // elsewhere" in GDI+.
            using (var bitmap = new Bitmap(size, size))
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                graphics.Clear(Color.Black);

                if (background != null)
                {
                    try
                    {
                        using (var ms = new MemoryStream(background))
                        using (var img = Image.FromStream(ms))
                        {
                            graphics.DrawImage(img, 0, 0, size, size);
                        }
                    }
                    catch
                    {
                        // unreadable image - plain black is fine
                    }
                }

                var lines = content.Lines.Where(l => l != null).ToList();

                if (lines.Count > 0)
                {
                    // Darken behind the text so it stays legible on busy artwork.
                    using (var shade = new SolidBrush(Color.FromArgb(content.State == BoardState.Inactive ? 110 : 150, 0, 0, 0)))
                    {
                        graphics.FillRectangle(shade, 0, 0, size, size);
                    }

                    var color = content.State == BoardState.Alert ? _alertColor : _primaryColor;
                    if (content.State == BoardState.Inactive) color = Color.FromArgb(170, color);

                    DrawLines(graphics, lines, color, size, content.Progress.HasValue ? 34 : 10);
                }

                if (content.Progress.HasValue)
                {
                    var p = Math.Max(0, Math.Min(1, content.Progress.Value));
                    var barColor = content.State == BoardState.Alert ? _alertColor : _primaryColor;

                    using (var back = new SolidBrush(Color.FromArgb(90, 255, 255, 255)))
                    using (var fill = new SolidBrush(barColor))
                    {
                        graphics.FillRectangle(back, 16, size - 30, size - 32, 16);
                        graphics.FillRectangle(fill, 16, size - 30, (float)((size - 32) * p), 16);
                    }
                }

                return BarRaider.SdTools.Tools.ImageToBase64(bitmap, true);
            }
        }

        /// <summary>Largest font size, at most <paramref name="start"/>, at which every line fits the width.</summary>
        private static float FitWidth(Graphics graphics, IEnumerable<string> lines, float start, float maxWidth)
        {
            var list = lines.ToList();

            for (var px = start; px > 12f; px -= 2f)
            {
                using (var font = new Font("Arial", px, FontStyle.Bold, GraphicsUnit.Pixel))
                {
                    if (list.All(l => graphics.MeasureString(l, font).Width <= maxWidth)) return px;
                }
            }

            return 12f;
        }

        /// <summary>
        /// Centres the lines vertically. The headline and the body lines are sized separately, so
        /// one long detail line doesn't shrink the headline; then both shrink together if the
        /// block is too tall.
        /// </summary>
        private static void DrawLines(Graphics graphics, List<string> lines, Color color, int size, int bottomReserve)
        {
            const float margin = 10f;
            var maxWidth = size - margin * 2;
            var maxHeight = size - margin - bottomReserve;

            var headFit = FitWidth(graphics, lines.Take(1), 58f, maxWidth);
            var bodyFit = FitWidth(graphics, lines.Skip(1), 34f, maxWidth);

            for (var scale = 1f; scale >= 0.3f; scale -= 0.05f)
            {
                var head = Math.Max(12f, headFit * scale);
                var body = Math.Max(12f, Math.Min(bodyFit * scale, head));

                using (var headFont = new Font("Arial", head, FontStyle.Bold, GraphicsUnit.Pixel))
                using (var bodyFont = new Font("Arial", body, FontStyle.Bold, GraphicsUnit.Pixel))
                {
                    var fonts = lines.Select((l, i) => i == 0 ? headFont : bodyFont).ToList();
                    var sizes = lines.Select((l, i) => graphics.MeasureString(l, fonts[i])).ToList();

                    var totalHeight = sizes.Sum(s => s.Height * 0.92f);

                    if (totalHeight > maxHeight && scale > 0.31f) continue;

                    var y = margin + (maxHeight - totalHeight) / 2f;

                    using (var brush = new SolidBrush(color))
                    using (var shadow = new SolidBrush(Color.FromArgb(200, 0, 0, 0)))
                    {
                        for (var i = 0; i < lines.Count; i++)
                        {
                            var text = lines[i];
                            var width = sizes[i].Width;

                            // Last resort for very long text: trim with an ellipsis.
                            while (width > maxWidth && text.Length > 3)
                            {
                                text = text.Substring(0, text.Length - 2).TrimEnd() + "…";
                                width = graphics.MeasureString(text, fonts[i]).Width;
                            }

                            var x = (size - width) / 2f;

                            graphics.DrawString(text, fonts[i], shadow, x + 2, y + 2);
                            graphics.DrawString(text, fonts[i], brush, x, y);

                            y += sizes[i].Height * 0.92f;
                        }
                    }

                    return;
                }
            }
        }

        // ── Formatting helpers shared by the boards ────────────────────────────

        protected static string Credits(long value)
        {
            var abs = Math.Abs((double)value);
            var sign = value < 0 ? "-" : "";

            if (abs >= 1e9) return $"{sign}{abs / 1e9:0.##}B cr";
            if (abs >= 1e6) return $"{sign}{abs / 1e6:0.##}M cr";
            if (abs >= 1e3) return $"{sign}{abs / 1e3:0.#}K cr";

            return $"{value} cr";
        }

        protected static string Duration(TimeSpan span)
        {
            if (span.TotalSeconds < 0) span = TimeSpan.Zero;

            if (span.TotalDays >= 1) return $"{(int)span.TotalDays}d {span.Hours}h";
            if (span.TotalHours >= 1) return $"{(int)span.TotalHours}h {span.Minutes:00}m";
            if (span.TotalMinutes >= 1) return $"{span.Minutes}m {span.Seconds:00}s";

            return $"{span.Seconds}s";
        }

        /// <summary>Splits "LegalState" style names: "IllegalCargo" -> "Illegal Cargo".</summary>
        protected static string Words(string value)
        {
            if (string.IsNullOrEmpty(value)) return value;

            var chars = new List<char>();

            for (var i = 0; i < value.Length; i++)
            {
                if (i > 0 && char.IsUpper(value[i]) && !char.IsUpper(value[i - 1])) chars.Add(' ');
                chars.Add(value[i]);
            }

            return new string(chars.ToArray());
        }

        protected static double? OptionNumber(string option)
        {
            return double.TryParse(option, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var v)
                ? v
                : (double?)null;
        }
    }
}
