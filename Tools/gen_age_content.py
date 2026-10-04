# Generates the M11 age/tech content assets (GamePlan §12 content table, first-pass numbers).
# Deterministic GUIDs (uuid5 of the asset name), so re-running rewrites the same assets.
# Run from anywhere: python Tools/gen_age_content.py, then let Unity reimport. ContentTests validates the result.
import io, os, uuid

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Assets", "_Game", "Scriptables")
GUID_AGE_DEF = "a13593af433dd8440a3d802b066bb1d8"
GUID_AGE_DB = "241edd6ee45d45b4fa2b67e623070894"
GUID_TECH_DEF = "4a81a50374d8fd1488dba7cf9e9b874e"
GUID_TECH_DB = "a6bee4b53f8793c4b9cc00b60f5ad9a0"
GUID_ORDINANCE_DEF = "0b5f2a6c1d3e4f7a8b9c0d1e2f3a4b5c"   # Scripts/Simulation/Ages/OrdinanceDefinition.cs (M15)
GUID_ROAD_TIER_DEF = "7c2d9e4f1a5b4c6d8e0f1a2b3c4d5e6f"   # Scripts/Simulation/Ages/RoadTierDefinition.cs (M16)
GUID_EVENT_DEF = "5e9a3c7d1b2f4a6c8d0e1f2a3b4c5d6e"       # Scripts/Simulation/Ages/EventDefinition.cs (M17)
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
# 5 PollutionMultiplier, 6 LandValueBonus (M12), 7 CivicNeedMultiplier (M15), 8 LoanInterestMultiplier (M15),
# 9 TrafficMultiplier (M16), 10 HazardMultiplier (M17; target FireSpread / PlagueSpread / Breakdown)
R, C, I = "Residential", "Commercial", "Industrial"
def demand(zone, v): return (2, zone, v)
def research(v): return (1, "", v)
def happy(v): return (3, "", v)
def upkeep(v): return (4, "", v)
def pollution(v): return (5, "", v)
def land_value(v): return (6, "", v)
def civic_need(kind, v): return (7, kind, v)
def loan_interest(v): return (8, "", v)
def traffic(v): return (9, "", v)
def hazard(kind, v): return (10, kind, v)

