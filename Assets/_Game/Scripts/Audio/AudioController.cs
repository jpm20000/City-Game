using System.Collections.Generic;
using UnityEngine;

// Plays the game's audio (M18e). All sounds are 2D (the iso camera has no meaningful listener position), with
// four channels whose volumes come from SoundSettings:
//  - sound effects: AudioController.Play(id, worldPos) from anywhere (a no-op without a controller, so EditMode
//    tests and the sim harness stay silent); a pool of voices, +-6% pitch, a 60 ms gap per effect, panned by the
//    position on screen and quieter when zoomed out;
//  - ambience: per age a base (nature), city (crowds, traffic) and industry (workshops, factories) loop that
//    crossfade over 3 s when the age changes, plus a night loop and a fire loop; the mix follows the camera
//    zoom, the population, the industry share of the cells in view, the time of day and the fires burning;
//  - music: the age's playlist (empty until the player adds tracks), one track at a time with gaps between.
// Purely presentational: nothing here touches the sim or its RNG. Created at runtime by GameManager.
public sealed class AudioController : MonoBehaviour
{
    private const int k_Voices = 12;
    private const float k_MinGap = 0.06f, k_LevelUpGap = 0.25f;
    private const float k_AmbienceFade = 3f;

    public static AudioController Instance { get; private set; }

    // GameManager opens this around a sim tick so growth sounds (level-ups) only play for real growth, not for
    // the blocks that pop in at once when a city loads.
    public static bool AllowGrowthSounds;

    // The last effects played ("time id"), for checks in Play mode where nothing can be listened to.
    public static readonly List<string> PlayLog = new();

    public static void Play(SfxId id, Vector3? worldPosition = null)
    {
        if (Instance != null) Instance.PlayEffect(id, worldPosition);
    }

    // One looping ambience layer: two sources so a clip can be swapped by crossfading.
    private sealed class Layer
    {
        public readonly AudioSource[] Sources = new AudioSource[2];
        public readonly float[] Fade = new float[2];
        public int Active;
        public AudioClip Clip;
        public float Target;                // wanted level 0..1, before the channel volume
        public float Level;                 // smoothed

        public void SetClip(AudioClip clip)
        {
            if (clip == Clip) return;
            Clip = clip;
            Active = 1 - Active;
            AudioSource next = Sources[Active];
            next.clip = clip;
            Fade[Active] = 0f;
            if (clip != null)
            {
                next.time = Random.value * clip.length;
                next.Play();
            }
            else
            {
                next.Stop();
            }
        }

        public void Tick(float dt, float channelVolume, float fadeSeconds)
        {
            Level = Mathf.MoveTowards(Level, Target, dt / 1.5f);
            for (int i = 0; i < 2; i++)
            {
                float goal = i == Active && Clip != null ? 1f : 0f;
                Fade[i] = Mathf.MoveTowards(Fade[i], goal, dt / fadeSeconds);
                AudioSource source = Sources[i];
                source.volume = Fade[i] * Level * channelVolume;
                if (Fade[i] <= 0f && goal <= 0f && source.isPlaying) source.Stop();
            }
        }
    }

    private AudioCatalog m_Catalog;
    private GameManager m_Game;
    private Camera m_Camera;
    private readonly AudioSource[] m_Voices = new AudioSource[k_Voices];
    private int m_NextVoice;
    private readonly Dictionary<SfxId, float> m_LastPlayed = new();
    private Layer m_Base, m_City, m_Industry, m_Night, m_Fire;
    private float m_NextSample;
    private float m_ViewIndustry, m_ViewGrown;
    private AudioSource m_MusicSource;
    private float m_MusicFade, m_MusicGap = 8f;
    private int m_MusicAge = -1;
    private AudioClip m_LastTrack;

    public void Init(AudioCatalog catalog, GameManager game)
    {
        m_Catalog = catalog;
        m_Game = game;
        Instance = this;

        for (int i = 0; i < k_Voices; i++) m_Voices[i] = NewSource("Voice" + i, false);
        m_Base = NewLayer("Base");
        m_City = NewLayer("City");
        m_Industry = NewLayer("Industry");
        m_Night = NewLayer("Night");
        m_Fire = NewLayer("Fire");
        m_Night.SetClip(catalog != null ? catalog.Night : null);
        m_Fire.SetClip(catalog != null ? catalog.Fire : null);
        m_MusicSource = NewSource("Music", false);

        ApplyAge();
        GameEvents.AgeChanged += OnAgeChanged;
        GameEvents.CityLoaded += OnCityLoaded;
        GameEvents.InsufficientFunds += OnInsufficientFunds;
        GameEvents.Notification += OnNotification;
        GameEvents.TechCompleted += OnTechCompleted;
    }

