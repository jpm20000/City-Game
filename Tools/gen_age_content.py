# Generates the M11 age/tech content assets (GamePlan §12 content table, first-pass numbers).
# Deterministic GUIDs (uuid5 of the asset name), so re-running rewrites the same assets.
# Run from anywhere: python Tools/gen_age_content.py, then let Unity reimport. ContentTests validates the result.
import io, os, uuid

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Assets", "_Game", "Scriptables")
GUID_AGE_DEF = "a13593af433dd8440a3d802b066bb1d8"
GUID_AGE_DB = "241edd6ee45d45b4fa2b67e623070894"
GUID_TECH_DEF = "4a81a50374d8fd1488dba7cf9e9b874e"
GUID_TECH_DB = "a6bee4b53f8793c4b9cc00b60f5ad9a0"
GUID_VISUAL_SET = "e8eec815828e415cad1c610865718a24"   # Scripts/Buildings/AgeVisualSet.cs (Assembly-CSharp)
NS = uuid.UUID("6f1c0e52-8d0a-4c3a-9a51-3c1d2b7e9f10")

def guid(name):
    return uuid.uuid5(NS, name).hex

def ref(g):
    return "{fileID: 11400000, guid: %s, type: 2}" % g

def q(text):
    return "'" + text.replace("'", "''") + "'"

def header(script_guid, name, cls, assembly="CityBuilder.Simulation"):
    return ("%%YAML 1.1\n%%TAG !u! tag:unity3d.com,2011:\n--- !u!114 &11400000\nMonoBehaviour:\n"
            "  m_ObjectHideFlags: 0\n  m_CorrespondingSourceObject: {fileID: 0}\n  m_PrefabInstance: {fileID: 0}\n"
            "  m_PrefabAsset: {fileID: 0}\n  m_GameObject: {fileID: 0}\n  m_Enabled: 1\n  m_EditorHideFlags: 0\n"
            "  m_Script: {fileID: 11500000, guid: %s, type: 3}\n  m_Name: %s\n"
            "  m_EditorClassIdentifier: %s::%s\n") % (script_guid, name, assembly, cls)

def write(path, text, g):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    io.open(path, "w", encoding="utf-8", newline="\n").write(text)
    meta = ("fileFormatVersion: 2\nguid: %s\nNativeFormatImporter:\n  externalObjects: {}\n"
            "  mainObjectFileID: 11400000\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n") % g
    io.open(path + ".meta", "w", encoding="utf-8", newline="\n").write(meta)

def array(lines, indent="  "):
    return "[]" if not lines else "\n" + "\n".join(indent + "- " + l for l in lines)

# Effect types: 0 UnlockBuilding, 1 ResearchMultiplier, 2 DemandMultiplier, 3 HappinessBonus, 4 UpkeepMultiplier,
# 5 PollutionMultiplier, 6 LandValueBonus (M12)
R, C, I = "Residential", "Commercial", "Industrial"
def demand(zone, v): return (2, zone, v)
def research(v): return (1, "", v)
def happy(v): return (3, "", v)
def upkeep(v): return (4, "", v)
def pollution(v): return (5, "", v)
def land_value(v): return (6, "", v)