# (asset name, id, display, age, cost, prerequisites, effects, description)
TECHS = [
    # Medieval
    ("Commons", "commons", "Commons", 0, 30, [], [], "Common land for all. Unlocks the Park, a village green that cheers nearby homes."),
    ("Masonry", "masonry", "Masonry", 0, 40, [], [], "Dressed stone for lasting walls. Leads to monasteries and, much later, architecture."),
    ("CropRotation", "crop_rotation", "Crop Rotation", 0, 35, [], [demand(R, 1.1)], "Fields rest in turn and feed more mouths. Residential demand +10%."),
    ("Smithing", "smithing", "Smithing", 0, 35, [], [demand(I, 1.1)], "Forges for tools and nails. Crafts demand +10%."),
    ("Monasticism", "monasticism", "Monasticism", 0, 50, ["masonry"], [], "Communities of learning. Unlocks the Monastery, which produces research and schools the homes around it."),
    ("Markets", "markets", "Markets", 0, 50, ["crop_rotation"], [demand(C, 1.1)], "A weekly market draws traders. Commercial demand +10%."),
    ("Guilds", "guilds", "Guilds", 0, 90, ["masonry", "markets", "smithing"], [demand(C, 1.1), demand(I, 1.1)], "Masters, journeymen and standards. Commercial and crafts demand +10%."),
    ("Charters", "charters", "Charters", 0, 70, ["markets"], [happy(0.02)], "A town charter grants rights to its citizens. Happiness +2%."),
    ("TownWatch", "town_watch", "Town Watch", 0, 40, ["charters"], [], "Watchmen walk the streets and ring the fire bell. Unlocks the Watch House and the Bucket Brigade."),
    ("Herbalism", "herbalism", "Herbalism", 0, 35, ["crop_rotation"], [], "Healing herbs and those who know them. Unlocks the Apothecary, which cares for the sick nearby."),
    # Renaissance
    ("PrintingPress", "printing_press", "Printing Press", 1, 200, ["monasticism"], [research(1.25)], "Books for everyone. Research +25%."),
    ("Academies", "academies", "Academies", 1, 250, ["printing_press"], [], "Halls of scholarship. Unlocks the Academy, which produces research and educates the homes around it."),
    ("Banking", "banking", "Banking", 1, 220, ["guilds"], [demand(C, 1.15), loan_interest(0.5)], "Credit and bills of exchange. Commercial demand +15%. Loans cost half the interest."),
    ("Aqueducts", "aqueducts", "Aqueducts", 1, 160, ["masonry"], [], "Channels carry spring water into town. Unlocks the Fountain, which waters and cheers the blocks around it."),
    ("Architecture", "architecture", "Architecture", 1, 200, ["masonry"], [land_value(0.05)], "Proportion, domes and planned facades. Land value +5% everywhere. Unlocks the Cobbled Street."),
    ("CivicPlanning", "civic_planning", "Civic Planning", 1, 260, ["architecture"], [happy(0.03)], "Squares, straight streets and order. Happiness +3%. Unlocks the Constabulary."),
    ("Watermills", "watermills", "Watermills", 1, 180, ["smithing"], [upkeep(0.9)], "Water does the heavy work. Upkeep -10%. Unlocks the Fire Engine House (hand pumps)."),
    # Industrial
    ("Electricity", "electricity", "Electricity", 2, 150, [], [], "Unlocks the Power Plant. From this age on, buildings need power to grow past level 1."),
    ("Waterworks", "waterworks", "Waterworks", 2, 100, [], [], "Unlocks the Water Tower. From this age on, water is piped along the roads and buildings need it to grow past level 1."),
    ("SteamPower", "steam_power", "Steam Power", 2, 400, ["watermills"], [demand(I, 1.2)], "Engines that never tire. Industrial demand +20%. Unlocks the Fire Station (steam fire engines)."),
    ("Factories", "factories", "Factories", 2, 500, ["steam_power"], [], "Mass production under one roof."),
    ("Railways", "railways", "Railways", 2, 500, ["steam_power"], [traffic(0.85)], "Iron roads connect the city to the world. Goods and commuters go by rail: commute trips -15%."),
    ("PublicSanitation", "public_sanitation", "Public Sanitation", 2, 450, ["civic_planning"], [happy(0.03)], "Sewers and clean water. Happiness +3%. Unlocks the Pumping Station and the Hospital."),
    ("Telegraph", "telegraph", "Telegraph", 2, 450, ["electricity", "printing_press"], [research(1.2)], "News at the speed of light. Research +20%. Unlocks the Police Station (call boxes)."),
    ("Universities", "universities", "Universities", 2, 500, ["telegraph", "academies"], [], "Research universities. Unlocks the University, which produces research and educates the homes around it."),
    ("SteelFrames", "steel_frames", "Steel Frames", 2, 550, ["factories"], [hazard("FireSpread", 0.5)], "Skeletons of steel let buildings climb. Fires spread half as fast."),
    ("ElectricTrams", "electric_trams", "Electric Trams", 2, 400, ["electricity"], [traffic(0.9)], "Cheap rides along the main streets. Commute trips -10%. Unlocks the Avenue."),
    ("Macadam", "macadam", "Macadam", 2, 150, ["architecture"], [], "Crushed-stone roads that carry carts in all weathers. Unlocks the Paved Road."),
    # Modern
    ("Automobiles", "automobiles", "Automobiles", 3, 900, ["railways"], [demand(C, 1.1), traffic(1.2)], "A car in every garage. Commercial demand +10%, but commute trips +20%. Unlocks the Highway."),
    ("Computing", "computing", "Computing", 3, 1000, ["telegraph"], [research(1.3)], "Machines that calculate. Research +30%. Unlocks the Research Lab."),
    ("Suburbs", "suburbs", "Suburbs", 3, 900, ["automobiles"], [demand(R, 1.15)], "Homes with gardens at the edge of town. Residential demand +15%."),
    ("MassMedia", "mass_media", "Mass Media", 3, 800, ["telegraph"], [], "Radio and television in every home."),
    ("Internet", "internet", "Internet", 3, 1400, ["computing"], [research(1.3)], "Everything connected. Research +30%."),
    ("Renewables", "renewables", "Renewables", 3, 1000, ["electricity"], [pollution(0.75)], "Power from wind and sun. Pollution -25%."),
    ("GreenBuilding", "green_building", "Green Building", 3, 1100, ["steel_frames"], [upkeep(0.9)], "Efficient buildings cost less to run. Upkeep -10%."),
    ("SmartGrid", "smart_grid", "Smart Grid", 3, 1300, ["computing", "renewables"], [hazard("Breakdown", 0.25)], "A grid that balances itself. Plants and pumps break down a quarter as often."),
    ("Antibiotics", "antibiotics", "Antibiotics", 3, 900, ["public_sanitation"], [], "Infections cured in days. Unlocks the Medical Centre."),
]

