using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BarRaider.SdTools;
using Newtonsoft.Json;

// ReSharper disable StringLiteralTypo

namespace Elite
{
    /// <summary>
    /// Fills a button's image settings from the artwork shipped in Images\Optional, so a freshly
    /// placed button is usable without picking a file for every state by hand.
    ///
    /// Coverage is guaranteed rather than best effort. Slots we have real artwork for get it from
    /// <see cref="ByAction"/>; everything else falls back to a plain colour plate chosen from the
    /// slot's name (see <see cref="GenericFor"/>), which reads correctly once the button title is
    /// set. That means adding a new image setting to a button can never leave it blank.
    ///
    /// Only empty slots and slots still holding one of our own defaults are written. A path
    /// pointing outside Images\Optional is the user's own choice and is never touched, so
    /// changing a function swaps the artwork while a hand-picked image survives.
    /// </summary>
    public static class DefaultImages
    {
        private static readonly string OptionalDirectory =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Images", "Optional");

        // Plain colour plates used where no specific artwork exists. They are half-height blocks
        // meant to sit under the key title, so they only read well on a button that has one.
        private const string GenericOn = "Green Top";
        private const string GenericOff = "Redtop";
        private const string GenericWarning = "Orange Bottom";
        private const string GenericDanger = "Red Bottom";
        private const string GenericNeutral = "Blue Top";

        /// <summary>Ship / Odyssey flight state artwork, shared by both status buttons.</summary>
        private static readonly Dictionary<string, string> ShipStatusImages =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["notActiveImage"] = "Deep Space",
                ["supercruiseChargingImage"] = "supercruise charge",
                ["fsdSupercruiseJumpingImage"] = "supercruise jump",
                ["supercruiseImage"] = "supercruise active",
                ["supercruiseOverchargeImage"] = "supercruise active - green top",
                ["supercruiseAssistImage"] = "supercruise standby2",
                ["normalSpaceImage"] = "Deep Space Local",
                ["hyperspaceChargingImage"] = "Hyperspace charge",
                ["fsdHyperspaceJumpingImage"] = "Hyperspace Jump",
                ["fuelScoopingImage"] = "Fuel Scoop Active",
                ["planetApproachImage"] = "Planet Approach v2",
                ["orbitalCruiseImage"] = "Orbital Cruise Active",
                ["deorbitingImage"] = "Deorbit Glide",
                ["planetFlightImage"] = "Planet Flight Active 2",
                ["landedImage"] = "Ship Landed",
                ["liftoffImage"] = "Liftoff Active",
                ["leavingPlanetImage"] = "Leaving Planet",
                ["noFireZoneImage"] = "No Fire Zone",
                ["stationApproachImage"] = "Space Station Approach",
                ["dockedAtStationImage"] = "Space Station Docked",
                ["stationInteriorImage"] = "Space Station Docking",
                ["onFootInStationImage"] = "Wireframe/On Foot"
            };

        private static readonly Dictionary<string, Dictionary<string, string>> ByAction =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["shipstatus"] = ShipStatusImages,
                ["odysseyshipstatus"] = ShipStatusImages,

                ["exobiology"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["defaultImage"] = "BIO Scan Zone Offline",
                    ["zoneAImage"] = "BIO Scan Zone A",
                    ["zoneBImage"] = "BIO Scan Zone B",
                    ["zoneCImage"] = "BIO Scan Zone C",
                    ["zoneDImage"] = "BIO Scan Zone D"
                },

                ["gravity"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["primaryImage"] = "Planet Gravity",
                    ["defaultImage"] = "Planet Gravity Disabled",
                    ["highGravityImage"] = "Gravity Warning"
                },

                ["latlonginfo"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["primaryImage"] = "Lat Long Active",
                    ["defaultImage"] = "Lat Long Not Active"
                },

                ["planetinfo"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["primaryImage"] = "Planet Info Active",
                    ["defaultImage"] = "Planet Info Not Active"
                },

                ["navtarget"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["activeImage"] = "Target Planet Selection Active",
                    ["defaultImage"] = "Target Planet Not Selected"
                },

                ["headingaltitude"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["primaryImage"] = "Planet Flight Active 2",
                    ["defaultImage"] = "Deep Space"
                },

