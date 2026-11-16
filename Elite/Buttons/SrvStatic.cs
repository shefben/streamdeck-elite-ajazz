using System;
using System.Collections.Generic;
using BarRaider.SdTools;

// ReSharper disable StringLiteralTypo

namespace Elite.Buttons
{
    /// <summary>
    /// SRV commands. Cargo scoop and headlights follow the Status.json flags, which the game
    /// sets for the SRV as well as the ship. Target Ahead is On while driving, and Eject All
    /// Cargo is On while there is cargo aboard to eject.
    /// </summary>
    [PluginActionId("com.mhwlng.elite.srvstatic")]
    public class SrvStatic : StateStaticBase
    {
        private static readonly Dictionary<string, FunctionInfo> FunctionList =
            new Dictionary<string, FunctionInfo>(StringComparer.OrdinalIgnoreCase)
            {
                ["SelectTarget_Buggy"] = new FunctionInfo
                {
                    BindingType = BindingType.Srv,
                    IsOn = () => EliteData.StatusData.InSRV
                },
                ["EjectAllCargo_Buggy"] = new FunctionInfo
                {
                    BindingType = BindingType.Srv,
                    IsOn = () => EliteData.StatusData.InSRV && EliteData.StatusData.Cargo > 0
                },
                ["ToggleCargoScoop_Buggy"] = new FunctionInfo
                {
                    BindingType = BindingType.Srv,
                    IsOn = () => EliteData.StatusData.InSRV && EliteData.StatusData.CargoScoopDeployed
                },
                ["HeadlightsBuggyButton"] = new FunctionInfo
                {
                    BindingType = BindingType.Srv,
                    IsOn = () => EliteData.StatusData.InSRV &&
                                 (EliteData.StatusData.LightsOn || EliteData.StatusData.SrvHighBeam)
                }
            };

        public SrvStatic(SDConnection connection, InitialPayload payload) : base(connection, payload)
        {
        }

        protected override string ActionKey => "srvstatic";

        protected override IReadOnlyDictionary<string, FunctionInfo> Functions => FunctionList;

        protected override string DefaultFunction => "SelectTarget_Buggy";
    }
}
