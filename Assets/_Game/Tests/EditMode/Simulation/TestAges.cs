using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

// Four ages shaped like GamePlan §12 (Medieval x0.5 / level 2, Renaissance x0.75, Industrial x1 with
// the power gate and Electricity as its starting tech, Modern x1.25) and a few techs per age. Water
// (M13): wells in Medieval / Renaissance, piped from Industrial on.
// Advancing is cheap and unconditional; the advancement rules are TechSystemTests' job.
internal sealed class TestAges : IDisposable
{
    public const int Medieval = 0, Renaissance = 1, Industrial = 2, Modern = 3;

    private readonly List<Object> m_Created = new();

    public AgeDatabase Ages { get; }
    public TechDatabase Techs { get; }

    public TestAges()
    {
        TechDefinition commons = Tech("commons", Medieval, 10f);
        TechDefinition masonry = Tech("masonry", Medieval, 30f);
        TechDefinition monasticism = Tech("monasticism", Medieval, 400f, masonry);
        TechDefinition printing = Tech("printing", Renaissance, 10f);
        TechDefinition architecture = Tech("architecture", Renaissance, 300f, masonry);
        TechDefinition electricity = Tech("electricity", Industrial, 10f);
        TechDefinition steam = Tech("steam", Industrial, 50f);
        TechDefinition computing = Tech("computing", Modern, 10f);

        Ages = Make<AgeDatabase>();
        Ages.Init(
            Age("medieval", 750, 2, 0.5f, false, WaterRule.Coverage, plagueRisk: 1f),
            Age("renaissance", 1450, 3, 0.75f, false, WaterRule.Coverage, plagueRisk: 0.6f),
            Age("industrial", 1760, 3, 1f, true, WaterRule.Piped, new[] { electricity }),
            Age("modern", 1945, 3, 1.25f, true, WaterRule.Piped));
        Techs = Make<TechDatabase>();
        Techs.Init(commons, masonry, monasticism, printing, architecture, electricity, steam, computing);
    }

    public TechDefinition this[string id] => Techs.GetById(id);

    public void Dispose()
    {
        foreach (Object o in m_Created) Object.DestroyImmediate(o);
        m_Created.Clear();
    }

    // Moves the city into the next age right away: the advance becomes the active project (anything
    // active goes back to the queue) and is paid from the pool plus 1 RP.
    public static void Advance(SimulationSystem sim)
    {
        int age = sim.Tech.CurrentAge;
        if (!sim.Tech.SetActiveAdvance(sim.Population.Population)) throw new InvalidOperationException("Can't advance.");
        sim.Tech.Step(1f);
        if (sim.Tech.CurrentAge != age + 1) throw new InvalidOperationException("Advance didn't complete.");
    }

    private T Make<T>() where T : ScriptableObject
    {
        T instance = ScriptableObject.CreateInstance<T>();
        m_Created.Add(instance);
        return instance;
    }

    private TechDefinition Tech(string id, int age, float cost, params TechDefinition[] prerequisites)
    {
        TechDefinition tech = Make<TechDefinition>();
        tech.Init(id, age, cost, prerequisites);
        return tech;
    }

    private AgeDefinition Age(string id, int year, int maxLevel, float scale, bool power, WaterRule water,
        TechDefinition[] starting = null, float plagueRisk = 0f)
    {
        AgeDefinition age = Make<AgeDefinition>();
        age.Init(id, year, maxLevel, scale, power, advanceCost: 1f, startingTechs: starting,
            startingMoney: 20000f + 10000f * year / 750f, water: water, plagueRisk: plagueRisk);
        return age;
    }
}
