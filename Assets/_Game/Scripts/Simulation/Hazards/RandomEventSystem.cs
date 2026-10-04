using System;
using System.Collections.Generic;
using UnityEngine;

// Random events with choices (M17). Every EventIntervalMin..Max days (one draw, counted from the day the last event
// was answered) the city is offered one of the eligible events (a weighted pick, one draw, never one offered in
// the last EventRepeatDays). It stays Pending until Choose(i); the runtime pauses on it, and one nobody answers
// within EventAutoDays takes its last (free) choice. A choice charges its price, pays its Reward and research
// points at once and, if it has lasting effects, adds an active effect for Days days; the active effects are folded
// into TechModifiers (TechSystem.SetEventEffects). All draws happen in Step; Choose draws nothing.
// State lives in DisasterSystem (PendingEvent, PendingDays, DaysToNextEvent = 0 means "not scheduled yet",
// ActiveEvents, RecentEvents).
public sealed class RandomEventSystem
{
    private readonly DisasterSystem m_Owner;
    private readonly BalanceConfig m_Config;
    private readonly TechDatabase m_Techs;
    private readonly TechSystem m_Tech;
    private readonly EconomySystem m_Economy;
    private readonly PopulationSystem m_Population;
    private readonly SimRandom m_Random;
    private readonly List<EventDefinition> m_Eligible = new();
    private readonly List<TechEffect> m_Effects = new();

    public event Action<EventDefinition> Offered;
    public event Action<EventDefinition, int> Resolved;      // the event and the choice taken

    // The event waiting for an answer (null = none) and the choice taken when this step (or Choose) resolved one.
    public EventDefinition Pending => m_Techs != null ? m_Techs.GetEventById(m_Owner.PendingEvent) : null;
    public EventDefinition LastResolved { get; private set; }
    public int LastChoice { get; private set; } = -1;
    public bool LastWasAutomatic { get; private set; }

    public RandomEventSystem(DisasterSystem owner, BalanceConfig config, TechDatabase techs, TechSystem tech, EconomySystem economy,
        PopulationSystem population, SimRandom random)
    {
        m_Owner = owner;
        m_Config = config;
        m_Techs = techs;
        m_Tech = tech;
        m_Economy = economy;
        m_Population = population;
        m_Random = random;
    }

    public int PendingDays => m_Owner.PendingDays;
    public IReadOnlyList<EventRecord> Active => m_Owner.ActiveEvents;

    // Days the running effects of an event have left (0 = not running).
    public int DaysLeft(string eventId)
    {
        foreach (EventRecord record in m_Owner.ActiveEvents)
        {
            if (record.Id == eventId) return record.DaysLeft;
        }
        return 0;
    }

    // What choice i of the pending event costs the city now.
    public float PriceOf(int choice)
    {
        EventDefinition pending = Pending;
        if (pending == null || choice < 0 || choice >= pending.Choices.Length) return 0f;
        return pending.Choices[choice].PriceAt(m_Population.Population);
    }

    public bool CanChoose(int choice)
    {
        EventDefinition pending = Pending;
        if (pending == null || choice < 0 || choice >= pending.Choices.Length) return false;
        EventChoice option = pending.Choices[choice];
        return option.IsFree || m_Economy.Money >= option.PriceAt(m_Population.Population);
    }

    // Takes choice i of the pending event. False when nothing is pending, the choice does not exist or the city can't pay.
    public bool Choose(int choice) => Choose(choice, false);

    private bool Choose(int choice, bool automatic)
    {
        EventDefinition pending = Pending;
        if (pending == null || !CanChoose(choice)) return false;
        EventChoice option = pending.Choices[choice];

        float price = option.PriceAt(m_Population.Population);
        if (price > 0f) m_Economy.Spend(price);
        if (option.Reward > 0f) m_Economy.Refund(option.Reward);
        if (option.ResearchPoints > 0f) m_Tech?.Step(option.ResearchPoints);
        if (option.HasLastingEffects)
        {
            RemoveActive(pending.Id);
            m_Owner.ActiveEvents.Add(new EventRecord(pending.Id, choice, option.Days));
            RefreshEffects();
        }

        m_Owner.RecentEvents.RemoveAll(r => r.Id == pending.Id);
        m_Owner.RecentEvents.Add(new EventRecord(pending.Id, -1, Mathf.Max(1, m_Config.EventRepeatDays)));
        m_Owner.PendingEvent = "";
        m_Owner.PendingDays = 0;
        m_Owner.DaysToNextEvent = 0;        // scheduled by the next Step
        LastResolved = pending;
        LastChoice = choice;
        LastWasAutomatic = automatic;
        Resolved?.Invoke(pending, choice);
        return true;
    }

    private void RemoveActive(string id) => m_Owner.ActiveEvents.RemoveAll(r => r.Id == id);

    // Re-folds the running events' effects into the techs' modifiers (after a choice, an expiry or a load).
    public void RefreshEffects()
    {
        if (m_Tech == null) return;
        m_Effects.Clear();
        foreach (EventRecord record in m_Owner.ActiveEvents)
        {
            EventDefinition definition = m_Techs.GetEventById(record.Id);
            if (definition == null || record.Choice < 0 || record.Choice >= definition.Choices.Length) continue;
            m_Effects.AddRange(definition.Choices[record.Choice].Effects ?? Array.Empty<TechEffect>());
        }
        m_Tech.SetEventEffects(m_Effects);
    }

