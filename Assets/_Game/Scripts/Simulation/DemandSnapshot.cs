public readonly struct DemandSnapshot
{
    public readonly float Residential;
    public readonly float Commercial;
    public readonly float Industrial;

    public DemandSnapshot(float residential, float commercial, float industrial)
    {
        Residential = residential;
        Commercial = commercial;
        Industrial = industrial;
    }

    public float Get(ZoneType zone)
    {
        switch (zone)
        {
            case ZoneType.Residential: return Residential;
            case ZoneType.Commercial: return Commercial;
            case ZoneType.Industrial: return Industrial;
            default: return 0f;
        }
    }
}
