namespace GpxToFusion.Export;

public static class PolylineSimplifier
{
    /// <summary>
    /// Douglas–Peucker. Returns indices (ascending) of kept points; first and last are always kept.
    /// Iterative to avoid stack overflow on long tracks.
    /// </summary>
    public static List<int> Simplify(IReadOnlyList<(double X, double Y)> pts, double tolerance)
    {
        int n = pts.Count;
        if (n <= 2) return Enumerable.Range(0, n).ToList();
        var keep = new bool[n];
        keep[0] = keep[n - 1] = true;
        double tol2 = tolerance * tolerance;

        var stack = new Stack<(int A, int B)>();
        stack.Push((0, n - 1));
        while (stack.Count > 0)
        {
            var (a, b) = stack.Pop();
            if (b - a < 2) continue;
            double maxD = -1;
            int idx = -1;
            for (int i = a + 1; i < b; i++)
            {
                double d = SegmentDistance2(pts[i], pts[a], pts[b]);
                if (d > maxD) { maxD = d; idx = i; }
            }
            if (maxD > tol2)
            {
                keep[idx] = true;
                stack.Push((a, idx));
                stack.Push((idx, b));
            }
        }

        var result = new List<int>();
        for (int i = 0; i < n; i++) if (keep[i]) result.Add(i);
        return result;
    }

    private static double SegmentDistance2((double X, double Y) p, (double X, double Y) a, (double X, double Y) b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        double len2 = dx * dx + dy * dy;
        double t = len2 == 0 ? 0 : Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2, 0, 1);
        double ex = a.X + t * dx - p.X, ey = a.Y + t * dy - p.Y;
        return ex * ex + ey * ey;
    }
}
