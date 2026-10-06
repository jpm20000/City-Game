using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

// M25a: the rules a custom placeable building must pass (CustomBuildingValidator, Assembly-CSharp-Editor, reached by
// name like ArtContractTests does; BuildingDefinition lives in Assembly-CSharp and the tech data in the Simulation
// asmdef, neither of which this assembly references, so they are made and read through ScriptableObject / SerializedObject).
// The shipped definitions are the reference: they must all pass.
public sealed class CustomBuildingValidatorTests
{
    private const string DatabasePath = "Assets/_Game/Scriptables/Buildings/BuildingDatabase.asset";

    private const int CategoryZone = 0, CategoryService = 2;

    private static MethodInfo s_Validate;
    private static Type s_DefinitionType;
    private readonly List<UnityEngine.Object> m_Made = new();

    [SetUp]
    public void SetUp()
    {
        Type type = Type.GetType("CustomBuildingValidator, Assembly-CSharp-Editor");
        Assert.IsNotNull(type, "CustomBuildingValidator not found in Assembly-CSharp-Editor");
        s_Validate = type.GetMethod("Validate", BindingFlags.Public | BindingFlags.Static);
        Assert.IsNotNull(s_Validate, "CustomBuildingValidator.Validate");
        s_DefinitionType = Type.GetType("BuildingDefinition, Assembly-CSharp");
        Assert.IsNotNull(s_DefinitionType, "BuildingDefinition");
    }

    [TearDown]
    public void TearDown()
    {
        foreach (UnityEngine.Object o in m_Made) if (o != null) UnityEngine.Object.DestroyImmediate(o);
        m_Made.Clear();
    }

    // others: the database entries as the typed IEnumerable<BuildingDefinition> the validator takes (null = none).
    private static bool Validate(ScriptableObject def, IEnumerable<ScriptableObject> others, ICollection<string> techs, List<string> errors)
    {
        object typed = null;
        if (others != null)
        {
            var list = new List<ScriptableObject>(others);
            Array array = Array.CreateInstance(s_DefinitionType, list.Count);
            for (int i = 0; i < list.Count; i++) array.SetValue(list[i], i);
            typed = array;
        }
        return (bool)s_Validate.Invoke(null, new object[] { def, typed, techs, errors });
    }

    private static List<ScriptableObject> DatabaseEntries(out ScriptableObject database)
    {
        database = AssetDatabase.LoadAssetAtPath<ScriptableObject>(DatabasePath);
        Assert.IsNotNull(database, DatabasePath);
        SerializedProperty entries = new SerializedObject(database).FindProperty("m_Entries");
        var list = new List<ScriptableObject>();
        for (int i = 0; i < entries.arraySize; i++)
        {
            if (entries.GetArrayElementAtIndex(i).objectReferenceValue is ScriptableObject def) list.Add(def);
        }
        return list;
    }

    private static HashSet<string> TechIds()
    {
        var set = new HashSet<string>();
        foreach (string guid in AssetDatabase.FindAssets("t:TechDatabase"))
        {
            var db = AssetDatabase.LoadAssetAtPath<ScriptableObject>(AssetDatabase.GUIDToAssetPath(guid));
            SerializedProperty techs = new SerializedObject(db).FindProperty("m_Techs");
            for (int i = 0; i < techs.arraySize; i++)
            {
                var tech = techs.GetArrayElementAtIndex(i).objectReferenceValue;
                if (tech != null) set.Add(new SerializedObject(tech).FindProperty("m_Id").stringValue);
            }
        }
        Assert.IsNotEmpty(set, "no TechDatabase");
        return set;
    }