# (asset, id, display, year, max level, scale, power, techs to advance, required, pop, advance cost, starting techs, money, zone names,
#  pollution scale, pollution radius (M12: workshops pollute little and close by, factories a lot, clean energy less),
#  water rule (M13: 1 Coverage = wells and fountains, 2 Piped = towers and pumps on the road network),
#  fire risk (M14: timber towns burn, brick and steel less),
#  loan amount (M15),
#  plague risk (M17: 1 = the Medieval baseline, 0 = no plague))
AGES = [
    ("Medieval", "medieval", "Medieval Age", 750, 2, 0.5, False, 0, [], 0, 0, [], 20000, ["", "", "Crafts"], 0.3, 1, 1, 0.6, 10000, 1.0),
    ("Renaissance", "renaissance", "Renaissance", 1450, 3, 0.75, False, 5, [], 120, 250, [], 30000, ["", "", "Workshops"], 0.5, 2, 1, 0.45, 15000, 0.6),
    ("Industrial", "industrial", "Industrial Age", 1760, 3, 1.0, True, 4, [], 350, 1200, ["electricity", "waterworks", "macadam"], 50000, ["", "", ""], 1.0, 3, 2, 0.35, 25000, 0),
    ("Modern", "modern", "Modern Age", 1945, 3, 1.25, True, 5, [], 650, 3000, [], 80000, ["", "", ""], 0.6, 3, 2, 0.2, 40000, 0),
]


