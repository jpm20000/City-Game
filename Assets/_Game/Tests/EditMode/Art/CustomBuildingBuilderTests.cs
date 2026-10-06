using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

// M25b: CustomBuildingBuilder (what the wizard runs) in a temporary folder with a temporary database, so the real
// BuildingDatabase is never touched. Types live in Assembly-CSharp / -Editor, which this assembly can't reference, so
// they are reached by name like the other art tests do.
public sealed class CustomBuildingBuilderTests
{
    private const string Root = "Assets/_Game/Tests/_TmpCustom";

    private static Type s_Request, s_Builder, s_Contract;
    private ScriptableObject m_Database;
    private readonly List<UnityEngine.Object> m_Made = new();

    [SetUp]
    public void SetUp()
    {
        s_Request = Type.GetType("CustomBuildingRequest, Assembly-CSharp-Editor");
        s_Builder = Type.GetType("CustomBuildingBuilder, Assembly-CSharp-Editor");
        s_Contract = Type.GetType("ArtContract, Assembly-CSharp-Editor");
        Assert.IsNotNull(s_Request);
        Assert.IsNotNull(s_Builder);
        Assert.IsNotNull(s_Contract);

        if (!AssetDatabase.IsValidFolder(Root)) AssetDatabase.CreateFolder("Assets/_Game/Tests", "_TmpCustom");
        m_Database = ScriptableObject.CreateInstance(Type.GetType("BuildingDatabase, Assembly-CSharp"));
        AssetDatabase.CreateAsset(m_Database, Root + "/Db.asset");
    }

    [TearDown]
    public void TearDown()
    {
        foreach (UnityEngine.Object o in m_Made) if (o != null) UnityEngine.Object.DestroyImmediate(o);
        m_Made.Clear();
        AssetDatabase.DeleteAsset(Root);
        AssetDatabase.Refresh();
    }

    // A two-material model, deliberately not unit-sized and off-centre, as a modder's would be.
    private GameObject Model(bool assetMaterials = true)
    {
        var root = new GameObject("Model");
        for (int i = 0; i < 2; i++)
        {
            GameObject part = GameObject.CreatePrimitive(PrimitiveType.Cube);
            part.transform.SetParent(root.transform, false);
            part.transform.localScale = new Vector3(3f, 1f + i, 2f);
            part.transform.localPosition = new Vector3(5f, 4f + i * 1.5f, -2f);
            var material = new Material(Shader.Find("Sprites/Default")) { name = "TmpMat" + i };
            if (assetMaterials) AssetDatabase.CreateAsset(material, $"{Root}/TmpMat{i}.mat");
            part.GetComponent<MeshRenderer>().sharedMaterial = material;
        }
        m_Made.Add(root);
        return root;
    }

    private object Request(string id, GameObject model, string tech = "", bool wrap = true)
    {
        object r = Activator.CreateInstance(s_Request);
        void Set(string field, object value) => s_Request.GetField(field).SetValue(r, value);
        Set("Id", id);
        Set("DisplayName", id.Replace('_', ' '));
        Set("Source", model);
        Set("WrapModel", wrap);
        Set("Size", new Vector2Int(2, 2));
        Set("Height", 0.8f);
        Set("Cost", 700);
        Set("JobsProvided", 6);
        Set("RequiredTech", tech);
        Set("Database", m_Database);
        Set("DefinitionFolder", Root + "/Defs");
        Set("PrefabFolder", Root + "/Prefabs");
        return r;
    }

    private ScriptableObject Create(object request, ICollection<string> techs, List<string> errors)
    {
        return (ScriptableObject)s_Builder.GetMethod("Create").Invoke(null, new[] { request, techs, errors });
    }

    private static int EntryCount(ScriptableObject db) => new SerializedObject(db).FindProperty("m_Entries").arraySize;

    [Test]
    public void NormalizeId_MakesASafeId()
    {
        MethodInfo normalize = s_Builder.GetMethod("NormalizeId");
        Assert.AreEqual("market_hall", normalize.Invoke(null, new object[] { "  Market Hall " }));
        Assert.AreEqual("a_b_2", normalize.Invoke(null, new object[] { "A/b -- 2!" }));
        Assert.AreEqual("", normalize.Invoke(null, new object[] { "  ?! " }));
    }

