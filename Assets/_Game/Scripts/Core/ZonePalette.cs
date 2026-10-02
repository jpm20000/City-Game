using UnityEngine;

// Single source for zone colours: toolbar swatches, the zone overlay tint and grown buildings.
public static class ZonePalette
{
    public static readonly Color Residential = new Color(0.40f, 0.85f, 0.35f);
    public static readonly Color Commercial = new Color(0.30f, 0.55f, 0.95f);
    public static readonly Color Industrial = new Color(0.95f, 0.80f, 0.25f);

    public static Color Get(ZoneType zone)
    {
        switch (zone)
        {
            case ZoneType.Residential: return Residential;
            case ZoneType.Commercial: return Commercial;
            case ZoneType.Industrial: return Industrial;
            default: return Color.gray;
        }
    }
}
