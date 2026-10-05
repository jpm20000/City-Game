using System.Text;
using NUnit.Framework;
using UnityEngine;

// M21a probe (throwaway): days to research each tech and to advance with the engaged player, and the RP sources.
public sealed class ResearchProbe
{
    [Test, Explicit("M21 probe")]
    public void Probe()
    {
        var config = ScriptableObject.CreateInstance<BalanceConfig>();
        var city = new EngagedCity(config, 0);
        var sb = new StringBuilder();
        int lastDay = 0;
        float lowest = 1f;
        city.Sim.Tech.TechCompleted += t => { sb.AppendLine($"  d{city.Day,3} (+{city.Day - lastDay,2}) tech {t.Id} (age {t.Age}, cost {t.Cost})"); lastDay = city.Day; };
        city.Sim.Tech.AgeAdvanced += a => { sb.AppendLine($"  d{city.Day,3} (+{city.Day - lastDay,2}) ADVANCE to {a}"); lastDay = city.Day; };
        for (int day = 0; day < 4000 && city.Sim.Tech.CurrentAge < 3; day++)
        {
            city.RunDay();
            float happiness = city.Sim.Population.AverageHappiness;
            if (happiness < lowest - 0.005f)
            {
                lowest = happiness;
                HappinessBreakdown h = city.Sim.Population.Happiness;
                sb.AppendLine($"LOW d{city.Day} pop {city.Sim.Population.Population} happy {happiness:F3}: base {h.Base:F2} unemp {h.Unemployment:F2} tax {h.Taxes:F2} poll {h.Pollution:F2} serv {h.Services:F2} power {h.Power:F2} home {h.Homeless:F2} tech {h.Technology:F2} water {h.Water:F2} crime {h.Crime:F2} fire {h.Fire:F2} health {h.Health:F2} traffic {h.Traffic:F2} | age {city.Sim.Tech.CurrentAge}");
            }
            if (day % 200 == 199)
            {
                ResearchBreakdown b = city.Sim.ResearchBreakdown();
                sb.AppendLine($"d{city.Day}: pop {city.Sim.Population.Population} RP {b.Total:F1} (commercial {b.Commercial:F1}, buildings {b.Buildings:F1}, education {b.Education:F1}, x{b.Multiplier:F2}) active {(city.Sim.Tech.Active != null ? city.Sim.Tech.Active.Cost.ToString() : "-")}");
            }
        }
        sb.AppendLine(city.Report());
        TestContext.WriteLine(sb.ToString());
        Object.DestroyImmediate(config);
    }
}
