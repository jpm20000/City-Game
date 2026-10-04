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
        Assert.Contains(m_Techs.GetById("waterworks"), m_Ages[m_Ages.Legacy].StartingTechs);
        CollectionAssert.AreEqual(new[] { WaterRule.Coverage, WaterRule.Coverage, WaterRule.Piped, WaterRule.Piped },
            new[] { m_Ages[0].Water, m_Ages[1].Water, m_Ages[2].Water, m_Ages[3].Water }, "wells, then piped water (M13)");
        Assert.AreEqual(AgeRules.Legacy.Water, m_Ages[m_Ages.Legacy].Rules.Water, "Industrial = the age-less rules");
        Assert.AreEqual(37, m_Techs.Techs.Count);   // + Aqueducts, Waterworks (M13); Town Watch, Herbalism, Universities, Antibiotics (M14); Macadam (M16)
    }

    // M16: the five road tiers exist, each tier's tech is in the tree, and an Industrial start keeps today's road.
    [Test]
    public void RoadTiers_Content()
    {
        Assert.AreEqual(5, m_Techs.RoadTiers.Count);
        for (int i = 0; i < 5; i++) Assert.AreEqual(i + 1, m_Techs.RoadTiers[i].Tier);
        Assert.IsNull(m_Techs.RoadTiers[0].RequiredTech, "dirt needs no tech");
        Assert.AreEqual("architecture", m_Techs.RoadTiers[1].RequiredTech.Id);
        Assert.AreEqual("macadam", m_Techs.RoadTiers[2].RequiredTech.Id);
        Assert.AreEqual("electric_trams", m_Techs.RoadTiers[3].RequiredTech.Id);
        Assert.AreEqual("automobiles", m_Techs.RoadTiers[4].RequiredTech.Id);
        Assert.IsFalse(m_Techs.RoadTiers[4].Frontage);

        var paved = m_Techs.RoadTiers[2];
        Assert.AreEqual(m_Config.RoadCost, paved.Cost, "Paved mirrors the legacy road");
        Assert.AreEqual(m_Config.RoadUpkeepPerDay, paved.UpkeepPerDay);
        Assert.AreEqual(m_Config.RoadCapacity, paved.Capacity);
        Assert.AreEqual(m_Config.RoadTravelCost, paved.TravelCost);

        Assert.Contains(m_Techs.GetById("macadam"), m_Ages[m_Ages.Legacy].StartingTechs);
        var tech = new TechSystem(m_Ages, m_Techs, m_Config);
        tech.StartNew(m_Ages.Legacy);
        var tiers = new RoadTiers(m_Config, m_Techs, t => tech.IsResearched(t));
        Assert.AreEqual(RoadTiers.Paved, tiers.BestStreetTier);
        tech.StartNew(0);
        Assert.AreEqual(RoadTiers.Dirt, new RoadTiers(m_Config, m_Techs, t => tech.IsResearched(t)).BestStreetTier);
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

    // AgeVisualSet (M11e) lives in Assembly-CSharp too, so it is found by type name and read through
    // SerializedObject. Every age needs exactly one set; Industrial's fallback style (the placeholder
    // blocks, used where a slot has no prefab) must stay plain. Since M18b the slots hold kit prefabs
    // (ArtContractTests checks them), so Industrial no longer looks like the pre-ages game.
    [Test]
    public void EveryAge_HasOneVisualSet_AndIndustrialFallbackIsPlain()
    {
        var sets = new Dictionary<string, SerializedObject>();
        foreach (string guid in AssetDatabase.FindAssets("t:AgeVisualSet"))
        {
            var so = new SerializedObject(AssetDatabase.LoadAssetAtPath<ScriptableObject>(AssetDatabase.GUIDToAssetPath(guid)));
            string ageId = so.FindProperty("m_AgeId").stringValue;
            Assert.IsTrue(m_Ages.IndexOf(ageId) >= 0, $"visual set for unknown age '{ageId}'");
            Assert.IsFalse(sets.ContainsKey(ageId), $"two visual sets for '{ageId}'");
            sets[ageId] = so;
        }
        for (int i = 0; i < m_Ages.Count; i++) Assert.IsTrue(sets.ContainsKey(m_Ages[i].Id), $"no visual set for '{m_Ages[i].Id}'");

        SerializedObject industrial = sets["industrial"];
        foreach (string zone in new[] { "m_Residential", "m_Commercial", "m_Industrial" })
        {
            SerializedProperty levels = industrial.FindProperty(zone);
            Assert.AreEqual(3, levels.arraySize, zone);
            for (int level = 0; level < 3; level++)
            {
                SerializedProperty style = levels.GetArrayElementAtIndex(level);
                Assert.AreEqual(0f, style.FindPropertyRelative("Tint").colorValue.a, $"{zone} L{level + 1} tint");
                Assert.AreEqual(0, style.FindPropertyRelative("Roof").enumValueIndex, $"{zone} L{level + 1} roof");
                Assert.AreEqual(0f, style.FindPropertyRelative("RoofColor").colorValue.a, $"{zone} L{level + 1} roof colour");
                Assert.AreEqual(1f, style.FindPropertyRelative("HeightScale").floatValue, $"{zone} L{level + 1} height");
            }
        }
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
        Assert.AreEqual(3f, research["monastery"]);
        Assert.AreEqual(5f, research["academy"]);
    }

    // M14: each civic line has exactly the planned building per age (an age is its RequiredTech's age),
    // every civic building has a reach, a strength in (0, 1] and a reachable tech, and strengths rise
    // with age within a line. Education buildings also produce research.
    [Test]
    public void CivicLines_HaveOneBuildingPerPlannedAge_GettingStronger()
    {
        var expected = new Dictionary<ServiceKind, string[]>
        {
            [ServiceKind.Order] = new[] { "watch_house", "constabulary", "police_station", null },
            [ServiceKind.Fire] = new[] { "bucket_brigade", "fire_engine_house", "fire_station", null },
            [ServiceKind.Health] = new[] { "apothecary", null, "hospital", "medical_centre" },
            [ServiceKind.Education] = new[] { "monastery", "academy", "university", "research_lab" },
        };
        var found = new Dictionary<ServiceKind, string[]>();
        var strength = new Dictionary<string, float>();
        var database = AssetDatabase.LoadAssetAtPath<ScriptableObject>(BuildingDatabasePath);
        SerializedProperty entries = new SerializedObject(database).FindProperty("m_Entries");
        for (int i = 0; i < entries.arraySize; i++)
        {
            var so = new SerializedObject(entries.GetArrayElementAtIndex(i).objectReferenceValue);
            var kind = (ServiceKind)so.FindProperty("m_CivicKind").intValue;
            if (kind == ServiceKind.None) continue;
            string id = so.FindProperty("m_Id").stringValue;
            TechDefinition tech = m_Techs.GetById(so.FindProperty("m_RequiredTech").stringValue);
            Assert.IsNotNull(tech, $"{id}: civic buildings need a tech");
            Assert.Greater(so.FindProperty("m_CivicRadius").intValue, 0, $"{id} reach");
            float s = so.FindProperty("m_CivicStrength").floatValue;
            Assert.That(s, Is.GreaterThan(0f).And.LessThanOrEqualTo(1f), $"{id} strength");
            Assert.IsNotNull(so.FindProperty("m_Prefab").objectReferenceValue, $"{id} prefab");
            if (kind == ServiceKind.Education) Assert.Greater(so.FindProperty("m_ResearchPerDay").floatValue, 0f, $"{id} research");

            if (!found.TryGetValue(kind, out string[] perAge)) found[kind] = perAge = new string[m_Ages.Count];
            Assert.IsNull(perAge[tech.Age], $"two {kind} buildings in age {tech.Age}: {perAge[tech.Age]} and {id}");
            perAge[tech.Age] = id;
            strength[id] = s;
        }

        foreach (KeyValuePair<ServiceKind, string[]> line in expected)
        {
            CollectionAssert.AreEqual(line.Value, found[line.Key], $"{line.Key} line");
            float last = 0f;
            foreach (string id in line.Value)
            {
                if (id == null) continue;
                Assert.Greater(strength[id], last, $"{id} must be stronger than the line's earlier tiers");
                last = strength[id];
            }
        }
    }

    [Test]
    public void Ages_OfferLoansThatGrowWithTheAge()
    {
        float previous = 0f;
        for (int i = 0; i < m_Ages.Count; i++)
        {
            Assert.Greater(m_Ages[i].LoanAmount, previous, $"{m_Ages[i].Id} loan amount");
            previous = m_Ages[i].LoanAmount;
        }
        Assert.AreEqual(m_Config.LoanAmount, m_Ages[m_Ages.Legacy].LoanAmount, "the age-less amount is the Industrial one");
    }

    [Test]
    public void Ordinances_AreSpreadOverTheAges_AndTheirEffectsAreWellFormed()
    {
        var perAge = new int[m_Ages.Count];
        foreach (OrdinanceDefinition ordinance in m_Techs.Ordinances)
        {
            perAge[ordinance.Age]++;
            Assert.IsNotEmpty(ordinance.Effects, ordinance.Id);
            foreach (TechEffect effect in ordinance.Effects)
            {
                Assert.AreNotEqual(TechEffectType.UnlockBuilding, effect.Type, $"{ordinance.Id}: ordinances do not unlock buildings");
                if (effect.Type == TechEffectType.CivicNeedMultiplier)
                {
                    Assert.IsTrue(effect.Target == "Order" || effect.Target == "Fire" || effect.Target == "Health", $"{ordinance.Id}: {effect.Target}");
                }
            }
        }
        for (int age = 0; age < perAge.Length; age++) Assert.That(perAge[age], Is.InRange(2, 5), $"ordinances in {m_Ages[age].Id}");
    }

    [Test]
    public void Events_AreValid_AndEveryAgeHasAtLeastThreeToDraw()
    {
        Assert.AreEqual(13, m_Techs.Events.Count);
        var perAge = new int[m_Ages.Count];
        foreach (EventDefinition definition in m_Techs.Events)
        {
            Assert.That(definition.Choices.Length, Is.InRange(2, 3), definition.Id);
            Assert.IsTrue(definition.Choices[definition.Choices.Length - 1].IsFree, $"{definition.Id}: the last choice is free");
            Assert.IsNotEmpty(definition.Text, definition.Id);
            for (int age = definition.MinAge; age <= definition.MaxAge; age++) perAge[age]++;
            foreach (EventChoice choice in definition.Choices)
            {
                Assert.IsNotEmpty(choice.Description, $"{definition.Id}: {choice.Label}");
                if (choice.Effects != null && choice.Effects.Length > 0) Assert.AreNotEqual(0, choice.Days, $"{definition.Id}: {choice.Label} needs a duration");
            }
        }
        for (int age = 0; age < perAge.Length; age++) Assert.GreaterOrEqual(perAge[age], 3, $"events in {m_Ages[age].Id}");
    }

    [Test]
    public void Hazards_PlagueRiskFollowsTheAges_AndTheHazardEffectsAreWired()
    {
        Assert.AreEqual(1f, m_Ages[0].PlagueRisk);
        Assert.AreEqual(0.6f, m_Ages[1].PlagueRisk);
        Assert.AreEqual(0f, m_Ages[2].PlagueRisk);
        Assert.AreEqual(0f, m_Ages[3].PlagueRisk);

        Assert.AreEqual(0.5f, HazardOf(m_Techs.GetById("steel_frames").Effects, HazardKind.FireSpread));
        Assert.AreEqual(0.25f, HazardOf(m_Techs.GetById("smart_grid").Effects, HazardKind.Breakdown));
        Assert.AreEqual(0.8f, HazardOf(m_Techs.GetOrdinanceById("building_code").Effects, HazardKind.FireSpread));
        OrdinanceDefinition quarantine = m_Techs.GetOrdinanceById("quarantine");
        Assert.AreEqual("monasticism", quarantine.RequiredTech.Id);
        Assert.AreEqual(0.5f, HazardOf(quarantine.Effects, HazardKind.PlagueSpread));
    }

    private static float HazardOf(TechEffect[] effects, HazardKind kind)
    {
        float value = 1f;
        foreach (TechEffect effect in effects)
        {
            if (effect.Type == TechEffectType.HazardMultiplier && effect.Target == kind.ToString()) value *= effect.Value;
        }
        return value;
    }
}
