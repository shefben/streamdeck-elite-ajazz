using System;
using System.Collections.Generic;
using BarRaider.SdTools;

// ReSharper disable StringLiteralTypo

namespace Elite.Buttons
{
    /// <summary>
    /// Trailblazers colonisation commands. The game does not report the settlement placement
    /// camera in Status.json, so placement mode is tracked from the keys sent here: triggering
    /// the colonisation module enters it, and exiting or placing the settlement leaves it, as
    /// does docking or leaving the ship.
    ///
    /// Trigger Colonisation Module is On while it can be used (flying the main ship). Placement
    /// commands are On while placement mode is active, and the camera movement commands - which
    /// press and hold - are also On while their key is held.
    /// </summary>
    [PluginActionId("com.mhwlng.elite.colonisationstatic")]
    public class ColonisationStatic : StateStaticBase
    {
        private static bool _placementActive;

        private static bool InShip()
        {
            var s = EliteData.StatusData;

            return s.InMainShip && !s.Docked && !s.OnFoot && !s.InSRV && !s.InFighter;
        }

        private static bool Placing()
        {
            if (!InShip()) _placementActive = false;

            return _placementActive;
        }

        private static FunctionInfo Tap() => new FunctionInfo
        {
            BindingType = BindingType.Ship,
            IsOn = Placing
        };

        private static FunctionInfo Hold() => new FunctionInfo
        {
            BindingType = BindingType.Ship,
            Hold = true,
            IsOn = Placing
        };

        private static readonly Dictionary<string, FunctionInfo> FunctionList =
            new Dictionary<string, FunctionInfo>(StringComparer.OrdinalIgnoreCase)
            {
                ["TriggerColonisationModule"] = new FunctionInfo
                {
                    BindingType = BindingType.Ship,
                    IsOn = InShip
                },
                ["PlaceSettlement"] = Tap(),
                ["ChangeConstructionOption"] = Tap(),
                ["RotateSettlementLeft"] = Tap(),
                ["RotateSettlementRight"] = Tap(),
                ["ExitSettlementPlacementCamera"] = Tap(),
                ["PlacementCamSpeedInc"] = Tap(),
                ["PlacementCamSpeedDec"] = Tap(),
                ["MovePlacementCamForward"] = Hold(),
                ["MovePlacementCamBackwards"] = Hold(),
                ["MovePlacementCamLeft"] = Hold(),
                ["MovePlacementCamRight"] = Hold(),
                ["MovePlacementCamUp"] = Hold(),
                ["MovePlacementCamDown"] = Hold(),
                ["PitchPlacementCameraUp"] = Hold(),
                ["PitchPlacementCameraDown"] = Hold(),
                ["YawPlacementCameraLeft"] = Hold(),
                ["YawPlacementCameraRight"] = Hold()
            };

        public ColonisationStatic(SDConnection connection, InitialPayload payload) : base(connection, payload)
        {
        }

        protected override string ActionKey => "colonisationstatic";

        protected override IReadOnlyDictionary<string, FunctionInfo> Functions => FunctionList;

        protected override string DefaultFunction => "TriggerColonisationModule";

        protected override void OnSent(string function)
        {
            switch (function)
            {
                case "TriggerColonisationModule":
                    if (InShip()) _placementActive = true;
                    break;

                case "ExitSettlementPlacementCamera":
                case "PlaceSettlement":
                    _placementActive = false;
                    break;
            }
        }
    }
}
