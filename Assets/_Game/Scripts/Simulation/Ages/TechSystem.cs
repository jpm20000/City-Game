using System;
using System.Collections.Generic;

// Research state (GamePlan §12, M11): the current age, researched techs, one active project plus
// a queue, and the RP pool. Progress is a pool held toward the active project, so switching
// projects loses nothing and leftover RP carries into the next one. With nothing active, RP bank
// up to ResearchBankDays x the day's income. Advancing an age is a project too: it can be
// started once AdvanceStatus is Ready, and completing it moves CurrentAge on immediately.
public sealed class TechSystem
{
    private readonly AgeDatabase m_Ages;
    private readonly TechDatabase m_Techs;
    private readonly BalanceConfig m_Config;
    private readonly HashSet<TechDefinition> m_Researched = new();
    private readonly List<ResearchProject> m_Queue = new();

    public AgeDatabase Ages => m_Ages;
    public TechDatabase Techs => m_Techs;

    public int CurrentAge { get; private set; }
    public AgeDefinition CurrentAgeDefinition => m_Ages[CurrentAge];
    public AgeRules Rules => CurrentAgeDefinition.Rules;

    public ResearchProject Active { get; private set; }
    public IReadOnlyList<ResearchProject> Queue => m_Queue;
    public float Progress { get; private set; }
    public float ResearchPerDay { get; private set; }     // the RP income of the last Step
    public TechModifiers Modifiers { get; private set; } = TechModifiers.None;
    public int ResearchedCount => m_Researched.Count;

    public event Action<TechDefinition> TechCompleted;
    public event Action<int> AgeAdvanced;                 // new age index

    public TechSystem(AgeDatabase ages, TechDatabase techs, BalanceConfig config)
    {
        m_Ages = ages ?? throw new ArgumentNullException(nameof(ages));
        m_Techs = techs ?? throw new ArgumentNullException(nameof(techs));
        m_Config = config ?? throw new ArgumentNullException(nameof(config));
        if (ages.Count == 0) throw new ArgumentException("AgeDatabase has no ages.", nameof(ages));
        StartNew(Math.Max(0, ages.Legacy));
    }

    public bool IsResearched(TechDefinition tech) => tech != null && m_Researched.Contains(tech);

    // Researched techs in database order (deterministic, independent of completion order).
    public IEnumerable<TechDefinition> Researched
    {
        get
        {
            foreach (TechDefinition tech in m_Techs.Techs)
            {
                if (tech != null && m_Researched.Contains(tech)) yield return tech;
            }
        }
    }

    public int ResearchedInAge(int age)
    {
        int count = 0;
        foreach (TechDefinition tech in m_Researched)
        {
            if (tech.Age == age) count++;
        }
        return count;
    }

    public bool IsPlanned(ResearchProject project) => project != null && (project.Equals(Active) || m_Queue.Contains(project));
    public bool IsPlanned(TechDefinition tech) => tech != null && IsPlanned(ResearchProject.For(tech));

    // Can be researched right now: reached its age, prerequisites done, not done or planned already.
    public bool CanResearch(TechDefinition tech)
    {
        if (!IsOpen(tech)) return false;
        foreach (TechDefinition prerequisite in tech.Prerequisites)
        {
            if (!IsResearched(prerequisite)) return false;
        }
        return true;
    }

    // Can be added to the plan: like CanResearch, but prerequisites may be planned ahead of it,
    // and the queue must have room.
    public bool CanEnqueue(TechDefinition tech)
    {
        if (!IsOpen(tech) || !HasRoom()) return false;
        foreach (TechDefinition prerequisite in tech.Prerequisites)
        {
            if (!IsResearched(prerequisite) && !IsPlanned(prerequisite)) return false;
        }
        return true;
    }

    public AdvanceStatus GetAdvanceStatus(int population)
    {
        int next = CurrentAge + 1;
        if (next >= m_Ages.Count) return new AdvanceStatus(-1, 0f, 0, 0, 0, population, 0);

        AgeDefinition age = m_Ages[next];
        int missing = 0;
        foreach (TechDefinition required in age.RequiredTechs)
        {
            if (!IsResearched(required)) missing++;
        }
        return new AdvanceStatus(next, age.AdvanceCost, ResearchedInAge(CurrentAge), age.TechsToAdvance, missing,
            population, age.PopulationToEnter);
    }

