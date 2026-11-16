using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using WindowsInput;
using WindowsInput.Native;
using BarRaider.SdTools;
using EliteJournalReader.Events;
using Newtonsoft.Json.Linq;
using System.Globalization;
using NLog.Fluent;
using System.Diagnostics;

namespace Elite.Buttons
{
    static class StreamDeckCommon
    {
        private static Dictionary<string, string> _lastStatus = new Dictionary<string, string>();

        private static object myLock = new object();

        public static bool InputRunning;
        public static bool ForceStop = false;


        // internal rather than private: the UI Navigation button reuses this to gate
        // a keypress on game state, so the same condition set backs both features.
        internal static bool CheckProfileState(Profile.ProfileType profileType)
        {
            var state = false;

            switch (profileType)
            {
                case Profile.ProfileType.InternalPanel:
                    state = EliteData.StatusData.GuiFocus == StatusGuiFocus.InternalPanel;
                    break;
                case Profile.ProfileType.InvInternalPanel:
                    state = EliteData.StatusData.GuiFocus != StatusGuiFocus.InternalPanel;
                    break;
                case Profile.ProfileType.ExternalPanel:
                    state = EliteData.StatusData.GuiFocus == StatusGuiFocus.ExternalPanel;
                    break;
                case Profile.ProfileType.InvExternalPanel:
                    state = EliteData.StatusData.GuiFocus != StatusGuiFocus.ExternalPanel;
                    break;
                case Profile.ProfileType.CommsPanel:
                    state = EliteData.StatusData.GuiFocus == StatusGuiFocus.CommsPanel;
                    break;
                case Profile.ProfileType.InvCommsPanel:
                    state = EliteData.StatusData.GuiFocus != StatusGuiFocus.CommsPanel;
                    break;
                case Profile.ProfileType.RolePanel:
                    state = EliteData.StatusData.GuiFocus == StatusGuiFocus.RolePanel;
                    break;
                case Profile.ProfileType.InvRolePanel:
                    state = EliteData.StatusData.GuiFocus != StatusGuiFocus.RolePanel;
                    break;
                case Profile.ProfileType.GalaxyMap:
                    state = EliteData.StatusData.GuiFocus == StatusGuiFocus.GalaxyMap;
                    break;
                case Profile.ProfileType.InvGalaxyMap:
                    state = EliteData.StatusData.GuiFocus != StatusGuiFocus.GalaxyMap;
                    break;
                case Profile.ProfileType.SystemMap:
                    state = EliteData.StatusData.GuiFocus == StatusGuiFocus.SystemMap;
                    break;
                case Profile.ProfileType.InvSystemMap:
                    state = EliteData.StatusData.GuiFocus != StatusGuiFocus.SystemMap;
                    break;
                case Profile.ProfileType.Orrery:
                    state = EliteData.StatusData.GuiFocus == StatusGuiFocus.Orrery;
                    break;
                case Profile.ProfileType.InvOrrery:
                    state = EliteData.StatusData.GuiFocus != StatusGuiFocus.Orrery;
                    break;
                case Profile.ProfileType.FSSMode:
                    state = EliteData.StatusData.GuiFocus == StatusGuiFocus.FSSMode;
                    break;
                case Profile.ProfileType.InvFSSMode:
                    state = EliteData.StatusData.GuiFocus != StatusGuiFocus.FSSMode;
                    break;
                case Profile.ProfileType.SAAMode:
                    state = EliteData.StatusData.GuiFocus == StatusGuiFocus.SAAMode;
                    break;
                case Profile.ProfileType.InvSAAMode:
                    state = EliteData.StatusData.GuiFocus != StatusGuiFocus.SAAMode;
                    break;
                case Profile.ProfileType.InFighter:
                    state = EliteData.StatusData.InFighter;
                    break;
                case Profile.ProfileType.InvInFighter:
                    state = !EliteData.StatusData.InFighter;
                    break;
                case Profile.ProfileType.SrvTurret:
                    state = EliteData.StatusData.SrvTurret;
                    break;
                case Profile.ProfileType.InvSrvTurret:
                    state = !EliteData.StatusData.SrvTurret;
                    break;
                case Profile.ProfileType.InSRV:
                    state = EliteData.StatusData.InSRV;
                    break;
                case Profile.ProfileType.InvInSRV:
                    state = !EliteData.StatusData.InSRV;
                    break;
                case Profile.ProfileType.OnFoot:
                    state = EliteData.StatusData.OnFoot;
                    break;
                case Profile.ProfileType.InvOnFoot:
                    state = !EliteData.StatusData.OnFoot;
                    break;
                case Profile.ProfileType.AnalysisMode:
                    state = EliteData.StatusData.HudInAnalysisMode;
                    break;
                case Profile.ProfileType.InvAnalysisMode:
                    state = !EliteData.StatusData.HudInAnalysisMode;
                    break;
                case Profile.ProfileType.CargoScoop:
                    state = EliteData.StatusData.CargoScoopDeployed;
                    break;
                case Profile.ProfileType.InvCargoScoop:
                    state = !EliteData.StatusData.CargoScoopDeployed;
                    break;
                case Profile.ProfileType.Hardpoints:
                    state = EliteData.StatusData.HardpointsDeployed;
                    break;
                case Profile.ProfileType.InvHardpoints:
                    state = !EliteData.StatusData.HardpointsDeployed;
                    break;

                case Profile.ProfileType.BeingInterdicted:
                    state = EliteData.StatusData.BeingInterdicted;
                    break;
                case Profile.ProfileType.InvBeingInterdicted:
                    state = !EliteData.StatusData.BeingInterdicted;
                    break;
                case Profile.ProfileType.InMainShip:
                    state = EliteData.StatusData.InMainShip;
                    break;
                case Profile.ProfileType.InvInMainShip:
                    state = !EliteData.StatusData.InMainShip;
                    break;

                case Profile.ProfileType.Landed:
                    state = EliteData.StatusData.Landed;
                    break;
                case Profile.ProfileType.InvLanded:
                    state = !EliteData.StatusData.Landed;
                    break;
                case Profile.ProfileType.Supercruise:
                    state = EliteData.StatusData.Supercruise;
                    break;
                case Profile.ProfileType.InvSupercruise:
                    state = !EliteData.StatusData.Supercruise;
                    break;

                case Profile.ProfileType.Docked:
                    state = EliteData.StatusData.Docked;
                    break;
                case Profile.ProfileType.InvDocked:
                    state = !EliteData.StatusData.Docked;
                    break;

                case Profile.ProfileType.LandingGearDown:
                    state = EliteData.StatusData.LandingGearDown;
                    break;
                case Profile.ProfileType.InvLandingGearDown:
                    state = !EliteData.StatusData.LandingGearDown;
                    break;
                case Profile.ProfileType.ShieldsUp:
                    state = EliteData.StatusData.ShieldsUp;
                    break;
                case Profile.ProfileType.InvShieldsUp:
                    state = !EliteData.StatusData.ShieldsUp;
                    break;
                case Profile.ProfileType.FlightAssistOff:
                    state = EliteData.StatusData.FlightAssistOff;
                    break;
                case Profile.ProfileType.InvFlightAssistOff:
                    state = !EliteData.StatusData.FlightAssistOff;
                    break;

                case Profile.ProfileType.InvInWing:
                    state = !EliteData.StatusData.InWing;
                    break;
                case Profile.ProfileType.InWing:
                    state = EliteData.StatusData.InWing;
                    break;
                case Profile.ProfileType.InvLightsOn:
                    state = !EliteData.StatusData.LightsOn;
                    break;
                case Profile.ProfileType.LightsOn:
                    state = EliteData.StatusData.LightsOn;
                    break;
                case Profile.ProfileType.InvSilentRunning:
                    state = !EliteData.StatusData.SilentRunning;
                    break;
                case Profile.ProfileType.SilentRunning:
                    state = EliteData.StatusData.SilentRunning;
                    break;
                case Profile.ProfileType.InvScoopingFuel:
                    state = !EliteData.StatusData.ScoopingFuel;
                    break;
                case Profile.ProfileType.ScoopingFuel:
                    state = EliteData.StatusData.ScoopingFuel;
                    break;
                case Profile.ProfileType.InvSrvHandbrake:
                    state = !EliteData.StatusData.SrvHandbrake;
                    break;
                case Profile.ProfileType.SrvHandbrake:
                    state = EliteData.StatusData.SrvHandbrake;
                    break;
                case Profile.ProfileType.InvSrvUnderShip:
                    state = !EliteData.StatusData.SrvUnderShip;
                    break;
                case Profile.ProfileType.SrvUnderShip:
                    state = EliteData.StatusData.SrvUnderShip;
                    break;
                case Profile.ProfileType.InvSrvDriveAssist:
                    state = !EliteData.StatusData.SrvDriveAssist;
                    break;
                case Profile.ProfileType.SrvDriveAssist:
                    state = EliteData.StatusData.SrvDriveAssist;
                    break;
                case Profile.ProfileType.InvFsdMassLocked:
                    state = !EliteData.StatusData.FsdMassLocked;
                    break;
                case Profile.ProfileType.FsdMassLocked:
                    state = EliteData.StatusData.FsdMassLocked;
                    break;
                case Profile.ProfileType.InvFsdCharging:
                    state = !EliteData.StatusData.FsdCharging;
                    break;
                case Profile.ProfileType.FsdCharging:
                    state = EliteData.StatusData.FsdCharging;
                    break;
                case Profile.ProfileType.InvFsdCooldown:
                    state = !EliteData.StatusData.FsdCooldown;
                    break;
                case Profile.ProfileType.FsdCooldown:
                    state = EliteData.StatusData.FsdCooldown;
                    break;
                case Profile.ProfileType.InvLowFuel:
                    state = !EliteData.StatusData.LowFuel;
                    break;
                case Profile.ProfileType.LowFuel:
                    state = EliteData.StatusData.LowFuel;
                    break;
                case Profile.ProfileType.InvOverheating:
                    state = !EliteData.StatusData.Overheating;
                    break;
                case Profile.ProfileType.Overheating:
                    state = EliteData.StatusData.Overheating;
                    break;
                case Profile.ProfileType.InvHasLatLong:
                    state = !EliteData.StatusData.HasLatLong;
                    break;
                case Profile.ProfileType.HasLatLong:
                    state = EliteData.StatusData.HasLatLong;
                    break;
                case Profile.ProfileType.InvIsInDanger:
                    state = !EliteData.StatusData.IsInDanger;
                    break;
                case Profile.ProfileType.IsInDanger:
                    state = EliteData.StatusData.IsInDanger;
                    break;
                case Profile.ProfileType.InvNightVision:
                    state = !EliteData.StatusData.NightVision;
                    break;
                case Profile.ProfileType.NightVision:
                    state = EliteData.StatusData.NightVision;
                    break;
                case Profile.ProfileType.InvFsdJump:
                    state = !EliteData.StatusData.FsdJump;
                    break;
                case Profile.ProfileType.FsdJump:
                    state = EliteData.StatusData.FsdJump;
                    break;
                case Profile.ProfileType.InvSrvHighBeam:
                    state = !EliteData.StatusData.SrvHighBeam;
                    break;
                case Profile.ProfileType.SrvHighBeam:
                    state = EliteData.StatusData.SrvHighBeam;
                    break;
                case Profile.ProfileType.InvInTaxi:
                    state = !EliteData.StatusData.InTaxi;
                    break;
                case Profile.ProfileType.InTaxi:
                    state = EliteData.StatusData.InTaxi;
                    break;
                case Profile.ProfileType.InvInMulticrew:
                    state = !EliteData.StatusData.InMulticrew;
                    break;
                case Profile.ProfileType.InMulticrew:
                    state = EliteData.StatusData.InMulticrew;
                    break;
                case Profile.ProfileType.InvGlideMode:
                    state = !EliteData.StatusData.GlideMode;
                    break;
                case Profile.ProfileType.GlideMode:
                    state = EliteData.StatusData.GlideMode;
                    break;
                case Profile.ProfileType.InvTelepresenceMulticrew:
                    state = !EliteData.StatusData.TelepresenceMulticrew;
                    break;
                case Profile.ProfileType.TelepresenceMulticrew:
                    state = EliteData.StatusData.TelepresenceMulticrew;
                    break;
                case Profile.ProfileType.InvPhysicalMulticrew:
                    state = !EliteData.StatusData.PhysicalMulticrew;
                    break;
                case Profile.ProfileType.PhysicalMulticrew:
                    state = EliteData.StatusData.PhysicalMulticrew;
                    break;
                case Profile.ProfileType.InvFsdhyperdrivecharging:
                    state = !EliteData.StatusData.Fsdhyperdrivecharging;
                    break;
                case Profile.ProfileType.Fsdhyperdrivecharging:
                    state = EliteData.StatusData.Fsdhyperdrivecharging;
                    break;
            }

            return state;
        }

