using System;
using System.Collections.Generic;
using UnityEngine;

// Every tech in the game. The tree is pure data; Validate (run by an EditMode test) catches broken edits.
[CreateAssetMenu(fileName = "TechDatabase", menuName = "CityBuilder/Tech Database")]
public sealed class TechDatabase : ScriptableObject
{
    [SerializeField] private List<TechDefinition> m_Techs = new();
    [Tooltip("(M15) City-wide policies unlocked by techs.")]
    [SerializeField] private List<OrdinanceDefinition> m_Ordinances = new();

    [Tooltip("(M16) The five road tiers, each unlocked by a tech. Both databases or neither, like ordinances.")]
    [SerializeField] private List<RoadTierDefinition> m_RoadTiers = new();

    [Tooltip("(M17) Random events with choices. Both databases or neither, like ordinances.")]
    [SerializeField] private List<EventDefinition> m_Events = new();

    private Dictionary<string, TechDefinition> m_ById;

    public IReadOnlyList<TechDefinition> Techs => m_Techs;
    public IReadOnlyList<OrdinanceDefinition> Ordinances => m_Ordinances;
    public IReadOnlyList<RoadTierDefinition> RoadTiers => m_RoadTiers;
    public IReadOnlyList<EventDefinition> Events => m_Events;

    public EventDefinition GetEventById(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        foreach (EventDefinition definition in m_Events)
        {
            if (definition != null && definition.Id == id) return definition;
        }
        return null;
    }

    public OrdinanceDefinition GetOrdinanceById(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        foreach (OrdinanceDefinition ordinance in m_Ordinances)
        {
            if (ordinance != null && ordinance.Id == id) return ordinance;
        }
        return null;
    }

    public TechDefinition GetById(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        if (m_ById == null)
        {
            m_ById = new Dictionary<string, TechDefinition>();
            foreach (TechDefinition tech in m_Techs)
            {
                if (tech != null && !string.IsNullOrEmpty(tech.Id)) m_ById.TryAdd(tech.Id, tech);
            }
        }
        return m_ById.TryGetValue(id, out TechDefinition found) ? found : null;
    }

    // Row for a tech in a panel laid out by age: 0 with no prerequisite from its own age, else one more
    // than its deepest same-age prerequisite (earlier-age prerequisites are already researched by
    // the time its age arrives, so they don't push it down).
    public int DepthInAge(TechDefinition tech)
    {
        return DepthInAge(tech, new HashSet<TechDefinition>());
    }

    private static int DepthInAge(TechDefinition tech, HashSet<TechDefinition> visiting)
    {
        if (tech == null || !visiting.Add(tech)) return 0;   // cycles are a Validate error
        int depth = 0;
        foreach (TechDefinition prerequisite in tech.Prerequisites)
        {
            if (prerequisite != null && prerequisite.Age == tech.Age)
                depth = Math.Max(depth, DepthInAge(prerequisite, visiting) + 1);
        }
        visiting.Remove(tech);
        return depth;
    }

    public int CountInAge(int age)
    {
        int count = 0;
        foreach (TechDefinition tech in m_Techs)
        {
            if (tech != null && tech.Age == age) count++;
        }
        return count;
    }

