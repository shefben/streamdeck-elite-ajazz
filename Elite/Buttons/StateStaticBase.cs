using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using BarRaider.SdTools;
using EliteJournalReader;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

// ReSharper disable StringLiteralTypo

namespace Elite.Buttons
{
    /// <summary>
    /// A static (one key, one command) button that also shows an On or Off image, picked from
    /// live game state. Subclasses list the functions they offer, which binding set each one
    /// lives in, and what "on" means for it - a toggle that is engaged, or a command that can be
    /// used right now.
    ///
    /// Functions listed as hold functions press the key down while the Stream Deck key is held
    /// and release it on key-up, for camera movement and the like; everything else is a tap.
    /// </summary>
    public abstract class StateStaticBase : EliteKeypadBase
    {
        protected class PluginSettings
        {
            public static PluginSettings CreateDefaultSettings()
            {
                return new PluginSettings
                {
                    Function = string.Empty,
                    PrimaryImageFilename = string.Empty,
                    SecondaryImageFilename = string.Empty,
                    ClickSoundFilename = string.Empty
                };
            }

            [JsonProperty(PropertyName = "function")]
            public string Function { get; set; }

            [FilenameProperty]
            [JsonProperty(PropertyName = "primaryImage")]
            public string PrimaryImageFilename { get; set; }

            [FilenameProperty]
            [JsonProperty(PropertyName = "secondaryImage")]
            public string SecondaryImageFilename { get; set; }

            [FilenameProperty]
            [JsonProperty(PropertyName = "clickSound")]
            public string ClickSoundFilename { get; set; }
        }

        /// <summary>One selectable command.</summary>
        protected class FunctionInfo
        {
            public BindingType BindingType { get; set; }
            public bool Hold { get; set; }
            public Func<bool> IsOn { get; set; }
        }

        protected PluginSettings settings;

        private string _onImage;
        private string _offImage;
        private CachedSound _clickSound;
        private bool _keyHeld;
        private FunctionInfo _heldFunction;
        private readonly object _displayLock = new object();

        /// <summary>Short action key used for default artwork, e.g. "onfootstatic".</summary>
        protected abstract string ActionKey { get; }

        /// <summary>Function name to command details. Names match the binding names.</summary>
        protected abstract IReadOnlyDictionary<string, FunctionInfo> Functions { get; }

        /// <summary>Picked when the button is first dropped.</summary>
        protected abstract string DefaultFunction { get; }

        /// <summary>True while a hold function's key is down.</summary>
        protected bool KeyHeld => _keyHeld;

        /// <summary>Called after a command was sent, so toggles the game doesn't report can be tracked.</summary>
        protected virtual void OnSent(string function) { }

        protected StateStaticBase(SDConnection connection, InitialPayload payload) : base(connection, payload)
        {
            if (payload.Settings == null || payload.Settings.Count == 0)
            {
                settings = PluginSettings.CreateDefaultSettings();
                settings.Function = DefaultFunction;

                DefaultImages.Apply(settings, ActionKey, settings.Function);

                Connection.SetSettingsAsync(JObject.FromObject(settings)).Wait();
            }
            else
            {
                settings = payload.Settings.ToObject<PluginSettings>();

                if (string.IsNullOrEmpty(settings.Function) || !Functions.ContainsKey(settings.Function))
                {
                    settings.Function = DefaultFunction;
                }

                if (DefaultImages.Apply(settings, ActionKey, settings.Function))
                {
                    Connection.SetSettingsAsync(JObject.FromObject(settings)).Wait();
                }
            }

            HandleFileNames();
            HandleDisplay();

            Program.JournalWatcher.MessageReceived += HandleEliteEvents;
        }

        private void HandleEliteEvents(object sender, MessageReceivedEventArgs args)
        {
            HandleDisplay();
        }

        private FunctionInfo Current =>
            settings?.Function != null && Functions.TryGetValue(settings.Function, out var info) ? info : null;

        /// <summary>True when the selected function's On image should show.</summary>
        protected bool IsOn()
        {
            try
            {
                if (_keyHeld && Current?.Hold == true) return true;

                return Current?.IsOn?.Invoke() == true;
            }
            catch
            {
                return false;
            }
        }