        private static bool CheckProfileStates(List<Profile.ProfileType> profileTypes)
        {
            foreach (var profileType in profileTypes)
            {
                var state = CheckProfileState(profileType);
                if (!state) return false;
            }

            return true;

        }

        // Profile switching used to run only from a button's OnTick. A profile with no Elite
        // buttons on it (an empty "Elite InSRV" / "Elite InFighter", or any non-Elite profile)
        // never ticks, so once the deck switched there it could never switch back out. Remember
        // the last connection seen for each device and drive switching from our own timer too.
        // SDConnection.Dispose only unhooks events; its StreamDeckConnection (the shared plugin
        // websocket) stays valid, so a remembered connection can still send switchToProfile
        // after the button that supplied it has disappeared.
        private static readonly Dictionary<string, ISDConnection> _connections = new Dictionary<string, ISDConnection>();
        private static readonly object _connectionsLock = new object();
        private static Timer _profileTimer;

        public static void StartProfileSwitchTimer()
        {
            if (_profileTimer != null) return;

            _profileTimer = new Timer(_ =>
            {
                List<ISDConnection> connections;

                lock (_connectionsLock)
                {
                    connections = _connections.Values.ToList();
                }

                foreach (var connection in connections)
                {
                    try
                    {
                        SwitchProfileForState(connection);
                    }
                    catch (Exception ex)
                    {
                        Logger.Instance.LogMessage(TracingLevel.ERROR, $"Profile switch timer: {ex.Message}");
                    }
                }
            }, null, 1000, 1000);
        }

