using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BarRaider.SdTools;

// ReSharper disable StringLiteralTypo

namespace Elite.Buttons
{
    /// <summary>Text helpers for boards that show names of unknown length.</summary>
    internal static class BoardText
    {
        /// <summary>Breaks a long name into at most two lines at the space nearest the middle.</summary>
        public static IEnumerable<string> Wrap(string text, int maxLine = 13)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= maxLine || !text.Contains(' '))
            {
                yield return text;
                yield break;
            }

            var middle = text.Length / 2;
            var best = -1;

            for (var i = 0; i < text.Length; i++)
            {
                if (text[i] == ' ' && (best < 0 || Math.Abs(i - middle) < Math.Abs(best - middle))) best = i;
            }

            yield return text.Substring(0, best).Trim();
            yield return text.Substring(best + 1).Trim();
        }

        public static string Title(string value)
        {
            if (string.IsNullOrEmpty(value)) return value;

            return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(value.Replace('_', ' ').ToLowerInvariant());
        }
    }

    // ── 1. Legal state and credit balance ────────────────────────────────────
    [PluginActionId("com.mhwlng.elite.legalbalance")]
    public class LegalBalance : InfoBoardBase
    {
        private static readonly HashSet<string> Bad = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Wanted", "Hostile", "Warrant", "IllegalCargo", "Speeding", "PassengerWanted"
        };

        public LegalBalance(SDConnection connection, InitialPayload payload) : base(connection, payload) { }

        protected override string ActionKey => "legalbalance";

        protected override BoardContent BuildContent()
        {
            var s = EliteData.StatusData;
            var legal = s.LegalState;
            var knownLegal = !string.IsNullOrEmpty(legal) && legal != "Unknown";

            if (!knownLegal && !s.Balance.HasValue) return BoardContent.Inactive("LEGAL", "--");

            return new BoardContent
            {
                State = knownLegal && Bad.Contains(legal) ? BoardState.Alert : BoardState.Active,
                Lines = new List<string>
                {
                    knownLegal ? Words(legal).ToUpperInvariant() : "--",
                    s.Balance.HasValue ? Credits(s.Balance.Value) : "-- cr"
                }
            };
        }
    }

    // ── 3. Destination ───────────────────────────────────────────────────────
    [PluginActionId("com.mhwlng.elite.destination")]
    public class DestinationBoard : InfoBoardBase
    {
        public DestinationBoard(SDConnection connection, InitialPayload payload) : base(connection, payload) { }

        protected override string ActionKey => "destination";

        protected override BoardContent BuildContent()
        {
            var name = EliteData.StatusData.DestinationDisplayName;

            if (string.IsNullOrEmpty(name)) return BoardContent.Inactive("NO", "DESTINATION");

            var lines = new List<string> { "DEST" };
            lines.AddRange(BoardText.Wrap(name));

            return new BoardContent { State = BoardState.Active, Lines = lines };
        }
    }

    // ── 4. Suit vitals ───────────────────────────────────────────────────────
    [PluginActionId("com.mhwlng.elite.suitvitals")]
    public class SuitVitals : InfoBoardBase
    {
        public SuitVitals(SDConnection connection, InitialPayload payload) : base(connection, payload) { }

        protected override string ActionKey => "suitvitals";

        protected override BoardContent BuildContent()
        {
            var s = EliteData.StatusData;

            if (!s.OnFoot) return BoardContent.Inactive("SUIT", "offline");

            var lines = new List<string>
            {
                $"O2 {s.Oxygen * 100:0}%",
                $"HP {s.Health * 100:0}%"
            };

            var env = new List<string>();
            if (s.Temperature > 0) env.Add($"{s.Temperature - 273.15:0}°C");
            if (s.Gravity > 0) env.Add($"{s.Gravity:0.00}g");
            if (env.Count > 0) lines.Add(string.Join("  ", env));

            var alert = s.LowOxygen || s.LowHealth || s.VeryCold || s.VeryHot;

            // Put the problem first so it gets the headline size.
            if (s.LowHealth && !s.LowOxygen) lines.Reverse(0, 2);

            return new BoardContent { State = alert ? BoardState.Alert : BoardState.Active, Lines = lines };
        }
    }

    // ── 9. Missions ──────────────────────────────────────────────────────────
    [PluginActionId("com.mhwlng.elite.missions")]
    public class MissionsBoard : InfoBoardBase
    {
        public MissionsBoard(SDConnection connection, InitialPayload payload) : base(connection, payload) { }

        protected override string ActionKey => "missions";

        protected override BoardContent BuildContent()
        {
            var missions = InfoBoardData.ActiveMissions();

            if (missions.Count == 0)
            {
                return BoardContent.Inactive(InfoBoardData.MissionsKnown ? "0" : "--", "MISSIONS");
            }

            var now = DateTime.UtcNow;
            var next = missions.Where(m => m.Expiry.HasValue).OrderBy(m => m.Expiry.Value).FirstOrDefault();

            // Option: hours left at which the board turns to Alert. Default one hour.
            var alertHours = OptionNumber(settings.Option) ?? 1.0;

            var lines = new List<string> { $"{missions.Count} MISSION{(missions.Count == 1 ? "" : "S")}" };
            var alert = false;

            if (next != null)
            {
                var left = next.Expiry.Value - now;

                lines.Add(next.Name ?? "Mission");
                lines.Add(left.TotalSeconds <= 0 ? "EXPIRED" : Duration(left) + " left");

                alert = left.TotalHours < alertHours;
            }
            else
            {
                lines.Add(missions[0].Name ?? "Mission");
            }

            return new BoardContent { State = alert ? BoardState.Alert : BoardState.Active, Lines = lines };
        }
    }

    // ── 12. Fleet carrier ────────────────────────────────────────────────────
    [PluginActionId("com.mhwlng.elite.carrier")]
    public class CarrierBoard : InfoBoardBase
    {
        public CarrierBoard(SDConnection connection, InitialPayload payload) : base(connection, payload) { }

        protected override string ActionKey => "carrier";

        protected override BoardContent BuildContent()
        {
            var c = InfoBoardData.Carrier();

            if (c == null) return BoardContent.Inactive("CARRIER", "no data");

            var lines = new List<string> { c.Callsign ?? c.Name ?? "CARRIER" };
            var alert = false;

            if (c.Departure.HasValue)
            {
                var left = c.Departure.Value - DateTime.UtcNow;

                lines.Add("> " + (c.JumpBody ?? c.JumpSystem ?? "jump"));
                lines.Add(left.TotalSeconds > 0 ? "in " + Duration(left) : "jumping");

                // Lockdown starts about 3m20s before departure.
                alert = left.TotalSeconds > -300 && left.TotalSeconds < 200;
            }
            else
            {
                lines.Add(c.Location ?? "idle");
            }

            if (c.Fuel.HasValue)
            {
                lines.Add($"Tritium {c.Fuel}t");

                // Option: tritium level at which to warn. Default 135t, roughly a full-range jump.
                var low = OptionNumber(settings.Option) ?? 135;
                if (c.Fuel.Value < low) alert = true;
            }

            if (c.Balance.HasValue) lines.Add(Credits(c.Balance.Value));

            return new BoardContent { State = alert ? BoardState.Alert : BoardState.Active, Lines = lines };
        }
    }

    // ── 13. Colonisation depot ───────────────────────────────────────────────
    [PluginActionId("com.mhwlng.elite.colonisationdepot")]
    public class ColonisationDepot : InfoBoardBase
    {
        public ColonisationDepot(SDConnection connection, InitialPayload payload) : base(connection, payload) { }

        protected override string ActionKey => "colonisationdepot";

        protected override BoardContent BuildContent()
        {
            var d = InfoBoardData.Depot();

            if (d == null) return BoardContent.Inactive("DEPOT", "no data");

            if (d.Failed)
            {
                return new BoardContent
                {
                    State = BoardState.Alert,
                    Lines = new List<string> { "FAILED", d.StationName ?? "Depot" },
                    Progress = d.Progress
                };
            }

            var lines = new List<string>
            {
                d.Complete ? "DONE" : $"{d.Progress * 100:0.#}%",
                d.StationName ?? "Depot"
            };

            if (!d.Complete)
            {
                var remaining = d.Resources.Where(r => r.Remaining > 0).OrderByDescending(r => r.Remaining).ToList();

                if (remaining.Count > 0)
                {
                    lines.Add($"{remaining[0].Name} {remaining[0].Remaining:N0}t");
                    lines.Add($"{remaining.Sum(r => r.Remaining):N0}t / {remaining.Count} left");
                }
            }

            return new BoardContent { State = BoardState.Active, Lines = lines, Progress = d.Progress };
        }
    }

    // ── 15. Docking ──────────────────────────────────────────────────────────
    [PluginActionId("com.mhwlng.elite.docking")]
    public class DockingBoard : InfoBoardBase
    {
        public DockingBoard(SDConnection connection, InitialPayload payload) : base(connection, payload) { }

        protected override string ActionKey => "docking";

        protected override BoardContent BuildContent()
        {
            var d = InfoBoardData.Docking();
            var age = DateTime.UtcNow - d.Time;
            var station = d.Station ?? "";

            switch (d.State)
            {
                case InfoBoardData.DockingState.Requested:
                    return Active("REQUESTED", station);

                case InfoBoardData.DockingState.Granted:
                    return Active(d.Pad.HasValue ? $"PAD {d.Pad}" : "GRANTED", station);

                case InfoBoardData.DockingState.Denied when age.TotalMinutes < 5:
                    return new BoardContent
                    {
                        State = BoardState.Alert,
                        Lines = new List<string> { "DENIED", Words(d.Reason) ?? "", station }
                    };

                case InfoBoardData.DockingState.Timeout when age.TotalMinutes < 5:
                    return new BoardContent
                    {
                        State = BoardState.Alert,
                        Lines = new List<string> { "TIMED OUT", station }
                    };

                case InfoBoardData.DockingState.Docked when EliteData.StatusData.Docked:
                    return Active("DOCKED", station);
            }

            return BoardContent.Inactive("DOCKING", "--");
        }

        private static BoardContent Active(string headline, string station)
        {
            var lines = new List<string> { headline };
            lines.AddRange(BoardText.Wrap(station));

            return new BoardContent { State = BoardState.Active, Lines = lines };
        }
    }

    // ── 16. Target info ──────────────────────────────────────────────────────
    [PluginActionId("com.mhwlng.elite.targetinfo")]
    public class TargetInfoBoard : InfoBoardBase
    {
        public TargetInfoBoard(SDConnection connection, InitialPayload payload) : base(connection, payload) { }

        protected override string ActionKey => "targetinfo";

        protected override BoardContent BuildContent()
        {
            var t = InfoBoardData.Target();

            if (t == null) return BoardContent.Inactive("NO", "TARGET");

            var lines = new List<string> { BoardText.Title(t.Ship) ?? "Contact" };

            if (t.ScanStage >= 1 && !string.IsNullOrEmpty(t.Rank)) lines.Add(t.Rank);

            if (t.Shield.HasValue || t.Hull.HasValue)
            {
                lines.Add($"S {t.Shield ?? 0:0}%  H {t.Hull ?? 0:0}%");
            }

            if (!string.IsNullOrEmpty(t.Subsystem) && t.SubsystemHealth.HasValue)
            {
                lines.Add($"{t.Subsystem} {t.SubsystemHealth:0}%");
            }

            if ((t.Bounty ?? 0) > 0) lines.Add("Bounty " + Credits(t.Bounty.Value));
            else if (!string.IsNullOrEmpty(t.Legal)) lines.Add(Words(t.Legal));

            return new BoardContent { State = t.Wanted ? BoardState.Alert : BoardState.Active, Lines = lines };
        }
    }

    // ── 17. Fuel scoop ───────────────────────────────────────────────────────
    [PluginActionId("com.mhwlng.elite.fuelscoop")]
    public class FuelScoopBoard : InfoBoardBase
    {
        // Fuel samples while scooping, for the scoop rate. Status.json updates a few times a
        // second while scooping, and the tick reads it once a second.
        private readonly Queue<(DateTime Time, double Fuel)> _samples = new Queue<(DateTime, double)>();

        public FuelScoopBoard(SDConnection connection, InitialPayload payload) : base(connection, payload) { }

        protected override string ActionKey => "fuelscoop";

        protected override BoardContent BuildContent()
        {
            var s = EliteData.StatusData;
            var fuel = s.Fuel?.FuelMain ?? 0;
            var capacity = EliteData.FuelCapacityMain;

            if (s.OnFoot || s.InSRV || (fuel <= 0 && capacity <= 0)) return BoardContent.Inactive("FUEL", "--");

            var tank = capacity > 0 ? $"{fuel:0.0}/{capacity:0}t" : $"{fuel:0.0}t";
            double? fraction = capacity > 0 ? fuel / capacity : (double?)null;

            lock (_samples)
            {
                if (!s.ScoopingFuel)
                {
                    _samples.Clear();
                }
                else
                {
                    var now = DateTime.UtcNow;
                    _samples.Enqueue((now, fuel));
                    while (_samples.Count > 2 && (now - _samples.Peek().Time).TotalSeconds > 8) _samples.Dequeue();
                }

                if (s.ScoopingFuel)
                {
                    var lines = new List<string> { "SCOOPING", tank };

                    var first = _samples.Peek();
                    var last = _samples.Last();
                    var seconds = (last.Time - first.Time).TotalSeconds;

                    if (seconds >= 1.5 && last.Fuel > first.Fuel)
                    {
                        var rate = (last.Fuel - first.Fuel) / seconds;
                        lines.Add($"{rate:0.00} t/s");

                        if (capacity > 0)
                        {
                            lines.Add("full " + Duration(TimeSpan.FromSeconds((capacity - fuel) / rate)));
                        }
                    }
                    else if (InfoBoardData.LastScooped > 0 &&
                             (DateTime.UtcNow - InfoBoardData.LastScoopTime).TotalMinutes < 2)
                    {
                        lines.Add($"+{InfoBoardData.LastScooped:0.0}t");
                    }

                    return new BoardContent { State = BoardState.Active, Lines = lines, Progress = fraction };
                }
            }

            var recent = InfoBoardData.LastScooped > 0 &&
                         (DateTime.UtcNow - InfoBoardData.LastScoopTime).TotalMinutes < 2;

            return new BoardContent
            {
                State = s.LowFuel ? BoardState.Alert : BoardState.Inactive,
                Lines = recent
                    ? new List<string> { "FUEL", tank, $"+{InfoBoardData.LastScooped:0.0}t" }
                    : new List<string> { "FUEL", tank },
                Progress = fraction
            };
        }
    }
}
