using System;
using System.Collections.Generic;
using UnityEngine;

// One Esc press goes to exactly one thing (M19b): handlers register with a priority and the highest one that accepts
// the press takes it. GameFlow dispatches each press and opens the pause menu when nobody wants it. A handler returns
// true when it used the press (it closed something, cancelled a tool, or deliberately swallowed it).
public static class EscapeRouter
{
    // Lowest to highest. The pause menu is a window itself, so it sits below the dialogs opened from it.
    public const int SidePanel = 40;
    public const int Tool = 50;
    public const int PauseMenu = 80;
    public const int Window = 90;
    public const int NewCity = 95;
    public const int Confirm = 100;
    public const int Event = 110;      // the event popup needs a choice: Esc is swallowed while it is up

    private sealed class Entry
    {
        public object Owner;
        public int Priority;
        public Func<bool> Handler;
    }

    private static readonly List<Entry> s_Entries = new();

    // Last dispatch's taker, for logs and tests ("" when the press fell through).
    public static string LastHandled { get; private set; } = "";

    public static void Register(object owner, int priority, Func<bool> handler)
    {
        if (owner == null) throw new ArgumentNullException(nameof(owner));
        Unregister(owner);
        s_Entries.Add(new Entry { Owner = owner, Priority = priority, Handler = handler });
    }

    public static void Unregister(object owner)
    {
        s_Entries.RemoveAll(e => ReferenceEquals(e.Owner, owner));
    }

    // True when a handler took the press.
    public static bool Dispatch()
    {
        var ordered = new List<Entry>(s_Entries);
        ordered.Sort((a, b) => b.Priority.CompareTo(a.Priority));
        foreach (Entry entry in ordered)
        {
            if (!s_Entries.Contains(entry)) continue;
            if (!entry.Handler()) continue;
            LastHandled = entry.Owner.GetType().Name;
            return true;
        }
        LastHandled = "";
        return false;
    }

    public static int Count => s_Entries.Count;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    {
        s_Entries.Clear();
        LastHandled = "";
    }
}
