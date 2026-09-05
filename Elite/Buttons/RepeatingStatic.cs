using System;
using System.Threading.Tasks;
using BarRaider.SdTools;
using EliteJournalReader;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

// ReSharper disable StringLiteralTypo

//using EliteAPI.Logging;

namespace Elite.Buttons
{

    [PluginActionId("com.mhwlng.elite.repeatingstatic")]
    public class RepeatingStatic : EliteKeypadBase
    {
        protected class PluginSettings
        {
            public static PluginSettings CreateDefaultSettings()
            {
                var instance = new PluginSettings
                {
                    Function = string.Empty,
                    ImageFilename = string.Empty,
                };

                return instance;
            }

            [JsonProperty(PropertyName = "function")]
            public string Function { get; set; }

            [FilenameProperty]
            [JsonProperty(PropertyName = "image")]
            public string ImageFilename { get; set; }

        }


        PluginSettings settings;
        private string _imageBase64;


        public RepeatingStatic(SDConnection connection, InitialPayload payload) : base(connection, payload)
        {
            if (payload.Settings == null || payload.Settings.Count == 0)
            {
                //Logger.Instance.LogMessage(TracingLevel.DEBUG, "Repeating Static Constructor #1");

                settings = PluginSettings.CreateDefaultSettings();

                // A button dropped for the first time arrives with no settings at all, so seed the
                // default artwork here too - otherwise it stays blank until something else saves.
                DefaultImages.Apply(settings, "repeatingstatic", settings.Function);

                Connection.SetSettingsAsync(JObject.FromObject(settings)).Wait();

            }
            else
            {
                //Logger.Instance.LogMessage(TracingLevel.DEBUG, "Repeating Static Constructor #2");

                settings = payload.Settings.ToObject<PluginSettings>();

                if (DefaultImages.Apply(settings, "repeatingstatic", settings.Function))
                {
                    Connection.SetSettingsAsync(JObject.FromObject(settings)).Wait();
                }

                HandleFileNames();
            }

            HandleDisplay();
        }

        /// <summary>
        /// The key image is ours to draw, so push it whenever it might have been reset. The
        /// library hashes what it last sent and drops an unchanged image, so repeating this on
        /// every tick costs nothing after the first send.
        /// </summary>
        public override void OnTick()
        {
            base.OnTick();

            HandleDisplay();
        }

        private void HandleDisplay()
        {
            try
            {
                if (string.IsNullOrEmpty(_imageBase64))
                {
                    Connection.SetDefaultImageAsync().Wait();
                }
                else
                {
                    Connection.SetImageAsync(_imageBase64).Wait();
                }
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.ERROR, $"RepeatingStatic HandleDisplay: {ex}");
            }
        }

        public override void KeyPressed(KeyPayload payload)
        {
            EliteKeys.SendKeypressDown(settings.Function);
        }

        public override void KeyReleased(KeyPayload payload)
        {
            EliteKeys.SendKeypressUp(settings.Function);
        }

        public override void Dispose()
        {
            base.Dispose();

            //Logger.Instance.LogMessage(TracingLevel.DEBUG, "Destructor called #1");
        }


        public override void ReceivedSettings(ReceivedSettingsPayload payload)
        {
            //Logger.Instance.LogMessage(TracingLevel.DEBUG, "ReceivedSettings");

            // New in StreamDeck-Tools v2.0:
            BarRaider.SdTools.Tools.AutoPopulateSettings(settings, payload.Settings);

            // Name the key after the function that was just picked, so a plain colour
            // plate still says what the button does.
            var autoTitle = FunctionTitles.For("repeatingstatic", settings.Function);
            if (!string.IsNullOrEmpty(autoTitle))
            {
                Connection.SetTitleAsync(autoTitle).Wait();
            }

            DefaultImages.Apply(settings, "repeatingstatic", settings.Function);

            HandleFileNames();

            HandleDisplay();
        }

        private void HandleFileNames()
        {
            _imageBase64 = Tools.FileToBase64(settings.ImageFilename, true);

            Connection.SetSettingsAsync(JObject.FromObject(settings)).Wait();
        }

    }
}