# Ordinances (M15): city-wide policies a tech unlocks. (asset, id, display, tech id, cost/day, cost/resident/day, effects, description)
ORDINANCES = [
    ("FeastDays", "feast_days", "Feast Days", "charters", 0, 0.03, [happy(0.03), demand(C, 1.05)],
     "Holidays cheer the town and the market. Happiness +3%, commercial demand +5%."),
    ("Curfew", "curfew", "Curfew", "town_watch", 3, 0, [civic_need("Order", 0.7), happy(-0.01), demand(C, 0.9)],
     "Bells ring the town indoors at dusk. Crime -30%, happiness -1%, commercial demand -10%."),
    ("Quarantine", "quarantine", "Quarantine", "monasticism", 0, 0.02, [hazard("PlagueSpread", 0.5), demand(C, 0.9)],
     "Monks keep the sick apart. Plague spreads half as fast, commercial demand -10%."),
    ("HerbGardens", "herb_gardens", "Herb Gardens", "herbalism", 0, 0.02, [civic_need("Health", 0.85)],
     "Every household tends healing herbs. Sickness -15%."),
    ("FireCode", "fire_code", "Fire Code", "architecture", 0, 0.02, [civic_need("Fire", 0.7), demand(I, 0.95)],
     "Stone chimneys and cleared thatch. Fire risk -30%, industrial demand -5%."),
    ("StreetLighting", "street_lighting", "Street Lighting", "civic_planning", 0, 0.03, [civic_need("Order", 0.8), happy(0.01)],
     "Lamps along the straight streets. Crime -20%, happiness +1%."),
    ("PublicLectures", "public_lectures", "Public Lectures", "printing_press", 0, 0.04, [research(1.1)],
     "Open lectures spread learning. Research +10%."),
    ("SmokeAbatement", "smoke_abatement", "Smoke Abatement", "factories", 0, 0.03, [pollution(0.8), demand(I, 0.9)],
     "Tall chimneys and smoke inspectors. Pollution -20%, industrial demand -10%."),
    ("BuildingCode", "building_code", "Building Code", "steel_frames", 0, 0.02, [civic_need("Fire", 0.75), hazard("FireSpread", 0.8), land_value(0.02)],
     "Inspected frames and fire doors. Fire risk -25%, fires spread 20% slower, land value +2%."),
    ("WorkmensFares", "workmens_fares", "Workmen's Fares", "electric_trams", 0, 0.03, [demand(R, 1.1)],
     "Cheap tram fares for commuters. Residential demand +10%."),
    ("FreeClinics", "free_clinics", "Free Clinics", "public_sanitation", 0, 0.04, [civic_need("Health", 0.8)],
     "A doctor in every district. Sickness -20%."),
    ("NeighbourhoodWatch", "neighbourhood_watch", "Neighbourhood Watch", "mass_media", 2, 0, [civic_need("Order", 0.8), happy(0.01)],
     "Residents report what they see. Crime -20%, happiness +1%."),
    ("CarFreeSundays", "car_free_sundays", "Car-free Sundays", "automobiles", 0, 0.02, [pollution(0.9), happy(0.02), demand(C, 0.95), traffic(0.9)],
     "One quiet day a week. Pollution -10%, happiness +2%, commute trips -10%, commercial demand -5%."),
]
# Road tiers (M16): (asset, tier, id, display, tech id or None, cost, upkeep/day, capacity, travel cost, frontage, obsolete by)
ROAD_TIERS = [
    ("DirtTrack", 1, "dirt", "Dirt Track", None, 20, 0.4, 60, 6, True, 2),
    ("CobbledStreet", 2, "cobble", "Cobbled Street", "architecture", 35, 0.7, 100, 5, True, 3),
    ("PavedRoad", 3, "paved", "Paved Road", "macadam", 50, 1.0, 160, 4, True, 0),
    ("Avenue", 4, "avenue", "Avenue", "electric_trams", 150, 3.0, 400, 3, True, 0),
    ("Highway", 5, "highway", "Highway", "automobiles", 400, 6.0, 1200, 1, False, 0),
]
ORDINANCE_OF_TECH = {o[3]: o[2] for o in ORDINANCES}