    // Appends a message per problem to errors; true when there are none. Checks: unique non-empty
    // Ids, ages in range, prerequisites listed here and from the same or an earlier age, no cycles,
    // each age has enough techs in the previous age to advance into it, and an age's required and
    // starting techs are reachable when they are needed.
    public bool Validate(AgeDatabase ages, List<string> errors)
    {
        if (ages == null) throw new ArgumentNullException(nameof(ages));
        if (errors == null) throw new ArgumentNullException(nameof(errors));
        int before = errors.Count;

        var known = new HashSet<TechDefinition>();
        var ids = new HashSet<string>();
        for (int i = 0; i < m_Techs.Count; i++)
        {
            TechDefinition tech = m_Techs[i];
            if (tech == null)
            {
                errors.Add($"Tech #{i} is missing.");
                continue;
            }
            known.Add(tech);
            if (string.IsNullOrEmpty(tech.Id)) errors.Add($"Tech #{i} has no Id.");
            else if (!ids.Add(tech.Id)) errors.Add($"Duplicate tech Id '{tech.Id}'.");
            if (!ages.IsValidIndex(tech.Age)) errors.Add($"Tech '{tech.Id}' has age {tech.Age}, outside the AgeDatabase.");
            if (tech.Cost <= 0f) errors.Add($"Tech '{tech.Id}' must cost more than 0 RP.");
        }

        foreach (TechDefinition tech in m_Techs)
        {
            if (tech == null) continue;
            foreach (TechDefinition prerequisite in tech.Prerequisites)
            {
                if (prerequisite == null) errors.Add($"Tech '{tech.Id}' has a missing prerequisite.");
                else if (!known.Contains(prerequisite)) errors.Add($"Tech '{tech.Id}' needs '{prerequisite.Id}', which is not in the TechDatabase.");
                else if (prerequisite.Age > tech.Age) errors.Add($"Tech '{tech.Id}' (age {tech.Age}) needs '{prerequisite.Id}' from a later age ({prerequisite.Age}).");
            }
        }

        AddCycleErrors(errors);

        for (int a = 0; a < ages.Count; a++)
        {
            AgeDefinition age = ages[a];
            if (age == null) continue;
            if (a > 0 && CountInAge(a - 1) < age.TechsToAdvance)
                errors.Add($"Age '{age.Id}' needs {age.TechsToAdvance} techs of the previous age, which only has {CountInAge(a - 1)}.");
            foreach (TechDefinition required in age.RequiredTechs)
            {
                if (required == null) errors.Add($"Age '{age.Id}' has a missing required tech.");
                else if (!known.Contains(required)) errors.Add($"Age '{age.Id}' requires '{required.Id}', which is not in the TechDatabase.");
                else if (required.Age >= a) errors.Add($"Age '{age.Id}' requires '{required.Id}', which can only be researched once in that age.");
            }
            foreach (TechDefinition starting in age.StartingTechs)
            {
                if (starting == null) errors.Add($"Age '{age.Id}' has a missing starting tech.");
                else if (!known.Contains(starting)) errors.Add($"Age '{age.Id}' starts with '{starting.Id}', which is not in the TechDatabase.");
                else if (starting.Age > a) errors.Add($"Age '{age.Id}' starts with '{starting.Id}' from a later age.");
                else
                {
                    foreach (TechDefinition prerequisite in starting.Prerequisites)
                    {
                        if (prerequisite != null && prerequisite.Age >= a && Array.IndexOf(age.StartingTechs, prerequisite) < 0)
                            errors.Add($"Age '{age.Id}' starts with '{starting.Id}' but not its prerequisite '{prerequisite.Id}'.");
                    }
                }
            }
        }
        var ordinanceIds = new HashSet<string>();
        for (int i = 0; i < m_Ordinances.Count; i++)
        {
            OrdinanceDefinition ordinance = m_Ordinances[i];
            if (ordinance == null)
            {
                errors.Add($"Ordinance #{i} is missing.");
                continue;
            }
            if (string.IsNullOrEmpty(ordinance.Id)) errors.Add($"Ordinance #{i} has no Id.");
            else if (!ordinanceIds.Add(ordinance.Id)) errors.Add($"Duplicate ordinance Id '{ordinance.Id}'.");
            if (ordinance.RequiredTech == null || !known.Contains(ordinance.RequiredTech))
                errors.Add($"Ordinance '{ordinance.Id}' needs a tech that is in the TechDatabase.");
            if (ordinance.CostPerDay < 0f || ordinance.CostPerResident < 0f) errors.Add($"Ordinance '{ordinance.Id}' has a negative cost.");
        }

        var tierNumbers = new HashSet<int>();
        for (int i = 0; i < m_RoadTiers.Count; i++)
        {
            RoadTierDefinition tier = m_RoadTiers[i];
            if (tier == null)
            {
                errors.Add($"Road tier #{i} is missing.");
                continue;
            }
            if (tier.Tier < 1 || tier.Tier > GridData.MaxRoadTier) errors.Add($"Road tier '{tier.Id}' has tier {tier.Tier}, outside 1..{GridData.MaxRoadTier}.");
            else if (!tierNumbers.Add(tier.Tier)) errors.Add($"Duplicate road tier {tier.Tier}.");
            if (tier.RequiredTech != null && !known.Contains(tier.RequiredTech))
                errors.Add($"Road tier '{tier.Id}' needs a tech that is in the TechDatabase.");
            if (tier.Cost < 0 || tier.UpkeepPerDay < 0f) errors.Add($"Road tier '{tier.Id}' has a negative cost.");
            if (tier.Capacity <= 0f) errors.Add($"Road tier '{tier.Id}' needs a capacity above 0.");
            if (tier.TravelCost < 1) errors.Add($"Road tier '{tier.Id}' needs a travel cost of at least 1.");
        }

        var eventIds = new HashSet<string>();
        for (int i = 0; i < m_Events.Count; i++)
        {
            EventDefinition definition = m_Events[i];
            if (definition == null)
            {
                errors.Add($"Event #{i} is missing.");
                continue;
            }
            if (string.IsNullOrEmpty(definition.Id)) errors.Add($"Event #{i} has no Id.");
            else if (!eventIds.Add(definition.Id)) errors.Add($"Duplicate event Id '{definition.Id}'.");
            if (definition.MinAge > definition.MaxAge || !ages.IsValidIndex(definition.MinAge) || !ages.IsValidIndex(definition.MaxAge))
                errors.Add($"Event '{definition.Id}' has a bad age range {definition.MinAge}..{definition.MaxAge}.");
            if (definition.RequiredTech != null && !known.Contains(definition.RequiredTech))
                errors.Add($"Event '{definition.Id}' needs a tech that is in the TechDatabase.");
            if (definition.Weight <= 0f) errors.Add($"Event '{definition.Id}' needs a weight above 0.");
            EventChoice[] choices = definition.Choices;
            if (choices.Length < 2 || choices.Length > 3) errors.Add($"Event '{definition.Id}' needs 2 or 3 choices.");
            else if (!choices[choices.Length - 1].IsFree) errors.Add($"Event '{definition.Id}': the last choice must be free.");
            foreach (EventChoice choice in choices)
            {
                if (string.IsNullOrEmpty(choice.Label)) errors.Add($"Event '{definition.Id}' has a choice without a label.");
                if (choice.Cost < 0f || choice.CostPerResident < 0f || choice.Reward < 0f || choice.ResearchPoints < 0f)
                    errors.Add($"Event '{definition.Id}' has a negative price or reward.");
                if (choice.Effects != null && choice.Effects.Length > 0 && choice.Days <= 0)
                    errors.Add($"Event '{definition.Id}' has effects with no duration.");
            }
        }

        return errors.Count == before;
    }

