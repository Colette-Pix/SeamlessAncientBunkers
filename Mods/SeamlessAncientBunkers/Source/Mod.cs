using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace SeamlessAncientBunkers
{
    public class Settings : ModSettings
    {
        public bool enabled = true, food = true, rest = true, work = true;
        public bool joy = true, medical = true, haul = true, debug;
        public int scanInterval = 600, maxCandidates = 128, maxMaps = 12, maxHops = 4, minStay = 1800;
        public override void ExposeData()
        {
            Scribe_Values.Look(ref enabled, "enabled", true);
            Scribe_Values.Look(ref food, "food", true);
            Scribe_Values.Look(ref rest, "rest", true);
            Scribe_Values.Look(ref work, "work", true);
            Scribe_Values.Look(ref joy, "joy", true); Scribe_Values.Look(ref medical, "medical", true); Scribe_Values.Look(ref haul, "haul", true); Scribe_Values.Look(ref debug,"debug");
            Scribe_Values.Look(ref scanInterval,"scanInterval",600); Scribe_Values.Look(ref maxCandidates,"maxCandidates",128);
            Scribe_Values.Look(ref maxMaps,"maxMaps",12); Scribe_Values.Look(ref maxHops,"maxHops",4); Scribe_Values.Look(ref minStay,"minStay",1800);
            scanInterval=Mathf.Clamp(scanInterval,120,2400); maxCandidates=Mathf.Clamp(maxCandidates,32,512); maxMaps=Mathf.Clamp(maxMaps,2,32); maxHops=Mathf.Clamp(maxHops,1,8); minStay=Mathf.Clamp(minStay,300,6000);
        }
    }

    public class BunkerMod : Mod
    {
        public static Settings Settings;
        public BunkerMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<Settings>();
            new Harmony("colet.seamlessancientbunkers").PatchAll();
        }
        public override string SettingsCategory() => "Seamless Ancient Bunkers";
        public override void DoSettingsWindowContents(Rect rect)
        {
            var listing = new Listing_Standard();
            listing.Begin(rect);
            listing.CheckboxLabeled("Enable automatic traffic after bunker clearance", ref Settings.enabled);
            listing.CheckboxLabeled("Food: compare suitable food on linked maps", ref Settings.food);
            listing.CheckboxLabeled("Rest: travel to linked beds (animals included)", ref Settings.rest);
            listing.CheckboxLabeled("Assigned work across linked maps", ref Settings.work);
            listing.CheckboxLabeled("Recreation fallback", ref Settings.joy);
            listing.CheckboxLabeled("Medical bed routing", ref Settings.medical);
            listing.CheckboxLabeled("Stockpile hauling and material supply", ref Settings.haul);
            listing.CheckboxLabeled("Diagnostic logging",ref Settings.debug);
            listing.Label("Scan interval (ticks): "+Settings.scanInterval); Settings.scanInterval=(int)listing.Slider(Settings.scanInterval,120,2400);
            listing.Label("Candidates per scan: "+Settings.maxCandidates); Settings.maxCandidates=(int)listing.Slider(Settings.maxCandidates,32,512);
            listing.Gap();
            listing.Label("Colonists, slaves, controlled mechs and trained animals can use connected floors. Opened routes respect entrance and area restrictions. Assaulting enemies can pursue through hatches. Use hatch controls for manual travel and remote work orders.");
            if (listing.ButtonText("Prepare this save for mod removal"))
            {
                Manager.Current?.Cleanup();
                Settings.enabled = false;
                WriteSettings();
                Messages.Message("Bunker traffic disabled and custom travel jobs cancelled. Save now, quit, then disable this mod.", MessageTypeDefOf.NeutralEvent, false);
            }
            listing.End();
        }
    }
}
