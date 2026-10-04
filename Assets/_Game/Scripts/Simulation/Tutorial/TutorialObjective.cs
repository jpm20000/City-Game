using System;

// One step of the Medieval tutorial (M19f). Measure gives (done so far, needed); the step is met when done >= needed.
// Highlight names the toolbar / HUD control the runtime outlines ("" = none); Help marks steps that wait on growth, where
// the card adds the top growth blocker.
public sealed class TutorialObjective
{
    public string Id;
    public string Title;
    public string Body;
    public string Unit = "";
    public string Highlight = "";
    public bool Help;
    public Func<TutorialSnapshot, (int done, int needed)> Measure;

    public bool IsMet(TutorialSnapshot s)
    {
        (int done, int needed) = Measure(s);
        return done >= needed;
    }

    public string Progress(TutorialSnapshot s)
    {
        (int done, int needed) = Measure(s);
        if (needed <= 1) return "";
        return $"{Math.Min(done, needed)} / {needed} {Unit}".TrimEnd();
    }
}
