using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using BarRaider.SdTools;
using EliteJournalReader;
using Newtonsoft.Json.Linq;

// ReSharper disable StringLiteralTypo

namespace Elite
{
    /// <summary>
    /// Journal-driven state behind the info board keys: missions, fleet carrier, colonisation
    /// depot, docking, ship target and fuel scooping. Everything is read from the raw journal
    /// JSON so the boards don't depend on the typed event classes keeping up with the game.
    ///
    /// Missions, carrier and depot state outlive a game session, so on startup the newest
    /// journals are replayed (<see cref="Backfill"/>). Each tracker keeps the timestamp of the
    /// last event it applied and ignores anything older, which makes the live watcher's own
    /// replay of the current session file harmless.
    /// </summary>
    public static class InfoBoardData
    {
        private static readonly object Sync = new object();

        // ── Missions ────────────────────────────────────────────────────────────
        public class MissionInfo
        {
            public long Id { get; set; }
            public string Name { get; set; }
            public string Destination { get; set; }
            public DateTime? Expiry { get; set; }
        }

        private static readonly Dictionary<long, MissionInfo> Missions = new Dictionary<long, MissionInfo>();
        private static DateTime _missionsTime = DateTime.MinValue;
        public static bool MissionsKnown { get; private set; }

        // ── Fleet carrier ───────────────────────────────────────────────────────
        public class CarrierInfo
        {
            public string Callsign { get; set; }
            public string Name { get; set; }
            public int? Fuel { get; set; }
            public long? Balance { get; set; }
            public string Location { get; set; }
            public string JumpSystem { get; set; }
            public string JumpBody { get; set; }
            public DateTime? Departure { get; set; }
        }

        private static CarrierInfo _carrier;
        private static DateTime _carrierTime = DateTime.MinValue;

        // ── Colonisation depot ─────────────────────────────────────────────────
        public class DepotResource
        {
            public string Name { get; set; }
            public int Required { get; set; }
            public int Provided { get; set; }
            public int Remaining => Math.Max(0, Required - Provided);
        }

        public class DepotInfo
        {
            public long MarketId { get; set; }
            public string StationName { get; set; }
            public double Progress { get; set; }
            public bool Complete { get; set; }
            public bool Failed { get; set; }
            public List<DepotResource> Resources { get; set; } = new List<DepotResource>();
        }

        private static DepotInfo _depot;
        private static DateTime _depotTime = DateTime.MinValue;
        private static readonly Dictionary<long, string> StationNames = new Dictionary<long, string>();

        // ── Docking ─────────────────────────────────────────────────────────────
        public enum DockingState
        {
            None,
            Requested,
            Granted,
            Denied,
            Timeout,
            Cancelled,
            Docked
        }

        public class DockingInfo
        {
            public DockingState State { get; set; }
            public string Station { get; set; }
            public int? Pad { get; set; }
            public string Reason { get; set; }
            public DateTime Time { get; set; }
        }

        private static DockingInfo _docking = new DockingInfo();

        // ── Target ──────────────────────────────────────────────────────────────
        public class TargetInfo
        {
            public string Ship { get; set; }
            public int ScanStage { get; set; }
            public string Pilot { get; set; }
            public string Rank { get; set; }
            public double? Shield { get; set; }
            public double? Hull { get; set; }
            public string Legal { get; set; }
            public long? Bounty { get; set; }
            public string Faction { get; set; }
            public string Subsystem { get; set; }
            public double? SubsystemHealth { get; set; }
            public bool Wanted => Legal != null &&
                                  (Legal.Equals("Wanted", StringComparison.OrdinalIgnoreCase) ||
                                   Legal.Equals("WantedEnemy", StringComparison.OrdinalIgnoreCase) ||
                                   Legal.Equals("Enemy", StringComparison.OrdinalIgnoreCase) ||
                                   Legal.Equals("Hunter", StringComparison.OrdinalIgnoreCase) ||
                                   (Bounty ?? 0) > 0);
        }

        private static TargetInfo _target;

        // ── Fuel scoop ──────────────────────────────────────────────────────────
        public static double LastScooped { get; private set; }
        public static DateTime LastScoopTime { get; private set; } = DateTime.MinValue;