        private void HandleDisplay()
        {
            lock (_displayLock)
            {
                try
                {
                    var image = IsOn() ? _onImage : _offImage;

                    if (string.IsNullOrEmpty(image))
                    {
                        // Only one of the pair picked: show it for both states rather than blank.
                        image = _onImage ?? _offImage;
                    }

                    if (string.IsNullOrEmpty(image))
                    {
                        Connection.SetDefaultImageAsync().Wait();
                    }
                    else
                    {
                        Connection.SetImageAsync(image).Wait();
                    }
                }
                catch (Exception ex)
                {
                    Logger.Instance.LogMessage(TracingLevel.ERROR, $"{GetType().Name} HandleDisplay: {ex.Message}");
                }
            }
        }

        public override void OnTick()
        {
            base.OnTick();

            HandleDisplay();
        }

        /// <summary>The binding for a function, read from the active bindings by name.</summary>
        protected static StandardBindingInfo BindingFor(string function, FunctionInfo info)
        {
            if (Program.Binding == null || !Program.Binding.TryGetValue(info.BindingType, out var bindings) ||
                bindings == null)
            {
                return null;
            }

            return typeof(UserBindings).GetProperty(function)?.GetValue(bindings) as StandardBindingInfo;
        }

        public override void KeyPressed(KeyPayload payload)
        {
            var info = Current;

            if (info == null) return;

            if (Program.Binding == null)
            {
                Connection.ShowAlert().Wait();
                return;
            }

            var binding = BindingFor(settings.Function, info);

            // A command with no keyboard binding can't be simulated - say so on the key rather
            // than silently doing nothing.
            if (string.IsNullOrEmpty(CommandTools.BuildInputText(binding)))
            {
                Logger.Instance.LogMessage(TracingLevel.WARN,
                    $"{settings.Function} has no keyboard binding in the active Elite preset.");
                Connection.ShowAlert().Wait();
                return;
            }

            if (info.Hold)
            {
                StreamDeckCommon.SendKeypressDown(binding);
                _keyHeld = true;
                _heldFunction = info;
            }
            else
            {
                if (StreamDeckCommon.InputRunning)
                {
                    StreamDeckCommon.ForceStop = true;
                    return;
                }

                StreamDeckCommon.ForceStop = false;
                StreamDeckCommon.SendKeypress(binding);
            }

            OnSent(settings.Function);

            PlayClickSound();

            // State flags lag the key press until the game rewrites Status.json; redraw shortly
            // after so the image follows without waiting for the next tick.
            Task.Delay(400).ContinueWith(_ => HandleDisplay());
        }

        public override void KeyReleased(KeyPayload payload)
        {
            if (!_keyHeld || _heldFunction == null) return;

            _keyHeld = false;

            var binding = BindingFor(settings.Function, _heldFunction);
            _heldFunction = null;

            if (binding != null) StreamDeckCommon.SendKeypressUp(binding);

            HandleDisplay();
        }

        private void PlayClickSound()
        {
            if (_clickSound == null) return;

            try
            {
                AudioPlaybackEngine.Instance.PlaySound(_clickSound);
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.FATAL, $"PlaySound: {ex}");
            }
        }

        public override void Dispose()
        {
            base.Dispose();

            Program.JournalWatcher.MessageReceived -= HandleEliteEvents;

            // Never leave a held key down if the button disappears mid-press.
            if (_keyHeld) KeyReleased(null);
        }

        public override void ReceivedSettings(ReceivedSettingsPayload payload)
        {
            var previousFunction = settings.Function;

            BarRaider.SdTools.Tools.AutoPopulateSettings(settings, payload.Settings);

            if (string.IsNullOrEmpty(settings.Function) || !Functions.ContainsKey(settings.Function))
            {
                settings.Function = DefaultFunction;
            }

            // A held key must be released under the binding it was pressed with.
            if (_keyHeld && previousFunction != settings.Function) KeyReleased(null);

            // Name the key after the function, as the Static button does.
            if (previousFunction != settings.Function)
            {
                var autoTitle = FunctionTitles.For(ActionKey, settings.Function);
                if (!string.IsNullOrEmpty(autoTitle)) Connection.SetTitleAsync(autoTitle).Wait();
            }

            DefaultImages.Apply(settings, ActionKey, settings.Function);

            HandleFileNames();
            HandleDisplay();
        }

        private void HandleFileNames()
        {
            _onImage = Tools.FileToBase64(settings.PrimaryImageFilename, true);
            _offImage = Tools.FileToBase64(settings.SecondaryImageFilename, true);

            if (string.IsNullOrEmpty(_onImage)) _onImage = null;
            if (string.IsNullOrEmpty(_offImage)) _offImage = null;

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
