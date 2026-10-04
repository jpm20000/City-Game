using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

public sealed class CellUtilsTests
{
    private static readonly Vector2Int Size = new Vector2Int(24, 24);

    [Test]
    public void IsInBounds_CornersAndEdges()
    {
        Assert.IsTrue(CellUtils.IsInBounds(Vector2Int.zero, Size));
        Assert.IsTrue(CellUtils.IsInBounds(new Vector2Int(23, 23), Size));
        Assert.IsFalse(CellUtils.IsInBounds(new Vector2Int(-1, 0), Size));
        Assert.IsFalse(CellUtils.IsInBounds(new Vector2Int(24, 0), Size));
        Assert.IsFalse(CellUtils.IsInBounds(new Vector2Int(0, 24), Size));
    }

    [Test]
    public void Index_IsRowMajor()
    {
        Assert.AreEqual(0, CellUtils.Index(new Vector2Int(0, 0), 24));
        Assert.AreEqual(1, CellUtils.Index(new Vector2Int(1, 0), 24));
        Assert.AreEqual(24, CellUtils.Index(new Vector2Int(0, 1), 24));
        Assert.AreEqual(24 * 5 + 7, CellUtils.Index(new Vector2Int(7, 5), 24));
    }

    [Test]
    public void EffectiveSize_OddRotationsSwapDims()
    {
        Vector2Int size = new Vector2Int(3, 1);
        Assert.AreEqual(size, CellUtils.EffectiveSize(size, 0));
        Assert.AreEqual(new Vector2Int(1, 3), CellUtils.EffectiveSize(size, 1));
        Assert.AreEqual(size, CellUtils.EffectiveSize(size, 2));
        Assert.AreEqual(new Vector2Int(1, 3), CellUtils.EffectiveSize(size, 3));
    }

    [Test]
    public void CellToWorld_MapsToCellCenters()
    {
        Vector3 origin = Vector3.zero;
        Assert.That(CellUtils.CellToWorld(Vector2Int.zero, origin),
            Is.EqualTo(new Vector3(0.5f, 0f, 0.5f)).Within(0.0001f));
        Assert.That(CellUtils.CellToWorld(new Vector2Int(23, 23), origin),
            Is.EqualTo(new Vector3(23.5f, 0f, 23.5f)).Within(0.0001f));
    }

    [Test]
    public void WorldToCell_RoundTrips()
    {
        Vector3 origin = Vector3.zero;
        for (int y = 0; y < 24; y++)
        {
            for (int x = 0; x < 24; x++)
            {
                Vector2Int cell = new Vector2Int(x, y);
                Assert.AreEqual(cell, CellUtils.WorldToCell(CellUtils.CellToWorld(cell, origin), origin));
            }
        }
    }

    [Test]
    public void WorldToCell_FloorsToCell()
    {
        Vector3 origin = Vector3.zero;
        Assert.AreEqual(new Vector2Int(5, 7), CellUtils.WorldToCell(new Vector3(5.9f, 0f, 7.9f), origin));
    }

    [Test]
    public void GetFootprint_NoRotation_CoversFullArea()
    {
        List<Vector2Int> cells = new List<Vector2Int>(
            CellUtils.GetFootprint(new Vector2Int(2, 3), new Vector2Int(2, 3), 0));
        Assert.AreEqual(6, cells.Count);
        CollectionAssert.Contains(cells, new Vector2Int(2, 3));
        CollectionAssert.Contains(cells, new Vector2Int(3, 5));
    }

    [Test]
    public void GetFootprint_RotatedSwapsDimensions()
    {
        List<Vector2Int> cells = new List<Vector2Int>(
            CellUtils.GetFootprint(new Vector2Int(0, 0), new Vector2Int(2, 3), 1));
        Assert.AreEqual(6, cells.Count);
        CollectionAssert.Contains(cells, new Vector2Int(2, 0));
        CollectionAssert.Contains(cells, new Vector2Int(2, 1));
        CollectionAssert.DoesNotContain(cells, new Vector2Int(0, 2));
    }

    // M18a: buildings face a neighbouring road. Rotation 0 faces +y, 1 +x, 2 -y, 3 -x.
    [Test]
    public void FacingRoad_TurnsTowardTheOnlyRoadBeside_WhateverTheHash()
    {
        var cell = new Vector2Int(5, 5);
        var steps = new[] { new Vector2Int(0, 1), new Vector2Int(1, 0), new Vector2Int(0, -1), new Vector2Int(-1, 0) };
        for (int rotation = 0; rotation < 4; rotation++)
        {
            var grid = new GridData(12, 12);
            grid.SetRoad(cell + steps[rotation], true);
            for (uint hash = 0; hash < 8; hash++)
            {
                Assert.AreEqual(rotation, CellUtils.FacingRoad(grid, cell, hash << 20), $"road at step {rotation}, hash {hash}");
            }
        }
    }

    [Test]
    public void FacingRoad_CornerLotsPickByHash_AndNoRoadFallsBackToTheHash()
    {
        var cell = new Vector2Int(5, 5);
        var grid = new GridData(12, 12);
        grid.SetRoad(new Vector2Int(5, 6), true);   // +y
        grid.SetRoad(new Vector2Int(6, 5), true);   // +x
        var seen = new HashSet<int>();
        for (uint pick = 0; pick < 4; pick++) seen.Add(CellUtils.FacingRoad(grid, cell, pick << 22));
        CollectionAssert.AreEquivalent(new[] { 0, 1 }, seen, "both roads get chosen by some hash");
        Assert.AreEqual(CellUtils.FacingRoad(grid, cell, 1u << 22), CellUtils.FacingRoad(grid, cell, 1u << 22), "deterministic");

        var empty = new GridData(12, 12);
        for (uint pick = 0; pick < 4; pick++) Assert.AreEqual((int)pick, CellUtils.FacingRoad(empty, cell, pick << 20));
    }
}