    // The "Advance to the next age" project, or null when the city is in the last age.
    public ResearchProject NextAdvance()
    {
        int next = CurrentAge + 1;
        return next < m_Ages.Count ? ResearchProject.Advance(m_Ages[next], next) : null;
    }

    public bool CanAdvance(int population)
    {
        ResearchProject advance = NextAdvance();
        return advance != null && !IsPlanned(advance) && GetAdvanceStatus(population).Ready;
    }

    // Adds a tech to the plan (it becomes active if nothing is). False if it can't be queued.
    public bool Enqueue(TechDefinition tech)
    {
        if (!CanEnqueue(tech)) return false;
        Add(ResearchProject.For(tech));
        return true;
    }

    public bool EnqueueAdvance(int population)
    {
        if (!CanAdvance(population) || !HasRoom()) return false;
        Add(NextAdvance());
        return true;
    }

    // Makes a tech the active project now. The replaced project goes back to the front of the
    // queue (dropped if the queue is full); the RP pool carries over.
    public bool SetActive(TechDefinition tech)
    {
        if (tech == null) return false;
        ResearchProject project = ResearchProject.For(tech);
        if (project.Equals(Active)) return true;
        int queued = m_Queue.IndexOf(project);
        // A queued tech may still wait on a prerequisite planned ahead of it.
        if (queued < 0 ? !CanResearch(tech) : !PrerequisitesResearched(tech)) return false;
        if (queued >= 0) m_Queue.RemoveAt(queued);
        MakeActive(project);
        return true;
    }

    public bool SetActiveAdvance(int population)
    {
        ResearchProject advance = NextAdvance();
        if (advance == null) return false;
        if (advance.Equals(Active)) return true;
        if (!m_Queue.Remove(advance) && !CanAdvance(population)) return false;
        MakeActive(advance);
        return true;
    }

    // Removes a project from the plan, along with queued techs that depended on it.
    public bool Remove(ResearchProject project)
    {
        if (project == null) return false;
        if (project.Equals(Active))
        {
            Active = null;
            PromoteNext();
        }
        else if (!m_Queue.Remove(project)) return false;
        Prune();
        return true;
    }

    public bool Remove(TechDefinition tech) => tech != null && Remove(ResearchProject.For(tech));

    // One day of research. Returns how many projects completed (several if the pool covers them).
    public int Step(float researchPoints)
    {
        float rp = Math.Max(0f, researchPoints);
        ResearchPerDay = rp;

        if (Active == null)
        {
            float cap = m_Config.ResearchBankDays * rp;
            if (Progress < cap) Progress = Math.Min(Progress + rp, cap);
            return 0;
        }

        Progress += rp;
        int completed = 0;
        while (Active != null && Progress >= Active.Cost)
        {
            ResearchProject done = Active;
            Progress -= done.Cost;
            Active = null;

            if (done.IsAdvance)
            {
                CurrentAge = done.TargetAgeIndex;
            }
            else
            {
                m_Researched.Add(done.Tech);
                Modifiers = TechModifiers.Fold(Researched);
            }
            PromoteNext();
            completed++;

            if (done.IsAdvance) AgeAdvanced?.Invoke(CurrentAge);
            else TechCompleted?.Invoke(done.Tech);
        }
        return completed;
    }

    // A new city in the given age: every tech of earlier ages plus the age's StartingTechs is
    // researched; nothing is planned and the pool is empty.
    public void StartNew(int startAge)
    {
        if (!m_Ages.IsValidIndex(startAge)) throw new ArgumentOutOfRangeException(nameof(startAge));

        CurrentAge = startAge;
        m_Researched.Clear();
        foreach (TechDefinition tech in m_Techs.Techs)
        {
            if (tech != null && tech.Age < startAge) m_Researched.Add(tech);
        }
        foreach (TechDefinition tech in m_Ages[startAge].StartingTechs)
        {
            if (tech != null) m_Researched.Add(tech);
        }
        Active = null;
        m_Queue.Clear();
        Progress = 0f;
        ResearchPerDay = 0f;
        Modifiers = TechModifiers.Fold(Researched);
    }