        public static void HandleOnTick(ISDConnection connection)
        {
            var key = connection?.DeviceId ?? "";

            lock (_connectionsLock)
            {
                _connections[key] = connection;
            }

            // The timer does the switching, so it keeps running when no button is visible.
            if (_profileTimer == null) SwitchProfileForState(connection);
        }

        private static void SwitchProfileForState(ISDConnection connection)
        {
            lock (myLock)
            {
                // Logger.Instance.LogMessage(TracingLevel.DEBUG, "HandleOnTick called");
        
                // DeviceInfo() resolves the device by matching the id from willAppear against the
                // list sent at registration, and returns null when there is no match. Stream Dock
                // (Ajazz/Mirabox) registers one id and then announces a different one in
                // deviceDidConnect, which every later willAppear uses, so the lookup always fails
                // there. Dereferencing null would throw out of the library's tick loop, which has
                // no catch and is async void, killing the whole plugin process - so fall back to
                // the device list we parsed from the registration payload ourselves.
                var deviceInfo = connection.DeviceInfo();

                var deviceId = deviceInfo?.Id ?? connection.DeviceId;

                DeviceType deviceType;

                if (deviceInfo != null)
                {
                    deviceType = deviceInfo.Type;
                }
                else
                {
                    var fallback = Profile.HostDevices.FirstOrDefault(x => x.Id == deviceId) ??
                                   Profile.HostDevices.FirstOrDefault();

                    if (fallback == null) return;

                    deviceType = fallback.Type;

                    if (string.IsNullOrEmpty(deviceId)) deviceId = fallback.Id;
                }

                if (string.IsNullOrEmpty(deviceId)) return;
                // Logger.Instance.LogMessage(TracingLevel.DEBUG, $"DeviceType: {deviceType} ContainsKey: {Profile.Profiles.ContainsKey(deviceType)}");
        
                /*
    
    [Id: 49C5DB3C77BEDDEB4EE9DA88ED76CF59 Type: StreamDeckXL Size: Rows: 4 Columns: 8]
    [Id: 8F9F54D3085E17917873D7BA481EA6B7 Type: StreamDeckPlus Size: Rows: 2 Columns: 4]
    [Id: 8403C4817A9F3533236625316AE80F1A Type: StreamDeckClassic Size: Rows: 3 Columns: 5] RIGHT SIDE
    [Id: A2F05B845C24AD67556305C9EE9CA0DC Type: StreamDeckClassic Size: Rows: 3 Columns: 5] LEFT SIDE
    
    
    49C5DB3C77BEDDEB4EE9DA88ED76CF59  deviceInfo.Id,  connection.DeviceId*/

                if (!Profile.Profiles.ContainsKey(deviceType)) return;

                var profiles = Profile.Profiles[deviceType];

                if (!_lastStatus.ContainsKey(deviceId))
                {
                    _lastStatus.Add(deviceId, null);
                }

                try
                {
                   var bProcess = Process.GetProcessesByName("EliteDangerous64").FirstOrDefault();

                    if (bProcess == null) return;
    
                }
                catch // (Exception ex)
                {
                    // do nothing
                }

                /*
                EliteData.StatusData.GuiFocus == StatusGuiFocus.StationServices
                EliteData.StatusData.GuiFocus == StatusGuiFocus.Codex
                
                EliteData.StatusData.BeingInterdicted
                EliteData.StatusData.InMainShip
                EliteData.StatusData.Landed
                EliteData.StatusData.Supercruise
                EliteData.StatusData.Docked
    
                EliteData.StatusData.LandingGearDown
                EliteData.StatusData.ShieldsUp
                EliteData.StatusData.FlightAssistOff
    
                EliteData.StatusData.InWing
                EliteData.StatusData.LightsOn
                EliteData.StatusData.SilentRunning
                EliteData.StatusData.ScoopingFuel
                EliteData.StatusData.SrvHandbrake
                EliteData.StatusData.SrvUnderShip
                EliteData.StatusData.SrvDriveAssist
                EliteData.StatusData.FsdMassLocked
                EliteData.StatusData.FsdCharging
                EliteData.StatusData.FsdCooldown
                EliteData.StatusData.LowFuel
                EliteData.StatusData.Overheating
                EliteData.StatusData.HasLatLong
                EliteData.StatusData.IsInDanger
                EliteData.StatusData.NightVision
                EliteData.StatusData.AltitudeFromAverageRadius
                EliteData.StatusData.FsdJump
                EliteData.StatusData.SrvHighBeam
                
                EliteData.StatusData.InTaxi
                EliteData.StatusData.InMulticrew
                EliteData.StatusData.OnFootInStation
                EliteData.StatusData.OnFootOnPlanet
                EliteData.StatusData.AimDownSight
                EliteData.StatusData.LowOxygen
                EliteData.StatusData.LowHealth
                EliteData.StatusData.Cold
                EliteData.StatusData.Hot
                EliteData.StatusData.VeryCold
                EliteData.StatusData.VeryHot
                EliteData.StatusData.GlideMode
                EliteData.StatusData.OnFootInHangar
                EliteData.StatusData.OnFootSocialSpace
                EliteData.StatusData.OnFootExterior
                EliteData.StatusData.BreathableAtmosphere
                EliteData.StatusData.TelepresenceMulticrew
                EliteData.StatusData.PhysicalMulticrew
                EliteData.StatusData.Fsdhyperdrivecharging
                */

                foreach (var profile in profiles)
                {

                    var limitId = "";

                    var idFound = false;

                    var profileNameArray = profile.Name.Split(' ').Select(x => x.ToUpper().Trim())
                        .Where(x => x.Length == 6)
                        .ToList();
                    foreach (var namePart in profileNameArray)
                    {
                        if (long.TryParse(namePart, NumberStyles.HexNumber,
                                System.Globalization.CultureInfo.InvariantCulture, out var temp))
                        {
                            idFound = true;

                            if (deviceId.Contains(namePart))
                            {
                                 limitId = deviceId;
                            }
                        }
                    }

                    if (!idFound || !string.IsNullOrEmpty(limitId))
                    {
                        var state = CheckProfileStates(profile.ProfileTypes);

                        if (state)
                        {
                            var key = string.Join(",", profile.ProfileTypes.Select(x => x.ToString()).ToArray());

                            if (_lastStatus[deviceId] != key)
                            {
                                 Logger.Instance.LogMessage(TracingLevel.DEBUG,
                                    "switch profile " + key + " to " +
                                    profile.Name +
                                    " for " + profile.DeviceType + " " + deviceId);

                                _lastStatus[deviceId] = key;

                                try
                                {
                                    AsyncHelper.RunSync(() => connection.SwitchProfileAsync(profile.Name));
                                    Logger.Instance.LogMessage(TracingLevel.DEBUG, $"SwitchProfileAsync completed for: {profile.Name}");
                                }
                                catch (Exception ex)
                                {
                                    Logger.Instance.LogMessage(TracingLevel.ERROR, $"SwitchProfileAsync failed: {ex.Message}");
                                }
                            }

                            return;
                        }
                    }
                }

                var mainProfiles = profiles.Where(x => x.ProfileTypes.Contains(Profile.ProfileType.Main)).ToList();

                foreach (var p in mainProfiles)
                {
                    var limitId = "";

                    var idFound = false;

                    var profileNameArray = p.Name.Split(' ').Select(x => x.ToUpper().Trim())
                        .Where(x => x.Length == 6).ToList();
                    foreach (var namePart in profileNameArray)
                    {
                        if (long.TryParse(namePart, NumberStyles.HexNumber,
                                System.Globalization.CultureInfo.InvariantCulture, out var temp))
                        {
                            idFound = true;

                            if (deviceId.Contains(namePart))
                            {
                                limitId = deviceId;
                            }
                        }
                    }

                    if (!idFound || !string.IsNullOrEmpty(limitId))
                    {
                        if (_lastStatus[deviceId] != Profile.ProfileType.Main.ToString())
                        {
                            Logger.Instance.LogMessage(TracingLevel.DEBUG,
                                "switch profile " + Profile.ProfileType.Main +
                                " to " +
                                p.Name + " for " +
                                p.DeviceType + " " + deviceId);

                            _lastStatus[deviceId] = Profile.ProfileType.Main.ToString();

                            try
                            {
                                AsyncHelper.RunSync(() => connection.SwitchProfileAsync(p.Name));
                                Logger.Instance.LogMessage(TracingLevel.DEBUG, $"SwitchProfileAsync completed for: {p.Name}");
                            }
                            catch (Exception ex)
                            {
                                Logger.Instance.LogMessage(TracingLevel.ERROR, $"SwitchProfileAsync failed: {ex.Message}");
                            }

                            break;

                        }
                    }
                }
            }
        }