# Random events (M17): (asset, id, title, text, min age, max age, min population, tech id or None, weight, choices).
# A choice is (label, description, cost, cost per resident, reward, research points, days, effects); the last one is free.
EVENTS = [
    ("TravellingFair", "travelling_fair", "Travelling Fair", "A fair of tumblers, traders and singers asks to set up on the green.", 0, 1, 40, None, 1,
     [("Host the fair", "Happiness +4% and commercial demand +20% for 20 days.", 200, 0.5, 0, 0, 20, [happy(0.04), demand(C, 1.2)]),
      ("Turn them away", "Nothing happens.", 0, 0, 0, 0, 0, [])]),
    ("PoorHarvest", "poor_harvest", "Poor Harvest", "Blight in the fields: bread will be short this season.", 0, 1, 60, None, 1,
     [("Buy grain", "Costs money, no other effect.", 0, 1.0, 0, 0, 0, []),
      ("Tighten belts", "Residential demand -30% and happiness -2% for 30 days.", 0, 0, 0, 0, 30, [demand(R, 0.7), happy(-0.02)])]),
    ("WanderingScholars", "wandering_scholars", "Wandering Scholars", "A band of scholars arrives, looking for a patron.", 0, 1, 80, None, 1,
     [("Lodge them", "+80 research points.", 300, 0, 0, 80, 0, []),
      ("Send them on", "Nothing happens.", 0, 0, 0, 0, 0, [])]),
    ("Bandits", "bandits", "Bandits on the Road", "Armed bands prey on carts outside the walls.", 0, 0, 100, None, 1,
     [("Hire sellswords", "Crime -30% for 30 days.", 400, 0, 0, 0, 30, [civic_need("Order", 0.7)]),
      ("Bar the gates", "Commercial demand -15% and crime +20% for 30 days.", 0, 0, 0, 0, 30, [demand(C, 0.85), civic_need("Order", 1.2)])]),
    ("WealthyPatron", "patron", "A Wealthy Patron", "A rich merchant offers to beautify the town, for a fee.", 1, 1, 200, None, 1,
     [("Commission a fresco", "Land value +3% and happiness +2% for 90 days.", 1500, 0, 0, 0, 90, [land_value(0.03), happy(0.02)]),
      ("Decline", "Nothing happens.", 0, 0, 0, 0, 0, [])]),
    ("MerchantFleet", "merchant_fleet", "Merchant Fleet", "A fleet of merchants seeks a trading partner.", 1, 2, 250, "banking", 1,
     [("Invest in the voyage", "Commercial demand +25% for 45 days.", 2000, 0, 0, 0, 45, [demand(C, 1.25)]),
      ("Pass", "Nothing happens.", 0, 0, 0, 0, 0, [])]),
    ("Pamphleteers", "pamphleteers", "Pamphleteers", "Printers hawk pamphlets on every corner.", 1, 2, 200, "printing_press", 1,
     [("Let them print", "Research +15% and crime +10% for 30 days.", 0, 0, 0, 0, 30, [research(1.15), civic_need("Order", 1.1)]),
      ("Ban them", "Happiness -1% for 30 days.", 0, 0, 0, 0, 30, [happy(-0.01)])]),
    ("Strike", "strike", "Workers Strike", "The mills are silent: workers demand better pay.", 2, 3, 400, None, 1,
     [("Raise wages", "Costs money, no other effect.", 0, 2.0, 0, 0, 0, []),
      ("Wait it out", "Industrial demand -40% and happiness -2% for 20 days.", 0, 0, 0, 0, 20, [demand(I, 0.6), happy(-0.02)])]),
    ("WorldsFair", "worlds_fair", "Worlds Fair", "A great exhibition could put the city on the map.", 2, 2, 500, "telegraph", 1,
     [("Host the fair", "Happiness +5%, commercial demand +20% for 30 days and +200 research points.", 8000, 0, 0, 200, 30, [happy(0.05), demand(C, 1.2)]),
      ("Decline", "Nothing happens.", 0, 0, 0, 0, 0, [])]),
    ("SmogWeek", "smog_week", "Smog Week", "A pall of smoke hangs over the city.", 2, 3, 400, "factories", 1,
     [("Slow the mills", "Industrial demand -20% and pollution -40% for 15 days.", 0, 0, 0, 0, 15, [demand(I, 0.8), pollution(0.6)]),
      ("Keep working", "Happiness -3% for 15 days.", 0, 0, 0, 0, 15, [happy(-0.03)])]),
    ("StartupBoom", "startup_boom", "Startup Boom", "A wave of young companies wants a home here.", 3, 3, 700, "computing", 1,
     [("Offer tax breaks", "Commercial demand +30% and research +20% for 60 days.", 5000, 0, 0, 0, 60, [demand(C, 1.3), research(1.2)]),
      ("Let it be", "Nothing happens.", 0, 0, 0, 0, 0, [])]),
    ("Heatwave", "heatwave", "Heatwave", "Days of killing heat: the sick and the old suffer.", 2, 3, 600, None, 1,
     [("Open cooling centres", "Costs money, no other effect.", 0, 2.0, 0, 0, 0, []),
      ("Ride it out", "Sickness +50% and happiness -2% for 15 days.", 0, 0, 0, 0, 15, [civic_need("Health", 1.5), happy(-0.02)])]),
    ("FilmShoot", "film_shoot", "Film Shoot", "A studio wants to close your streets for a week.", 3, 3, 600, "mass_media", 1,
     [("Close the streets", "Earn $4,000; commute trips +30% for 7 days.", 0, 0, 4000, 0, 7, [traffic(1.3)]),
      ("Refuse", "Nothing happens.", 0, 0, 0, 0, 0, [])]),
]

