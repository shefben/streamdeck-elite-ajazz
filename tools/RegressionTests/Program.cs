using System.Reflection;
using System.Runtime.CompilerServices;
using Elite;
using EliteJournalReader;
using Newtonsoft.Json;

var failures = 0;
var directory = Path.Combine(Path.GetTempPath(), "elite-regression-" + Guid.NewGuid());
Directory.CreateDirectory(directory);
void Test(string name, Action action)
{
    try { action(); Console.WriteLine("PASS " + name); }
    catch (Exception ex) { failures++; Console.WriteLine("FAIL " + name + ": " + ex.GetBaseException().Message); }
}
void Check(bool value, string message) { if (!value) throw new Exception(message); }
string Resolve(string preset)
{
    var type = typeof(UserBindings).Assembly.GetType("Elite.BindingResolver") ?? throw new Exception("No version-independent binding resolver");
    return (string)type.GetMethod("Find", BindingFlags.Static | BindingFlags.Public).Invoke(null, [directory, preset]);
}
void Bind(string file, string preset, int minor) => File.WriteAllText(Path.Combine(directory, file),
    $"<Root PresetName=\"{preset}\" MajorVersion=\"4\" MinorVersion=\"{minor}\"><KeyboardLayout>en-US</KeyboardLayout></Root>");