# (asset name, id, display, age, cost, prerequisites, effects, description)
TECHS = [
    # Medieval
    ("Commons", "commons", "Commons", 0, 30, [], [], "Common land for all. Unlocks the Park, a village green that cheers nearby homes."),
    ("Masonry", "masonry", "Masonry", 0, 40, [], [], "Dressed stone for lasting walls. Leads to monasteries and, much later, architecture."),
    ("CropRotation", "crop_rotation", "Crop Rotation", 0, 35, [], [demand(R, 1.1)], "Fields rest in turn and feed more mouths. Residential demand +10%."),
    ("Smithing", "smithing", "Smithing", 0, 35, [], [demand(I, 1.1)], "Forges for tools and nails. Crafts demand +10%."),
    ("Monasticism", "monasticism", "Monasticism", 0, 50, ["masonry"], [], "Communities of learning. Unlocks the Monastery, which produces research."),
    ("Markets", "markets", "Markets", 0, 50, ["crop_rotation"], [demand(C, 1.1)], "A weekly market draws traders. Commercial demand +10%."),
    ("Guilds", "guilds", "Guilds", 0, 90, ["masonry", "markets", "smithing"], [demand(C, 1.1), demand(I, 1.1)], "Masters, journeymen and standards. Commercial and crafts demand +10%."),
    ("Charters", "charters", "Charters", 0, 70, ["markets"], [happy(0.02)], "A town charter grants rights to its citizens. Happiness +2%."),
    # Renaissance
    ("PrintingPress", "printing_press", "Printing Press", 1, 200, ["monasticism"], [research(1.25)], "Books for everyone. Research +25%."),
    ("Academies", "academies", "Academies", 1, 250, ["printing_press"], [], "Halls of scholarship. Unlocks the Academy, which produces research."),
    ("Banking", "banking", "Banking", 1, 220, ["guilds"], [demand(C, 1.15)], "Credit and bills of exchange. Commercial demand +15%."),
    ("Architecture", "architecture", "Architecture", 1, 200, ["masonry"], [], "Proportion, domes and planned facades."),
    ("CivicPlanning", "civic_planning", "Civic Planning", 1, 260, ["architecture"], [happy(0.03)], "Squares, straight streets and order. Happiness +3%."),
    ("Watermills", "watermills", "Watermills", 1, 180, ["smithing"], [upkeep(0.9)], "Water does the heavy work. Upkeep -10%."),
    # Industrial
    ("Electricity", "electricity", "Electricity", 2, 150, [], [], "Unlocks the Power Plant. From this age on, buildings need power to grow past level 1."),
    ("SteamPower", "steam_power", "Steam Power", 2, 400, ["watermills"], [demand(I, 1.2)], "Engines that never tire. Industrial demand +20%."),
    ("Factories", "factories", "Factories", 2, 500, ["steam_power"], [], "Mass production under one roof."),
    ("Railways", "railways", "Railways", 2, 500, ["steam_power"], [], "Iron roads connect the city to the world."),
    ("PublicSanitation", "public_sanitation", "Public Sanitation", 2, 450, ["civic_planning"], [happy(0.03)], "Sewers and clean water. Happiness +3%."),
    ("Telegraph", "telegraph", "Telegraph", 2, 450, ["electricity", "printing_press"], [research(1.2)], "News at the speed of light. Research +20%."),
    ("SteelFrames", "steel_frames", "Steel Frames", 2, 550, ["factories"], [], "Skeletons of steel let buildings climb."),
    ("ElectricTrams", "electric_trams", "Electric Trams", 2, 400, ["electricity"], [], "Cheap rides along the main streets."),
    # Modern
    ("Automobiles", "automobiles", "Automobiles", 3, 900, ["railways"], [], "A car in every garage."),
    ("Computing", "computing", "Computing", 3, 1000, ["telegraph"], [research(1.3)], "Machines that calculate. Research +30%."),
    ("Suburbs", "suburbs", "Suburbs", 3, 900, ["automobiles"], [demand(R, 1.15)], "Homes with gardens at the edge of town. Residential demand +15%."),
    ("MassMedia", "mass_media", "Mass Media", 3, 800, ["telegraph"], [], "Radio and television in every home."),
    ("Internet", "internet", "Internet", 3, 1400, ["computing"], [research(1.3)], "Everything connected. Research +30%."),
    ("Renewables", "renewables", "Renewables", 3, 1000, ["electricity"], [pollution(0.75)], "Power from wind and sun. Pollution -25%."),
    ("GreenBuilding", "green_building", "Green Building", 3, 1100, ["steel_frames"], [upkeep(0.9)], "Efficient buildings cost less to run. Upkeep -10%."),
    ("SmartGrid", "smart_grid", "Smart Grid", 3, 1300, ["computing", "renewables"], [], "A grid that balances itself."),
]