                ["fss"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["primaryImage"] = "FSSActive3",
                    ["secondaryImage"] = "FSSstandby3",
                    ["tertiaryImage"] = "FSS Offline"
                },

                ["route"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["primaryImage"] = "Route Jumps",
                    ["tertiaryImage"] = "Route Jumps Not Set"
                },

                ["routeadv"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["noRouteImage"] = "Route Jumps Not Set",
                    ["jumpsImage"] = "Route Jumps",
                    ["remainingImage"] = "Route Jumps",
                    ["nextImage"] = "Next System",
                    ["fuelImage"] = "Fuel Scoop Active",
                    ["progressImage"] = "Route Jumps",
                    ["jumpRangeImage"] = "Route Jumps"
                },

                // Neither community set has Odyssey suit artwork - RobManders' set predates
                // Odyssey and Andechs75 has no on-foot category - so the telemetry states use
                // colour plates chosen to match what they mean rather than the generic green.
                ["onfootexploration"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["inactiveImage"] = "Wireframe/On Foot",
                    ["gravityImage"] = "Planet Gravity",
                    ["oxygenImage"] = "Blue Top",
                    ["oxygenWarningImage"] = "Orange Bottom",
                    ["healthImage"] = "Green Top",
                    ["healthWarningImage"] = "Red Bottom",
                    ["temperatureImage"] = "Yellow Bottom",
                    ["atmosphereImage"] = "Dark Green Top",
                    ["atmosphereWarningImage"] = "Orange Bottom",
                    ["thermalImage"] = "Green Top",
                    ["thermalColdImage"] = "Blue Top",
                    ["thermalVeryColdImage"] = "Dark Blue Top",
                    ["thermalHotImage"] = "Orange Bottom",
                    ["thermalVeryHotImage"] = "Red Bottom"
                },

                // Same story for the Alert button's hazard states - no artwork exists for them,
                // so they get red for damage and amber for warnings.
                ["alert"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["defaultImage"] = "Deep Space",
                    ["heatWarningImage"] = "Orange Bottom",
                    ["lowFuelImage"] = "Yellow Bottom",
                    ["dockingDeniedImage"] = "Orange Bottom"
                },

                ["galaxysearch"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["backgroundImage"] = "Deep Space"
                },

                ["explorationsignals"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["backgroundImage"] = "Deep Space"
                },

                // Fallback plate for a function with no artwork of its own; the auto title
                // supplies the meaning.
                ["static"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["image"] = "Community/RobManders/Static-Button"
                },

