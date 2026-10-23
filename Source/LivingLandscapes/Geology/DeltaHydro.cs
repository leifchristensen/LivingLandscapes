using System.Collections.Generic;
using UnityEngine;

namespace LivingLandscapes.Geology;

/// <summary>Tuning of <see cref="DeltaHydro"/>. Depths in metres unless noted.</summary>
public class DeltaHydroSettings
{
    /// <summary>Bed updates; the water surface is solved again before each.</summary>
    public int epochs = 150;

    /// <summary>Relaxation sweeps of the water surface per epoch (the first epoch takes firstSweeps), and the over-relaxation factor.</summary>
    public int sweeps = 150;
    public int firstSweeps = 2000;
    public float overRelaxation = 1f;

    /// <summary>Sediment over the delta's life: this times the river's width squared times its depth (metres x cells).</summary>
    public float volumeWidths = 100f;

    /// <summary>
    /// Conveyance: discharge between cells grows with depth^conveyance times the surface's fall, scaled so the river at its own
    /// depth runs on a surface falling riverSlope metres per cell.
    /// </summary>
    public float conveyance = 1.67f;
    public float riverSlope = 0.002f;

    /// <summary>The surface's first guess falls this many metres per cell toward open water (the flow spreads, so less than riverSlope).</summary>
    public float guessSlope = 0.0005f;

    /// <summary>
    /// Sediment transport grows with discharge x velocity^(velocityExponent - 1) (Engelund-Hansen-like). Sediment also moves down
    /// the bed's slope (bedSlope x the transport x the bed's fall per cell): banks slump into channels, which sets their width.
    /// </summary>
    public float velocityExponent = 3f;
    public float bedSlope = 1.5f;

    /// <summary>The bed changes at most this many river depths per epoch; open water deeper than openDepth river depths is the sea's.</summary>
    public float maxBedChange = 0.05f;
    public float openDepth = 1.5f;

    /// <summary>Water shallower than this is dry; deeper than deepShare river depths is painted deep.</summary>
    public float minDepth = 0.02f;
    public float deepShare = 0.7f;
}

/// <summary>
/// A delta from a solved water surface (diffusive wave) and the bed's sediment balance (Exner). The river's discharge enters at
/// the mouth; the surface over all wet ground is solved so that the water flowing between neighbours (depth^5/3 x fall) balances,
/// with the sea's level over open water. Flow divides round islands and curves between them by itself. Sediment moves with the
/// flow, faster in fast water, and down the bed's slope; the bed rises where less leaves a cell than enters (the flow slows:
/// bars, fronts) and falls where more leaves (it speeds up: channels), resistant rock barely. Banks slumping into channels set
/// their width.
/// </summary>
public static class DeltaHydro
{
    public static int Grow(float[] height, bool[] sea, float[] erodibility, int start, Vector2 heading, float width, float depth, float seaLevel,
        int sizeX, int sizeZ, DeltaHydroSettings p, out float[] dischargeOut, out float[] surfaceOut, out string report)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        int n = sizeX * sizeZ;
        depth = Mathf.Max(depth, 0.2f);
        width = Mathf.Max(width, 1f);
        var discharge = dischargeOut = new float[n];
        var surface = surfaceOut = new float[n];

        var across = new Vector2(-heading.y, heading.x);
        var inlet = new List<int>();
        int half = Mathf.Max(0, Mathf.RoundToInt(width * 0.5f));
        for (int k = -half; k <= half; k++)
        {
            int x = Mathf.FloorToInt(start % sizeX + 0.5f + across.x * k), z = Mathf.FloorToInt(start / sizeX + 0.5f + across.y * k);
            // Only cells in the channel (low ground), so the river does not pour onto the banks beside its mouth.
            if (x >= 0 && z >= 0 && x < sizeX && z < sizeZ && !inlet.Contains(z * sizeX + x) && (height[z * sizeX + x] < seaLevel + 0.5f * depth || k == 0))
                inlet.Add(z * sizeX + x);
        }
        var isInlet = new bool[n];
        foreach (int i in inlet)
            isInlet[i] = true;

