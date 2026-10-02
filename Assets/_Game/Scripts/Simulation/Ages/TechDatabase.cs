using System;
using System.Collections.Generic;
using UnityEngine;

// Every tech in the game. The tree is pure data; Validate (run by an EditMode test) catches broken edits.
[CreateAssetMenu(fileName = "TechDatabase", menuName = "CityBuilder/Tech Database")]
public sealed class TechDatabase : ScriptableObject
{
    [SerializeField] private List<TechDefinition> m_Techs = new();

    private Dictionary<string, TechDefinition> m_ById;

    public IReadOnlyList<TechDefinition> Techs => m_Techs;

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
}