        // ── Snapshots for the buttons ──────────────────────────────────────────
        public static List<MissionInfo> ActiveMissions()
        {
            lock (Sync)
            {
                return Missions.Values
                    .Select(m => new MissionInfo { Id = m.Id, Name = m.Name, Destination = m.Destination, Expiry = m.Expiry })
                    .ToList();
            }
        }

        public static CarrierInfo Carrier()
        {
            lock (Sync)
            {
                if (_carrier == null) return null;

                return new CarrierInfo
                {
                    Callsign = _carrier.Callsign,
                    Name = _carrier.Name,
                    Fuel = _carrier.Fuel,
                    Balance = _carrier.Balance,
                    Location = _carrier.Location,
                    JumpSystem = _carrier.JumpSystem,
                    JumpBody = _carrier.JumpBody,
                    Departure = _carrier.Departure
                };
            }
        }

        public static DepotInfo Depot()
        {
            lock (Sync)
            {
                if (_depot == null) return null;

                return new DepotInfo
                {
                    MarketId = _depot.MarketId,
                    StationName = _depot.StationName ??
                                  (StationNames.TryGetValue(_depot.MarketId, out var n) ? n : null),
                    Progress = _depot.Progress,
                    Complete = _depot.Complete,
                    Failed = _depot.Failed,
                    Resources = _depot.Resources.Select(r => new DepotResource
                        { Name = r.Name, Required = r.Required, Provided = r.Provided }).ToList()
                };
            }
        }

        public static DockingInfo Docking()
        {
            lock (Sync)
            {
                return new DockingInfo
                {
                    State = _docking.State,
                    Station = _docking.Station,
                    Pad = _docking.Pad,
                    Reason = _docking.Reason,
                    Time = _docking.Time
                };
            }
        }

        public static TargetInfo Target()
        {
            lock (Sync)
            {
                if (_target == null) return null;

                return new TargetInfo
                {
                    Ship = _target.Ship,
                    ScanStage = _target.ScanStage,
                    Pilot = _target.Pilot,
                    Rank = _target.Rank,
                    Shield = _target.Shield,
                    Hull = _target.Hull,
                    Legal = _target.Legal,
                    Bounty = _target.Bounty,
                    Faction = _target.Faction,
                    Subsystem = _target.Subsystem,
                    SubsystemHealth = _target.SubsystemHealth
                };
            }
        }

        // ── Event handling ─────────────────────────────────────────────────────
        private static readonly HashSet<string> BackfillEvents = new HashSet<string>(StringComparer.Ordinal)
        {
            "Missions", "MissionAccepted", "MissionCompleted", "MissionFailed", "MissionAbandoned",
            "MissionRedirected",
            "CarrierStats", "CarrierJumpRequest", "CarrierJumpCancelled", "CarrierJump", "CarrierFinance",
            "CarrierDepositFuel", "CarrierLocation", "CarrierNameChanged",
            "ColonisationConstructionDepot", "Docked", "DockingGranted"
        };

        public static void HandleEliteEvents(object sender, MessageReceivedEventArgs args)
        {
            try
            {
                var raw = ((JournalEventArgs)args.EventArgs).OriginalEvent;

                if (raw is JObject obj) Apply(obj, live: true);
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.ERROR, $"InfoBoardData: {ex.Message}");
            }
        }

        private static string Str(JObject o, string localised, string plain = null)
        {
            var v = o.Value<string>(localised);

            if (string.IsNullOrEmpty(v) && plain != null) v = o.Value<string>(plain);

            return string.IsNullOrEmpty(v) ? null : v;
        }

        private static DateTime? Time(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null) return null;

            if (token.Type == JTokenType.Date) return token.Value<DateTime>().ToUniversalTime();

