using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

// M11d: the shipped age / tech / building content. A broken edit to the tech tree fails here
// rather than in the game.
public sealed class ContentTests
{
    private const string AgeDatabasePath = "Assets/_Game/Scriptables/Ages/AgeDatabase.asset";
    private const string TechDatabasePath = "Assets/_Game/Scriptables/Techs/TechDatabase.asset";
    private const string BuildingDatabasePath = "Assets/_Game/Scriptables/Buildings/BuildingDatabase.asset";

    private AgeDatabase m_Ages;
    private TechDatabase m_Techs;
    private BalanceConfig m_Config;

    [SetUp]
    public void SetUp()
    {
        m_Ages = AssetDatabase.LoadAssetAtPath<AgeDatabase>(AgeDatabasePath);
        m_Techs = AssetDatabase.LoadAssetAtPath<TechDatabase>(TechDatabasePath);
        m_Config = ScriptableObject.CreateInstance<BalanceConfig>();
        Assert.IsNotNull(m_Ages, AgeDatabasePath);
        Assert.IsNotNull(m_Techs, TechDatabasePath);
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(m_Config);
    }

    [Test]
    public void AgeAndTechDatabases_Validate()
    {
        var errors = new List<string>();
        m_Ages.Validate(errors);
        m_Techs.Validate(m_Ages, errors);
        CollectionAssert.IsEmpty(errors, string.Join("\n", errors));

        Assert.AreEqual(4, m_Ages.Count);
        Assert.AreEqual(new[] { "medieval", "renaissance", "industrial", "modern" },
            new[] { m_Ages[0].Id, m_Ages[1].Id, m_Ages[2].Id, m_Ages[3].Id });
        Assert.AreEqual(2, m_Ages.Legacy);
        Assert.Contains(m_Techs.GetById("electricity"), m_Ages[m_Ages.Legacy].StartingTechs);
        Assert.IsTrue(m_Ages[m_Ages.Legacy].UpgradesNeedPower);
        Assert.AreEqual(30, m_Techs.Techs.Count);
    }

    // Starting in any age, something can be researched right away.
    [Test]
    public void EveryStartingAge_HasSomethingToResearch()
    {
        var tech = new TechSystem(m_Ages, m_Techs, m_Config);
        for (int age = 0; age < m_Ages.Count; age++)
        {
            tech.StartNew(age);
            bool any = false;
            foreach (TechDefinition t in m_Techs.Techs) any |= tech.CanResearch(t);
            Assert.IsTrue(any, m_Ages[age].Id);
        }
    }

    // From Medieval, researching whatever is available and advancing whenever allowed reaches the
    // last age with every tech done.
    [Test]
    public void WholeTree_IsReachableFromMedieval()
    {
        var tech = new TechSystem(m_Ages, m_Techs, m_Config);
        tech.StartNew(0);
        const int population = 1000000;

        for (int guard = 0; guard < 200 && tech.ResearchedCount < m_Techs.Techs.Count; guard++)
        {
            bool progressed = false;
            foreach (TechDefinition t in m_Techs.Techs)
            {
                if (!tech.CanResearch(t)) continue;
                tech.SetActive(t);
                tech.Step(t.Cost);
                progressed = true;
            }
            if (!progressed)
            {
                Assert.IsTrue(tech.SetActiveAdvance(population), $"stuck in {tech.CurrentAgeDefinition.Id}");
                tech.Step(tech.Active.Cost);
            }
        }

        Assert.AreEqual(m_Ages.Count - 1, tech.CurrentAge);
        Assert.AreEqual(m_Techs.Techs.Count, tech.ResearchedCount);
    }

    // BuildingDefinition lives in Assembly-CSharp, which test assemblies can't reference, so its
    // fields are read through SerializedObject.
    [Test]
    public void BuildingRequiredTechs_ExistInTheTechDatabase()
    {
        var database = AssetDatabase.LoadAssetAtPath<ScriptableObject>(BuildingDatabasePath);
        Assert.IsNotNull(database, BuildingDatabasePath);
        SerializedProperty entries = new SerializedObject(database).FindProperty("m_Entries");
        Assert.IsNotNull(entries, "BuildingDatabase.m_Entries");

        var research = new Dictionary<string, float>();
        var required = new Dictionary<string, string>();
        for (int i = 0; i < entries.arraySize; i++)
        {
            Object def = entries.GetArrayElementAtIndex(i).objectReferenceValue;
            Assert.IsNotNull(def, $"entry {i}");
            var so = new SerializedObject(def);
            string id = so.FindProperty("m_Id").stringValue;
            string tech = so.FindProperty("m_RequiredTech").stringValue;
            if (!string.IsNullOrEmpty(tech)) Assert.IsNotNull(m_Techs.GetById(tech), $"{id} needs unknown tech '{tech}'");
            required[id] = tech;
            research[id] = so.FindProperty("m_ResearchPerDay").floatValue;
        }

        Assert.AreEqual("commons", required["park"]);
        Assert.AreEqual("electricity", required["power_plant"]);
        Assert.AreEqual("monasticism", required["monastery"]);
        Assert.AreEqual("academies", required["academy"]);
        Assert.AreEqual(2f, research["monastery"]);
        Assert.AreEqual(5f, research["academy"]);
    }
}
