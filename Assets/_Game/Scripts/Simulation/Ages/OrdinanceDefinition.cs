using System;
using UnityEngine;

// A city-wide policy the player can enact once its tech is researched (M15). It costs money every
// day while enacted and applies a list of effects, folded into TechModifiers like a tech's effects.
// Happiness effects count as the separate Ordinances happiness term.
[CreateAssetMenu(fileName = "Ordinance", menuName = "CityBuilder/Ordinance")]
public sealed class OrdinanceDefinition : ScriptableObject
{
    [SerializeField] private string m_Id = "";
    [SerializeField] private string m_DisplayName = "";
    [SerializeField, TextArea] private string m_Description = "";
    [Tooltip("The tech that unlocks this ordinance.")]
    [SerializeField] private TechDefinition m_RequiredTech;
    [Tooltip("Flat cost per day while enacted.")]
    [SerializeField] private float m_CostPerDay;
    [Tooltip("Extra cost per day per resident while enacted.")]
    [SerializeField] private float m_CostPerResident;
    [SerializeField] private TechEffect[] m_Effects = Array.Empty<TechEffect>();

    public string Id => m_Id;
    public string DisplayName => string.IsNullOrEmpty(m_DisplayName) ? m_Id : m_DisplayName;
    public string Description => m_Description;
    public TechDefinition RequiredTech => m_RequiredTech;
    public float CostPerDay => m_CostPerDay;
    public float CostPerResident => m_CostPerResident;
    public TechEffect[] Effects => m_Effects ?? Array.Empty<TechEffect>();

    // The age the ordinance belongs to (its tech's age), -1 without a tech.
    public int Age => m_RequiredTech != null ? m_RequiredTech.Age : -1;

    public float DailyCost(int population) => m_CostPerDay + m_CostPerResident * Mathf.Max(0, population);

    internal void Init(string id, TechDefinition requiredTech, float costPerDay, float costPerResident, TechEffect[] effects)
    {
        m_Id = id;
        m_DisplayName = id;
        m_RequiredTech = requiredTech;
        m_CostPerDay = costPerDay;
        m_CostPerResident = costPerResident;
        m_Effects = effects ?? Array.Empty<TechEffect>();
    }
}
