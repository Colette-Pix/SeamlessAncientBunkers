using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Xml.Linq;

// Metadata-only audit: does not load or execute Unity or pretend to simulate gameplay.
if (args.Length != 2) throw new ArgumentException("Arguments: game-directory mod-directory");
string game = Path.GetFullPath(args[0]), mod = Path.GetFullPath(args[1]);
using var gameFile = File.OpenRead(Path.Combine(game, "RimWorldWin64_Data/Managed/Assembly-CSharp.dll"));
using var gamePE = new PEReader(gameFile);
var gm = gamePE.GetMetadataReader();
using var modFile = File.OpenRead(Path.Combine(mod, "1.6/Assemblies/SeamlessAncientBunkers.dll"));
using var modPE = new PEReader(modFile);
var mm = modPE.GetMetadataReader();
int count = 0;
void Check(bool value, string label)
{
    if (!value) throw new Exception("FAILED: " + label);
    Console.WriteLine("PASS: " + label); count++;
}
TypeDefinition Type(MetadataReader m, string fullName) => m.GetTypeDefinition(m.TypeDefinitions.Single(h =>
{
    var t = m.GetTypeDefinition(h);
    return m.GetString(t.Namespace) + "." + m.GetString(t.Name) == fullName;
}));
MethodDefinition Method(string owner, string name) => gm.GetMethodDefinition(Type(gm, owner).GetMethods().Single(h => gm.GetString(gm.GetMethodDefinition(h).Name) == name));
foreach (var (owner, name, parameterCount) in new[]
{
    ("RimWorld.JobGiver_GetFood", "TryGiveJob", 1),
    ("RimWorld.JobGiver_GetRest", "TryGiveJob", 1),
    ("RimWorld.JobGiver_Work", "TryIssueJobPackage", 2),
    ("RimWorld.MapPortal", "GetGizmos", 0),
    ("RimWorld.JobGiver_GetJoy", "TryGiveJob", 1),
    ("RimWorld.JobGiver_PatientGoToBed", "TryGiveJob", 1)
})
{
    var method = Method(owner, name);
    Check(method.GetParameters().Count(h => gm.GetParameter(h).SequenceNumber > 0) == parameterCount,
        "Harmony target " + owner + "." + name);
    if (parameterCount > 0)
        Check(method.GetParameters().Any(h => gm.GetString(gm.GetParameter(h).Name) == "pawn"), "Harmony pawn argument name: " + owner);
}
Check(Type(gm, "RimWorld.Pawn_PlayerSettings").GetFields().Any(h => gm.GetString(gm.GetFieldDefinition(h).Name) == "allowedAreas"), "Map-specific allowed-area field exists");
Check((Method("RimWorld.JobDriver_EnterPortal", "MakeNewToils").Attributes & MethodAttributes.Virtual) != 0, "Portal driver toils can be overridden");
foreach (string type in new[] { "Manager", "Intent", "Cooldown", "Settings", "BunkerMod", "JobDriver_BunkerTravel", "JobDriver_Collect", "RemotePawnScope", "ProbeAudit", "Broker", "Graph", "WorkProbe", "NeedProbe", "Hauling", "Orders", "AnimalSupport", "RobotSupport", "ProcessorSupport", "EnemyPursuit", "GravshipSupport", "JobDriver_Pursue", "JobDriver_Dine", "ExtraNeeds", "Dining", "Gatherings", "Gathering", "SavedSelection", "CaravanQuery", "BlastDoorClaims", "BunkerLevels", "BunkerLevel", "BunkerLevelKeys", "OrderFeedback" })
    Check(!Type(mm, "SeamlessAncientBunkers." + type).Name.IsNil, "Compiled mod type " + type);
Check(!mm.MemberReferences.Any(h => new[] { "GetOtherMap", "GeneratePocketMap" }.Contains(mm.GetString(mm.GetMemberReference(h).Name))), "Mod has no map-generation/ GetOtherMap calls");
foreach(string type in new[]{"PendingDeparture","Departures","DepartureQuery","PawnTransit","PrisonerTransit","PenTransit","MechTransit","TransitPolicy","QueuedTransitOrders","GroupTransit","AnimalTransit","MentalTransit","DepartureStaging","ConsumerSupply","ServicesInventory","ServiceDeliveries"})
    Check(!Type(mm,"SeamlessAncientBunkers."+type).Name.IsNil,"Connected implementation present: "+type);
