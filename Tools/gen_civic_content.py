#!/usr/bin/env python3
"""Generates the M14 civic service buildings: line materials, placeholder prefabs (primitives under the
prefab contract: pivot at the ground centre, layer 9, a BoxCollider on the root, collider-less Decor
child in world units on top of the base slab) and BuildingDefinition assets, adds them to the
BuildingDatabase, and gives the Monastery / Academy their education reach. Re-runnable: GUIDs and
fileIDs are deterministic, so references survive regeneration. Edit the tables below and re-run.

    python3 Tools/gen_civic_content.py
"""
import hashlib
import os
import re
import uuid

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Assets", "_Game")
NS = uuid.UUID("6f1c2d6e-4b7a-4c55-9d3e-0c1a1c000000")

GUID_BUILDING_DEF = "79acbc5bd48b8f947ae8ae28bc4213ac"    # BuildingDefinition.cs
GUID_BUILDING_INSTANCE = "5f0c919fb94511542be5089325c675f8"  # BuildingInstance.cs
CUBE, CYLINDER = 10202, 10206

# Existing shared materials (Art/*.mat).
MAT = {
    "stone": "69ad755951c40d041b3adb2402400a94",    # MonasteryStone
    "wood": "19db1c86beab0df49a054159d0848ba1",     # TreeTrunk
    "brick": "d58b38757a2b30640a31792995cf1fd8",    # PumpBrick
    "wall": "a6bd453c2979afc4b90d7df841709450",     # AcademyWall
    "dome": "f6dac63f1c70a904589f02bfa3be6aa0",     # AcademyDome
    "water": "03c2172a78a6bef43b9f9bfe2b87a89b",    # WaterBlue
    "green": "566da1e38084acb4891e1954bdec878f",    # BuildingPark
    "tank": "eb71bb3a228ec044e9b4d7887ada58d4",     # TowerTank
}

# New line materials: (asset name, key, rgb). One shared material per line (no per-instance materials).
LINE_MATERIALS = [
    ("CivicOrder", "order", (0.26, 0.42, 0.78)),
    ("CivicFire", "fire", (0.80, 0.22, 0.18)),
    ("CivicHealth", "health", (0.93, 0.93, 0.90)),
    ("CivicEducation", "education", (0.52, 0.34, 0.70)),
]

KIND = {"order": 1, "fire": 2, "health": 3, "education": 4}

