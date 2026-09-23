using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Builds flat road ribbons and raised curbs, leaving overlapping junction lanes open.</summary>
public static class SplineJsonMeshBuilder
{
    private sealed class Geometry
    {
        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<int> triangles = new List<int>();
        private readonly List<Vector2> uv = new List<Vector2>();

        public void Triangle(Vector3 a, Vector3 b, Vector3 c)
        {
            if (Vector3.Cross(b - a, c - a).y < 0f) { var swap = b; b = c; c = swap; }
            int first = vertices.Count;
            vertices.Add(a); vertices.Add(b); vertices.Add(c);
            uv.Add(new Vector2(a.x, a.z)); uv.Add(new Vector2(b.x, b.z)); uv.Add(new Vector2(c.x, c.z));
            triangles.Add(first); triangles.Add(first + 1); triangles.Add(first + 2);
        }

        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal)
        {
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), normal) < 0f)
            { var swap = b; b = d; d = swap; }
            int first = vertices.Count;
            vertices.Add(a); vertices.Add(b); vertices.Add(c); vertices.Add(d);
            uv.Add(new Vector2(a.x, a.z)); uv.Add(new Vector2(b.x, b.z));
            uv.Add(new Vector2(c.x, c.z)); uv.Add(new Vector2(d.x, d.z));
            triangles.Add(first); triangles.Add(first + 1); triangles.Add(first + 2);
            triangles.Add(first); triangles.Add(first + 2); triangles.Add(first + 3);
        }

        public Mesh ToMesh(string name)
        {
            var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0); mesh.SetUVs(0, uv);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
        }
    }

    private struct Segment
    {
        public Vector3 a, b;
        public int path;
    }

    private struct FillTriangle { public Vector3 a, b, c; }

    public static void Build(IReadOnlyList<Vector3[]> paths, float width, float curbWidth, float curbHeight,
        out Mesh roadMesh, out Mesh curbMesh, IReadOnlyList<Vector3?> fillCenters = null)
    {
        if (width <= 0f || curbWidth < 0f || curbHeight < 0f) throw new ArgumentException("Invalid road dimensions.");
        var road = new Geometry();
        var curb = new Geometry();
        var segments = new List<Segment>();
        for (int p = 0; p < paths.Count; p++)
            for (int i = 1; i < paths[p].Length; i++)
                segments.Add(new Segment { a = paths[p][i - 1], b = paths[p][i], path = p });
        float half = width * 0.5f;
        var fills = new List<FillTriangle>();
        // A turn's control point lies on the straight crossing in this schema. Fill toward it
        // so the meeting road ribbons do not leave small enclosed holes in the junction.
        if (fillCenters != null)
            for (int p = 0; p < paths.Count; p++)
            {
                if (!fillCenters[p].HasValue || !Covered(fillCenters[p].Value, p, segments, half)) continue;
                Vector3 center = fillCenters[p].Value;
                for (int i = 1; i < paths[p].Length; i++)
                {
                    Vector3 a = paths[p][i - 1], b = paths[p][i];
                    if (Vector3.Cross(a - center, b - center).sqrMagnitude < 0.00000001f) continue;
                    fills.Add(new FillTriangle { a = center, b = a, c = b });
                    road.Triangle(center, a, b);
                }
            }
        for (int pathIndex = 0; pathIndex < paths.Count; pathIndex++)
        {
            Vector3[] points = paths[pathIndex];
            var offsets = new Vector3[points.Length];
            for (int i = 0; i < points.Length; i++)
            {
                Vector3 incoming = (points[i] - points[Mathf.Max(0, i - 1)]).normalized;
                Vector3 outgoing = (points[Mathf.Min(i + 1, points.Length - 1)] - points[i]).normalized;
                if (i == 0) incoming = outgoing;
                if (i == points.Length - 1) outgoing = incoming;
                Vector3 right = Vector3.Cross(Vector3.up, outgoing).normalized;
                Vector3 average = Vector3.Cross(Vector3.up, incoming + outgoing).normalized;
                if (average.sqrMagnitude < 0.01f) average = right;
                offsets[i] = average / Mathf.Max(0.5f, Vector3.Dot(average, right));
            }
            for (int i = 1; i < points.Length; i++)
            {
                Vector3 a = points[i - 1], b = points[i];
                Vector3 oa = offsets[i - 1], ob = offsets[i];
                road.Quad(a - oa * half, b - ob * half, b + ob * half, a + oa * half, Vector3.up);
                if (curbWidth == 0f || curbHeight == 0f) continue;
                foreach (int side in new[] { -1, 1 })
                {
                    Vector3 innerA = a + oa * (half * side), innerB = b + ob * (half * side);
                    // Only exposed boundaries receive curbs. Do not fence across another route.
                    if (Covered(innerA, pathIndex, segments, half) || Covered(innerB, pathIndex, segments, half) ||
                        Covered((innerA + innerB) * 0.5f, pathIndex, segments, half) ||
                        InFilledJunction(innerA, fills) || InFilledJunction(innerB, fills) ||
                        InFilledJunction((innerA + innerB) * 0.5f, fills)) continue;
                    Vector3 outerA = a + oa * ((half + curbWidth) * side);
                    Vector3 outerB = b + ob * ((half + curbWidth) * side);
                    Vector3 up = Vector3.up * curbHeight;
                    Vector3 outward = (oa + ob).normalized * side;
                    Vector3 forward = (b - a).normalized;
                    curb.Quad(innerA + up, innerB + up, outerB + up, outerA + up, Vector3.up);
                    curb.Quad(innerA, innerB, innerB + up, innerA + up, -outward);
                    curb.Quad(outerA, outerB, outerB + up, outerA + up, outward);
                    curb.Quad(innerA, outerA, outerA + up, innerA + up, -forward);
                    curb.Quad(innerB, outerB, outerB + up, innerB + up, forward);
                }
            }
        }
        roadMesh = road.ToMesh("SplineJson Road Surface");
        curbMesh = curb.ToMesh("SplineJson Raised Curbs");
    }

    private static bool Covered(Vector3 point, int ownPath, List<Segment> segments, float halfWidth)
    {
        float threshold = Mathf.Max(0f, halfWidth - 0.005f);
        foreach (var segment in segments)
        {
            if (segment.path == ownPath) continue;
            Vector3 ab = segment.b - segment.a;
            float lengthSquared = ab.sqrMagnitude;
            if (lengthSquared < 0.000001f) continue;
            float t = Vector3.Dot(point - segment.a, ab) / lengthSquared;
            if (t < 0f || t > 1f) continue;
            if ((point - (segment.a + ab * t)).sqrMagnitude < threshold * threshold) return true;
        }
        return false;
    }

    private static bool InFilledJunction(Vector3 point, List<FillTriangle> triangles)
    {
        foreach (var triangle in triangles)
        {
            float a = Cross(triangle.a, triangle.b, point);
            float b = Cross(triangle.b, triangle.c, point);
            float c = Cross(triangle.c, triangle.a, point);
            if ((a >= -0.00001f && b >= -0.00001f && c >= -0.00001f) ||
                (a <= 0.00001f && b <= 0.00001f && c <= 0.00001f)) return true;
        }
        return false;
    }

    private static float Cross(Vector3 a, Vector3 b, Vector3 p)
    {
        return (b.x - a.x) * (p.z - a.z) - (b.z - a.z) * (p.x - a.x);
    }
}