        private static async void SendInput(string inputText)
        {
            InputRunning = true;
            try
            {
                await Task.Run(() =>
                {
                    var text = inputText;
                    for (var idx = 0; idx < text.Length && !ForceStop; idx++)
                    {
                        var macro = CommandTools.ExtractMacro(text, idx);
                        if (macro == null) throw new ArgumentException("Invalid keyboard macro: " + text);
                        idx += macro.Length - 1;
                        macro = macro.Substring(1, macro.Length - 2);
                        CommandTools.HandleMacro(macro);
                    }
                });
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.ERROR, $"Keyboard input failed: {ex.Message}");
            }
            finally { InputRunning = false; }
        }


        private static void SendInputDown(string inputText)
        {
            var text = inputText;

            for (var idx = 0; idx < text.Length && !ForceStop; idx++)
            {
                var macro = CommandTools.ExtractMacro(text, idx);
                idx += macro.Length - 1;
                macro = macro.Substring(1, macro.Length - 2);

                CommandTools.HandleMacroDown(macro);
            }
        }

        private static void SendInputUp(string inputText)
        {
            var text = inputText;

            for (var idx = 0; idx < text.Length && !ForceStop; idx++)
            {
                var macro = CommandTools.ExtractMacro(text, idx);
                idx += macro.Length - 1;
                macro = macro.Substring(1, macro.Length - 2);

                CommandTools.HandleMacroUp(macro);
            }
        }


