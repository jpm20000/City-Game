# Sim harness (no Unity needed)

Runs the pure sim's EditMode tests (`CityBuilder.Grid` + `CityBuilder.Simulation` code and
`Tests/EditMode/Simulation`) under Mono with NUnitLite, for sessions without the Unity Editor (cloud
containers, CI). It is a stand-in, not a replacement: run the real Test Runner before calling a
milestone done.

    Tools/sim-harness/run.sh                         # all Simulation tests (first run installs the toolchain)
    Tools/sim-harness/run.sh --test=CivicTests       # one fixture / test (NUnitLite filters)
    Tools/sim-harness/run.sh --where "test =~ Water" # by name pattern
    Tools/sim-harness/run.sh --explicit --test=AgeBalanceTests.Report_FromMedievalWithAsset
    EXTRA="path/Probe.cs" Tools/sim-harness/run.sh --test=Probe   # compile a throwaway probe in too

**What it stubs** (this folder; Unity never compiles it — it is outside `Assets/`):
- `UnityStub.cs`: `Vector2Int`, `Vector2`, `Vector3`, `Mathf`, `Object` / `ScriptableObject`
  (`CreateInstance` calls `Awake` / `OnEnable`; destroyed objects compare equal to null), the
  inspector attributes, `Debug`, `Time`, `Application.dataPath`, `JsonUtility` (→ `Json.cs`).
- `Json.cs`: `JsonUtility` rules — public / `[SerializeField]` fields, arrays and `List<T>`, enums as
  ints, null arrays / strings written empty, fields missing from the JSON keep their initial value.
- `EditorStub.cs`: `AssetDatabase` (`LoadAssetAtPath`, `FindAssets("t:Type")`, `GUIDToAssetPath`)
  and `SerializedObject` / `SerializedProperty`, backed by the real `.asset` YAML and `.meta` GUIDs.
  Simulation types are filled by reflection (primitive arrays are Unity's little-endian hex);
  `Assembly-CSharp` types (`BuildingDefinition`, `AgeVisualSet`) load as `YamlAsset` and are read
  through `SerializedObject`. Fields an older asset lacks read as defaults, like Unity. References to other
  files (prefabs, materials) load as placeholder objects, so null checks on them work.

**Fidelity:** checked against M13's recorded Editor numbers: from Medieval 86 / 62 / 69 days (Modern on
day 217, 9 wells, min happiness 0.64) and the Industrial-start baseline 220 / 0.660 come out identical.

**Not covered:** `Assembly-CSharp` (runtime, UI, scenes, prefabs), the Grid tests, Play mode.

**Toolchain** (`setup.sh`, cached in `.cache/`, gitignored): `apt-get install mono-devel`, then Roslyn
`csc` (Microsoft.Net.Compilers.Toolset 4.8.0, C# 9) and NUnit / NUnitLite 3.13.3 from api.nuget.org.
The .NET SDK download host may be blocked by a network policy; NuGet and apt usually aren't.