    // A placeable prefab as the game wants it: a unit cube centred on the pivot (BuildingInstance scales it to the
    // footprint x height), layer 9, a collider and a BuildingInstance on the root. `extent` is the cube's edge.
    private GameObject MakePrefab(float extent = 1f, int layer = 9, bool instance = true)
    {
        var root = new GameObject("TestPrefab") { layer = layer };
        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cube);
        UnityEngine.Object.DestroyImmediate(body.GetComponent<Collider>());
        body.transform.SetParent(root.transform, false);
        body.transform.localScale = Vector3.one * extent;
        root.AddComponent<BoxCollider>();
        if (instance) root.AddComponent(Type.GetType("BuildingInstance, Assembly-CSharp"));
        m_Made.Add(root);
        return root;
    }

    private ScriptableObject MakeDefinition(string id, Vector2Int size, GameObject prefab, string tech = "", int cost = 100,
        int category = CategoryService)
    {
        var def = ScriptableObject.CreateInstance(s_DefinitionType);
        def.name = "Test_" + id;
        var so = new SerializedObject(def);
        so.FindProperty("m_Id").stringValue = id;
        so.FindProperty("m_DisplayName").stringValue = id;
        so.FindProperty("m_Category").enumValueIndex = category;
        so.FindProperty("m_Size").vector2IntValue = size;
        so.FindProperty("m_Cost").intValue = cost;
        so.FindProperty("m_RequiredTech").stringValue = tech;
        so.FindProperty("m_Prefab").objectReferenceValue = prefab;
        so.ApplyModifiedPropertiesWithoutUndo();
        m_Made.Add(def);
        return def;
    }

    [Test]
    public void EveryShippedPlaceable_Passes()
    {
        List<ScriptableObject> entries = DatabaseEntries(out _);
        HashSet<string> techs = TechIds();
        var errors = new List<string>();
        int checkedCount = 0;
        foreach (ScriptableObject def in entries)
        {
            int category = new SerializedObject(def).FindProperty("m_Category").enumValueIndex;
            if (category == CategoryZone || category == 1) continue;   // Zone, Road: tools, not placeables
            checkedCount++;
            Validate(def, entries, techs, errors);
        }
        TestContext.WriteLine($"{checkedCount} shipped placeable(s) checked");
        Assert.Greater(checkedCount, 10);
        Assert.IsEmpty(errors, string.Join("\n", errors));
    }

    [Test]
    public void AGoodCustomBuilding_Passes_AtAnySize()
    {
        var errors = new List<string>();
        foreach (var size in new[] { Vector2Int.one, new Vector2Int(3, 2) })
        {
            ScriptableObject def = MakeDefinition("market_hall", size, MakePrefab());
            Assert.IsTrue(Validate(def, null, TechIds(), errors), string.Join("; ", errors));
        }
    }

    [Test]
    public void ArtOutsideTheUnitBox_AndAMissingBuildingInstance_AreCaught()
    {
        var errors = new List<string>();
        Assert.IsFalse(Validate(MakeDefinition("big", Vector2Int.one, MakePrefab(extent: 1.6f)), null, TechIds(), errors));
        StringAssert.Contains("unit box", string.Join(";", errors));

        errors.Clear();
        Assert.IsFalse(Validate(MakeDefinition("bare", Vector2Int.one, MakePrefab(instance: false)), null, TechIds(), errors));
        StringAssert.Contains("BuildingInstance", string.Join(";", errors));
    }

    [Test]
    public void EachDefinitionProblem_IsCaught()
    {
        HashSet<string> techs = TechIds();
        var errors = new List<string>();
        GameObject art = MakePrefab();

        Assert.IsFalse(Validate(MakeDefinition("", Vector2Int.one, art), null, techs, errors));
        StringAssert.Contains("empty Id", string.Join(";", errors));

        errors.Clear();
        Assert.IsFalse(Validate(MakeDefinition("has space", Vector2Int.one, art), null, techs, errors));
        StringAssert.Contains("separators", string.Join(";", errors));

        errors.Clear();
        Assert.IsFalse(Validate(MakeDefinition("x", Vector2Int.one, null), null, techs, errors));
        StringAssert.Contains("no prefab", string.Join(";", errors));

        errors.Clear();
        Assert.IsFalse(Validate(MakeDefinition("x", Vector2Int.one, art, tech: "no_such_tech"), null, techs, errors));
        StringAssert.Contains("RequiredTech", string.Join(";", errors));

        errors.Clear();
        Assert.IsFalse(Validate(MakeDefinition("x", Vector2Int.one, art, cost: -5), null, techs, errors));
        StringAssert.Contains("negative cost", string.Join(";", errors));

        errors.Clear();
        Assert.IsFalse(Validate(MakeDefinition("x", Vector2Int.zero, art), null, techs, errors));
        StringAssert.Contains("at least 1x1", string.Join(";", errors));

        errors.Clear();
        Assert.IsFalse(Validate(MakeDefinition("x", Vector2Int.one, art, category: CategoryZone), null, techs, errors));
        StringAssert.Contains("not placeable", string.Join(";", errors));

        errors.Clear();
        Assert.IsFalse(Validate(MakeDefinition("x", Vector2Int.one, MakePrefab(layer: 0)), null, techs, errors));
        StringAssert.Contains("layer", string.Join(";", errors));
    }

    [Test]
    public void ADuplicateId_IsCaught_ButNotAgainstItself()
    {
        GameObject art = MakePrefab();
        ScriptableObject a = MakeDefinition("same", Vector2Int.one, art);
        ScriptableObject b = MakeDefinition("same", Vector2Int.one, art);
        var errors = new List<string>();
        Assert.IsTrue(Validate(a, new[] { a }, null, errors), string.Join("; ", errors));
        Assert.IsFalse(Validate(a, new[] { a, b }, null, errors));
        StringAssert.Contains("already used", string.Join(";", errors));
    }
}
