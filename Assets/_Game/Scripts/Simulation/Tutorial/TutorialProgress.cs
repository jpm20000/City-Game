// Where the player is in the tutorial (M19f): an index into TutorialContent. Saved as SaveData.Tutorial:
// -1 = no tutorial (or skipped), 0..Count-1 = the current objective, Count = finished. Objectives are sequential, and
// one already met when it becomes current completes at once.
public sealed class TutorialProgress
{
    public const int None = SaveData.NoTutorial;

    public int Index { get; private set; } = None;
    public bool Active => Index >= 0 && Index < TutorialContent.Count;
    public bool Finished => Index >= TutorialContent.Count;
    public TutorialObjective Current => Active ? TutorialContent.Objectives[Index] : null;

    public void Start() => Index = 0;
    public void Skip() => Index = None;

    public void Restore(int index)
    {
        Index = index < 0 ? None : index > TutorialContent.Count ? TutorialContent.Count : index;
    }

    // Advances over every objective the snapshot already meets; returns how many it completed.
    public int Evaluate(TutorialSnapshot snapshot)
    {
        int advanced = 0;
        while (Active && Current.IsMet(snapshot))
        {
            Index++;
            advanced++;
        }
        return advanced;
    }
}