    // Load. Unknown tech Ids and projects that are no longer valid are dropped; the count of
    // dropped Ids is returned so the caller can warn.
    public int Restore(int age, IEnumerable<string> researched, string active, float progress, IEnumerable<string> queue)
    {
        if (!m_Ages.IsValidIndex(age)) throw new ArgumentOutOfRangeException(nameof(age));

        int dropped = 0;
        CurrentAge = age;
        m_Researched.Clear();
        if (researched != null)
        {
            foreach (string id in researched)
            {
                TechDefinition tech = m_Techs.GetById(id);
                if (tech != null) m_Researched.Add(tech);
                else dropped++;
            }
        }
        Modifiers = TechModifiers.Fold(Researched);

        Active = null;
        m_Queue.Clear();
        Progress = Math.Max(0f, progress);
        ResearchPerDay = 0f;

        var planned = new List<string>();
        if (!string.IsNullOrEmpty(active)) planned.Add(active);
        if (queue != null) planned.AddRange(queue);
        foreach (string id in planned)
        {
            ResearchProject project = ProjectFromId(id);
            if (project == null || IsPlanned(project) || (project.IsAdvance ? project.TargetAgeIndex != CurrentAge + 1 : !IsOpen(project.Tech)))
            {
                dropped++;
                continue;
            }
            if (Active == null) Active = project;
            else m_Queue.Add(project);
        }
        int before = m_Queue.Count + (Active != null ? 1 : 0);
        Prune();
        dropped += before - (m_Queue.Count + (Active != null ? 1 : 0));
        return dropped;
    }

    public ResearchProject ProjectFromId(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        if (id.StartsWith(ResearchProject.AdvancePrefix, StringComparison.Ordinal))
        {
            int index = m_Ages.IndexOf(id.Substring(ResearchProject.AdvancePrefix.Length));
            return index >= 0 ? ResearchProject.Advance(m_Ages[index], index) : null;
        }
        TechDefinition tech = m_Techs.GetById(id);
        return tech != null ? ResearchProject.For(tech) : null;
    }

    private bool IsOpen(TechDefinition tech)
    {
        return tech != null && tech.Age <= CurrentAge && !IsResearched(tech) && !IsPlanned(tech);
    }

    private bool PrerequisitesResearched(TechDefinition tech)
    {
        foreach (TechDefinition prerequisite in tech.Prerequisites)
        {
            if (!IsResearched(prerequisite)) return false;
        }
        return true;
    }

    private bool HasRoom() => Active == null || m_Queue.Count < m_Config.ResearchQueueMax;

    private void Add(ResearchProject project)
    {
        if (Active == null) Active = project;
        else m_Queue.Add(project);
    }

    private void MakeActive(ResearchProject project)
    {
        if (Active != null) m_Queue.Insert(0, Active);
        Active = project;
        while (m_Queue.Count > m_Config.ResearchQueueMax) m_Queue.RemoveAt(m_Queue.Count - 1);
        Prune();
    }

    private void PromoteNext()
    {
        if (Active != null || m_Queue.Count == 0) return;
        Active = m_Queue[0];
        m_Queue.RemoveAt(0);
    }

    // Drops planned projects that can no longer run in plan order: a tech whose prerequisite is
    // neither researched nor planned ahead of it, or an advance that isn't to the next age.
    private void Prune()
    {
        var ahead = new HashSet<TechDefinition>();
        bool Valid(ResearchProject project)
        {
            if (project.IsAdvance) return project.TargetAgeIndex == CurrentAge + 1;
            foreach (TechDefinition prerequisite in project.Tech.Prerequisites)
            {
                if (!IsResearched(prerequisite) && !ahead.Contains(prerequisite)) return false;
            }
            return true;
        }

        if (Active != null && !Valid(Active)) Active = null;
        if (Active != null && !Active.IsAdvance) ahead.Add(Active.Tech);

        for (int i = 0; i < m_Queue.Count; i++)
        {
            if (!Valid(m_Queue[i]))
            {
                m_Queue.RemoveAt(i--);
                continue;
            }
            if (!m_Queue[i].IsAdvance) ahead.Add(m_Queue[i].Tech);
        }

        if (Active == null && m_Queue.Count > 0)
        {
            PromoteNext();
            Prune();    // the promoted project's prerequisites may have been ahead of it only
        }
    }
}
