using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BarRaider.SdTools;
using EliteJournalReader.Events;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

// ReSharper disable StringLiteralTypo

//using EliteAPI.Logging;

namespace Elite.Buttons
{

    [PluginActionId("com.mhwlng.elite.static")]
    public class Static : EliteKeypadBase
    {
        protected class PluginSettings
        {
            public static PluginSettings CreateDefaultSettings()
            {
                var instance = new PluginSettings
                {
                    Function = string.Empty,
                    ImageFilename = string.Empty,
                    ClickSoundFilename = string.Empty
				};

                return instance;
            }

            [JsonProperty(PropertyName = "function")]
            public string Function { get; set; }

            [FilenameProperty]
            [JsonProperty(PropertyName = "image")]
            public string ImageFilename { get; set; }

            [FilenameProperty]
            [JsonProperty(PropertyName = "clickSound")]
            public string ClickSoundFilename { get; set; }

		}


		PluginSettings settings;
        private CachedSound _clickSound = null;
        private string _imageBase64;


		public Static(SDConnection connection, InitialPayload payload) : base(connection, payload)
        {
            if (payload.Settings == null || payload.Settings.Count == 0)
            {
                //Logger.Instance.LogMessage(TracingLevel.DEBUG, "Static Constructor #1");

                settings = PluginSettings.CreateDefaultSettings();

                // A button dropped for the first time arrives with no settings at all, so seed the
                // default artwork here too - otherwise it stays blank until something else saves.
                DefaultImages.Apply(settings, "static", settings.Function);

                Connection.SetSettingsAsync(JObject.FromObject(settings)).Wait();

            }
            else
            {
                //Logger.Instance.LogMessage(TracingLevel.DEBUG, "Static Constructor #2");

                settings = payload.Settings.ToObject<PluginSettings>();

                if (DefaultImages.Apply(settings, "static"))
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
                Logger.Instance.LogMessage(TracingLevel.ERROR, $"Static HandleDisplay: {ex}");
            }
        }


        public override void KeyPressed(KeyPayload payload)
        {
            EliteKeys.SendKeypress(settings.Function);

            if (_clickSound != null)
            {
                try
                {
                    AudioPlaybackEngine.Instance.PlaySound(_clickSound);
                }
                catch (Exception ex)
                {
                    Logger.Instance.LogMessage(TracingLevel.FATAL, $"PlaySound: {ex}");
                }
            }

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
            var autoTitle = FunctionTitles.For("static", settings.Function);
            if (!string.IsNullOrEmpty(autoTitle))
            {
                Connection.SetTitleAsync(autoTitle).Wait();
            }

            DefaultImages.Apply(settings, "static");

            HandleFileNames();

            HandleDisplay();
        }

        private void HandleFileNames()
        {
            _imageBase64 = Tools.FileToBase64(settings.ImageFilename, true);

            _clickSound = null;
            if (File.Exists(settings.ClickSoundFilename))
            {
                try
                {
                    _clickSound = new CachedSound(settings.ClickSoundFilename);
                }
                catch (Exception ex)
                {
                    Logger.Instance.LogMessage(TracingLevel.FATAL, $"CachedSound: {settings.ClickSoundFilename} {ex}");

					_clickSound = null;
                    settings.ClickSoundFilename = null;
                }
			}

			Connection.SetSettingsAsync(JObject.FromObject(settings)).Wait();
		}

	}
}