# Each unlocking tech says so in its description.
TECHS = [(a, b, c, d, e, f, g, desc + (" Enables the %s ordinance." % ORDINANCE_OF_TECH[b] if b in ORDINANCE_OF_TECH else ""))
         for a, b, c, d, e, f, g, desc in TECHS]

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
for asset, aid, display, year, maxl, scale, power, toadv, req, pop, cost, starting, money, zones, pscale, pradius, water, fire, loan, plague in AGES:
    g = guid("age_" + aid)
    age_guid[aid] = g
    text = header(GUID_AGE_DEF, asset, "AgeDefinition")
    text += "  m_Id: %s\n  m_DisplayName: %s\n  m_StartYear: %d\n  m_MaxLevel: %d\n  m_CapacityScale: %s\n" % (aid, display, year, maxl, scale)
    text += "  m_UpgradesNeedPower: %d\n  m_Water: %d\n" % (1 if power else 0, water)
    text += "  m_PollutionScale: %s\n  m_PollutionRadius: %d\n  m_FireRisk: %s\n  m_LoanAmount: %s\n  m_PlagueRisk: %s\n  m_TechsToAdvance: %d\n" % (pscale, pradius, fire, loan, plague, toadv)
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
text += "  m_Ordinances:%s\n" % array([ref(guid("ordinance_" + o[1])) for o in ORDINANCES])
text += "  m_RoadTiers:%s\n" % array([ref(guid("road_tier_" + r[2])) for r in ROAD_TIERS])
text += "  m_Events:%s\n" % array([ref(guid("event_" + e[1])) for e in EVENTS])
write(os.path.join(ROOT, "Techs", "TechDatabase.asset"), text, guid("tech_database"))

for asset, oid, display, tech, per_day, per_resident, effects, desc in ORDINANCES:
    text = header(GUID_ORDINANCE_DEF, asset, "OrdinanceDefinition")
    text += "  m_Id: %s\n  m_DisplayName: %s\n  m_Description: %s\n  m_RequiredTech: %s\n" % (oid, q(display), q(desc), ref(tech_guid[tech]))
    text += "  m_CostPerDay: %s\n  m_CostPerResident: %s\n" % (per_day, per_resident)
    text += "  m_Effects:\n" + "".join("  - Type: %d\n    Target: %s\n    Value: %s\n" % (t, target, v) for t, target, v in effects)
    write(os.path.join(ROOT, "Techs", "Ordinances", asset + ".asset"), text, guid("ordinance_" + oid))

for asset, eid, title, etext, min_age, max_age, min_pop, tech, weight, choices in EVENTS:
    text = header(GUID_EVENT_DEF, asset, "EventDefinition")
    text += "  m_Id: %s\n  m_Title: %s\n  m_Text: %s\n  m_MinAge: %d\n  m_MaxAge: %d\n  m_MinPopulation: %d\n" % (eid, q(title), q(etext), min_age, max_age, min_pop)
    text += "  m_RequiredTech: %s\n  m_Weight: %s\n  m_Choices:\n" % (ref(tech_guid[tech]) if tech else "{fileID: 0}", weight)
    for label, cdesc, cost, per_res, reward, rp, days, effects in choices:
        text += "  - Label: %s\n    Description: %s\n    Cost: %s\n    CostPerResident: %s\n    Reward: %s\n    ResearchPoints: %s\n    Days: %d\n" % (
            q(label), q(cdesc), cost, per_res, reward, rp, days)
        if effects:
            text += "    Effects:\n" + "".join("    - Type: %d\n      Target: %s\n      Value: %s\n" % (t, target, v) for t, target, v in effects)
        else:
            text += "    Effects: []\n"
    write(os.path.join(ROOT, "Techs", "Events", asset + ".asset"), text, guid("event_" + eid))