    // Drops saved events this game doesn't have (unknown id, a choice that no longer exists); returns how many.
    public int Prune()
    {
        int dropped = 0;
        if (!string.IsNullOrEmpty(m_Owner.PendingEvent) && Pending == null)
        {
            m_Owner.PendingEvent = "";
            m_Owner.PendingDays = 0;
            dropped++;
        }
        dropped += m_Owner.ActiveEvents.RemoveAll(r =>
        {
            EventDefinition definition = m_Techs != null ? m_Techs.GetEventById(r.Id) : null;
            return definition == null || r.Choice < 0 || r.Choice >= definition.Choices.Length || r.DaysLeft <= 0;
        });
        dropped += m_Owner.RecentEvents.RemoveAll(r => (m_Techs == null || m_Techs.GetEventById(r.Id) == null) || r.DaysLeft <= 0);
        return dropped;
    }

    public void Step()
    {
        LastResolved = null;
        LastChoice = -1;
        LastWasAutomatic = false;
        if (m_Techs == null || m_Tech == null || m_Techs.Events.Count == 0) return;

        // Running effects and the repeat window count down.
        bool expired = false;
        List<EventRecord> active = m_Owner.ActiveEvents;
        for (int i = active.Count - 1; i >= 0; i--)
        {
            EventRecord record = active[i];
            record.DaysLeft--;
            if (record.DaysLeft <= 0)
            {
                active.RemoveAt(i);
                expired = true;
            }
            else
            {
                active[i] = record;
            }
        }
        if (expired) RefreshEffects();

        List<EventRecord> recent = m_Owner.RecentEvents;
        for (int i = recent.Count - 1; i >= 0; i--)
        {
            EventRecord record = recent[i];
            record.DaysLeft--;
            if (record.DaysLeft <= 0) recent.RemoveAt(i);
            else recent[i] = record;
        }

        if (!string.IsNullOrEmpty(m_Owner.PendingEvent))
        {
            m_Owner.PendingDays++;
            EventDefinition pending = Pending;
            if (pending != null && m_Owner.PendingDays > m_Config.EventAutoDays) Choose(pending.Choices.Length - 1, true);
            return;
        }

        if (m_Owner.DaysToNextEvent <= 0)
        {
            int min = Mathf.Max(1, m_Config.EventIntervalMin);
            int max = Mathf.Max(min, m_Config.EventIntervalMax);
            m_Owner.DaysToNextEvent = min + m_Random.NextInt(max - min + 1);
            return;
        }

        if (--m_Owner.DaysToNextEvent == 0) Offer();
    }

    // Offers the next event: a weighted pick among the eligible ones; none eligible = schedule again.
    private void Offer()
    {
        int age = m_Tech.CurrentAge;
        int population = m_Population.Population;
        m_Eligible.Clear();
        float total = 0f;
        foreach (EventDefinition definition in m_Techs.Events)
        {
            if (!IsEligible(definition, age, population)) continue;
            m_Eligible.Add(definition);
            total += Mathf.Max(0f, definition.Weight);
        }
        if (m_Eligible.Count == 0 || total <= 0f) return;

        float pick = m_Random.NextFloat() * total;
        EventDefinition chosen = m_Eligible[m_Eligible.Count - 1];
        float sum = 0f;
        foreach (EventDefinition definition in m_Eligible)
        {
            sum += Mathf.Max(0f, definition.Weight);
            if (sum >= pick)
            {
                chosen = definition;
                break;
            }
        }
        m_Owner.PendingEvent = chosen.Id;
        m_Owner.PendingDays = 0;
        Offered?.Invoke(chosen);
    }

    public bool IsEligible(EventDefinition definition, int age, int population)
    {
        if (definition == null || age < definition.MinAge || age > definition.MaxAge) return false;
        if (population < definition.MinPopulation) return false;
        if (definition.RequiredTech != null && !m_Tech.IsResearched(definition.RequiredTech)) return false;
        if (definition.Choices.Length < 2) return false;
        foreach (EventRecord record in m_Owner.RecentEvents)
        {
            if (record.Id == definition.Id) return false;
        }
        return true;
    }

    // Offers an event now (tests, DEBUG): the first eligible one, or the named one when it exists. False if one is pending.
    public bool OfferNow(string id = null)
    {
        if (m_Techs == null || !string.IsNullOrEmpty(m_Owner.PendingEvent)) return false;
        EventDefinition chosen = null;
        foreach (EventDefinition definition in m_Techs.Events)
        {
            if (id != null ? definition.Id == id : IsEligible(definition, m_Tech.CurrentAge, m_Population.Population))
            {
                chosen = definition;
                break;
            }
        }
        if (chosen == null) return false;
        m_Owner.PendingEvent = chosen.Id;
        m_Owner.PendingDays = 0;
        Offered?.Invoke(chosen);
        return true;
    }
}
