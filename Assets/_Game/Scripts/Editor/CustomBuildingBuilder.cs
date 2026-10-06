using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// M25b: what the Add Custom Building wizard does, as plain code so a test (and a RunCommand) can run it too.
// Writes a prefab that meets the placeable contract (ArtContract.ValidatePlaceable), a BuildingDefinition marked
// custom, and the BuildingDatabase entry; validates before anything is added to the database and rolls back the
// assets it just made when the check fails. Running it again on an existing custom Id updates that building in place.
public sealed class CustomBuildingRequest
{
    public string Id = "";
    public string DisplayName = "";
    [Tooltip("The model: a prefab asset or a scene object (a model asset such as an FBX works too).")]
    public GameObject Source;
    [Tooltip("True: build the game prefab around Source, fitting it to the unit box. False: Source already is a game prefab and is used as it is.")]
    public bool WrapModel = true;
    public Vector2Int Size = Vector2Int.one;
    public float Height = 1f;
    public int Cost = 500;
    public float UpkeepPerDay = 1f;
    public BuildingCategory Category = BuildingCategory.Service;
    public int HousingCapacity;
    public int JobsProvided;
    public float HappinessEffect;
    public string RequiredTech = "";
    public Sprite Icon;
    // Test hooks: a database and folders other than the real ones.
    public BuildingDatabase Database;
    public string DefinitionFolder = CustomBuildingBuilder.DefaultDefinitionFolder;
    public string PrefabFolder = CustomBuildingBuilder.DefaultPrefabFolder;
}

public static class CustomBuildingBuilder
{
    public const string DatabasePath = "Assets/_Game/Scriptables/Buildings/BuildingDatabase.asset";
    public const string DefaultDefinitionFolder = "Assets/_Game/Scriptables/Buildings/Custom";
    public const string DefaultPrefabFolder = "Assets/_Game/Prefabs/Custom";

    public static List<string> AllTechIds()
    {
        var ids = new List<string>();
        foreach (string guid in AssetDatabase.FindAssets("t:TechDatabase"))
        {
            var db = AssetDatabase.LoadAssetAtPath<TechDatabase>(AssetDatabase.GUIDToAssetPath(guid));
            if (db == null) continue;
            foreach (TechDefinition tech in db.Techs) if (tech != null) { if (!ids.Contains(tech.Id)) ids.Add(tech.Id); }
        }
        return ids;
    }

