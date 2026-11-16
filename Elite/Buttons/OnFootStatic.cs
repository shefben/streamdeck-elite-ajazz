using System;
using System.Collections.Generic;
using BarRaider.SdTools;

// ReSharper disable StringLiteralTypo

namespace Elite.Buttons
{
    /// <summary>
    /// Odyssey suit commands. Status.json reports neither the suit shield nor the suit
    /// flashlight, so those two remember the last toggle sent from this plugin and forget it
    /// whenever the commander is no longer on foot (embarking resets both in game).
    /// Pulse scan and energy cells show On whenever they can be used, i.e. while on foot.
    /// </summary>
    [PluginActionId("com.mhwlng.elite.onfootstatic")]
    public class OnFootStatic : StateStaticBase
    {
        private static bool _shieldOn;
        private static bool _flashlightOn;

        private static bool OnFoot()
        {
            if (EliteData.StatusData.OnFoot) return true;

            _shieldOn = false;
            _flashlightOn = false;

            return false;
        }

        private static readonly Dictionary<string, FunctionInfo> FunctionList =
            new Dictionary<string, FunctionInfo>(StringComparer.OrdinalIgnoreCase)
            {
                ["HumanoidPulseScanButton"] = new FunctionInfo
                {
                    BindingType = BindingType.OnFoot,
                    IsOn = OnFoot
                },
                ["HumanoidBattery"] = new FunctionInfo
                {
                    BindingType = BindingType.OnFoot,
                    IsOn = OnFoot
                },
                ["HumanoidToggleFlashlightButton"] = new FunctionInfo
                {
                    BindingType = BindingType.OnFoot,
                    IsOn = () => OnFoot() && _flashlightOn
                },
                ["HumanoidToggleShieldsButton"] = new FunctionInfo
                {
                    BindingType = BindingType.OnFoot,
                    IsOn = () => OnFoot() && _shieldOn
                }
            };

        public OnFootStatic(SDConnection connection, InitialPayload payload) : base(connection, payload)
        {
        }

        protected override string ActionKey => "onfootstatic";

        protected override IReadOnlyDictionary<string, FunctionInfo> Functions => FunctionList;

        protected override string DefaultFunction => "HumanoidPulseScanButton";

        protected override void OnSent(string function)
        {
            if (!EliteData.StatusData.OnFoot) return;

            if (string.Equals(function, "HumanoidToggleShieldsButton", StringComparison.OrdinalIgnoreCase))
            {
                _shieldOn = !_shieldOn;
            }
            else if (string.Equals(function, "HumanoidToggleFlashlightButton", StringComparison.OrdinalIgnoreCase))
            {
                _flashlightOn = !_flashlightOn;
            }
        }
    }
}
