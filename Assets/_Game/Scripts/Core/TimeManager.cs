using System;
using UnityEngine;

public sealed class TimeManager : MonoBehaviour
{
    // Guards against a frame hitch firing a burst of catch-up ticks.
    private const int k_MaxTicksPerFrame = 4;

    [SerializeField] private InputReader m_InputReader;

    private BalanceConfig m_Config;
    private float m_Accumulator;
    private GameSpeed m_SpeedBeforePause = GameSpeed.x1;

    public GameSpeed Speed { get; private set; } = GameSpeed.x1;
    public int Day { get; private set; } = 1;
    public int Month { get; private set; } = 1;
    public int Year { get; private set; } = 1;

    public event Action OnTick;

    public void Init(BalanceConfig config)
    {
        m_Config = config;
    }

    public void SetSpeed(GameSpeed speed)
    {
        if (Speed == speed) return;
        if (speed == GameSpeed.Paused) m_SpeedBeforePause = Speed;
        Speed = speed;
        GameEvents.RaiseSpeedChanged(speed);
    }

    public void TogglePause()
    {
        SetSpeed(Speed == GameSpeed.Paused ? m_SpeedBeforePause : GameSpeed.Paused);
    }

    // Load / new game. Drops any partial day so the first tick is a full day away.
    public void SetDate(int day, int month, int year)
    {
        Day = Mathf.Max(1, day);
        Month = Mathf.Max(1, month);
        Year = Mathf.Max(1, year);
        m_Accumulator = 0f;
        GameEvents.RaiseDateChanged(Day, Month, Year);
    }

    // Debug: runs whole days instantly through the same tick + calendar path as Update.
    public void DebugAdvanceDays(int days)
    {
        if (m_Config == null) return;
        for (int i = 0; i < days; i++)
        {
            OnTick?.Invoke();
            AdvanceDate();
        }
    }

    private void Update()
    {
        HandleSpeedInput();

        if (m_Config == null || Speed == GameSpeed.Paused) return;

        m_Accumulator += Time.deltaTime * Multiplier(Speed);

        int ticks = 0;
        while (m_Accumulator >= m_Config.SecondsPerDay && ticks < k_MaxTicksPerFrame)
        {
            m_Accumulator -= m_Config.SecondsPerDay;
            ticks++;
            OnTick?.Invoke();
            AdvanceDate();
        }
        if (ticks == k_MaxTicksPerFrame) m_Accumulator = 0f;
    }

    // Keys 1-4 map to Paused / 1x / 2x / 4x (SpeedDelta reads 1..4 via Scale processors).
    private void HandleSpeedInput()
    {
        if (m_InputReader == null) return;

        int key = m_InputReader.SpeedDelta;
        if (key >= 1 && key <= 4)
        {
            SetSpeed((GameSpeed)(key - 1));
        }
    }

    private void AdvanceDate()
    {
        Day++;
        if (Day > m_Config.DaysPerMonth)
        {
            Day = 1;
            Month++;
            if (Month > m_Config.MonthsPerYear)
            {
                Month = 1;
                Year++;
            }
        }
        GameEvents.RaiseDateChanged(Day, Month, Year);
    }

    private static float Multiplier(GameSpeed speed)
    {
        switch (speed)
        {
            case GameSpeed.x1: return 1f;
            case GameSpeed.x2: return 2f;
            case GameSpeed.x4: return 4f;
            default: return 0f;
        }
    }
}