# (asset, id, display, line, tech, size (x, z), cost, upkeep, reach, strength, research/day, slab height, slab material, decor)
# decor parts: (name, mesh, (x, y, z) centre above the slab top, (sx, sy, sz) world size, material key)
BUILDINGS = [
    ("WatchHouse", "watch_house", "Watch House", "order", "town_watch", (1, 1), 400, 2, 4, 0.5, 0, 0.08, "stone", [
        ("Hut", CUBE, (0, 0.22, 0), (0.5, 0.44, 0.5), "stone"),
        ("Roof", CUBE, (0, 0.48, 0), (0.6, 0.08, 0.6), "order"),
        ("Lantern", CUBE, (0.18, 0.6, 0.18), (0.1, 0.16, 0.1), "order"),
    ]),
    ("Constabulary", "constabulary", "Constabulary", "order", "civic_planning", (2, 1), 1500, 6, 6, 0.75, 0, 0.08, "stone", [
        ("Hall", CUBE, (0, 0.25, 0), (1.6, 0.5, 0.7), "stone"),
        ("Roof", CUBE, (0, 0.54, 0), (1.7, 0.08, 0.8), "order"),
        ("Door", CUBE, (0, 0.14, -0.36), (0.22, 0.28, 0.04), "order"),
    ]),
    ("PoliceStation", "police_station", "Police Station", "order", "telegraph", (2, 2), 4000, 30, 8, 1.0, 0, 0.1, "wall", [
        ("Block", CUBE, (0, 0.35, 0), (1.6, 0.7, 1.6), "wall"),
        ("Roof", CUBE, (0, 0.73, 0), (1.7, 0.06, 1.7), "order"),
        ("Beacon", CUBE, (0.5, 0.86, 0.5), (0.18, 0.2, 0.18), "order"),
    ]),
    ("BucketBrigade", "bucket_brigade", "Bucket Brigade", "fire", "town_watch", (1, 1), 300, 1, 3, 0.5, 0, 0.08, "wood", [
        ("Shed", CUBE, (0, 0.17, 0.08), (0.6, 0.34, 0.4), "wood"),
        ("Roof", CUBE, (0, 0.38, 0.08), (0.7, 0.07, 0.5), "fire"),
        ("Barrel", CYLINDER, (0.22, 0.1, -0.28), (0.18, 0.1, 0.18), "water"),
    ]),
    ("FireEngineHouse", "fire_engine_house", "Fire Engine House", "fire", "watermills", (2, 1), 1200, 5, 5, 0.75, 0, 0.08, "brick", [
        ("Hall", CUBE, (-0.15, 0.27, 0), (1.3, 0.54, 0.7), "brick"),
        ("Roof", CUBE, (-0.15, 0.58, 0), (1.4, 0.08, 0.8), "fire"),
        ("Tower", CUBE, (0.68, 0.45, 0), (0.3, 0.9, 0.3), "brick"),
        ("TowerCap", CUBE, (0.68, 0.94, 0), (0.36, 0.08, 0.36), "fire"),
    ]),
    ("FireStation", "fire_station", "Fire Station", "fire", "steam_power", (2, 2), 4000, 30, 8, 1.0, 0, 0.1, "brick", [
        ("Hall", CUBE, (-0.15, 0.3, 0), (1.3, 0.6, 1.6), "brick"),
        ("Roof", CUBE, (-0.15, 0.63, 0), (1.4, 0.06, 1.7), "fire"),
        ("Doors", CUBE, (-0.15, 0.2, -0.81), (1.0, 0.4, 0.03), "fire"),
        ("HoseTower", CUBE, (0.65, 0.65, 0.55), (0.35, 1.3, 0.35), "brick"),
        ("TowerCap", CUBE, (0.65, 1.33, 0.55), (0.42, 0.06, 0.42), "fire"),
    ]),
    ("Apothecary", "apothecary", "Apothecary", "health", "herbalism", (1, 1), 500, 2, 4, 0.5, 0, 0.08, "stone", [
        ("Shop", CUBE, (0, 0.22, 0), (0.55, 0.44, 0.55), "stone"),
        ("Roof", CUBE, (0, 0.48, 0), (0.62, 0.08, 0.62), "health"),
        ("CrossA", CUBE, (0, 0.53, 0), (0.3, 0.02, 0.09), "green"),
        ("CrossB", CUBE, (0, 0.53, 0), (0.09, 0.02, 0.3), "green"),
    ]),
    ("Hospital", "hospital", "Hospital", "health", "public_sanitation", (3, 2), 4500, 35, 9, 0.85, 0, 0.1, "wall", [
        ("Wing", CUBE, (0, 0.4, 0), (2.6, 0.8, 1.6), "wall"),
        ("Roof", CUBE, (0, 0.83, 0), (2.7, 0.06, 1.7), "health"),
        ("CrossA", CUBE, (0, 0.87, 0), (0.6, 0.02, 0.18), "green"),
        ("CrossB", CUBE, (0, 0.87, 0), (0.18, 0.02, 0.6), "green"),
    ]),
    ("MedicalCentre", "medical_centre", "Medical Centre", "health", "antibiotics", (3, 3), 10000, 70, 11, 1.0, 0, 0.1, "health", [
        ("Tower", CUBE, (0, 0.6, 0.2), (2.0, 1.2, 1.8), "health"),
        ("Podium", CUBE, (0, 0.2, -0.9), (2.6, 0.4, 0.7), "wall"),
        ("Roof", CUBE, (0, 1.23, 0.2), (2.1, 0.06, 1.9), "tank"),
        ("CrossA", CUBE, (0, 1.27, 0.2), (0.7, 0.02, 0.2), "green"),
        ("CrossB", CUBE, (0, 1.27, 0.2), (0.2, 0.02, 0.7), "green"),
    ]),
    ("University", "university", "University", "education", "universities", (3, 3), 7500, 40, 8, 0.85, 8, 0.1, "wall", [
        ("Hall", CUBE, (0, 0.35, 0.5), (2.4, 0.7, 1.2), "wall"),
        ("WingL", CUBE, (-0.9, 0.3, -0.5), (0.6, 0.6, 1.0), "wall"),
        ("WingR", CUBE, (0.9, 0.3, -0.5), (0.6, 0.6, 1.0), "wall"),
        ("Roof", CUBE, (0, 0.73, 0.5), (2.5, 0.06, 1.3), "education"),
        ("Dome", CYLINDER, (0, 0.88, 0.5), (0.6, 0.12, 0.6), "dome"),
    ]),
    ("ResearchLab", "research_lab", "Research Lab", "education", "computing", (2, 2), 10000, 50, 8, 1.0, 12, 0.1, "tank", [
        ("Block", CUBE, (0, 0.4, 0), (1.6, 0.8, 1.6), "tank"),
        ("Roof", CUBE, (0, 0.83, 0), (1.7, 0.06, 1.7), "education"),
        ("Dish", CYLINDER, (0.4, 0.92, 0.4), (0.5, 0.03, 0.5), "tank"),
        ("Mast", CUBE, (0.4, 0.98, 0.4), (0.05, 0.14, 0.05), "education"),
    ]),
]