                ["repeatingstatic"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["image"] = "Community/RobManders/Static-Button"
                }
            };

        // Community artwork bundled under Images\Optional\Community - see the README there for
        // sources and licences. RobManders' set was drawn for this plugin, so Toggle, Alarm,
        // Limpet, Power and Firegroup use it throughout for a consistent look.
        private const string Toggles = "Community/RobManders/Toggle Button/";
        private const string Alarms = "Community/RobManders/Alarm Button/";
        private const string Limpets = "Community/RobManders/Limpet Button/";
        private const string Pips = "Community/RobManders/Power Button/";
        private const string Groups = "Community/RobManders/Firegroup Button/";

        /// <summary>
        /// Toggle artwork, chosen by the selected function rather than by slot name.
        /// </summary>
        private static readonly Dictionary<string, (string Primary, string Secondary)> ToggleByFunction =
            new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase)
            {
                ["PlayerHUDModeToggle"] = (Toggles + "Cockpit-Mode_SCAN", Toggles + "Cockpit-Mode_CMBT"),
                ["ToggleCargoScoop"] = (Toggles + "Cargo-Scoop_DEPL", Toggles + "Cargo-Scoop_RETR"),
                ["LandingGearToggle"] = (Toggles + "Landing-Gear_DEPL", Toggles + "Landing-Gear_RETR"),
                ["ShipSpotLightToggle"] = (Toggles + "Lights_ON", Toggles + "Lights_OFF"),
                ["NightVisionToggle"] = (Toggles + "Night-Vision_ON", Toggles + "Night-Vision_OFF"),
                ["ToggleDriveAssist"] = (Toggles + "SRV-Drive-Assist_ON", Toggles + "SRV-Drive-Assist_OFF"),
                ["AutoBreakBuggyButton"] = (Toggles + "SRV-Handbrake_ON", Toggles + "SRV-Handbrake_OFF"),
                ["ToggleBuggyTurretButton"] = (Toggles + "SRV-Turret_ON", Toggles + "SRV-Turret_OFF"),
                ["DeployHardpointToggle"] = (Toggles + "Hard-Points_DEPL", Toggles + "Hard-Points_RETR"),
                ["ToggleFlightAssist"] = (Toggles + "Flight-Assist_ON", Toggles + "Flight-Assist_OFF"),
                ["ToggleButtonUpInput"] = (Toggles + "Silent-Running_ON", Toggles + "Silent-Running_OFF"),
                ["Supercruise"] = (Toggles + "Supercruise_ON", Toggles + "Supercruise_OFF"),
                ["GalaxyMap"] = (Toggles + "Galaxy-Map_ON", Toggles + "Galaxy-Map_OFF"),
                ["SystemMap"] = (Toggles + "System-Map_ON", Toggles + "System-Map_OFF"),
                ["FocusCommsPanel"] = (Toggles + "Comms-Panel_ON", Toggles + "Comms-Panel_OFF"),
                ["FocusLeftPanel"] = (Toggles + "Nav-Panel_ON", Toggles + "Nav-Panel_OFF"),
                ["FocusRadarPanel"] = (Toggles + "Role-Panel_ON", Toggles + "Role-Panel_OFF"),
                ["FocusRightPanel"] = (Toggles + "Systems-Panel_ON", Toggles + "Systems-Panel_OFF"),
                // No community art for the suit tool - the plugin's own pair still fits.
                ["HumanoidSwitchToSuitTool"] = ("genetic sampler 2", "genetic sampler off")
            };

        /// <summary>Alarm artwork: normal state and the triggered warning state.</summary>
        private static readonly Dictionary<string, (string Primary, string Secondary)> AlarmByFunction =
            new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase)
            {
                ["SelectHighestThreat"] = (Alarms + "Highest-Threat", Alarms + "Highest-Threat_WARN"),
                ["DeployHeatsink"] = (Alarms + "Heat-Sink", Alarms + "Heat-Sink_WARN"),
                ["DeployChaff"] = (Alarms + "Chaff", Alarms + "Chaff_WARN"),
                ["DeployShieldCell"] = (Alarms + "Shield-Cell", Alarms + "Shield-Cell_WARN")
            };

        /// <summary>Limpet artwork: available and unavailable.</summary>
        private static readonly Dictionary<string, (string Primary, string Secondary)> LimpetByFunction =
            new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase)
            {
                ["PRIMARY"] = (Limpets + "Collector-Limpet", Limpets + "Collector-Limpet_DISBL"),
                ["SECONDARY"] = (Limpets + "Prospector-Limpet", Limpets + "Prospector-Limpet_DISBL")
            };

        private const string Counter = "Community/Andechs75/Countermeasures/";
        private const string Target = "Community/Andechs75/Target/";
        private const string Fighter = "Community/Andechs75/Fighter/";
        private const string Mode = "Community/Andechs75/Mode/";
        private const string Ship = "Community/Andechs75/Ship/";
        private const string Fss = "Community/Andechs75/FSS/";

        /// <summary>
        /// Static / Repeating Static artwork, chosen by the selected function. These buttons only
        /// ever show one image, so the map is function to icon rather than an on/off pair.
        /// Anything not listed falls back to the plain Static plate, with the auto title on top.
        /// </summary>
        private static readonly Dictionary<string, string> StaticByFunction =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                // Countermeasures
                ["ChargeECM"] = Counter + "ECM",
                ["FireChaffLauncher"] = Counter + "CHAFF",
                ["DeployHeatSink"] = Counter + "HEAT_SINK",
                ["DeployHeatsink"] = Counter + "HEAT_SINK",
                ["UseShieldCell"] = Counter + "SHIELD_CELL",

                // Targeting
                ["CycleNextHostileTarget"] = Target + "TARGET_HOSTILE_NEXT",
                ["CyclePreviousHostileTarget"] = Target + "TARGET_HOSTILE_PREV",
                ["CycleNextSubsystem"] = Target + "TARGET_SUB_NEXT",
                ["CyclePreviousSubsystem"] = Target + "TARGET_SUB_PREV",
                ["CycleNextTarget"] = Target + "TARGET_CONTACT_NEXT",
                ["CyclePreviousTarget"] = Target + "TARGET_CONTACT_PREV",
                ["SelectTarget"] = Target + "TARGET_NEXT",
                ["SelectTarget_Buggy"] = Target + "TARGET_NEXT",
                ["SelectHighestThreat"] = Target + "TARGET_THREAT",
                ["TargetNextRouteSystem"] = Target + "TARGET_ROUTE",
                ["SelectTargetsTarget"] = Target + "WING_TARGET",
                ["WingNavLock"] = Target + "WING_NAVLOCK",
                ["TargetWingman0"] = Target + "WING_1",
                ["TargetWingman1"] = Target + "WING_2",
                ["TargetWingman2"] = Target + "WING_3",

                // Fighter / crew orders
                ["OpenOrders"] = Mode + "PANEL_ORDER",
                ["OrderAggressiveBehaviour"] = Fighter + "ATTACK",
                ["OrderDefensiveBehaviour"] = Fighter + "DEFEND",
                ["OrderFocusTarget"] = Fighter + "ENGAGE",
                ["OrderFollow"] = Fighter + "FOLLOW",
                ["OrderHoldFire"] = Fighter + "HOLD",
                ["OrderHoldPosition"] = Fighter + "FORMAT",
                ["OrderRequestDock"] = Fighter + "RECALL",

                // Panels - the Toggle set's panel art matches the rest of the plugin
                ["FocusCommsPanel"] = Toggles + "Comms-Panel_ON",
                ["FocusCommsPanel-ON"] = Toggles + "Comms-Panel_ON",
                ["FocusCommsPanel_Humanoid"] = Toggles + "Comms-Panel_ON",
                ["QuickCommsPanel"] = Mode + "PANEL_COM",
                ["QuickCommsPanel_Humanoid"] = Mode + "PANEL_COM",
                ["FocusLeftPanel"] = Toggles + "Nav-Panel_ON",
                ["FocusLeftPanel-ON"] = Toggles + "Nav-Panel_ON",
                ["FocusRadarPanel"] = Toggles + "Role-Panel_ON",
                ["FocusRadarPanel-ON"] = Toggles + "Role-Panel_ON",
                ["FocusRightPanel"] = Toggles + "Systems-Panel_ON",
                ["FocusRightPanel-ON"] = Toggles + "Systems-Panel_ON",

                // Fire groups
                ["CycleFireGroupNext"] = Mode + "PANEL_GROUP",
                ["CycleFireGroupPrevious"] = Mode + "PANEL_GROUP",

                // Flight
                ["Supercruise"] = Ship + "CRUISE_on",
                ["Hyperspace"] = Ship + "JUMP_on",
                ["RecallDismissShip"] = Ship + "SHIP_CALL",
                ["ToggleCargoScoop"] = Ship + "CARGO_SCOOP_on",
                ["ToggleCargoScoop-ON"] = Ship + "CARGO_SCOOP_on",
                ["ToggleCargoScoop-OFF"] = Ship + "CARGO_SCOOP",
                ["ToggleFlightAssist"] = Ship + "FLIGHT_on",
                ["ToggleFlightAssist-ON"] = Ship + "FLIGHT_on",
                ["ToggleFlightAssist-OFF"] = Ship + "FLIGHT",
                ["DeployHardpointToggle"] = Ship + "HARDPOINTS_on",
                ["DeployHardpointToggle-ON"] = Ship + "HARDPOINTS_on",
                ["DeployHardpointToggle-OFF"] = Ship + "HARDPOINTS",
                ["LandingGearToggle"] = Ship + "GEAR_on",
                ["LandingGearToggle-ON"] = Ship + "GEAR_on",
                ["LandingGearToggle-OFF"] = Ship + "GEAR",
                ["ShipSpotLightToggle"] = Ship + "LIGHT_on",
                ["ShipSpotLightToggle-ON"] = Ship + "LIGHT_on",
                ["ShipSpotLightToggle-OFF"] = Ship + "LIGHT",
                ["HumanoidToggleFlashlightButton"] = Ship + "LIGHT_on",
                ["NightVisionToggle"] = Ship + "NIGHT_VISION_on",
                ["NightVisionToggle-ON"] = Ship + "NIGHT_VISION_on",
                ["NightVisionToggle-OFF"] = Ship + "NIGHT_VISION",
                ["HumanoidToggleNightVisionButton"] = Ship + "NIGHT_VISION_on",
                ["ToggleButtonUpInput"] = Ship + "SILENT_on",
                ["ToggleButtonUpInput-ON"] = Ship + "SILENT_on",
                ["ToggleButtonUpInput-OFF"] = Ship + "SILENT",

                // Modes and maps
                ["PlayerHUDModeToggle"] = Mode + "ANALYSIS",
                ["PlayerHUDModeToggle-AM"] = Mode + "ANALYSIS",
                ["PlayerHUDModeToggle-CM"] = Mode + "MODE_MENU",
                ["GalaxyMapOpen-ON"] = Mode + "MODE_GAL_on",
                ["GalaxyMapOpen_Humanoid"] = Mode + "MODE_GAL_on",
                ["SystemMapOpen-ON"] = Mode + "MODE_SYS_on",
                ["SystemMapOpen_Humanoid"] = Mode + "MODE_SYS_on",
                ["OrbitLinesToggle"] = Mode + "MODE_SYS_orrery",
                ["Pause"] = Mode + "MODE_MENU",

                // Exploration
                ["ExplorationFSSTarget"] = Fss + "SELECT",
                ["ExplorationFSSDiscoveryScan"] = Fss + "SCAN",
                ["ExplorationFSSZoomIn"] = Fss + "ZOOM_IN",
                ["ExplorationFSSZoomOut"] = Fss + "ZOOM_OUT",

                // On foot
                ["HumanoidSwitchToSuitTool"] = "genetic sampler 2"
            };

        /// <summary>Power pip artwork, one image per capacitor.</summary>
        private static readonly Dictionary<string, string> PowerByFunction =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["SYS"] = Pips + "SYS_INFO",
                ["ENG"] = Pips + "ENG_INFO",
                ["WEP"] = Pips + "WEP_INFO",
                ["RST"] = Pips + "Reset-Power"
            };

        /// <summary>Absolute path of a bundled icon, or empty when it is not shipped.</summary>
        public static string Resolve(string iconName)
        {
            if (string.IsNullOrWhiteSpace(iconName)) return string.Empty;

            var path = Path.GetFullPath(Path.Combine(OptionalDirectory,
                iconName.Replace('/', Path.DirectorySeparatorChar) + ".png"));

            return File.Exists(path) ? path : string.Empty;
        }

        /// <summary>
        /// True when a saved path is one of ours, and so may be replaced by a newer default.
        /// </summary>
        private static bool IsOurs(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;

            try
            {
                return Path.GetFullPath(path)
                    .StartsWith(OptionalDirectory, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Colour plate for a slot we have no artwork for, picked from what the slot name implies.
        /// </summary>
        private static string GenericFor(string jsonName)
        {
            var n = jsonName.ToLowerInvariant();

            if (n.Contains("warning")) return GenericWarning;

            if (n.Contains("danger") || n.Contains("damage") || n.Contains("attack") ||
                n.Contains("breach") || n.Contains("shutdown") || n.Contains("destruct") ||
                n.Contains("denied") || n.Contains("interdict") || n.Contains("shieldsdown") ||
                n.Contains("lowfuel"))
            {
                return GenericDanger;
            }

            if (n.Contains("inactive") || n.Contains("notactive") || n.Contains("default") ||
                n.Contains("norotue") || n.Contains("noroute") || n.Contains("off"))
            {
                return GenericOff;
            }

            if (n.Contains("background")) return GenericNeutral;

            return GenericOn;
        }

        /// <summary>
        /// Fills every image slot on the given settings object that is empty or still holds one of
        /// our defaults.
        /// </summary>
        /// <param name="settings">The button's settings instance, modified in place.</param>
        /// <param name="actionKey">Short action name, e.g. "shipstatus" or "toggle".</param>
        /// <param name="function">Selected function, for actions whose artwork depends on it.</param>
        /// <returns>True when something changed and the settings need saving.</returns>
        public static bool Apply(object settings, string actionKey, string function = null)
        {
            if (settings == null) return false;

            try
            {
                ByAction.TryGetValue(actionKey, out var explicitMap);

                var byFunction = FunctionImages(actionKey, function);

                var changed = false;

                foreach (var property in settings.GetType()
                             .GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (property.PropertyType != typeof(string) || !property.CanWrite) continue;

                    // Sounds share the FilenameProperty marker, so go by the slot name.
                    var jsonName = property.GetCustomAttribute<JsonPropertyAttribute>()?.PropertyName;

                    if (string.IsNullOrEmpty(jsonName) ||
                        !jsonName.EndsWith("Image", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string iconName = null;

                    if (byFunction == null || !byFunction.TryGetValue(jsonName, out iconName))
                    {
                        if (explicitMap == null || !explicitMap.TryGetValue(jsonName, out iconName))
                        {
                            iconName = GenericFor(jsonName);
                        }
                    }

                    var current = (string)property.GetValue(settings);

                    // Leave a path the user picked themselves alone.
                    if (!string.IsNullOrWhiteSpace(current) && !IsOurs(current)) continue;

                    var resolved = Resolve(iconName);

                    if (string.IsNullOrEmpty(resolved) || resolved == current) continue;

                    property.SetValue(settings, resolved);

                    changed = true;
                }

                return changed;
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.ERROR, $"DefaultImages.Apply {actionKey}: {ex}");

                return false;
            }
        }

        /// <summary>
        /// Artwork for actions that pick it from the selected function. Returning null falls
        /// through to the explicit table and then the generic plates, so a function with no art
        /// still replaces the previous function's images rather than leaving them stale.
        /// </summary>
        private static Dictionary<string, string> FunctionImages(string actionKey, string function)
        {
            if (string.IsNullOrEmpty(function)) return null;

            (string Primary, string Secondary) pair;

            switch (actionKey.ToLowerInvariant())
            {
                case "toggle":
                    if (!ToggleByFunction.TryGetValue(function, out pair)) return null;
                    break;

                case "alarm":
                    if (!AlarmByFunction.TryGetValue(function, out pair)) return null;
                    break;

                case "limpet":
                    if (!LimpetByFunction.TryGetValue(function, out pair))
                    {
                        pair = (Limpets + "Limpets", Limpets + "Limpets_DISBL");
                    }
                    break;

                case "power":
                    // One image per capacitor; the three slots are pip states of the same one.
                    if (!PowerByFunction.TryGetValue(function, out var pip)) return null;

                    return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["primaryImage"] = pip,
                        ["secondaryImage"] = pip,
                        ["tertiaryImage"] = pip
                    };

                case "static":
                case "repeatingstatic":
                    // One image, not a pair. Unmapped functions fall through to the plain plate.
                    if (!StaticByFunction.TryGetValue(function, out var icon))
                    {
                        if (function.StartsWith("FireGroup-", StringComparison.OrdinalIgnoreCase) &&
                            function.Length == 11)
                        {
                            icon = Groups + "Group-" + char.ToUpperInvariant(function[10]) + "_ON";
                        }
                        else
                        {
                            return null;
                        }
                    }

                    return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["image"] = icon
                    };

                case "firegroup":
                    // The function here is the fire group index, 0-7 for A-H.
                    if (!int.TryParse(function, out var index) || index < 0 || index > 7) return null;

                    var group = Groups + "Group-" + (char)('A' + index);

                    return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["primaryImage"] = group + "_ON",
                        ["secondaryImage"] = group + "_OFF",
                        ["tertiaryImage"] = group + "_DISBL"
                    };

                default:
                    return null;
            }

            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["primaryImage"] = pair.Primary,
                ["secondaryImage"] = pair.Secondary
            };
        }
    }
}