for asset, tier, rid, display, tech, cost, upkeep, capacity, travel, frontage, obsolete in ROAD_TIERS:
    text = header(GUID_ROAD_TIER_DEF, asset, "RoadTierDefinition")
    text += "  m_Tier: %d\n  m_Id: %s\n  m_DisplayName: %s\n  m_RequiredTech: %s\n" % (
        tier, rid, q(display), ref(tech_guid[tech]) if tech else "{fileID: 0}")
    text += "  m_Cost: %d\n  m_UpkeepPerDay: %s\n  m_Capacity: %s\n  m_TravelCost: %d\n  m_Frontage: %d\n  m_ObsoleteBy: %d\n" % (
        cost, upkeep, capacity, travel, 1 if frontage else 0, obsolete)
    write(os.path.join(ROOT, "Techs", "RoadTiers", asset + ".asset"), text, guid("road_tier_" + rid))

# --- Age visual sets (M11e): fallback styles for the placeholder blocks, per zone and level 1..3. ---
# (tint rgba: body = zone colour blended toward rgb by a; roof: 0 Default, 1 Pitched, 2 None;
#  roof colour rgba, a = 0 -> shaded body colour; height multiplier). Prefabs are Unity's: see read_prefabs (M18a).
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

# M18a: Unity owns the Prefabs slots (the building kit generator / hand-made art fill them); this script owns
# tints, roofs and heights. Before rewriting a set it reads the existing Prefabs blocks and writes them back.
def read_prefabs(path):
    """field -> one list of raw YAML lines per style (the lines under its Prefabs: key; [] = empty slot)."""
    found, field = {}, None
    if not os.path.exists(path):
        return found
    lines = io.open(path, encoding="utf-8").read().split("\n")
    i = 0
    while i < len(lines):
        line = lines[i].rstrip("\r")
        if line.startswith("  m_") and line.endswith(":"):
            field = line.strip()[:-1]
            found[field] = []
        elif line.startswith("  - Prefabs:") and field is not None:
            block = []
            if line.strip() != "- Prefabs: []":
                while i + 1 < len(lines) and lines[i + 1].startswith("    - "):
                    i += 1
                    block.append(lines[i].rstrip("\r"))
            found[field].append(block)
        i += 1
    return found

def styles(field, entries, prefabs=None):
    out = "  %s:\n" % field
    existing = (prefabs or {}).get(field, [])
    for n, (tint, roof, roof_color, height) in enumerate(entries):
        block = existing[n] if n < len(existing) else []
        out += "  - Prefabs:%s\n    Tint: %s\n    Roof: %d\n    RoofColor: %s\n    HeightScale: %s\n" % (
            ("\n" + "\n".join(block)) if block else " []", color(tint), roof, color(roof_color), "%g" % height)
    return out

for asset, age_id, residential, commercial, industrial in VISUALS:
    text = header(GUID_VISUAL_SET, asset, "AgeVisualSet", "Assembly-CSharp")
    text += "  m_AgeId: %s\n" % age_id
    path = os.path.join(ROOT, "Ages", "Visuals", asset + ".asset")
    kept = read_prefabs(path)
    text += (styles("m_Residential", residential, kept) + styles("m_Commercial", commercial, kept)
             + styles("m_Industrial", industrial, kept))
    write(path, text, guid("visuals_" + age_id))

print("ages", len(AGES), "techs", len(TECHS), "ordinances", len(ORDINANCES), "events", len(EVENTS), "road tiers", len(ROAD_TIERS), "visual sets", len(VISUALS))
print("AgeDatabase guid", guid("age_database"), "TechDatabase guid", guid("tech_database"))
