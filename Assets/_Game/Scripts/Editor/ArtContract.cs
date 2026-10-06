using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// The M11 / M18 prefab contract for grown-block art, as a check (menu: CityBuilder > Validate Art).
// A prefab fills an AgeVisualSet slot when it: faces local +Z (its front, turned toward the street by
// CellUtils.FacingRoad), stands with its pivot at the ground centre of a 1x1 cell, sits inside that
// cell, is on layer 9 (Buildings), has a collider on the root (selection raycasts land on it), and
// uses at most two shared materials (never per-instance ones: they break SRP batching, the M10b
// 23 ms-per-frame problem). ContentTests runs it on every prefab in every visual set (by reflection,
// since Assembly-CSharp-Editor can't be referenced from the test assembly).
public static class ArtContract
{
    public const int BuildingsLayer = 9;
    public const float CellMargin = 0.04f;          // how far past the 1x1 cell the bounds may reach
    public const float GroundTolerance = 0.03f;     // the lowest point may sit this far off y = 0
    public const int MaxMaterials = 2;

    // Every style table of an AgeVisualSet: Medium, then Low and High (M23).
    public static readonly string[] Tables =
    {
        "m_Residential", "m_Commercial", "m_Industrial",
        "m_ResidentialLow", "m_CommercialLow", "m_IndustrialLow",
        "m_ResidentialHigh", "m_CommercialHigh", "m_IndustrialHigh",
    };

    // Allowed body height (cells, top of the bounds) per level 1..3. Wider than the placeholder
    // profiles (0.4-2.2 x the age's height scale and the +-15% jitter) so any age's art fits.
    private static readonly float[] s_MinHeight = { 0.15f, 0.40f, 0.60f };
    private static readonly float[] s_MaxHeight = { 1.20f, 2.00f, 3.40f };

    // Placeable buildings (M25) follow a different contract from grown blocks: BuildingInstance.ApplyTransform scales the
    // whole prefab to footprint x Definition.Height and stands it at height / 2, so the prefab is modelled as a UNIT
    // BOX centred on its pivot (every mesh inside -0.5..0.5 on all three axes), carries a BuildingInstance on the root,
    // sits on layer 9 with a collider on the root. Its optional Decor child (BuildingInstance.m_Decor) is counter-scaled
    // and authored in world units, so it is left out of the box check. The shipped placeables use 1-5 shared
    // materials (small flat-colour assets), not the kit's single material, so the limit is looser than for grown blocks.
    public const int MaxPlaceableMaterials = 6;
    public const float UnitBoxMargin = 0.02f;

    public static bool ValidatePlaceable(GameObject prefab, List<string> errors)
    {
        int before = errors.Count;
        if (prefab == null)
        {
            errors.Add("empty prefab");
            return false;
        }
        string name = prefab.name;
        if (prefab.layer != BuildingsLayer) errors.Add($"{name}: layer {prefab.layer}, must be {BuildingsLayer} (Buildings)");
        if (prefab.GetComponent<Collider>() == null) errors.Add($"{name}: no collider on the root (selection raycasts need it)");
        var instance = prefab.GetComponent<BuildingInstance>();
        if (instance == null) errors.Add($"{name}: no BuildingInstance on the root (placement needs it)");

        Transform decor = null;
        if (instance != null)
        {
            decor = new SerializedObject(instance).FindProperty("m_Decor").objectReferenceValue as Transform;
        }

        var materials = new HashSet<Material>();
        bool hasRenderer = false;
        foreach (Renderer renderer in prefab.GetComponentsInChildren<Renderer>(true))
        {
            hasRenderer = true;
            foreach (Material material in renderer.sharedMaterials)
            {
                if (material == null) errors.Add($"{name}/{renderer.name}: missing material");
                else materials.Add(material);
            }
        }
        if (!hasRenderer) errors.Add($"{name}: no renderers");
        if (materials.Count > MaxPlaceableMaterials) errors.Add($"{name}: {materials.Count} materials, at most {MaxPlaceableMaterials}");

        if (!TryGetBounds(prefab, decor, out Bounds bounds))
        {
            errors.Add($"{name}: no meshes to measure");
        }
        else
        {
            float limit = 0.5f + UnitBoxMargin;
            if (bounds.min.x < -limit || bounds.max.x > limit || bounds.min.y < -limit || bounds.max.y > limit
                || bounds.min.z < -limit || bounds.max.z > limit)
            {
                errors.Add($"{name}: bounds x {bounds.min.x:F2}..{bounds.max.x:F2}, y {bounds.min.y:F2}..{bounds.max.y:F2}, z {bounds.min.z:F2}..{bounds.max.z:F2} leave the unit box around the pivot (the game scales it to footprint x height)");
            }
        }
        return errors.Count == before;
    }