    // Depth-first search with an on-stack set; reports each cycle once, at the tech that closes it.
    private void AddCycleErrors(List<string> errors)
    {
        var done = new HashSet<TechDefinition>();
        var onStack = new HashSet<TechDefinition>();

        void Visit(TechDefinition tech)
        {
            if (tech == null || done.Contains(tech)) return;
            onStack.Add(tech);
            foreach (TechDefinition prerequisite in tech.Prerequisites)
            {
                if (prerequisite == null) continue;
                if (onStack.Contains(prerequisite)) errors.Add($"Tech '{tech.Id}' and '{prerequisite.Id}' are in a prerequisite cycle.");
                else Visit(prerequisite);
            }
            onStack.Remove(tech);
            done.Add(tech);
        }

        foreach (TechDefinition tech in m_Techs) Visit(tech);
    }

    internal void Init(params TechDefinition[] techs)
    {
        m_Techs = new List<TechDefinition>(techs);
        m_ById = null;
    }

    internal void InitRoadTiers(params RoadTierDefinition[] tiers)
    {
        m_RoadTiers = new List<RoadTierDefinition>(tiers);
    }

    internal void InitEvents(params EventDefinition[] events)
    {
        m_Events = new List<EventDefinition>(events);
    }

    internal void InitOrdinances(params OrdinanceDefinition[] ordinances)
    {
        m_Ordinances = new List<OrdinanceDefinition>(ordinances);
    }
}