    // Lower-case, spaces and separators to underscores: "Market Hall" -> "market_hall".
    public static string NormalizeId(string text)
    {
        var sb = new System.Text.StringBuilder();
        foreach (char c in (text ?? "").Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c) && c < 128) sb.Append(c);
            else if (sb.Length > 0 && sb[sb.Length - 1] != '_') sb.Append('_');
        }
        return sb.ToString().Trim('_');
    }

    // Returns the definition on success, null with the problems in `errors` otherwise (nothing is left behind then,
    // except the edits to an existing custom building, which are not undone).
    public static BuildingDefinition Create(CustomBuildingRequest request, ICollection<string> techIds, List<string> errors)
    {
        string id = NormalizeId(request.Id);
        if (id.Length == 0)
        {
            errors.Add("Id: enter an id (letters and digits)");
            return null;
        }
        if (request.Source == null)
        {
            errors.Add("Model: pick a prefab or model");
            return null;
        }
        BuildingDatabase database = request.Database != null ? request.Database : AssetDatabase.LoadAssetAtPath<BuildingDatabase>(DatabasePath);
        if (database == null)
        {
            errors.Add($"no BuildingDatabase at {DatabasePath}");
            return null;
        }

        BuildingDefinition existing = database.GetById(id);
        if (existing != null && !existing.IsCustom)
        {
            errors.Add($"Id '{id}' belongs to shipped content; pick another");
            return null;
        }

        string fileName = SafeFileName(string.IsNullOrWhiteSpace(request.DisplayName) ? id : request.DisplayName);
        var created = new List<string>();
        EnsureFolder(request.DefinitionFolder);
        EnsureFolder(request.PrefabFolder);

        // Prefab: wrapped around the model, or the given prefab as is.
        GameObject prefab;
        if (request.WrapModel)
        {
            string prefabPath = existing != null && existing.Prefab != null && AssetDatabase.GetAssetPath(existing.Prefab).StartsWith(request.PrefabFolder)
                ? AssetDatabase.GetAssetPath(existing.Prefab)
                : AssetDatabase.GenerateUniqueAssetPath($"{request.PrefabFolder}/{fileName}.prefab");
            bool isNew = !File.Exists(prefabPath);
            prefab = WrapModel(request.Source, fileName, prefabPath);
            if (isNew) created.Add(prefabPath);
        }
        else
        {
            if (!EditorUtility.IsPersistent(request.Source))
            {
                errors.Add("Model: with 'already a game prefab' the source must be a prefab asset");
                return null;
            }
            prefab = request.Source;
        }

        // Definition.
        string defPath = existing != null ? AssetDatabase.GetAssetPath(existing) : AssetDatabase.GenerateUniqueAssetPath($"{request.DefinitionFolder}/{fileName}.asset");
        BuildingDefinition def = existing;
        if (def == null)
        {
            def = ScriptableObject.CreateInstance<BuildingDefinition>();
            AssetDatabase.CreateAsset(def, defPath);
            created.Add(defPath);
        }
        var so = new SerializedObject(def);
        so.FindProperty("m_Id").stringValue = id;
        so.FindProperty("m_DisplayName").stringValue = string.IsNullOrWhiteSpace(request.DisplayName) ? id : request.DisplayName.Trim();
        so.FindProperty("m_Category").enumValueIndex = (int)request.Category;
        so.FindProperty("m_Icon").objectReferenceValue = request.Icon;
        so.FindProperty("m_Prefab").objectReferenceValue = prefab;
        so.FindProperty("m_Size").vector2IntValue = request.Size;
        so.FindProperty("m_Height").floatValue = request.Height;
        so.FindProperty("m_Cost").intValue = request.Cost;
        so.FindProperty("m_UpkeepPerDay").floatValue = request.UpkeepPerDay;
        so.FindProperty("m_HousingCapacity").intValue = request.HousingCapacity;
        so.FindProperty("m_JobsProvided").intValue = request.JobsProvided;
        so.FindProperty("m_HappinessEffect").floatValue = request.HappinessEffect;
        so.FindProperty("m_RequiredTech").stringValue = request.RequiredTech ?? "";
        so.FindProperty("m_IsCustom").boolValue = true;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(def);

        // Validate before the database sees it.
        var problems = new List<string>();
        var others = new List<BuildingDefinition>(database.Entries);
        if (!CustomBuildingValidator.Validate(def, others, techIds, problems))
        {
            errors.AddRange(problems);
            foreach (string path in created) AssetDatabase.DeleteAsset(path);
            return null;
        }

        var dbSo = new SerializedObject(database);
        SerializedProperty entries = dbSo.FindProperty("m_Entries");
        bool listed = false;
        for (int i = 0; i < entries.arraySize; i++) if (entries.GetArrayElementAtIndex(i).objectReferenceValue == def) listed = true;
        if (!listed)
        {
            entries.arraySize++;
            entries.GetArrayElementAtIndex(entries.arraySize - 1).objectReferenceValue = def;
            dbSo.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(database);
        }
        AssetDatabase.SaveAssets();
        return def;
    }

    // The game prefab: a unit box (BuildingInstance scales it to footprint x height) holding a copy of the model fitted
    // to fill it on all three axes, layer 9, a root collider and a BuildingInstance. Written to prefabPath (overwrites).
    private static GameObject WrapModel(GameObject source, string name, string prefabPath)
    {
        var root = new GameObject(name);
        try
        {
            GameObject model = EditorUtility.IsPersistent(source) ? (GameObject)PrefabUtility.InstantiatePrefab(source) : Object.Instantiate(source);
            model.name = "Model";
            model.transform.SetParent(root.transform, false);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            foreach (Collider collider in model.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(collider);

            if (TryRendererBounds(root, out Bounds bounds))
            {
                Vector3 size = bounds.size;
                model.transform.localScale = Vector3.Scale(model.transform.localScale,
                    new Vector3(1f / Mathf.Max(size.x, 1e-4f), 1f / Mathf.Max(size.y, 1e-4f), 1f / Mathf.Max(size.z, 1e-4f)));
                if (TryRendererBounds(root, out bounds)) model.transform.position -= bounds.center;
            }

            SetLayer(root.transform, ArtContract.BuildingsLayer);
            var box = root.AddComponent<BoxCollider>();
            box.size = Vector3.one;
            box.center = Vector3.zero;
            root.AddComponent<BuildingInstance>();
            return PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    private static bool TryRendererBounds(GameObject root, out Bounds bounds)
    {
        bounds = default;
        bool any = false;
        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (!any) { bounds = renderer.bounds; any = true; }
            else bounds.Encapsulate(renderer.bounds);
        }
        return any;
    }

    private static void SetLayer(Transform t, int layer)
    {
        t.gameObject.layer = layer;
        foreach (Transform child in t) SetLayer(child, layer);
    }

    private static string SafeFileName(string text)
    {
        var sb = new System.Text.StringBuilder();
        foreach (char c in text.Trim())
        {
            if (char.IsLetterOrDigit(c) && c < 128) sb.Append(c);
            else if (c == ' ' || c == '_' || c == '-') sb.Append('_');
        }
        return sb.Length > 0 ? sb.ToString() : "CustomBuilding";
    }

    private static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;
        string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
    }
}
