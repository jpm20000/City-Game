using NUnit.Framework;
using UnityEngine;

public sealed class RoadNetworkTests
{
    private GridData m_Grid;
    private RoadNetwork m_Roads;

    [SetUp]
    public void SetUp()
    {
        m_Grid = new GridData(24, 24);
        m_Roads = new RoadNetwork(m_Grid);
    }

    [Test]
    public void EmptyGrid_HasNoAccess()
    {
        Assert.IsFalse(m_Roads.HasRoadAccess(new Vector2Int(5, 5)));
        Assert.IsFalse(m_Roads.IsConnectedToEntry(new Vector2Int(5, 5)));
    }

    [Test]
    public void RoadOnEdge_GrantsAdjacentAccess()
    {
        m_Grid.SetRoad(new Vector2Int(0, 5), true);

        Assert.IsTrue(m_Roads.IsConnectedToEntry(new Vector2Int(0, 5)));
        Assert.IsTrue(m_Roads.HasRoadAccess(new Vector2Int(1, 5)));
    }

    [Test]
    public void InteriorRoad_NotConnectedToEdge_HasNoAccess()
    {
        m_Grid.SetRoad(new Vector2Int(10, 10), true);

        Assert.IsFalse(m_Roads.IsConnectedToEntry(new Vector2Int(10, 10)));
        Assert.IsFalse(m_Roads.HasRoadAccess(new Vector2Int(10, 9)));
    }

    [Test]
    public void ChainFromEdge_GrantsAccessAlongPath()
    {
        m_Grid.SetRoad(new Vector2Int(0, 0), true);
        m_Grid.SetRoad(new Vector2Int(1, 0), true);
        m_Grid.SetRoad(new Vector2Int(2, 0), true);
        m_Grid.SetRoad(new Vector2Int(3, 0), true);

        Assert.IsTrue(m_Roads.IsConnectedToEntry(new Vector2Int(3, 0)));
        Assert.IsTrue(m_Roads.HasRoadAccess(new Vector2Int(3, 1)));
        Assert.IsTrue(m_Roads.HasRoadAccess(new Vector2Int(1, 1)));
    }

    [Test]
    public void Diagonal_DoesNotCountAsAdjacent()
    {
        m_Grid.SetRoad(new Vector2Int(0, 0), true);

        Assert.IsFalse(m_Roads.HasRoadAccess(new Vector2Int(1, 1)));
    }

    [Test]
    public void FloatingCluster_HasNoAccess_WhileEdgeClusterDoes()
    {
        m_Grid.SetRoad(new Vector2Int(0, 0), true);
        m_Grid.SetRoad(new Vector2Int(0, 1), true);

        m_Grid.SetRoad(new Vector2Int(10, 10), true);
        m_Grid.SetRoad(new Vector2Int(10, 11), true);

        Assert.IsTrue(m_Roads.HasRoadAccess(new Vector2Int(1, 0)));
        Assert.IsFalse(m_Roads.HasRoadAccess(new Vector2Int(9, 10)));
        Assert.IsFalse(m_Roads.IsConnectedToEntry(new Vector2Int(10, 10)));
        Assert.IsTrue(m_Roads.IsConnectedToEntry(new Vector2Int(0, 0)));
    }

    [Test]
    public void OutOfBounds_ReturnsFalse()
    {
        Assert.IsFalse(m_Roads.HasRoadAccess(new Vector2Int(-1, -1)));
        Assert.IsFalse(m_Roads.IsConnectedToEntry(new Vector2Int(24, 24)));
    }

    [Test]
    public void RoadChanges_InvalidateConnectivity()
    {
        m_Grid.SetRoad(new Vector2Int(10, 10), true);
        Assert.IsFalse(m_Roads.HasRoadAccess(new Vector2Int(10, 9)));

        m_Grid.SetRoad(new Vector2Int(10, 10), false);
        m_Grid.SetRoad(new Vector2Int(0, 0), true);
        Assert.IsFalse(m_Roads.HasRoadAccess(new Vector2Int(10, 9)));
        Assert.IsTrue(m_Roads.HasRoadAccess(new Vector2Int(1, 0)));
    }
}