# Existing education buildings (M11): (asset, reach, strength).
EXISTING = [("Monastery", 5, 0.4), ("Academy", 6, 0.6)]


def guid(name):
    return uuid.uuid5(NS, name).hex


def file_id(*parts):
    h = hashlib.sha1("/".join(str(p) for p in parts).encode()).digest()
    return int.from_bytes(h[:8], "little") & 0x3FFFFFFFFFFFFFFF or 1


def num(v):
    return ("%g" % v) if v != int(v) else str(int(v))


def vec(v):
    return "{x: %s, y: %s, z: %s}" % tuple(num(c) for c in v)


def write(path, text, meta_guid, importer):
    with open(path, "w", newline="\n") as f:
        f.write(text)
    meta = path + ".meta"
    if importer == "native":
        body = "NativeFormatImporter:\n  externalObjects: {}\n  mainObjectFileID: %s\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n"
        main = "2100000" if path.endswith(".mat") else "11400000"
        body = body % main
    else:
        body = "PrefabImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n"
    with open(meta, "w", newline="\n") as f:
        f.write("fileFormatVersion: 2\nguid: %s\n%s" % (meta_guid, body))


# --- Materials: copies of PumpBrick.mat (URP Simple Lit) with the line colour. ---
template = open(os.path.join(ROOT, "Art", "PumpBrick.mat")).read()
for name, key, rgb in LINE_MATERIALS:
    text = template.replace("m_Name: PumpBrick", "m_Name: " + name)
    colour = "{r: %s, g: %s, b: %s, a: 1}" % rgb
    text = re.sub(r"_BaseColor: \{[^}]*\}", "_BaseColor: " + colour, text)
    text = re.sub(r"_Color: \{[^}]*\}", "_Color: " + colour, text)
    g = guid("mat_" + key)
    MAT[key] = g
    write(os.path.join(ROOT, "Art", name + ".mat"), text, g, "native")

RENDERER = """--- !u!23 &{rid}
MeshRenderer:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: {go}}}
  m_Enabled: 1
  m_CastShadows: 1
  m_ReceiveShadows: 1
  m_DynamicOccludee: 1
  m_StaticShadowCaster: 0
  m_MotionVectors: 1
  m_LightProbeUsage: 1
  m_ReflectionProbeUsage: 1
  m_RayTracingMode: 2
  m_RayTraceProcedural: 0
  m_RayTracingAccelStructBuildFlagsOverride: 0
  m_RayTracingAccelStructBuildFlags: 1
  m_SmallMeshCulling: 1
  m_ForceMeshLod: -1
  m_MeshLodSelectionBias: 0
  m_RenderingLayerMask: 1
  m_RendererPriority: 0
  m_Materials:
  - {{fileID: 2100000, guid: {mat}, type: 2}}
  m_StaticBatchInfo:
    firstSubMesh: 0
    subMeshCount: 0
  m_StaticBatchRoot: {{fileID: 0}}
  m_ProbeAnchor: {{fileID: 0}}
  m_LightProbeVolumeOverride: {{fileID: 0}}
  m_ScaleInLightmap: 1
  m_ReceiveGI: 1
  m_PreserveUVs: 1
  m_IgnoreNormalsForChartDetection: 0
  m_ImportantGI: 0
  m_StitchLightmapSeams: 1
  m_SelectedEditorRenderState: 3
  m_MinimumChartSize: 4
  m_AutoUVMaxDistance: 0.5
  m_AutoUVMaxAngle: 89
  m_LightmapParameters: {{fileID: 0}}
  m_GlobalIlluminationMeshLod: 0
  m_SortingLayerID: 0
  m_SortingLayer: 0
  m_SortingOrder: 0
  m_MaskInteraction: 0
  m_AdditionalVertexStreams: {{fileID: 0}}
"""


def game_object(go, name, components):
    text = "--- !u!1 &%d\nGameObject:\n  m_ObjectHideFlags: 0\n  m_CorrespondingSourceObject: {fileID: 0}\n" % go
    text += "  m_PrefabInstance: {fileID: 0}\n  m_PrefabAsset: {fileID: 0}\n  serializedVersion: 6\n  m_Component:\n"
    text += "".join("  - component: {fileID: %d}\n" % c for c in components)
    text += "  m_Layer: 9\n  m_Name: %s\n  m_TagString: Untagged\n  m_Icon: {fileID: 0}\n" % name
    text += "  m_NavMeshLayer: 0\n  m_StaticEditorFlags: 0\n  m_IsActive: 1\n"
    return text