Check(mm.GetAssemblyDefinition().Version == new Version(0,5,2,0),"Release assembly version is 0.5.2");
var transitDefs=XDocument.Load(Path.Combine(mod,"1.6/Defs/TransitJobs.xml"));
foreach(var (name,driver) in new[]{("SAB_CollectPassenger","JobDriver_CollectPassenger"),("SAB_ExitConnected","JobDriver_ConnectedExit"),("SAB_ReleaseAfterTransit","JobDriver_ReleaseAfterTransit")})
{
    var job=transitDefs.Descendants("JobDef").Single(x=>x.Element("defName")?.Value==name);
    Check(job.Element("driverClass")?.Value=="SeamlessAncientBunkers."+driver&&!Type(mm,"SeamlessAncientBunkers."+driver).Name.IsNil,"Passenger/exit driver and definition: "+name);
    Check(job.Element("carryThingAfterJob")?.Value=="true"&&job.Element("dropThingBeforeJob")?.Value=="false","Passenger retention flags: "+name);
}
var about = XDocument.Load(Path.Combine(mod, "About/About.xml"));
Check(about.Descendants("packageId").Any(x => x.Value == "ludeon.rimworld.odyssey"), "Odyssey dependency declared");
Check(about.Descendants("packageId").Any(x => x.Value == "brrainz.harmony"), "Harmony dependency declared");
var defs = XDocument.Load(Path.Combine(mod, "1.6/Defs/JobDefs.xml"));
foreach(var (name,driver) in new[]{("SAB_Travel","JobDriver_BunkerTravel"),("SAB_Collect","JobDriver_Collect"),("SAB_Pursue","JobDriver_Pursue"),("SAB_Dine","JobDriver_Dine")})
{
    var job=defs.Descendants("JobDef").Single(x=>x.Element("defName")?.Value==name);
    Check(job.Element("driverClass")?.Value=="SeamlessAncientBunkers."+driver,"Job definition matches compiled driver: "+name);
    Check(job.Element("carryThingAfterJob")?.Value=="true"&&job.Element("dropThingBeforeJob")?.Value=="false","Cargo retention flags: "+name);
    Check(job.Element("checkOverrideOnExpire")==null,"No invalid JobDef override field: "+name);
}
var odyssey = XDocument.Load(Path.Combine(game, "Data/Odyssey/Defs/ThingDefs_Buildings/Buildings_Misc.xml"));
foreach(var (owner,name,n) in new[]{("RimWorld.Building_GravEngine","CanLaunch",1),("Verse.WorldComponent_GravshipController","InitiateTakeoff",2),("RimWorld.GravshipUtility","GenerateGravship",1),("RimWorld.GravshipPlacementUtility","PlaceGravshipInMap",4)})
    Check(Method(owner,name).GetParameters().Count(h=>gm.GetParameter(h).SequenceNumber>0)==n,"Gravship integration target: "+owner+"."+name);
var hatch = odyssey.Descendants("ThingDef").Single(x => x.Element("defName")?.Value == "AncientHatch");
Check(hatch.Element("passability")?.Value == "Impassable", "Installed hatch requires nearby landing");
Check(hatch.Element("portal")?.Element("exitDef")?.Value == "AncientHatchExit", "Installed hatch/exit pairing");
Check(Directory.GetFiles(Path.Combine(mod, "1.6/Assemblies"), "*.dll").Select(Path.GetFileName).SequenceEqual(new[] { "SeamlessAncientBunkers.dll" }), "Only original mod assembly is distributed");
var keys=XDocument.Load(Path.Combine(mod,"1.6/Defs/KeyBindingDefs.xml"));
Check(keys.Descendants("KeyBindingDef").Count()==2,"Two level key bindings declared");
var concepts=XDocument.Load(Path.Combine(mod,"1.6/Defs/ConceptDefs.xml"));
Check(concepts.Descendants("helpText").Single().Value.Contains("{Key:SAB_LevelUp}")&&concepts.Descendants("helpText").Single().Value.Contains("{Key:SAB_LevelDown}"),"Learning Helper displays configured shortcuts");
Check(File.Exists(Path.Combine(mod,"About/Preview.png")),"Workshop preview supplied");
Console.WriteLine($"{count} API/package checks passed. Gameplay and save/load are NOT tested by this tool.");