    public static bool Validate(GameObject prefab, int level, List<string> errors)
    {
        int before = errors.Count;
        string name = prefab != null ? prefab.name : "(null)";
        if (prefab == null)
        {
            errors.Add("empty slot entry");
            return false;
        }

        if (prefab.layer != BuildingsLayer) errors.Add($"{name}: layer {prefab.layer}, must be {BuildingsLayer} (Buildings)");
        if (prefab.GetComponent<Collider>() == null) errors.Add($"{name}: no collider on the root (selection raycasts need it)");

        var materials = new HashSet<Material>();
        bool hasRenderer = false;
        foreach (Renderer renderer in prefab.GetComponentsInChildren<Renderer>(true))
        {
            hasRenderer = true;
            foreach (Material material in renderer.sharedMaterials)
            {
                if (material == null) errors.Add($"{name}/{renderer.name}: missing material");
                else materials.Add(material);
            }
        }
        if (!hasRenderer) errors.Add($"{name}: no renderers");
        if (materials.Count > MaxMaterials) errors.Add($"{name}: {materials.Count} materials, at most {MaxMaterials}");

        if (!TryGetBounds(prefab, null, out Bounds bounds))
        {
            errors.Add($"{name}: no meshes to measure");
        }
        else
        {
            float half = 0.5f + CellMargin;
            if (bounds.min.x < -half || bounds.max.x > half || bounds.min.z < -half || bounds.max.z > half)
            {
                errors.Add($"{name}: footprint x {bounds.min.x:F2}..{bounds.max.x:F2}, z {bounds.min.z:F2}..{bounds.max.z:F2} leaves the 1x1 cell around the pivot");
            }
            if (Mathf.Abs(bounds.min.y) > GroundTolerance)
            {
                errors.Add($"{name}: lowest point at y = {bounds.min.y:F2}, the pivot must be at ground level");
            }
            int i = Mathf.Clamp(level, 1, 3) - 1;
            if (bounds.max.y < s_MinHeight[i] || bounds.max.y > s_MaxHeight[i])
            {
                errors.Add($"{name}: height {bounds.max.y:F2} outside {s_MinHeight[i]:F2}..{s_MaxHeight[i]:F2} for level {level}");
            }
        }

        return errors.Count == before;
    }

    // Bounds of every mesh under the root, in the root's own space (the root's transform is ignored: the
    // game places the instance itself). Works on prefab assets, where Renderer.bounds is not reliable.
    private static bool TryGetBounds(GameObject root, Transform exclude, out Bounds bounds)
    {
        bounds = default;
        bool any = false;
        Matrix4x4 toRoot = root.transform.worldToLocalMatrix;
        foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
        {
            Mesh mesh = filter.sharedMesh;
            if (mesh == null) continue;
            if (exclude != null && filter.transform.IsChildOf(exclude)) continue;
            Matrix4x4 matrix = toRoot * filter.transform.localToWorldMatrix;
            Bounds local = mesh.bounds;
            for (int c = 0; c < 8; c++)
            {
                Vector3 corner = local.center + Vector3.Scale(local.extents, new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1));
                Vector3 point = matrix.MultiplyPoint3x4(corner);
                if (!any) { bounds = new Bounds(point, Vector3.zero); any = true; }
                else bounds.Encapsulate(point);
            }
        }
        return any;
    }

    [MenuItem("CityBuilder/Validate Art")]
    public static void ValidateAll()
    {
        var errors = new List<string>();
        int checkedPrefabs = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:AgeVisualSet"))
        {
            var set = AssetDatabase.LoadAssetAtPath<ScriptableObject>(AssetDatabase.GUIDToAssetPath(guid));
            var so = new SerializedObject(set);
            foreach (string zone in Tables)
            {
                SerializedProperty levels = so.FindProperty(zone);
                for (int level = 0; level < levels.arraySize; level++)
                {
                    SerializedProperty prefabs = levels.GetArrayElementAtIndex(level).FindPropertyRelative("Prefabs");
                    for (int p = 0; p < prefabs.arraySize; p++)
                    {
                        var prefab = prefabs.GetArrayElementAtIndex(p).objectReferenceValue as GameObject;
                        checkedPrefabs++;
                        var slotErrors = new List<string>();
                        if (!Validate(prefab, level + 1, slotErrors))
                        {
                            foreach (string error in slotErrors) errors.Add($"{set.name} {zone} L{level + 1}: {error}");
                        }
                    }
                }
            }
        }
        if (errors.Count == 0) Debug.Log($"Art contract: {checkedPrefabs} prefab slot(s) OK.");
        else Debug.LogError($"Art contract: {errors.Count} problem(s) in {checkedPrefabs} prefab slot(s):\n" + string.Join("\n", errors));
    }
}
