using System.Collections.Generic;
using UnityEngine;

namespace LivingLandscapes.Overlay;

/// <summary>
/// Bakes line segments into flat quad meshes, so thousands of contour pieces draw in a few calls per frame
/// instead of one call per segment. Split like vanilla's CellBoolDrawer to stay under the 16-bit index limit.
/// </summary>
public class LineMeshBuilder
{
    private const int MaxQuadsPerMesh = 16383;

    private readonly List<Mesh> meshes = new List<Mesh>();
    private readonly List<Vector3> verts = new List<Vector3>();
    private readonly List<int> tris = new List<int>();
    private readonly float altitude;

    public LineMeshBuilder(float altitude)
    {
        this.altitude = altitude;
    }

    public void Add(Vector2 a, Vector2 b, float width)
    {
        Vector2 dir = b - a;
        if (dir.sqrMagnitude < 1e-8f)
            return;
        // Extend each end by half the width so consecutive segments overlap instead of leaving notches.
        Vector2 along = dir.normalized * (width * 0.5f);
        Vector2 side = new Vector2(-along.y, along.x);
        a -= along;
        b += along;

        int start = verts.Count;
        verts.Add(new Vector3(a.x - side.x, altitude, a.y - side.y));
        verts.Add(new Vector3(a.x + side.x, altitude, a.y + side.y));
        verts.Add(new Vector3(b.x + side.x, altitude, b.y + side.y));
        verts.Add(new Vector3(b.x - side.x, altitude, b.y - side.y));
        tris.Add(start);
        tris.Add(start + 1);
        tris.Add(start + 2);
        tris.Add(start);
        tris.Add(start + 2);
        tris.Add(start + 3);

        if (verts.Count / 4 >= MaxQuadsPerMesh)
            Flush();
    }

    public List<Mesh> Finish()
    {
        Flush();
        return meshes;
    }

    private void Flush()
    {
        if (verts.Count == 0)
            return;
        var mesh = new Mesh { name = "LivingLandscapesLines" };
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        meshes.Add(mesh);
        verts.Clear();
        tris.Clear();
    }
}