    [Test]
    public void Create_WrapsTheModel_WritesTheAssets_AndListsItInTheDatabase()
    {
        var errors = new List<string>();
        ScriptableObject def = Create(Request("market_hall", Model()), new HashSet<string> { "banking" }, errors);
        Assert.IsNotNull(def, string.Join("; ", errors));

        var so = new SerializedObject(def);
        Assert.AreEqual("market_hall", so.FindProperty("m_Id").stringValue);
        Assert.IsTrue(so.FindProperty("m_IsCustom").boolValue);
        Assert.AreEqual(6, so.FindProperty("m_JobsProvided").intValue);
        Assert.AreEqual(1, EntryCount(m_Database));

        var prefab = so.FindProperty("m_Prefab").objectReferenceValue as GameObject;
        Assert.IsNotNull(prefab);
        Assert.IsTrue(AssetDatabase.GetAssetPath(prefab).StartsWith(Root + "/Prefabs"));
        var contractErrors = new List<string>();
        var args = new object[] { prefab, contractErrors };
        Assert.IsTrue((bool)s_Contract.GetMethod("ValidatePlaceable").Invoke(null, args), string.Join("; ", contractErrors));
        Assert.AreEqual(2, prefab.GetComponentsInChildren<Renderer>(true).Length);
    }

    [Test]
    public void CreatingTheSameIdAgain_UpdatesInPlace()
    {
        var errors = new List<string>();
        ScriptableObject first = Create(Request("market_hall", Model()), null, errors);
        Assert.IsNotNull(first, string.Join("; ", errors));

        object again = Request("market_hall", Model());
        s_Request.GetField("Cost").SetValue(again, 900);
        ScriptableObject second = Create(again, null, errors);
        Assert.AreSame(first, second);
        Assert.AreEqual(900, new SerializedObject(second).FindProperty("m_Cost").intValue);
        Assert.AreEqual(1, EntryCount(m_Database));
        Assert.AreEqual(1, AssetDatabase.FindAssets("t:BuildingDefinition", new[] { Root }).Length);
    }

    [Test]
    public void AFailedCheck_AddsNothing_AndLeavesNoAssets()
    {
        var errors = new List<string>();
        ScriptableObject def = Create(Request("bad", Model(), tech: "no_such_tech"), new HashSet<string> { "banking" }, errors);
        Assert.IsNull(def);
        StringAssert.Contains("RequiredTech", string.Join(";", errors));
        Assert.AreEqual(0, EntryCount(m_Database));
        Assert.AreEqual(0, AssetDatabase.FindAssets("t:BuildingDefinition", new[] { Root }).Length);
        Assert.AreEqual(0, AssetDatabase.FindAssets("t:Prefab", new[] { Root }).Length);
    }

    [Test]
    public void AModelWithSceneOnlyMaterials_IsRefusedAndRolledBack()
    {
        // Materials that are not assets can't be saved into a prefab; the check names the problem.
        var errors = new List<string>();
        Assert.IsNull(Create(Request("loose", Model(assetMaterials: false)), null, errors));
        StringAssert.Contains("material", string.Join(";", errors));
        Assert.AreEqual(0, EntryCount(m_Database));
        Assert.AreEqual(0, AssetDatabase.FindAssets("t:Prefab", new[] { Root }).Length);
    }

    [Test]
    public void NoModelOrNoId_IsRefusedWithAMessage()
    {
        var errors = new List<string>();
        Assert.IsNull(Create(Request("x", null), null, errors));
        StringAssert.Contains("Model", string.Join(";", errors));
        errors.Clear();
        Assert.IsNull(Create(Request("  ", Model()), null, errors));
        StringAssert.Contains("Id", string.Join(";", errors));
    }

    [Test]
    public void AShippedId_IsNeverOverwritten()
    {
        // A non-custom entry in the temp database stands in for shipped content.
        var shipped = ScriptableObject.CreateInstance(Type.GetType("BuildingDefinition, Assembly-CSharp"));
        var sso = new SerializedObject(shipped);
        sso.FindProperty("m_Id").stringValue = "hospital";
        sso.ApplyModifiedPropertiesWithoutUndo();
        AssetDatabase.CreateAsset(shipped, Root + "/Shipped.asset");
        var dbSo = new SerializedObject(m_Database);
        dbSo.FindProperty("m_Entries").arraySize = 1;
        dbSo.FindProperty("m_Entries").GetArrayElementAtIndex(0).objectReferenceValue = shipped;
        dbSo.ApplyModifiedPropertiesWithoutUndo();

        var errors = new List<string>();
        Assert.IsNull(Create(Request("hospital", Model()), null, errors));
        StringAssert.Contains("shipped", string.Join(";", errors));
    }
}
