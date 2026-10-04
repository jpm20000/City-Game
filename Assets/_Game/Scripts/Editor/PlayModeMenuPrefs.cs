using UnityEditor;

// Editor switch: Play mode starts straight in the city instead of the main menu (GameFlow reads the same pref).
public static class PlayModeMenuPrefs
{
    private const string Pref = "CityGame.SkipMenu";
    private const string MenuPath = "CityBuilder/Skip Main Menu In Play Mode";

    [MenuItem(MenuPath)]
    private static void Toggle() => EditorPrefs.SetBool(Pref, !EditorPrefs.GetBool(Pref, false));

    [MenuItem(MenuPath, true)]
    private static bool ToggleValidate()
    {
        Menu.SetChecked(MenuPath, EditorPrefs.GetBool(Pref, false));
        return true;
    }
}
