using NUnit.Framework;
using UnityEngine;

public sealed class GridDataTests
{
    private GridData m_Grid;

    [SetUp]
    public void SetUp()
    {
        m_Grid = new GridData(24, 24);
    }

    [Test]
    public void NewGrid_IsEmpty()
    {
        Assert.AreEqual(ZoneType.None, m_Grid.GetZone(new Vector2Int(3, 4)));
        Assert.IsFalse(m_Grid.IsRoad(new Vector2Int(3, 4)));
        Assert.IsFalse(m_Grid.IsOccupied(new Vector2Int(3, 4)));
    }

    [Test]
    public void SetZone_RaisesEventOnce()
    {
        int events = 0;
        m_Grid.OnCellChanged += _ => events++;

        m_Grid.SetZone(new Vector2Int(2, 2), ZoneType.Residential);
        m_Grid.SetZone(new Vector2Int(2, 2), ZoneType.Residential);

        Assert.AreEqual(ZoneType.Residential, m_Grid.GetZone(new Vector2Int(2, 2)));
        Assert.AreEqual(1, events);
    }

    [Test]
    public void Occupy_BlocksPlacement_AndReleaseFrees()
    {
        Assert.IsTrue(m_Grid.Occupy(new Vector2Int(5, 5), new Vector2Int(2, 2), 0, 42));

        Assert.IsTrue(m_Grid.IsOccupied(new Vector2Int(5, 5)));
        Assert.IsTrue(m_Grid.IsOccupied(new Vector2Int(6, 6)));
        Assert.AreEqual(42, m_Grid.GetOccupant(new Vector2Int(6, 6)));
        Assert.IsFalse(m_Grid.CanPlace(new Vector2Int(5, 5), Vector2Int.one, 0));

        m_Grid.Release(new Vector2Int(5, 5), new Vector2Int(2, 2), 0);

        Assert.IsFalse(m_Grid.IsOccupied(new Vector2Int(5, 5)));
        Assert.IsTrue(m_Grid.CanPlace(new Vector2Int(5, 5), Vector2Int.one, 0));
    }

    [Test]
    public void Occupy_Overlapping_Fails()
    {
        Assert.IsTrue(m_Grid.Occupy(new Vector2Int(0, 0), new Vector2Int(3, 3), 0, 1));
        Assert.IsFalse(m_Grid.Occupy(new Vector2Int(2, 2), new Vector2Int(2, 2), 0, 2));
    }

    [Test]
    public void Occupy_OutOfBounds_Fails()
    {
        Assert.IsFalse(m_Grid.Occupy(new Vector2Int(23, 23), new Vector2Int(2, 2), 0, 1));
    }

    [Test]
    public void Occupy_ZeroId_Fails()
    {
        Assert.IsFalse(m_Grid.Occupy(new Vector2Int(0, 0), Vector2Int.one, 0, 0));
    }

    [Test]
    public void Road_BlocksPlacement()
    {
        m_Grid.SetRoad(new Vector2Int(10, 10), true);

        Assert.IsFalse(m_Grid.CanPlace(new Vector2Int(10, 10), Vector2Int.one, 0));
        Assert.IsFalse(m_Grid.Occupy(new Vector2Int(10, 10), Vector2Int.one, 0, 7));
    }

    [Test]
    public void Occupy_OddRotation_SwapsFootprint()
    {
        Assert.IsTrue(m_Grid.Occupy(new Vector2Int(10, 10), new Vector2Int(2, 3), 1, 9));

        Assert.IsTrue(m_Grid.IsOccupied(new Vector2Int(12, 10)));
        Assert.IsTrue(m_Grid.IsOccupied(new Vector2Int(10, 11)));
        Assert.IsFalse(m_Grid.IsOccupied(new Vector2Int(10, 12)));

        m_Grid.Release(new Vector2Int(10, 10), new Vector2Int(2, 3), 1);

        Assert.IsFalse(m_Grid.IsOccupied(new Vector2Int(12, 10)));
        Assert.IsTrue(m_Grid.CanPlace(new Vector2Int(10, 10), new Vector2Int(2, 3), 1));
    }

    [Test]
    public void InBounds_MatchesDimensions()
    {
        Assert.IsTrue(m_Grid.InBounds(Vector2Int.zero));
        Assert.IsTrue(m_Grid.InBounds(new Vector2Int(23, 23)));
        Assert.IsFalse(m_Grid.InBounds(new Vector2Int(-1, 0)));
        Assert.IsFalse(m_Grid.InBounds(new Vector2Int(0, 24)));
    }

    [Test]
    public void Resize_EmptiesMap_AndRaisesOnlyOnResized()
    {
        m_Grid.SetRoad(new Vector2Int(1, 1), true);
        m_Grid.SetZone(new Vector2Int(2, 2), ZoneType.Residential);
        m_Grid.SetBuildingLevel(new Vector2Int(2, 2), 2);
        m_Grid.Occupy(new Vector2Int(5, 5), Vector2Int.one, 0, 3);
        int resized = 0, changed = 0;
        m_Grid.OnResized += () => resized++;
        m_Grid.OnCellChanged += _ => changed++;

        m_Grid.Resize(40, 30);

        Assert.AreEqual(40, m_Grid.Width);
        Assert.AreEqual(30, m_Grid.Height);
        Assert.AreEqual(1, resized);
        Assert.AreEqual(0, changed);
        Assert.IsTrue(m_Grid.InBounds(new Vector2Int(39, 29)));
        Assert.IsFalse(m_Grid.InBounds(new Vector2Int(40, 0)));
        Assert.AreEqual(0, m_Grid.CountRoads());
        foreach (Vector2Int cell in new[] { new Vector2Int(1, 1), new Vector2Int(2, 2), new Vector2Int(5, 5) })
        {
            Assert.AreEqual(ZoneType.None, m_Grid.GetZone(cell));
            Assert.AreEqual(0, m_Grid.GetBuildingLevel(cell));
            Assert.IsFalse(m_Grid.IsOccupied(cell));
        }
    }
}