def transform(tid, go, pos, scale, children, father):
    text = "--- !u!4 &%d\nTransform:\n  m_ObjectHideFlags: 0\n  m_CorrespondingSourceObject: {fileID: 0}\n" % tid
    text += "  m_PrefabInstance: {fileID: 0}\n  m_PrefabAsset: {fileID: 0}\n  m_GameObject: {fileID: %d}\n" % go
    text += "  serializedVersion: 2\n  m_LocalRotation: {x: 0, y: 0, z: 0, w: 1}\n"
    text += "  m_LocalPosition: %s\n  m_LocalScale: %s\n  m_ConstrainProportionsScale: 0\n" % (vec(pos), vec(scale))
    text += "  m_Children:%s\n" % ("".join("\n  - {fileID: %d}" % c for c in children) if children else " []")
    text += "  m_Father: {fileID: %d}\n  m_LocalEulerAnglesHint: {x: 0, y: 0, z: 0}\n" % father
    return text


def mesh_filter(mid, go, mesh):
    return ("--- !u!33 &%d\nMeshFilter:\n  m_ObjectHideFlags: 0\n  m_CorrespondingSourceObject: {fileID: 0}\n"
            "  m_PrefabInstance: {fileID: 0}\n  m_PrefabAsset: {fileID: 0}\n  m_GameObject: {fileID: %d}\n"
            "  m_Mesh: {fileID: %d, guid: 0000000000000000e000000000000000, type: 0}\n") % (mid, go, mesh)


def box_collider(cid, go):
    return ("--- !u!65 &%d\nBoxCollider:\n  m_ObjectHideFlags: 0\n  m_CorrespondingSourceObject: {fileID: 0}\n"
            "  m_PrefabInstance: {fileID: 0}\n  m_PrefabAsset: {fileID: 0}\n  m_GameObject: {fileID: %d}\n"
            "  m_Material: {fileID: 0}\n  m_IncludeLayers:\n    serializedVersion: 2\n    m_Bits: 0\n"
            "  m_ExcludeLayers:\n    serializedVersion: 2\n    m_Bits: 0\n  m_LayerOverridePriority: 0\n"
            "  m_IsTrigger: 0\n  m_ProvidesContacts: 0\n  m_Enabled: 1\n  serializedVersion: 3\n"
            "  m_Size: {x: 1, y: 1, z: 1}\n  m_Center: {x: 0, y: 0, z: 0}\n") % (cid, go)


def prefab(asset, slab_mat, parts):
    f = lambda *p: file_id(asset, *p)
    root_go, root_tr, root_mf, root_col, root_mr, root_bi = (f("root", c) for c in ("go", "tr", "mf", "col", "mr", "bi"))
    decor_go, decor_tr = f("decor", "go"), f("decor", "tr")
    part_ids = [(f(p[0], "go"), f(p[0], "tr"), f(p[0], "mf"), f(p[0], "mr")) for p in parts]

    text = "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n"
    text += game_object(root_go, asset, [root_tr, root_mf, root_col, root_mr, root_bi])
    text += transform(root_tr, root_go, (0, 0, 0), (1, 1, 1), [decor_tr], 0)
    text += mesh_filter(root_mf, root_go, CUBE)
    text += box_collider(root_col, root_go)
    text += RENDERER.format(rid=root_mr, go=root_go, mat=MAT[slab_mat])
    text += ("--- !u!114 &%d\nMonoBehaviour:\n  m_ObjectHideFlags: 0\n  m_CorrespondingSourceObject: {fileID: 0}\n"
             "  m_PrefabInstance: {fileID: 0}\n  m_PrefabAsset: {fileID: 0}\n  m_GameObject: {fileID: %d}\n"
             "  m_Enabled: 1\n  m_EditorHideFlags: 0\n  m_Script: {fileID: 11500000, guid: %s, type: 3}\n"
             "  m_Name: \n  m_EditorClassIdentifier: Assembly-CSharp::BuildingInstance\n  m_Decor: {fileID: %d}\n"
             ) % (root_bi, root_go, GUID_BUILDING_INSTANCE, decor_tr)
    text += game_object(decor_go, "Decor", [decor_tr])
    text += transform(decor_tr, decor_go, (0, 0, 0), (1, 1, 1), [ids[1] for ids in part_ids], root_tr)
    for (name, mesh, pos, scale, mat), (go, tr, mf, mr) in zip(parts, part_ids):
        text += game_object(go, name, [tr, mf, mr])
        text += transform(tr, go, pos, scale, [], decor_tr)
        text += mesh_filter(mf, go, mesh)
        text += RENDERER.format(rid=mr, go=go, mat=MAT[mat])
    g = guid("prefab_" + asset)
    write(os.path.join(ROOT, "Prefabs", "Buildings", asset + ".prefab"), text, g, "prefab")
    return root_go, g