Test("Newest version and internal preset name win over stale filename", () =>
{
    Bind("Pilot.4.2.binds", "Pilot", 2); Bind("Custom.4.14.binds", "Pilot", 14);
    Check(Resolve("Pilot") == Path.Combine(directory, "Custom.4.14.binds"), "Selected stale bindings");
});
Test("Versions sort numerically and unrelated presets are excluded", () =>
{
    Bind("Future.4.9.binds", "Future", 9); Bind("Future.4.10.binds", "Future", 10);
    Bind("Other.4.99.binds", "Other", 99);
    Check(Resolve("Future") == Path.Combine(directory, "Future.4.10.binds"), "Incorrect version ordering");
});
Test("Malformed bindings do not hide a valid preset", () =>
{
    File.WriteAllText(Path.Combine(directory, "Broken.binds"), "<Root");
    Check(Resolve("Future") != null, "Malformed file blocked resolution");
});
Test("Active preset marker follows future major versions", () =>
{
    var old = Path.Combine(directory, "StartPreset.4.start");
    var current = Path.Combine(directory, "StartPreset.12.start");
    File.WriteAllText(old, "Pilot"); File.WriteAllText(current, "Future");
    File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-1));
    var type = typeof(UserBindings).Assembly.GetType("Elite.BindingResolver");
    Check((string)type.GetMethod("FindStartPreset").Invoke(null, [directory]) == current, "Selected old marker");
});
Test("Preset marker tolerates CRLF and a trailing newline", () =>
{
    var path = Path.Combine(directory, "StartPreset.12.start");
    File.WriteAllText(path, "General\r\nShip\r\nSRV\r\nFoot\r\n");
    var program = typeof(UserBindings).Assembly.GetType("Elite.Program");
    var names = (string[])program.GetMethod("ReadStartPreset").Invoke(null, [path]);
    Check(names.SequenceEqual(new[] { "General", "Ship", "SRV", "Foot" }), "Wrong preset categories");
});
Test("New binding versions trigger a reload without restarting the plugin", () =>
{
    using var signal = new ManualResetEventSlim();
    using var watcher = new KeyBindingWatcher(directory, "*.binds");
    watcher.KeyBindingUpdated += (_, _) => signal.Set(); watcher.StartWatching();
    Bind("Future.5.0.binds", "Future", 0);
    Check(signal.Wait(3000), "No notification for newly created version");
    watcher.StopWatching();
});
Test("Status changes within one second and after clock reset are delivered", () =>
{
    using var watcher = new TestStatusWatcher();
    var path = Path.Combine(directory, "Status.json");
    int count = 0; watcher.StatusUpdated += (_, _) => count++;
    foreach (var (time, pips) in new[] { ("2026-11-14T01:00:00Z", "[4,4,4]"), ("2026-11-14T01:00:00Z", "[2,8,2]"), ("2026-11-13T01:00:00Z", "[8,2,2]") })
    {
        File.WriteAllText(path, $"{{\"timestamp\":\"{time}\",\"event\":\"Status\",\"Flags\":8,\"Pips\":{pips}}}"); watcher.Read(path);
    }
    Check(count == 3 && watcher.LastEvent.Pips.System == 8, "Dropped changed telemetry");
    watcher.Read(path); Check(count == 3, "Duplicate status emitted");
});
Test("Transient invalid status preserves the last known game state", () =>
{
    using var watcher = new TestStatusWatcher(); var path = Path.Combine(directory, "Status.json");
    int count = 0; watcher.StatusUpdated += (_, _) => count++;
    File.WriteAllText(path, "{\"timestamp\":\"2026-11-14T01:00:00Z\",\"Flags\":8,\"Pips\":[2,8,2]}"); watcher.Read(path);
    File.WriteAllText(path, "{\"timestamp\":"); watcher.Read(path);
    Check(count == 1 && watcher.LastEvent.Pips.Engine == 8, "Invalid read reset the display");
});
Test("Status watcher follows atomic file replacement and updates real flags and all pips", () =>
{
    var folder = Path.Combine(directory, "watched"); Directory.CreateDirectory(folder);
    var path = Path.Combine(folder, "Status.json");
    File.WriteAllText(path, "{\"timestamp\":\"2026-11-14T01:00:00Z\",\"Flags\":8,\"Pips\":[4,4,4]}");
    using var signal = new ManualResetEventSlim();
    using var watcher = new StatusWatcher(folder);
    watcher.StatusUpdated += (_, evt) => {
        EliteData.HandleStatusEvents(null, evt);
        if (evt.Pips.System == 2) signal.Set();
    };
    watcher.StartWatching();
    File.WriteAllText(path + ".tmp", "{\"timestamp\":\"2026-11-14T01:00:00Z\",\"Flags\":12,\"Pips\":[2,8,2],\"FutureField\":123}");
    File.Move(path + ".tmp", path, true);
    Check(signal.Wait(4000), "Replacement status was not read");
    Check(EliteData.StatusData.LandingGearDown && EliteData.StatusData.Pips.SequenceEqual(new[] {2,8,2}), "State propagation failed");
    watcher.StopWatching();
});
Test("Power press without a keyboard binding cannot invent game pips", () =>
{
    var assembly = typeof(UserBindings).Assembly;
    var program = assembly.GetType("Elite.Program");
    var bindings = (Dictionary<BindingType, UserBindings>)program.GetField("Binding").GetValue(null);
    bindings[BindingType.Ship] = new UserBindings { IncreaseEnginesPower = new StandardBindingInfo {
        Primary = new PrimaryInfo { Device = "{NoDevice}", Key = "" },
        Secondary = new SecondaryInfo { Device = "{NoDevice}", Key = "" } } };
    bindings[BindingType.OnFoot] = new UserBindings { KeyboardLayout = "en-US" };
    var type = assembly.GetType("Elite.Buttons.Power");
    var button = RuntimeHelpers.GetUninitializedObject(type);
    var field = type.GetField("settings", BindingFlags.NonPublic | BindingFlags.Instance);
    field.SetValue(button, JsonConvert.DeserializeObject("{\"function\":\"ENG\"}", field.FieldType));
    EliteData.StatusData.Pips = [4,4,4];
    type.GetMethod("KeyPressed").Invoke(button, [null]);
    Check(EliteData.StatusData.Pips.SequenceEqual(new[] {4,4,4}), "Button fabricated pip state");
});
Test("Secondary keyboard chords and absent bindings are handled safely", () =>
{
    var type = typeof(UserBindings).Assembly.GetType("Elite.CommandTools");
    var build = type.GetMethod("BuildInputText");
    Check((string)build.Invoke(null, new object[] { null }) == "", "Absent control was not ignored");
    var binding = new StandardBindingInfo { Secondary = new SecondaryInfo { Device = "Keyboard", Key = "Key_F2",
        Modifier = new List<Elite.Binding> { new Elite.Binding { Device = "Keyboard", Key = "Key_LeftControl" },
                                      new Elite.Binding { Device = "Keyboard", Key = "Key_LeftShift" } } } };
    var macro = (string)build.Invoke(null, [binding]);
    Check(macro == "{DIKLSHIFT}{DIKLCONTROL}{DIKF2}", "Incorrect secondary keyboard chord");
    var extract = type.GetMethod("ExtractKeyStrokes", BindingFlags.Static | BindingFlags.NonPublic);
    var keys = (System.Collections.ICollection)extract.Invoke(null, [macro]);
    Check(keys.Count == 3, "Keyboard chord could not be converted into scan codes");
});
// ── New static buttons and info boards ───────────────────────────────────
var eliteAssembly = typeof(UserBindings).Assembly;
var applyJournal = eliteAssembly.GetType("Elite.InfoBoardData")
    .GetMethod("Apply", BindingFlags.Static | BindingFlags.NonPublic);