            return DateTime.TryParse(token.Value<string>(), CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var t)
                ? t
                : (DateTime?)null;
        }

        /// <summary>Applies one journal event. Live events also drive session-only state.</summary>
        private static void Apply(JObject e, bool live)
        {
            var name = e.Value<string>("event");

            if (string.IsNullOrEmpty(name)) return;

            var ts = Time(e["timestamp"]) ?? DateTime.UtcNow;

            lock (Sync)
            {
                switch (name)
                {
                    // ── Missions ──
                    case "Missions":
                        if (ts < _missionsTime) break;
                        _missionsTime = ts;
                        MissionsKnown = true;

                        var keep = new Dictionary<long, MissionInfo>();
                        foreach (var m in (e["Active"] as JArray) ?? new JArray())
                        {
                            if (!(m is JObject mo)) continue;
                            var id = mo.Value<long?>("MissionID") ?? 0;
                            if (id == 0) continue;

                            Missions.TryGetValue(id, out var known);

                            var expires = mo.Value<double?>("Expires");
                            keep[id] = new MissionInfo
                            {
                                Id = id,
                                Name = known?.Name ?? MissionName(mo.Value<string>("Name")),
                                Destination = known?.Destination,
                                Expiry = expires.HasValue && expires.Value > 0
                                    ? ts.AddSeconds(expires.Value)
                                    : known?.Expiry
                            };
                        }

                        Missions.Clear();
                        foreach (var kv in keep) Missions[kv.Key] = kv.Value;
                        break;

                    case "MissionAccepted":
                    {
                        if (ts < _missionsTime) break;
                        _missionsTime = ts;
                        var id = e.Value<long?>("MissionID") ?? 0;
                        if (id == 0) break;

                        Missions[id] = new MissionInfo
                        {
                            Id = id,
                            Name = Str(e, "LocalisedName") ?? MissionName(e.Value<string>("Name")),
                            Destination = Str(e, "DestinationStation") ?? Str(e, "DestinationSystem"),
                            Expiry = Time(e["Expiry"])
                        };
                        break;
                    }

                    case "MissionRedirected":
                    {
                        if (ts < _missionsTime) break;
                        _missionsTime = ts;
                        var id = e.Value<long?>("MissionID") ?? 0;
                        if (Missions.TryGetValue(id, out var m))
                        {
                            m.Destination = Str(e, "NewDestinationStation") ?? Str(e, "NewDestinationSystem") ?? m.Destination;
                        }
                        break;
                    }

                    case "MissionCompleted":
                    case "MissionFailed":
                    case "MissionAbandoned":
                        if (ts < _missionsTime) break;
                        _missionsTime = ts;
                        Missions.Remove(e.Value<long?>("MissionID") ?? 0);
                        break;

                    // ── Carrier ──
                    case "CarrierStats":
                        if (ts < _carrierTime) break;
                        _carrierTime = ts;
                        _carrier = _carrier ?? new CarrierInfo();
                        _carrier.Callsign = Str(e, "Callsign") ?? _carrier.Callsign;
                        _carrier.Name = Str(e, "Name") ?? _carrier.Name;
                        _carrier.Fuel = e.Value<int?>("FuelLevel") ?? _carrier.Fuel;
                        _carrier.Balance = e["Finance"]?.Value<long?>("CarrierBalance") ?? _carrier.Balance;
                        break;

                    case "CarrierNameChanged":
                        if (ts < _carrierTime) break;
                        _carrierTime = ts;
                        _carrier = _carrier ?? new CarrierInfo();
                        _carrier.Callsign = Str(e, "Callsign") ?? _carrier.Callsign;
                        _carrier.Name = Str(e, "Name") ?? _carrier.Name;
                        break;

                    case "CarrierJumpRequest":
                        if (ts < _carrierTime) break;
                        _carrierTime = ts;
                        _carrier = _carrier ?? new CarrierInfo();
                        _carrier.JumpSystem = Str(e, "SystemName");
                        _carrier.JumpBody = Str(e, "Body");
                        _carrier.Departure = Time(e["DepartureTime"]);
                        break;

                    case "CarrierJumpCancelled":
                        if (ts < _carrierTime || _carrier == null) break;
                        _carrierTime = ts;
                        _carrier.JumpSystem = null;
                        _carrier.JumpBody = null;
                        _carrier.Departure = null;
                        break;

                    case "CarrierJump":
                        if (ts < _carrierTime) break;
                        _carrierTime = ts;
                        _carrier = _carrier ?? new CarrierInfo();
                        _carrier.Location = Str(e, "StarSystem") ?? _carrier.Location;
                        _carrier.JumpSystem = null;
                        _carrier.JumpBody = null;
                        _carrier.Departure = null;
                        break;

                    case "CarrierLocation":
                        if (ts < _carrierTime) break;
                        _carrierTime = ts;
                        _carrier = _carrier ?? new CarrierInfo();
                        _carrier.Location = Str(e, "StarSystem") ?? _carrier.Location;
                        // A location report after the planned departure means the jump happened.
                        if (_carrier.Departure.HasValue && ts > _carrier.Departure.Value.AddMinutes(1))
                        {
                            _carrier.JumpSystem = null;
                            _carrier.JumpBody = null;
                            _carrier.Departure = null;
                        }
                        break;

                    case "CarrierFinance":
                        if (ts < _carrierTime) break;
                        _carrierTime = ts;
                        _carrier = _carrier ?? new CarrierInfo();
                        _carrier.Balance = e.Value<long?>("CarrierBalance") ?? _carrier.Balance;
                        break;

                    case "CarrierDepositFuel":
                        if (ts < _carrierTime) break;
                        _carrierTime = ts;
                        _carrier = _carrier ?? new CarrierInfo();
                        _carrier.Fuel = e.Value<int?>("Total") ?? _carrier.Fuel;
                        break;

                    // ── Colonisation ──
                    case "ColonisationConstructionDepot":
                    {
                        if (ts < _depotTime) break;
                        _depotTime = ts;
                        var marketId = e.Value<long?>("MarketID") ?? 0;

                        _depot = new DepotInfo
                        {
                            MarketId = marketId,
                            StationName = StationNames.TryGetValue(marketId, out var station) ? station : null,
                            Progress = e.Value<double?>("ConstructionProgress") ?? 0,
                            Complete = e.Value<bool?>("ConstructionComplete") ?? false,
                            Failed = e.Value<bool?>("ConstructionFailed") ?? false,
                            Resources = ((e["ResourcesRequired"] as JArray) ?? new JArray())
                                .OfType<JObject>()
                                .Select(r => new DepotResource
                                {
                                    Name = Str(r, "Name_Localised", "Name"),
                                    Required = r.Value<int?>("RequiredAmount") ?? 0,
                                    Provided = r.Value<int?>("ProvidedAmount") ?? 0
                                }).ToList()
                        };
                        break;
                    }

                    // ── Docking ──
                    case "DockingRequested":
                        SetDocking(DockingState.Requested, e, ts);
                        break;

                    case "DockingGranted":
                        RememberStation(e);
                        SetDocking(DockingState.Granted, e, ts);
                        _docking.Pad = e.Value<int?>("LandingPad");
                        break;

                    case "DockingDenied":
                        SetDocking(DockingState.Denied, e, ts);
                        _docking.Reason = Str(e, "Reason");
                        break;

                    case "DockingTimeout":
                        SetDocking(DockingState.Timeout, e, ts);
                        break;

                    case "DockingCancelled":
                        SetDocking(DockingState.Cancelled, e, ts);
                        break;

                    case "Docked":
                        RememberStation(e);
                        if (!live) break;
                        var pad = _docking.State == DockingState.Granted ? _docking.Pad : null;
                        SetDocking(DockingState.Docked, e, ts);
                        _docking.Pad = pad;
                        ClearTarget();
                        break;

                    case "Undocked":
                        _docking = new DockingInfo { Time = ts };
                        break;

                    // ── Target ──
                    case "ShipTargeted":
                        if (!(e.Value<bool?>("TargetLocked") ?? false))
                        {
                            ClearTarget();
                            break;
                        }

                        _target = new TargetInfo
                        {
                            Ship = Str(e, "Ship_Localised", "Ship"),
                            ScanStage = e.Value<int?>("ScanStage") ?? 0,
                            Pilot = Str(e, "PilotName_Localised", "PilotName"),
                            Rank = Str(e, "PilotRank"),
                            Shield = e.Value<double?>("ShieldHealth"),
                            Hull = e.Value<double?>("HullHealth"),
                            Legal = Str(e, "LegalStatus"),
                            Bounty = e.Value<long?>("Bounty"),
                            Faction = Str(e, "Faction"),
                            Subsystem = Str(e, "Subsystem_Localised", "Subsystem"),
                            SubsystemHealth = e.Value<double?>("SubsystemHealth")
                        };
                        break;

                    case "FSDJump":
                    case "SupercruiseEntry":
                    case "Died":
                    case "Shutdown":
                        ClearTarget();
                        if (name == "Shutdown" || name == "Died") _docking = new DockingInfo { Time = ts };
                        break;

                    // ── Fuel scoop ──
                    case "FuelScoop":
                        LastScooped = e.Value<double?>("Scooped") ?? 0;
                        LastScoopTime = ts;
                        break;
                }
            }
        }

        private static void ClearTarget() => _target = null;

        private static void SetDocking(DockingState state, JObject e, DateTime ts)
        {
            _docking = new DockingInfo
            {
                State = state,
                Station = Str(e, "StationName_Localised", "StationName") ?? _docking.Station,
                Pad = _docking.Pad,
                Time = ts
            };
        }

        private static void RememberStation(JObject e)
        {
            var id = e.Value<long?>("MarketID") ?? 0;
            var station = Str(e, "StationName_Localised", "StationName");

            if (id == 0 || station == null) return;

            StationNames[id] = station;

            if (_depot != null && _depot.MarketId == id) _depot.StationName = station;
        }

        /// <summary>"Mission_Courier_Elections" -> "Courier Elections".</summary>
        private static string MissionName(string internalName)
        {
            if (string.IsNullOrEmpty(internalName)) return "Mission";

            var n = internalName.Replace("Mission_", "").Replace("_name", "").Replace('_', ' ').Trim();

            return string.IsNullOrEmpty(n) ? "Mission" : n;
        }

        /// <summary>
        /// Replays recent journals so long-lived state (missions, carrier, depot) is shown before
        /// the game writes anything new. Walks newest first until each tracker has a starting
        /// event, then applies the gathered events in order.
        /// </summary>
        public static void Backfill(string journalPath)
        {
            try
            {
                var files = Directory.GetFiles(journalPath, "Journal.*.log")
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .Take(400)
                    .ToList();

                var gathered = new List<List<JObject>>();
                bool haveMissions = false, haveCarrier = false, haveDepot = false;

                foreach (var file in files)
                {
                    string text;
                    using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (var reader = new StreamReader(fs))
                    {
                        text = reader.ReadToEnd();
                    }

                    var events = new List<JObject>();

                    foreach (var line in text.Split('\n'))
                    {
                        var i = line.IndexOf("\"event\":\"", StringComparison.Ordinal);
                        if (i < 0) continue;
                        var start = i + 9;
                        var end = line.IndexOf('"', start);
                        if (end < 0) continue;
                        var name = line.Substring(start, end - start);

                        if (!BackfillEvents.Contains(name)) continue;

                        try
                        {
                            events.Add(JObject.Parse(line));
                        }
                        catch
                        {
                            // partial line at the end of a live file
                        }

                        if (name == "Missions") haveMissions = true;
                        else if (name == "CarrierStats") haveCarrier = true;
                        else if (name == "ColonisationConstructionDepot") haveDepot = true;
                    }

                    gathered.Add(events);

                    // Missions always come with a login, so a few files settle them; carrier and
                    // depot can be weeks old, so keep going (bounded) until those are found too.
                    if (haveMissions && haveCarrier && haveDepot) break;
                }

                gathered.Reverse();

                foreach (var e in gathered.SelectMany(x => x))
                {
                    Apply(e, live: false);
                }

                Logger.Instance.LogMessage(TracingLevel.INFO,
                    $"InfoBoardData backfill: {gathered.Count} journal(s), missions={Missions.Count} carrier={(_carrier != null)} depot={(_depot != null)}");
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.ERROR, $"InfoBoardData backfill: {ex}");
            }
        }
    }
}
