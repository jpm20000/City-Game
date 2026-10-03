using System;
using UnityEngine;

// The top-left panels (Taxes, Research) share one slot under the HUD: opening one closes the others.
public static class SidePanels
{
    public static event Action<object> Opened;

    public static void RaiseOpened(object panel) => Opened?.Invoke(panel);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSubscribers()
    {
        Opened = null;
    }
}