        public static void SendKeypress(StandardBindingInfo keyInfo, int repeatCount = 1)
        {
            var inputText = CommandTools.BuildInputText(keyInfo);

            if (!string.IsNullOrEmpty(inputText))
            {

                //Logger.Instance.LogMessage(TracingLevel.DEBUG, $"{inputText}");

                for (var i = 0; i < repeatCount; i++)
                {
                    if (repeatCount > 1 && i > 0)
                    {
                        Thread.Sleep(20);
                    } 
                    SendInput("{" + inputText + "}");

                }

                // keyboard test page : https://w3c.github.io/uievents/tools/key-event-viewer.html
            }
            else
            {
                Logger.Instance.LogMessage(TracingLevel.WARN,
                    "Command has no keyboard binding in the active game preset. Assign a keyboard binding in Elite controls; joystick-only commands cannot be simulated.");
            }

        }

        public static void SendKeypressDown(StandardBindingInfo keyInfo)
        {
            var inputText = CommandTools.BuildInputText(keyInfo);

            if (!string.IsNullOrEmpty(inputText))
            {
                SendInputDown("{" + inputText + "}");
            }
        }


        public static void SendKeypressUp(StandardBindingInfo keyInfo)
        {
            var inputText = CommandTools.BuildInputText(keyInfo);

            if (!string.IsNullOrEmpty(inputText))
            {
                SendInputUp("{" + inputText + "}");
            }
        }

        public static bool CheckForGif(string imageFilename)
        {
            return imageFilename?.ToLower().EndsWith(".gif") == true;
        }

    }
}