# (asset, id, display, year, max level, scale, power, techs to advance, required, pop, advance cost, starting techs, money, zone names,
#  pollution scale, pollution radius (M12: workshops pollute little and close by, factories a lot, clean energy less))
AGES = [
    ("Medieval", "medieval", "Medieval Age", 750, 2, 0.5, False, 0, [], 0, 0, [], 20000, ["", "", "Crafts"], 0.3, 1),
    ("Renaissance", "renaissance", "Renaissance", 1450, 3, 0.75, False, 5, [], 120, 250, [], 30000, ["", "", "Workshops"], 0.5, 2),
    ("Industrial", "industrial", "Industrial Age", 1760, 3, 1.0, True, 4, [], 350, 1200, ["electricity"], 50000, ["", "", ""], 1.0, 3),
    ("Modern", "modern", "Modern Age", 1945, 3, 1.25, True, 5, [], 650, 3000, [], 80000, ["", "", ""], 0.6, 3),
]

tech_guid = {t[1]: guid("tech_" + t[1]) for t in TECHS}

for asset, tid, display, age, cost, prereqs, effects, desc in TECHS:
    text = header(GUID_TECH_DEF, asset, "TechDefinition")
    text += "  m_Id: %s\n  m_DisplayName: %s\n  m_Description: %s\n  m_Age: %d\n  m_Cost: %s\n" % (tid, display, q(desc), age, cost)
    text += "  m_Prerequisites: %s\n" % array([ref(tech_guid[p]) for p in prereqs])
    if effects:
        text += "  m_Effects:\n" + "".join("  - Type: %d\n    Target: %s\n    Value: %s\n" % (t, target, v) for t, target, v in effects)
    else:
        text += "  m_Effects: []\n"
    write(os.path.join(ROOT, "Techs", asset + ".asset"), text, tech_guid[tid])

age_guid = {}
for asset, aid, display, year, maxl, scale, power, toadv, req, pop, cost, starting, money, zones, pscale, pradius in AGES:
    g = guid("age_" + aid)
    age_guid[aid] = g
    text = header(GUID_AGE_DEF, asset, "AgeDefinition")
    text += "  m_Id: %s\n  m_DisplayName: %s\n  m_StartYear: %d\n  m_MaxLevel: %d\n  m_CapacityScale: %s\n" % (aid, display, year, maxl, scale)
    text += "  m_UpgradesNeedPower: %d\n  m_UpgradesNeedWater: 0\n" % (1 if power else 0)
    text += "  m_PollutionScale: %s\n  m_PollutionRadius: %d\n  m_TechsToAdvance: %d\n" % (pscale, pradius, toadv)
    text += "  m_RequiredTechs: %s\n" % array([ref(tech_guid[t]) for t in req])
    text += "  m_PopulationToEnter: %d\n  m_AdvanceCost: %s\n" % (pop, cost)
    text += "  m_StartingTechs: %s\n" % array([ref(tech_guid[t]) for t in starting])
    text += "  m_StartingMoney: %s\n" % money
    text += "  m_ZoneNames:\n" + "".join("  - %s\n" % z for z in zones)
    write(os.path.join(ROOT, "Ages", asset + ".asset"), text, g)

text = header(GUID_AGE_DB, "AgeDatabase", "AgeDatabase")
text += "  m_Ages:%s\n" % array([ref(age_guid[a[1]]) for a in AGES])
write(os.path.join(ROOT, "Ages", "AgeDatabase.asset"), text, guid("age_database"))

text = header(GUID_TECH_DB, "TechDatabase", "TechDatabase")
text += "  m_Techs:%s\n" % array([ref(tech_guid[t[1]]) for t in TECHS])
write(os.path.join(ROOT, "Techs", "TechDatabase.asset"), text, guid("tech_database"))

