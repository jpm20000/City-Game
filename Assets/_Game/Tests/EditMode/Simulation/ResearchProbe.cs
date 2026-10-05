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
        city.Sim.Tech.TechCompleted += t => { sb.AppendLine($"  d{city.Day,3} (+{city.Day - lastDay,2}) tech {t.Id} (age {t.Age}, cost {t.Cost})"); lastDay = city.Day; };
        city.Sim.Tech.AgeAdvanced += a => { sb.AppendLine($"  d{city.Day,3} (+{city.Day - lastDay,2}) ADVANCE to {a}"); lastDay = city.Day; };
        for (int day = 0; day < 400 && city.Sim.Tech.CurrentAge < 3; day++)
        {
            city.RunDay();
            if (day % 15 == 14)
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