    private AudioSource NewSource(string name, bool loop)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        AudioSource source = go.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = loop;
        source.spatialBlend = 0f;
        return source;
    }

    private Layer NewLayer(string name)
    {
        var layer = new Layer();
        for (int i = 0; i < 2; i++) layer.Sources[i] = NewSource(name + i, true);
        return layer;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        GameEvents.AgeChanged -= OnAgeChanged;
        GameEvents.CityLoaded -= OnCityLoaded;
        GameEvents.InsufficientFunds -= OnInsufficientFunds;
        GameEvents.Notification -= OnNotification;
        GameEvents.TechCompleted -= OnTechCompleted;
    }

    // ---- event hooks -------------------------------------------------------------------------------------

    private void OnAgeChanged(int age)
    {
        ApplyAge();
        PlayEffect(SfxId.AgeAdvance, null);
    }

    private void OnCityLoaded()
    {
        ApplyAge();
        PlayEffect(SfxId.Load, null);
    }

    private void OnInsufficientFunds(float cost) => PlayEffect(SfxId.NoMoney, null);

    private void OnNotification(string message) => PlayEffect(SfxId.Toast, null);

    private void OnTechCompleted(string id) => PlayEffect(SfxId.ResearchDone, null);

    private int CurrentAge()
    {
        SimulationSystem sim = m_Game != null ? m_Game.Simulation : null;
        return sim != null && sim.Tech != null ? sim.Tech.CurrentAge : 2;
    }

    private void ApplyAge()
    {
        if (m_Catalog == null) return;
        AudioCatalog.AmbienceSet set = m_Catalog.GetAmbience(CurrentAge());
        m_Base.SetClip(set?.Base);
        m_City.SetClip(set?.City);
        m_Industry.SetClip(set?.Industry);
    }

    // ---- sound effects -----------------------------------------------------------------------------------

    private void PlayEffect(SfxId id, Vector3? worldPosition)
    {
        if (m_Catalog == null) return;
        AudioCatalog.SfxEntry entry = m_Catalog.GetSfx(id);
        if (entry == null) return;

        float now = Time.unscaledTime;
        float gap = id == SfxId.LevelUp ? k_LevelUpGap : k_MinGap;
        if (m_LastPlayed.TryGetValue(id, out float last) && now - last < gap) return;
        m_LastPlayed[id] = now;

        float volume = entry.Volume * SoundSettings.Effective(SoundChannel.Sfx);
        float pan = 0f;
        if (worldPosition.HasValue)
        {
            Camera cam = GameCamera();
            if (cam != null)
            {
                Vector3 view = cam.WorldToViewportPoint(worldPosition.Value);
                pan = Mathf.Clamp((view.x - 0.5f) * 1.2f, -0.6f, 0.6f);
                if (view.x < -0.1f || view.x > 1.1f || view.y < -0.1f || view.y > 1.1f) volume *= 0.5f;
                volume *= Mathf.Lerp(0.55f, 1f, ZoomIn(cam));
            }
        }

        // Always logged (the checks in Play mode read it); a silent mix just plays nothing.
        PlayLog.Add($"{now:F1} {id}");
        if (PlayLog.Count > 200) PlayLog.RemoveRange(0, 100);
        if (volume <= 0.001f) return;

        AudioClip clip = entry.Clips[Random.Range(0, entry.Clips.Length)];
        AudioSource voice = m_Voices[m_NextVoice];
        m_NextVoice = (m_NextVoice + 1) % k_Voices;
        voice.clip = clip;
        voice.volume = Mathf.Clamp01(volume);
        voice.pitch = 1f + Random.Range(-0.06f, 0.06f);
        voice.panStereo = pan;
        voice.Play();
    }

    private Camera GameCamera()
    {
        if (m_Camera == null) m_Camera = Camera.main;
        return m_Camera;
    }

    // 1 when zoomed right in, 0 when the whole map is in view.
    private static float ZoomIn(Camera cam) => Mathf.Clamp01(Mathf.InverseLerp(30f, 5f, cam.orthographicSize));

    // ---- ambience and music ------------------------------------------------------------------------------

    private void Update()
    {
        if (m_Catalog == null || m_Game == null || m_Game.Simulation == null) return;
        float dt = Time.unscaledDeltaTime;
        Camera cam = GameCamera();

        if (Time.unscaledTime >= m_NextSample)
        {
            m_NextSample = Time.unscaledTime + 0.5f;
            SampleView(cam);
        }

        float zoom = cam != null ? ZoomIn(cam) : 0.5f;
        float night = m_Game.DayNight != null ? m_Game.DayNight.Night : 0f;
        int population = m_Game.Population != null ? m_Game.Population.Population : 0;
        float town = Mathf.Clamp01(population / 400f);
        float industryShare = m_ViewGrown > 0f ? m_ViewIndustry / m_ViewGrown : 0f;
        int burning = m_Game.Simulation.Disasters != null ? m_Game.Simulation.Disasters.Fire.BurningCount : 0;

        m_Base.Target = (0.55f + 0.45f * zoom) * (1f - 0.55f * town) * (1f - 0.6f * night);
        m_City.Target = town * (0.5f + 0.5f * zoom) * (1f - 0.4f * night);
        m_Industry.Target = Mathf.Clamp01(industryShare * 2.5f) * Mathf.Clamp01(m_ViewIndustry / 12f) * (0.5f + 0.5f * zoom);
        m_Night.Target = night * 0.8f;
        m_Fire.Target = Mathf.Clamp01(burning / 6f) * 0.8f;

        float ambience = SoundSettings.Effective(SoundChannel.Ambience);
        m_Base.Tick(dt, ambience, k_AmbienceFade);
        m_City.Tick(dt, ambience, k_AmbienceFade);
        m_Industry.Tick(dt, ambience, k_AmbienceFade);
        m_Night.Tick(dt, ambience, k_AmbienceFade);
        m_Fire.Tick(dt, ambience, 1f);

        TickMusic(dt);
    }

    // Counts the grown cells in the camera's view (its corners hit the ground plane; a cheap bounded scan).
    private void SampleView(Camera cam)
    {
        m_ViewIndustry = m_ViewGrown = 0f;
        GridData grid = m_Game.Grid;
        if (cam == null || grid == null) return;

        var ground = new Plane(Vector3.up, Vector3.zero);
        Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = new Vector2(float.MinValue, float.MinValue);
        foreach (Vector2 corner in new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) })
        {
            Ray ray = cam.ViewportPointToRay(corner);
            if (!ground.Raycast(ray, out float t)) continue;
            Vector3 p = ray.GetPoint(t);
            min = Vector2.Min(min, new Vector2(p.x, p.z));
            max = Vector2.Max(max, new Vector2(p.x, p.z));
        }
        if (min.x > max.x) return;

        int x0 = Mathf.Max(0, Mathf.FloorToInt(min.x)), x1 = Mathf.Min(grid.Width - 1, Mathf.CeilToInt(max.x));
        int y0 = Mathf.Max(0, Mathf.FloorToInt(min.y)), y1 = Mathf.Min(grid.Height - 1, Mathf.CeilToInt(max.y));
        for (int y = y0; y <= y1; y++)
        {
            for (int x = x0; x <= x1; x++)
            {
                var cell = new Vector2Int(x, y);
                if (grid.GetBuildingLevel(cell) == 0) continue;
                m_ViewGrown++;
                if (grid.GetZone(cell) == ZoneType.Industrial) m_ViewIndustry++;
            }
        }
    }

    private void TickMusic(float dt)
    {
        int age = CurrentAge();
        AudioCatalog.MusicSet set = m_Catalog.GetMusic(age);
        bool has = set != null && set.Tracks != null && set.Tracks.Length > 0;
        float volume = SoundSettings.Effective(SoundChannel.Music);

        if (m_MusicAge != age)
        {
            // A new age: fade the old track out, then the new playlist starts after the gap.
            if (m_MusicSource.isPlaying) m_MusicFade = Mathf.MoveTowards(m_MusicFade, 0f, dt / 2f);
            if (!m_MusicSource.isPlaying || m_MusicFade <= 0f)
            {
                m_MusicSource.Stop();
                m_MusicAge = age;
                m_MusicGap = 3f;
            }
        }
        else if (m_MusicSource.isPlaying)
        {
            m_MusicFade = Mathf.MoveTowards(m_MusicFade, 1f, dt / 3f);
        }
        else if (has)
        {
            m_MusicGap -= dt;
            if (m_MusicGap <= 0f)
            {
                AudioClip track = set.Tracks[Random.Range(0, set.Tracks.Length)];
                if (track == m_LastTrack && set.Tracks.Length > 1) track = set.Tracks[(System.Array.IndexOf(set.Tracks, track) + 1) % set.Tracks.Length];
                m_LastTrack = track;
                m_MusicSource.clip = track;
                m_MusicFade = 0f;
                m_MusicSource.Play();
                m_MusicGap = Random.Range(15f, 45f);
            }
        }
        m_MusicSource.volume = m_MusicFade * volume;
    }
}