        float riverQ = width * depth;
        // The river's discharge per cell of its width (its depth, at the mouth's velocity) on riverSlope.
        float friction = depth / (Mathf.Pow(depth, p.conveyance) * p.riverSlope);
        float perEpoch = p.volumeWidths * width * width * depth / Mathf.Max(1, p.epochs);
        // Transport at the mouth's velocity carries the supply: the coefficient on discharge x velocity^(e-1).
        float coefficient = perEpoch / riverQ;
        float maxChange = p.maxBedChange * depth;
        float floor = seaLevel - 2f * depth;
        var fixedLevel = new bool[n];
        var original = (float[])height.Clone();
        // First guess of the surface: falling gently toward open water along the water (distance through cells under the sea's
        // level), so relaxation only corrects it locally; dry ground rests at its bed.
        var toSea = new float[n];
        var queue = new Queue<int>();
        for (int i = 0; i < n; i++)
        {
            int x = i % sizeX, z = i / sizeX;
            bool edge = x == 0 || z == 0 || x == sizeX - 1 || z == sizeZ - 1;
            bool open = !isInlet[i] && sea[i] && (seaLevel - height[i] > p.openDepth * depth || (edge && height[i] < seaLevel));
            toSea[i] = open ? 0f : -1f;
            if (open)
                queue.Enqueue(i);
        }
        while (queue.Count > 0)
        {
            int c = queue.Dequeue();
            int cx = c % sizeX, cz = c / sizeX;
            foreach ((int dx, int dz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                int x = cx + dx, z = cz + dz;
                if (x < 0 || z < 0 || x >= sizeX || z >= sizeZ)
                    continue;
                int nb = z * sizeX + x;
                if (toSea[nb] >= 0f || (height[nb] >= seaLevel && !isInlet[nb]))
                    continue;
                toSea[nb] = toSea[c] + 1f;
                queue.Enqueue(nb);
            }
        }
        for (int i = 0; i < n; i++)
            surface[i] = toSea[i] >= 0f ? Mathf.Max(height[i], seaLevel + p.guessSlope * toSea[i]) : Mathf.Max(seaLevel, height[i]);
        var kx = new float[n];   // conductance to the east neighbour
        var kz = new float[n];   // conductance to the north neighbour
        var fx = new float[n];   // water flux to the east neighbour
        var fz = new float[n];
        var sx = new float[n];   // sediment flux to the east neighbour
        var sz = new float[n];
        var change = new float[n];
        float supplied = 0f, deposited = 0f, eroded = 0f;

        float Depth(int i) => Mathf.Max(0f, surface[i] - height[i]);
        float Conductance(int a, int b)
        {
            // Water between two cells: the depth over the higher bed, to the conveyance power.
            float h = Mathf.Max(0f, Mathf.Min(surface[a], surface[b]) * 0.5f + Mathf.Max(surface[a], surface[b]) * 0.5f - Mathf.Max(height[a], height[b]));
            return h <= p.minDepth ? 0f : friction * Mathf.Pow(h, p.conveyance);
        }

        for (int epoch = 0; epoch < p.epochs; epoch++)
        {
            // Open water holds the sea's level; the inlet keeps its depth.
            for (int i = 0; i < n; i++)
            {
                int x = i % sizeX, z = i / sizeX;
                bool edge = x == 0 || z == 0 || x == sizeX - 1 || z == sizeZ - 1;
                fixedLevel[i] = !isInlet[i] && sea[i] && (seaLevel - original[i] > p.openDepth * depth || (edge && height[i] < seaLevel));
                if (fixedLevel[i])
                    surface[i] = seaLevel;
            }
            foreach (int i in inlet)
                height[i] = Mathf.Min(height[i], surface[i] - depth);

            // The water surface: Gauss-Seidel with over-relaxation on the flow balance (inflow at the inlet, sea level over open water).
            int sweeps = epoch == 0 ? p.firstSweeps : p.sweeps;
            for (int sweep = 0; sweep < sweeps; sweep++)
            {
                for (int i = 0; i < n; i++)
                {
                    int x = i % sizeX, z = i / sizeX;
                    kx[i] = x + 1 < sizeX ? Conductance(i, i + 1) : 0f;
                    kz[i] = z + 1 < sizeZ ? Conductance(i, i + sizeX) : 0f;
                }
                for (int i = 0; i < n; i++)
                {
                    if (fixedLevel[i])
                        continue;
                    int x = i % sizeX, z = i / sizeX;
                    float ke = kx[i], kw = x > 0 ? kx[i - 1] : 0f, kn = kz[i], ks = z > 0 ? kz[i - sizeX] : 0f;
                    float total = ke + kw + kn + ks;
                    float source = isInlet[i] ? riverQ / inlet.Count : 0f;
                    if (total <= 0f)
                    {
                        // Dry ground (or a cell the water cannot leave): its surface rests at its bed, or rises with any inflow.
                        if (source > 0f)
                            surface[i] += 0.01f;
                        continue;
                    }
                    float balanced = (source + ke * (x + 1 < sizeX ? surface[i + 1] : 0f) + kw * (x > 0 ? surface[i - 1] : 0f)
                        + kn * (z + 1 < sizeZ ? surface[i + sizeX] : 0f) + ks * (z > 0 ? surface[i - sizeX] : 0f)) / total;
                    surface[i] = Mathf.Max(height[i], surface[i] + p.overRelaxation * (balanced - surface[i]));
                }
            }

            // Fluxes on faces: water by conductance and fall; sediment with the water, faster in fast water, and down the bed's slope.
            for (int i = 0; i < n; i++)
            {
                int x = i % sizeX, z = i / sizeX;
                fx[i] = x + 1 < sizeX ? kx[i] * (surface[i] - surface[i + 1]) : 0f;
                fz[i] = z + 1 < sizeZ ? kz[i] * (surface[i] - surface[i + sizeX]) : 0f;
            }
            for (int i = 0; i < n; i++)
            {
                int x = i % sizeX, z = i / sizeX;
                float qx = 0.5f * (fx[i] + (x > 0 ? fx[i - 1] : 0f)), qz = 0.5f * (fz[i] + (z > 0 ? fz[i - sizeX] : 0f));
                discharge[i] = Mathf.Sqrt(qx * qx + qz * qz);
            }
            float Velocity(int i) => discharge[i] / Mathf.Max(Depth(i), p.minDepth) / (riverQ / width / depth);
            for (int i = 0; i < n; i++)
            {
                int x = i % sizeX, z = i / sizeX;
                sx[i] = x + 1 < sizeX ? Sediment(fx[i], i, i + 1) : 0f;
                sz[i] = z + 1 < sizeZ ? Sediment(fz[i], i, i + sizeX) : 0f;
            }
            float Sediment(float water, int a, int b)
            {
                if (water == 0f)
                    return 0f;
                int up = water > 0f ? a : b;
                float u = Velocity(up);
                float carried = coefficient * water * Mathf.Pow(u, p.velocityExponent - 1f);
                // Down the bed's slope: toward the lower bed, in proportion to what moves.
                return carried + p.bedSlope * Mathf.Abs(carried) * (height[a] - height[b]);
            }

            // The bed: what enters less what leaves (supply at the inlet). Erosion scaled by erodibility; open water keeps what arrives.
            System.Array.Clear(change, 0, n);
            for (int i = 0; i < n; i++)
            {
                int x = i % sizeX, z = i / sizeX;
                float net = (x > 0 ? sx[i - 1] : 0f) - sx[i] + (z > 0 ? sz[i - sizeX] : 0f) - sz[i];
                if (isInlet[i])
                    net += perEpoch / inlet.Count;
                change[i] = net;
            }
            supplied += perEpoch;
            for (int i = 0; i < n; i++)
            {
                if (isInlet[i] || change[i] == 0f)
                    continue;
                float c = change[i];
                if (c < 0f)
                    c *= erodibility?[i] ?? 1f;
                c = Mathf.Clamp(c, -maxChange, maxChange);
                // Deposits build at most to the water's surface; the bed is not scoured below the floor.
                if (c > 0f)
                    c = Mathf.Min(c, Mathf.Max(0f, surface[i] - height[i]));
                else
                    c = Mathf.Max(c, floor - height[i]);
                height[i] += c;
                if (c > 0f) deposited += c; else eroded -= c;
            }
        }

        float reachingSea = 0f;
        int wetCells = 0;
        for (int i = 0; i < n; i++)
        {
            int x = i % sizeX, z = i / sizeX;
            if (fixedLevel[i])
                reachingSea += (x > 0 ? fx[i - 1] : 0f) - fx[i] + (z > 0 ? fz[i - sizeX] : 0f) - fz[i];
            if (Depth(i) > p.minDepth)
                wetCells++;
        }
        int built = 0, deep = 0;
        for (int i = 0; i < n; i++)
        {
            if (sea[i] && height[i] > seaLevel + 0.05f)
                built++;
            if (Depth(i) > p.deepShare * depth && !fixedLevel[i])
                deep++;
        }
        report = $"{p.epochs} epochs in {watch.ElapsedMilliseconds} ms; mouth {width:F1} wide, {depth:F2} m deep; sediment {supplied:F0} m-cells: " +
                 $"deposited {deposited:F0}, eroded {eroded:F0}; {built} cells of land built over the sea, {deep} deep-water cells; " +
                 $"inlet surface {surface[start] - seaLevel:F2} m above the sea; water reaching the sea {reachingSea:F2} of {riverQ:F2}; {wetCells} wet cells";
        return built;
    }
}