# --- Age visual sets (M11e): fallback styles for the placeholder blocks, per zone and level 1..3. ---
# (tint rgba: body = zone colour blended toward rgb by a; roof: 0 Default, 1 Pitched, 2 None;
#  roof colour rgba, a = 0 -> shaded body colour; height multiplier). No prefabs yet (M18).
def style(tint, roof, roof_color, height):
    return (tint, roof, roof_color, height)

PLAIN = style((0, 0, 0, 0), 0, (0, 0, 0, 0), 1.0)
TIMBER, THATCH, DARK_WOOD = (0.80, 0.70, 0.52, 0.55), (0.62, 0.50, 0.30, 1), (0.40, 0.28, 0.20, 1)
SANDSTONE, TERRACOTTA, BRICK = (0.88, 0.80, 0.64, 0.40), (0.70, 0.38, 0.26, 1), (0.66, 0.40, 0.32, 0.40)
CONCRETE, GLASS, STEEL = (0.70, 0.76, 0.82, 0.30), (0.55, 0.70, 0.85, 0.40), (0.72, 0.74, 0.76, 0.35)
NONE = (0, 0, 0, 0)

VISUALS = [
    # asset, age id, residential L1-3, commercial L1-3, industrial L1-3
    ("MedievalVisuals", "medieval",
     [style(TIMBER, 1, THATCH, 0.90), style(TIMBER, 1, THATCH, 0.80), style(TIMBER, 1, THATCH, 0.75)],
     [style(TIMBER, 1, DARK_WOOD, 0.85), style(TIMBER, 1, DARK_WOOD, 0.80), style(TIMBER, 1, DARK_WOOD, 0.75)],
     [style((0.62, 0.52, 0.40, 0.5), 1, (0.35, 0.30, 0.26, 1), 0.90)] * 3),
    ("RenaissanceVisuals", "renaissance",
     [style(SANDSTONE, 1, TERRACOTTA, 0.95), style(SANDSTONE, 1, TERRACOTTA, 0.90), style(SANDSTONE, 1, TERRACOTTA, 0.85)],
     [style(SANDSTONE, 1, TERRACOTTA, 1.0), style(SANDSTONE, 1, TERRACOTTA, 0.95), style(SANDSTONE, 0, NONE, 0.90)],
     [style(BRICK, 1, (0.40, 0.30, 0.28, 1), 1.0)] * 3),
    ("IndustrialVisuals", "industrial", [PLAIN] * 3, [PLAIN] * 3, [PLAIN] * 3),
    ("ModernVisuals", "modern",
     [style(CONCRETE, 0, NONE, 1.0), style(CONCRETE, 0, NONE, 1.15), style(CONCRETE, 0, NONE, 1.30)],
     [style(GLASS, 0, NONE, 1.0), style(GLASS, 0, NONE, 1.20), style(GLASS, 0, NONE, 1.40)],
     [style(STEEL, 0, NONE, 1.0)] * 3),
]

def color(c):
    return "{r: %s, g: %s, b: %s, a: %s}" % tuple(c)

def styles(field, entries):
    out = "  %s:\n" % field
    for tint, roof, roof_color, height in entries:
        out += "  - Prefabs: []\n    Tint: %s\n    Roof: %d\n    RoofColor: %s\n    HeightScale: %s\n" % (
            color(tint), roof, color(roof_color), height)
    return out

for asset, age_id, residential, commercial, industrial in VISUALS:
    text = header(GUID_VISUAL_SET, asset, "AgeVisualSet", "Assembly-CSharp")
    text += "  m_AgeId: %s\n" % age_id
    text += styles("m_Residential", residential) + styles("m_Commercial", commercial) + styles("m_Industrial", industrial)
    write(os.path.join(ROOT, "Ages", "Visuals", asset + ".asset"), text, guid("visuals_" + age_id))

print("ages", len(AGES), "techs", len(TECHS), "visual sets", len(VISUALS))
print("AgeDatabase guid", guid("age_database"), "TechDatabase guid", guid("tech_database"))