def building_asset(asset, bid, display, line, tech, size, cost, upkeep, reach, strength, research, height, prefab_ref):
    text = ("%%YAML 1.1\n%%TAG !u! tag:unity3d.com,2011:\n--- !u!114 &11400000\nMonoBehaviour:\n"
            "  m_ObjectHideFlags: 0\n  m_CorrespondingSourceObject: {fileID: 0}\n  m_PrefabInstance: {fileID: 0}\n"
            "  m_PrefabAsset: {fileID: 0}\n  m_GameObject: {fileID: 0}\n  m_Enabled: 1\n  m_EditorHideFlags: 0\n"
            "  m_Script: {fileID: 11500000, guid: %s, type: 3}\n  m_Name: %s\n"
            "  m_EditorClassIdentifier: Assembly-CSharp::BuildingDefinition\n") % (GUID_BUILDING_DEF, asset)
    text += "  m_Id: %s\n  m_DisplayName: %s\n  m_Category: 2\n  m_Icon: {fileID: 0}\n" % (bid, display)
    text += "  m_Prefab: {fileID: %d, guid: %s, type: 3}\n" % prefab_ref
    text += "  m_Size: {x: %d, y: %d}\n  m_Cost: %d\n  m_UpkeepPerDay: %s\n" % (size[0], size[1], cost, num(upkeep))
    text += "  m_HousingCapacity: 0\n  m_JobsProvided: 0\n  m_HappinessEffect: 0\n  m_ZoneRestriction: 0\n"
    text += "  m_Height: %s\n  m_CoverageRadius: 0\n  m_PowerSupply: 0\n  m_RequiredTech: %s\n" % (num(height), tech)
    text += "  m_ResearchPerDay: %s\n  m_Pollution: 0\n  m_PollutionRadius: 0\n" % num(research)
    text += "  m_WaterSupply: 0\n  m_WaterRadius: 0\n  m_ObsoleteAge: \n"
    text += civic_fields(line, reach, strength)
    g = guid("building_" + bid)
    write(os.path.join(ROOT, "Scriptables", "Buildings", asset + ".asset"), text, g, "native")
    return g


def civic_fields(line, reach, strength):
    return "  m_CivicKind: %d\n  m_CivicRadius: %d\n  m_CivicStrength: %s\n" % (KIND[line], reach, num(strength))


building_guids = []
for asset, bid, display, line, tech, size, cost, upkeep, reach, strength, research, height, slab, decor in BUILDINGS:
    prefab_ref = prefab(asset, slab, decor)
    building_guids.append(building_asset(asset, bid, display, line, tech, size, cost, upkeep, reach, strength,
                                         research, height, prefab_ref))

# Monastery / Academy: education reach (fields Unity would write in declaration order after m_ResearchPerDay).
TAIL = ["m_Pollution", "m_PollutionRadius", "m_WaterSupply", "m_WaterRadius", "m_ObsoleteAge",
        "m_CivicKind", "m_CivicRadius", "m_CivicStrength"]
for asset, reach, strength in EXISTING:
    path = os.path.join(ROOT, "Scriptables", "Buildings", asset + ".asset")
    lines = [l for l in open(path).read().splitlines() if l.split(":")[0].strip() not in TAIL]
    values = {"m_Pollution": "0", "m_PollutionRadius": "0", "m_WaterSupply": "0", "m_WaterRadius": "0", "m_ObsoleteAge": ""}
    text = "\n".join(lines) + "\n" + "".join("  %s: %s\n" % (k, values[k]) for k in TAIL[:5]) + civic_fields("education", reach, strength)
    with open(path, "w", newline="\n") as f:
        f.write(text)

# BuildingDatabase: append the new entries once.
db_path = os.path.join(ROOT, "Scriptables", "Buildings", "BuildingDatabase.asset")
db = open(db_path).read()
for g in building_guids:
    entry = "  - {fileID: 11400000, guid: %s, type: 2}\n" % g
    if entry not in db:
        db = db.rstrip("\n") + "\n" + entry
with open(db_path, "w", newline="\n") as f:
    f.write(db)

print("materials", len(LINE_MATERIALS), "buildings", len(BUILDINGS), "updated", len(EXISTING))
