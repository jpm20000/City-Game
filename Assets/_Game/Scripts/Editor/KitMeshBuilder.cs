using System.Collections.Generic;
using UnityEngine;

// Flat-shaded mesh builder for the generated building kit (M18b). Every face samples one swatch of the
// shared palette texture (all of a face's vertices share the swatch centre UV, so there is no bleeding),
// which lets a whole building be one mesh, one renderer and one shared material.
public sealed class KitMeshBuilder
{
    private readonly List<Vector3> m_Vertices = new();
    private readonly List<Vector3> m_Normals = new();
    private readonly List<Vector2> m_Uvs = new();
    private readonly List<int> m_Triangles = new();
    private readonly System.Func<int, Vector2> m_SwatchUv;

    public KitMeshBuilder(System.Func<int, Vector2> swatchUv)
    {
        m_SwatchUv = swatchUv;
    }

    public int VertexCount => m_Vertices.Count;

    // Quad given in any order that walks the outline; the winding is fixed so it faces `outward`.
    public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, int swatch, Vector3 outward)
    {
        Vector3 normal = Vector3.Cross(b - a, c - b);
        if (normal.sqrMagnitude < 1e-12f) normal = Vector3.Cross(c - b, d - c);
        if (Vector3.Dot(normal, outward) < 0f)
        {
            (a, d) = (d, a);
            (b, c) = (c, b);
            normal = -normal;
        }
        if (normal.sqrMagnitude < 1e-12f) return;       // degenerate (a hip's collapsed end)
        normal.Normalize();
        int i = m_Vertices.Count;
        Vector2 uv = m_SwatchUv(swatch);
        m_Vertices.Add(a); m_Vertices.Add(b); m_Vertices.Add(c); m_Vertices.Add(d);
        for (int k = 0; k < 4; k++) { m_Normals.Add(normal); m_Uvs.Add(uv); }
        m_Triangles.Add(i); m_Triangles.Add(i + 1); m_Triangles.Add(i + 2);
        m_Triangles.Add(i); m_Triangles.Add(i + 2); m_Triangles.Add(i + 3);
    }

    public void Tri(Vector3 a, Vector3 b, Vector3 c, int swatch, Vector3 outward)
    {
        Vector3 normal = Vector3.Cross(b - a, c - b);
        if (Vector3.Dot(normal, outward) < 0f)
        {
            (a, c) = (c, a);
            normal = -normal;
        }
        if (normal.sqrMagnitude < 1e-12f) return;
        normal.Normalize();
        int i = m_Vertices.Count;
        Vector2 uv = m_SwatchUv(swatch);
        m_Vertices.Add(a); m_Vertices.Add(b); m_Vertices.Add(c);
        for (int k = 0; k < 3; k++) { m_Normals.Add(normal); m_Uvs.Add(uv); }
        m_Triangles.Add(i); m_Triangles.Add(i + 1); m_Triangles.Add(i + 2);
    }

    // Axis-aligned box; the top face may use another swatch; the bottom is skipped unless asked for.
    public void Box(Vector3 min, Vector3 max, int side, int top = -1, bool bottom = false)
    {
        if (top < 0) top = side;
        Vector3 p000 = new(min.x, min.y, min.z), p100 = new(max.x, min.y, min.z);
        Vector3 p010 = new(min.x, max.y, min.z), p110 = new(max.x, max.y, min.z);
        Vector3 p001 = new(min.x, min.y, max.z), p101 = new(max.x, min.y, max.z);
        Vector3 p011 = new(min.x, max.y, max.z), p111 = new(max.x, max.y, max.z);
        Quad(p001, p101, p111, p011, side, Vector3.forward);    // +Z (front)
        Quad(p000, p100, p110, p010, side, Vector3.back);       // -Z
        Quad(p100, p101, p111, p110, side, Vector3.right);      // +X
        Quad(p000, p001, p011, p010, side, Vector3.left);       // -X
        Quad(p010, p110, p111, p011, top, Vector3.up);
        if (bottom) Quad(p000, p100, p101, p001, side, Vector3.down);
    }

    // Gabled roof. The ridge runs along X when alongX (sloped faces on the +Z / -Z sides) or along Z. The
    // eaves overhang by `over`; the gable ends are filled with `gableSwatch` (the wall colour).
    public void Gable(float x0, float x1, float z0, float z1, float y0, float rise, bool alongX, int roofSwatch, int gableSwatch, float over)
    {
        x0 -= over; x1 += over; z0 -= over; z1 += over;
        float ridgeY = y0 + rise;
        if (alongX)
        {
            float zc = (z0 + z1) * 0.5f;
            Quad(new(x0, y0, z1), new(x1, y0, z1), new(x1, ridgeY, zc), new(x0, ridgeY, zc), roofSwatch, new Vector3(0f, 1f, 1f));
            Quad(new(x0, y0, z0), new(x1, y0, z0), new(x1, ridgeY, zc), new(x0, ridgeY, zc), roofSwatch, new Vector3(0f, 1f, -1f));
            Tri(new(x0, y0, z0), new(x0, y0, z1), new(x0, ridgeY, zc), gableSwatch, Vector3.left);
            Tri(new(x1, y0, z0), new(x1, y0, z1), new(x1, ridgeY, zc), gableSwatch, Vector3.right);
        }
        else
        {
            float xc = (x0 + x1) * 0.5f;
            Quad(new(x1, y0, z0), new(x1, y0, z1), new(xc, ridgeY, z1), new(xc, ridgeY, z0), roofSwatch, new Vector3(1f, 1f, 0f));
            Quad(new(x0, y0, z0), new(x0, y0, z1), new(xc, ridgeY, z1), new(xc, ridgeY, z0), roofSwatch, new Vector3(-1f, 1f, 0f));
            Tri(new(x0, y0, z0), new(x1, y0, z0), new(xc, ridgeY, z0), gableSwatch, Vector3.back);
            Tri(new(x0, y0, z1), new(x1, y0, z1), new(xc, ridgeY, z1), gableSwatch, Vector3.forward);
        }
    }

    // Hipped roof: a frustum whose top is the ridge (along the longer side) or a point (square plan).
    public void Hip(float x0, float x1, float z0, float z1, float y0, float rise, int swatch, float over)
    {
        x0 -= over; x1 += over; z0 -= over; z1 += over;
        float hx = (x1 - x0) * 0.5f, hz = (z1 - z0) * 0.5f, cx = (x0 + x1) * 0.5f, cz = (z0 + z1) * 0.5f;
        float ty = y0 + rise;
        float tx = hx > hz ? hx - hz : 0f, tz = hz > hx ? hz - hx : 0f;
        Vector3 a = new(x0, y0, z0), b = new(x1, y0, z0), c = new(x1, y0, z1), d = new(x0, y0, z1);
        Vector3 ta = new(cx - tx, ty, cz - tz), tb = new(cx + tx, ty, cz - tz), tc = new(cx + tx, ty, cz + tz), td = new(cx - tx, ty, cz + tz);
        Quad(d, c, tc, td, swatch, new Vector3(0f, 1f, 1f));
        Quad(a, b, tb, ta, swatch, new Vector3(0f, 1f, -1f));
        Quad(b, c, tc, tb, swatch, new Vector3(1f, 1f, 0f));
        Quad(a, d, td, ta, swatch, new Vector3(-1f, 1f, 0f));
    }

    // Single-pitch roof falling toward +Z (the street), high at the back.
    public void Lean(float x0, float x1, float z0, float z1, float yFront, float rise, int roofSwatch, int sideSwatch, float over)
    {
        x0 -= over; x1 += over; z0 -= over; z1 += over;
        float yBack = yFront + rise;
        Quad(new(x0, yFront, z1), new(x1, yFront, z1), new(x1, yBack, z0), new(x0, yBack, z0), roofSwatch, new Vector3(0f, 1f, 1f));
        Tri(new(x0, yFront, z0), new(x0, yFront, z1), new(x0, yBack, z0), sideSwatch, Vector3.left);
        Tri(new(x1, yFront, z0), new(x1, yFront, z1), new(x1, yBack, z0), sideSwatch, Vector3.right);
    }

    // Saw-tooth roof: `teeth` ramps along X, each glazed on its upright face (north light).
    public void SawTooth(float x0, float x1, float z0, float z1, float y0, float rise, int teeth, int roofSwatch, int glazeSwatch, int sideSwatch)
    {
        float step = (x1 - x0) / teeth;
        for (int t = 0; t < teeth; t++)
        {
            float xa = x0 + t * step, xb = xa + step;
            Quad(new(xa, y0 + rise, z0), new(xa, y0 + rise, z1), new(xb, y0, z1), new(xb, y0, z0), roofSwatch, new Vector3(1f, 1f, 0f));
            Quad(new(xa, y0, z0), new(xa, y0, z1), new(xa, y0 + rise, z1), new(xa, y0 + rise, z0), glazeSwatch, Vector3.left);
            Tri(new(xa, y0, z0), new(xb, y0, z0), new(xa, y0 + rise, z0), sideSwatch, Vector3.back);
            Tri(new(xa, y0, z1), new(xb, y0, z1), new(xa, y0 + rise, z1), sideSwatch, Vector3.forward);
        }
    }

    // Vertical cylinder / cone frustum (stacks, tanks, towers' cones, kilns).
    public void Cylinder(float cx, float cz, float radius, float y0, float y1, float topRadius, int segments, int side, int top = -1)
    {
        if (top < 0) top = side;
        for (int s = 0; s < segments; s++)
        {
            float a0 = s * Mathf.PI * 2f / segments, a1 = (s + 1) * Mathf.PI * 2f / segments;
            Vector3 b0 = new(cx + Mathf.Cos(a0) * radius, y0, cz + Mathf.Sin(a0) * radius);
            Vector3 b1 = new(cx + Mathf.Cos(a1) * radius, y0, cz + Mathf.Sin(a1) * radius);
            Vector3 t0 = new(cx + Mathf.Cos(a0) * topRadius, y1, cz + Mathf.Sin(a0) * topRadius);
            Vector3 t1 = new(cx + Mathf.Cos(a1) * topRadius, y1, cz + Mathf.Sin(a1) * topRadius);
            Vector3 outward = new(Mathf.Cos((a0 + a1) * 0.5f), 0f, Mathf.Sin((a0 + a1) * 0.5f));
            Quad(b0, b1, t1, t0, side, outward);
            if (topRadius > 0.001f) Tri(t0, t1, new Vector3(cx, y1, cz), top, Vector3.up);
        }
    }

    public Mesh ToMesh(Mesh target = null)
    {
        Mesh mesh = target != null ? target : new Mesh();
        mesh.Clear();
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt16;
        mesh.SetVertices(m_Vertices);
        mesh.SetNormals(m_Normals);
        mesh.SetUVs(0, m_Uvs);
        mesh.SetTriangles(m_Triangles, 0);
        mesh.RecalculateBounds();
        mesh.Optimize();
        return mesh;
    }
}
