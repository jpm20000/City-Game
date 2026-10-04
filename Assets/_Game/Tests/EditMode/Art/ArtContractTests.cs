using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

// M18a: the prefab contract for AgeVisualSet slots. ArtContract lives in Assembly-CSharp-Editor and the
// visual sets in Assembly-CSharp, which test assemblies can't reference, so both are reached by name.
public sealed class ArtContractTests
{
    private static MethodInfo s_Validate;
    private readonly List<GameObject> m_Made = new();

    [SetUp]
    public void SetUp()
    {
        Type type = Type.GetType("ArtContract, Assembly-CSharp-Editor");
        Assert.IsNotNull(type, "ArtContract not found in Assembly-CSharp-Editor");
        s_Validate = type.GetMethod("Validate", BindingFlags.Public | BindingFlags.Static);
        Assert.IsNotNull(s_Validate, "ArtContract.Validate");
    }

    [TearDown]
    public void TearDown()
    {
        foreach (GameObject go in m_Made) if (go != null) UnityEngine.Object.DestroyImmediate(go);
        m_Made.Clear();
    }

    private static bool Validate(GameObject prefab, int level, List<string> errors)
    {
        return (bool)s_Validate.Invoke(null, new object[] { prefab, level, errors });
    }

    // A one-cube building: footprint 0.6 x 0.6, height `height`, on layer 9 with a root collider.
    private GameObject Make(float height, float footprint = 0.6f, int layer = ArtLayer, bool collider = true, float lift = 0f)
    {
        var root = new GameObject("TestBuilding") { layer = layer };
        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cube);
        UnityEngine.Object.DestroyImmediate(body.GetComponent<Collider>());
        body.transform.SetParent(root.transform, false);
        body.transform.localScale = new Vector3(footprint, height, footprint);
        body.transform.localPosition = new Vector3(0f, height * 0.5f + lift, 0f);
        if (collider) root.AddComponent<BoxCollider>();
        m_Made.Add(root);
        return root;
    }

    private const int ArtLayer = 9;

    [Test]
    public void AGoodBuilding_Passes()
    {
        var errors = new List<string>();
        Assert.IsTrue(Validate(Make(0.9f), 2, errors), string.Join("; ", errors));
        Assert.IsEmpty(errors);
    }

    [Test]
    public void WrongLayer_NoCollider_TooWide_Floating_AndWrongHeight_AreEachCaught()
    {
        var errors = new List<string>();
        Assert.IsFalse(Validate(Make(0.9f, layer: 0), 2, errors));
        StringAssert.Contains("layer", string.Join(";", errors));

        errors.Clear();
        Assert.IsFalse(Validate(Make(0.9f, collider: false), 2, errors));
        StringAssert.Contains("collider", string.Join(";", errors));

        errors.Clear();
        Assert.IsFalse(Validate(Make(0.9f, footprint: 1.3f), 2, errors));
        StringAssert.Contains("1x1 cell", string.Join(";", errors));

        errors.Clear();
        Assert.IsFalse(Validate(Make(0.9f, lift: 0.3f), 2, errors));
        StringAssert.Contains("ground level", string.Join(";", errors));

        errors.Clear();
        Assert.IsFalse(Validate(Make(0.2f), 3, errors));
        StringAssert.Contains("height", string.Join(";", errors));
        errors.Clear();
        Assert.IsFalse(Validate(Make(2.5f), 2, errors));
        StringAssert.Contains("height", string.Join(";", errors));
    }

    [Test]
    public void ThreeMaterials_AreOneTooMany()
    {
        GameObject building = Make(0.9f);
        var renderer = building.GetComponentInChildren<MeshRenderer>();
        renderer.sharedMaterials = new[] { new Material(Shader.Find("Sprites/Default")), new Material(Shader.Find("Sprites/Default")), new Material(Shader.Find("Sprites/Default")) };
        var errors = new List<string>();
        Assert.IsFalse(Validate(building, 2, errors));
        StringAssert.Contains("materials", string.Join(";", errors));
        foreach (Material m in renderer.sharedMaterials) UnityEngine.Object.DestroyImmediate(m);
    }

    [Test]
    public void EveryPrefabInEveryVisualSet_MeetsTheContract()
    {
        var errors = new List<string>();
        int slots = 0;
        string[] guids = AssetDatabase.FindAssets("t:AgeVisualSet");
        Assert.IsNotEmpty(guids, "no AgeVisualSet assets");
        foreach (string guid in guids)
        {
            var set = AssetDatabase.LoadAssetAtPath<ScriptableObject>(AssetDatabase.GUIDToAssetPath(guid));
            var so = new SerializedObject(set);
            foreach (string zone in new[] { "m_Residential", "m_Commercial", "m_Industrial" })
            {
                SerializedProperty levels = so.FindProperty(zone);
                for (int level = 0; level < levels.arraySize; level++)
                {
                    SerializedProperty prefabs = levels.GetArrayElementAtIndex(level).FindPropertyRelative("Prefabs");
                    for (int p = 0; p < prefabs.arraySize; p++)
                    {
                        slots++;
                        var prefab = prefabs.GetArrayElementAtIndex(p).objectReferenceValue as GameObject;
                        var slotErrors = new List<string>();
                        if (!Validate(prefab, level + 1, slotErrors))
                        {
                            foreach (string error in slotErrors) errors.Add($"{set.name} {zone} L{level + 1}: {error}");
                        }
                    }
                }
            }
        }
        Assert.IsEmpty(errors, string.Join("\n", errors));
        TestContext.WriteLine($"{slots} prefab slot(s) checked");
    }
}