void Journal(string json) => applyJournal.Invoke(null, [Newtonsoft.Json.Linq.JObject.Parse(json), true]);
object Board(string typeName, string settingsJson = "{}")
{
    var type = eliteAssembly.GetType(typeName) ?? throw new Exception("No type " + typeName);
    var board = RuntimeHelpers.GetUninitializedObject(type);
    var field = type.BaseType.GetField("settings", BindingFlags.NonPublic | BindingFlags.Instance);
    field.SetValue(board, JsonConvert.DeserializeObject(settingsJson, field.FieldType));
    return board;
}
(string State, List<string> Lines, double? Progress) Content(object board)
{
    var content = board.GetType().GetMethod("BuildContent", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(board, null);
    var t = content.GetType();
    return (t.GetProperty("State").GetValue(content).ToString(), (List<string>)t.GetProperty("Lines").GetValue(content),
        (double?)t.GetProperty("Progress").GetValue(content));
}
string Ts(int minutesAgo = 0) => DateTime.UtcNow.AddMinutes(-minutesAgo).ToString("yyyy-MM-ddTHH:mm:ssZ");

Test("New on-foot, SRV and colonisation bindings deserialise from a binds file", () =>
{
    var xml = "<Root PresetName=\"X\" MajorVersion=\"4\" MinorVersion=\"2\">" +
              "<HumanoidPulseScanButton><Primary Device=\"Keyboard\" Key=\"Key_B\" /></HumanoidPulseScanButton>" +
              "<TriggerColonisationModule><Primary Device=\"Keyboard\" Key=\"Key_Y\" /></TriggerColonisationModule>" +
              "<MovePlacementCamForward><Primary Device=\"Keyboard\" Key=\"Key_W\" /></MovePlacementCamForward>" +
              "<PlacementCamSpeedInc><Primary Device=\"Keyboard\" Key=\"Key_Equals\" /></PlacementCamSpeedInc></Root>";
    var b = (UserBindings)new System.Xml.Serialization.XmlSerializer(typeof(UserBindings)).Deserialize(new StringReader(xml));
    Check(b.HumanoidPulseScanButton?.Primary?.Key == "Key_B", "Pulse scan binding not read");
    Check(b.TriggerColonisationModule?.Primary?.Key == "Key_Y", "Colonisation module binding not read");
    Check(b.MovePlacementCamForward?.Primary?.Key == "Key_W", "Placement camera binding not read");
    Check(b.PlacementCamSpeedInc?.Primary?.Key == "Key_Equals", "Placement speed binding not read");
});
Test("Every new static button function maps to a real binding property", () =>
{
    foreach (var name in new[] { "Elite.Buttons.OnFootStatic", "Elite.Buttons.SrvStatic", "Elite.Buttons.ColonisationStatic" })
    {
        var list = (System.Collections.IDictionary)eliteAssembly.GetType(name)
            .GetField("FunctionList", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        foreach (string function in list.Keys)
        {
            Check(typeof(UserBindings).GetProperty(function)?.PropertyType.IsAssignableTo(typeof(StandardBindingInfo)) == true,
                name + ": no binding property " + function);
        }
    }
});
Test("Every new static function has shipped On and Off artwork", () =>
{
    var art = Path.Combine(AppContext.BaseDirectory, "Images", "Optional");
    var map = (System.Collections.IDictionary)typeof(DefaultImages)
        .GetField("StateStaticByFunction", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
    foreach (var name in new[] { "Elite.Buttons.OnFootStatic", "Elite.Buttons.SrvStatic", "Elite.Buttons.ColonisationStatic" })
    {
        var list = (System.Collections.IDictionary)eliteAssembly.GetType(name)
            .GetField("FunctionList", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        foreach (string function in list.Keys)
        {
            Check(map.Contains(function), "No artwork mapping for " + function);
            foreach (var suffix in new[] { "_ON", "_OFF" })
            {
                var file = Path.Combine(art, "Generated", (string)map[function] + suffix + ".png");
                Check(File.Exists(file), "Missing " + file);
            }
        }
    }
});
Test("SRV static images follow cargo scoop and headlight state", () =>
{
    var type = eliteAssembly.GetType("Elite.Buttons.SrvStatic");
    var button = RuntimeHelpers.GetUninitializedObject(type);
    var field = type.BaseType.GetField("settings", BindingFlags.NonPublic | BindingFlags.Instance);
    var isOn = type.BaseType.GetMethod("IsOn", BindingFlags.Instance | BindingFlags.NonPublic);
    void Set(string f) => field.SetValue(button, JsonConvert.DeserializeObject("{\"function\":\"" + f + "\"}", field.FieldType));
    EliteData.StatusData.InSRV = true; EliteData.StatusData.CargoScoopDeployed = false; EliteData.StatusData.LightsOn = false;
    Set("ToggleCargoScoop_Buggy"); Check(!(bool)isOn.Invoke(button, null), "Scoop showed On while retracted");
    EliteData.StatusData.CargoScoopDeployed = true; Check((bool)isOn.Invoke(button, null), "Scoop showed Off while deployed");
    Set("HeadlightsBuggyButton"); Check(!(bool)isOn.Invoke(button, null), "Lights showed On while off");
    EliteData.StatusData.LightsOn = true; Check((bool)isOn.Invoke(button, null), "Lights showed Off while on");
    EliteData.StatusData.InSRV = false; Check(!(bool)isOn.Invoke(button, null), "Lights showed On outside the SRV");
    EliteData.StatusData.LightsOn = false; EliteData.StatusData.CargoScoopDeployed = false;
});
Test("On-foot shield and flashlight track toggles and reset on embark", () =>
{
    var type = eliteAssembly.GetType("Elite.Buttons.OnFootStatic");
    var button = RuntimeHelpers.GetUninitializedObject(type);
    var field = type.BaseType.GetField("settings", BindingFlags.NonPublic | BindingFlags.Instance);
    var isOn = type.BaseType.GetMethod("IsOn", BindingFlags.Instance | BindingFlags.NonPublic);
    var sent = type.GetMethod("OnSent", BindingFlags.Instance | BindingFlags.NonPublic);
    field.SetValue(button, JsonConvert.DeserializeObject("{\"function\":\"HumanoidToggleShieldsButton\"}", field.FieldType));
    EliteData.StatusData.OnFoot = true;
    Check(!(bool)isOn.Invoke(button, null), "Shield started On");
    sent.Invoke(button, ["HumanoidToggleShieldsButton"]); Check((bool)isOn.Invoke(button, null), "Shield toggle not tracked");
    sent.Invoke(button, ["HumanoidToggleShieldsButton"]); Check(!(bool)isOn.Invoke(button, null), "Second toggle not tracked");
    sent.Invoke(button, ["HumanoidToggleShieldsButton"]);
    EliteData.StatusData.OnFoot = false; isOn.Invoke(button, null);
    EliteData.StatusData.OnFoot = true; Check(!(bool)isOn.Invoke(button, null), "Shield state survived embarking");
    EliteData.StatusData.OnFoot = false;
});
Test("Missions board counts missions and alerts near expiry", () =>
{
    Journal($"{{\"timestamp\":\"{Ts(10)}\",\"event\":\"Missions\",\"Active\":[],\"Failed\":[],\"Complete\":[]}}");
    Journal($"{{\"timestamp\":\"{Ts(5)}\",\"event\":\"MissionAccepted\",\"LocalisedName\":\"Courier\",\"MissionID\":1,\"Expiry\":\"{DateTime.UtcNow.AddHours(10):yyyy-MM-ddTHH:mm:ssZ}\"}}");
    Journal($"{{\"timestamp\":\"{Ts(4)}\",\"event\":\"MissionAccepted\",\"LocalisedName\":\"Rescue\",\"MissionID\":2,\"Expiry\":\"{DateTime.UtcNow.AddMinutes(30):yyyy-MM-ddTHH:mm:ssZ}\"}}");
    var c = Content(Board("Elite.Buttons.MissionsBoard", "{\"option\":\"1\"}"));
    Check(c.Lines[0] == "2 MISSIONS" && c.Lines[1] == "Rescue" && c.State == "Alert", "Bad missions board: " + string.Join("/", c.Lines) + " " + c.State);
    Journal($"{{\"timestamp\":\"{Ts(3)}\",\"event\":\"MissionCompleted\",\"MissionID\":2}}");
    c = Content(Board("Elite.Buttons.MissionsBoard", "{\"option\":\"1\"}"));
    Check(c.Lines[0] == "1 MISSION" && c.State == "Active", "Completed mission not removed");
    // An older replayed event must not resurrect it.
    Journal($"{{\"timestamp\":\"{Ts(60)}\",\"event\":\"MissionAccepted\",\"LocalisedName\":\"Old\",\"MissionID\":3}}");
    Check(Content(Board("Elite.Buttons.MissionsBoard")).Lines[0] == "1 MISSION", "Stale replay applied");
});
Test("Carrier board shows jump countdown and low tritium", () =>
{
    Journal($"{{\"timestamp\":\"{Ts(2)}\",\"event\":\"CarrierStats\",\"Callsign\":\"ABC-123\",\"FuelLevel\":100,\"Finance\":{{\"CarrierBalance\":2500000000}}}}");
    Journal($"{{\"timestamp\":\"{Ts(1)}\",\"event\":\"CarrierJumpRequest\",\"SystemName\":\"Sol\",\"DepartureTime\":\"{DateTime.UtcNow.AddMinutes(14):yyyy-MM-ddTHH:mm:ssZ}\"}}");
    var c = Content(Board("Elite.Buttons.CarrierBoard", "{\"option\":\"135\"}"));
    Check(c.Lines[0] == "ABC-123" && c.Lines[1] == "> Sol" && c.Lines[2].StartsWith("in 1") && c.Lines.Contains("Tritium 100t") && c.Lines.Contains("2.5B cr"),
        "Bad carrier board: " + string.Join("/", c.Lines));
    Check(c.State == "Alert", "Low tritium not flagged");
});
Test("Colonisation depot board shows progress and largest remaining commodity", () =>
{
    Journal($"{{\"timestamp\":\"{Ts(1)}\",\"event\":\"Docked\",\"StationName\":\"Depot X\",\"MarketID\":77}}");
    Journal($"{{\"timestamp\":\"{Ts(0)}\",\"event\":\"ColonisationConstructionDepot\",\"MarketID\":77,\"ConstructionProgress\":0.4,\"ConstructionComplete\":false,\"ConstructionFailed\":false," +
            "\"ResourcesRequired\":[{\"Name_Localised\":\"Steel\",\"RequiredAmount\":1000,\"ProvidedAmount\":200},{\"Name_Localised\":\"Titanium\",\"RequiredAmount\":100,\"ProvidedAmount\":100}]}");
    var c = Content(Board("Elite.Buttons.ColonisationDepot"));
    Check(c.Lines[0] == "40%" && c.Lines[1] == "Depot X" && c.Lines[2] == "Steel 800t" && c.Progress == 0.4,
        "Bad depot board: " + string.Join("/", c.Lines));
});
Test("Docking board shows pad, then denial as an alert", () =>
{
    Journal($"{{\"timestamp\":\"{Ts(0)}\",\"event\":\"DockingGranted\",\"LandingPad\":16,\"StationName\":\"Backlund Reach\",\"MarketID\":5}}");
    var c = Content(Board("Elite.Buttons.DockingBoard"));
    Check(c.Lines[0] == "PAD 16" && c.State == "Active", "Bad granted board: " + string.Join("/", c.Lines));
    Journal($"{{\"timestamp\":\"{Ts(0)}\",\"event\":\"DockingDenied\",\"Reason\":\"TooLarge\",\"StationName\":\"Backlund Reach\"}}");
    c = Content(Board("Elite.Buttons.DockingBoard"));
    Check(c.Lines[0] == "DENIED" && c.Lines[1] == "Too Large" && c.State == "Alert", "Bad denied board: " + string.Join("/", c.Lines));
    Journal($"{{\"timestamp\":\"{Ts(0)}\",\"event\":\"Undocked\"}}");
});
Test("Target board flags wanted targets and clears on unlock", () =>
{
    Journal($"{{\"timestamp\":\"{Ts(0)}\",\"event\":\"ShipTargeted\",\"TargetLocked\":true,\"Ship\":\"anaconda\",\"ScanStage\":3,\"PilotRank\":\"Elite\",\"ShieldHealth\":50.0,\"HullHealth\":90.0,\"LegalStatus\":\"Wanted\",\"Bounty\":120000}}");
    var c = Content(Board("Elite.Buttons.TargetInfoBoard"));
    Check(c.Lines[0] == "Anaconda" && c.Lines.Contains("S 50%  H 90%") && c.Lines.Contains("Bounty 120K cr") && c.State == "Alert",
        "Bad target board: " + string.Join("/", c.Lines));
    Journal($"{{\"timestamp\":\"{Ts(0)}\",\"event\":\"ShipTargeted\",\"TargetLocked\":false}}");
    Check(Content(Board("Elite.Buttons.TargetInfoBoard")).State == "Inactive", "Target not cleared");
});
Test("Legal board alerts when wanted and formats the balance", () =>
{
    EliteData.StatusData.LegalState = "Wanted"; EliteData.StatusData.Balance = 12_345_678;
    var c = Content(Board("Elite.Buttons.LegalBalance"));
    Check(c.Lines[0] == "WANTED" && c.Lines[1] == "12.35M cr" && c.State == "Alert", "Bad legal board: " + string.Join("/", c.Lines));
    EliteData.StatusData.LegalState = "Clean";
    Check(Content(Board("Elite.Buttons.LegalBalance")).State == "Active", "Clean flagged");
});
Test("Info boards render a 256px key image for every state", () =>
{
    foreach (var name in new[] { "LegalBalance", "DestinationBoard", "SuitVitals", "MissionsBoard", "CarrierBoard",
                                 "ColonisationDepot", "DockingBoard", "TargetInfoBoard", "FuelScoopBoard" })
    {
        var board = Board("Elite.Buttons." + name);
        var baseType = board.GetType().BaseType;
        var content = baseType.GetNestedType("BoardContent");
        var c = Activator.CreateInstance(content);
        content.GetProperty("Lines").SetValue(c, new List<string> { "HEADLINE TEXT", "a fairly long second line here", "third" });
        content.GetProperty("Progress").SetValue(c, 0.5);
        content.GetProperty("State").SetValue(c, Enum.Parse(baseType.GetNestedType("BoardState"), "Active"));
        foreach (var lockName in new[] { "_drawLock" })
            baseType.GetField(lockName, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(board, new object());
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        baseType.GetField("_primaryColor", flags).SetValue(board, System.Drawing.Color.White);
        baseType.GetField("_alertColor", flags).SetValue(board, System.Drawing.Color.Orange);
        baseType.GetField("_activeImage", flags).SetValue(board,
            File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Images", "Optional", "Generated", "Board-Active.png")));
        var image = (string)baseType.GetMethod("Render", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(board, [c]);
        Check(image != null && image.StartsWith("data:image/png;base64,"), name + " did not render");
        var dump = Environment.GetEnvironmentVariable("ELITE_BOARD_DUMP");
        if (!string.IsNullOrEmpty(dump))
            File.WriteAllBytes(Path.Combine(dump, name + ".png"), Convert.FromBase64String(image.Substring(image.IndexOf(',') + 1)));
    }
});

// Opt-in: replay a real journal folder through the info board backfill.
var realJournals = Environment.GetEnvironmentVariable("ELITE_JOURNAL_DIR");
if (!string.IsNullOrEmpty(realJournals))
{
    Test("Backfill from real journals completes", () =>
    {
        var data = eliteAssembly.GetType("Elite.InfoBoardData");
        data.GetMethod("Backfill").Invoke(null, [realJournals]);
        foreach (var name in new[] { "MissionsBoard", "CarrierBoard", "ColonisationDepot" })
        {
            var c = Content(Board("Elite.Buttons." + name));
            Console.WriteLine($"     {name}: {c.State} {string.Join(" / ", c.Lines)}");
        }
    });
}

Directory.Delete(directory, true);
return failures == 0 ? 0 : 1;

class TestStatusWatcher : StatusWatcher
{
    public void Read(string path) => UpdateStatus(path, 0);
}
