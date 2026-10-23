using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace LivingLandscapes.Geology;

/// <summary>Tuning of <see cref="DeltaGrowth"/>, set with the channel settings in XML. Defaults follow pyDeltaRCM where it has one.</summary>
public class DeltaGrowthSettings
{
    public bool enabled = false;

    /// <summary>Every river mouth at least this wide (cells) grows a delta, largest first, at most maxMouths.</summary>
    public float minMouthWidth = 2f;
    public int maxMouths = 6;

    /// <summary>
    /// Sediment the river delivers over the delta's life (metres x cells): this times its width squared times its depth, so a
    /// creek builds a small delta and a large river a large one.
    /// </summary>
    public float volumeWidths = 100f;

    /// <summary>Time steps, and water and sediment parcels released per step.</summary>
    public int steps = 200;
    public int waterParcels = 400;
    public int sedimentParcels = 400;

    /// <summary>
    /// The water surface: along each parcel's path it is the sea's where the jet has spread into open water, and rises upstream
    /// from there by surfaceSlope metres per cell (DeltaRCM's S0). It is smoothed over wet cells (surfaceSmoothing passes, each
    /// moving a cell smoothShare of the way to its wet neighbours' mean) and takes surfaceUpdate of each step's new surface.
    /// </summary>
    public float surfaceSlope = 0.008f;
    public int surfaceSmoothing = 10;
    public float smoothShare = 0.9f;
    public float surfaceUpdate = 0.1f;

    /// <summary>
    /// A parcel's way: weight = depth^theta x alignment with the routing direction / distance, the direction being surfaceSteering
    /// of the water surface's fall plus the rest of the flow field's direction (DeltaRCM's gamma). Sand keeps to deep water,
    /// water and mud spread more.
    /// </summary>
    public float surfaceSteering = 0.1f;

    /// <summary>
    /// The alignment term is raised to this power: 1 is DeltaRCM's walk, which spreads a jet about 45 degrees a step; higher keeps
    /// the jet's momentum, so it runs on several widths before it slows and drops its bar.
    /// </summary>
    public float alignmentExponent = 1f;
    public float waterTheta = 1f;
    public float sandTheta = 2f;
    public float mudTheta = 1f;

    /// <summary>Share of each step's flow field that is new (DeltaRCM's omega_flow); a 3x3 average first evens out the few parcels.</summary>
    public float flowUpdate = 0.5f;
    public bool flowBlur = true;

    /// <summary>
    /// Tidal range (metres): the platform the delta builds within half of it below the sea is exposed at low tide, so it becomes
    /// land (flats, mud and marsh) standing just above the sea; channels through it stay open.
    /// </summary>
    public float tidalRange = 0.6f;

    /// <summary>Water shallower than this (metres) is dry ground.</summary>
    public float dryDepth = 0.02f;

    /// <summary>Share of the load that is sand (DeltaRCM's f_bedload); the rest is mud.</summary>
    public float sandShare = 0.3f;

    /// <summary>
    /// Velocities as shares of the river's at the mouth. Sand moves at a capacity growing with velocity^beta (the mouth's flux at
    /// the mouth's velocity): it settles where more passes than the flow there carries, and lifts bed above sandErode. Mud settles
    /// below mudDeposit and lifts bed above mudErode. One parcel changes the bed by at most bedChangeCap of the water's depth.
    /// </summary>
    public float beta = 3f;
    public float sandErode = 1.05f;
    public float mudDeposit = 0.3f;
    public float mudErode = 1.5f;
    public float bedChangeCap = 0.25f;

    /// <summary>Smoothing of the wet bed after each step (DeltaRCM's topographic diffusion).</summary>
    public float bedDiffusion = 0.1f;

    /// <summary>Parcels end in water this many channel depths deep; the bed is not scoured deeper than maxScour depths below the sea.</summary>
    public float deepStop = 4f;
    public float maxScour = 2f;

    /// <summary>
    /// Open water: deeper than this many channel depths, or slower than openWaterVelocity of the mouth's flow; the surface is the
    /// sea's there. Over the shallow delta platform it rises toward the mouth, so deposits build a topset above the sea.
    /// </summary>
    public float openWaterDepth = 0.5f;
    public float openWaterVelocity = 0f;

    /// <summary>
    /// Channels: cells carrying at least this share of the river's flow per unit width. A channel cell splits where a second way
    /// at least two directions apart takes this share of its water. Routing reads the last recordShare of the steps.
    /// </summary>
    public float channelShare = 0.1f;
    public float splitShare = 0.2f;
    public float recordShare = 0.25f;
}

/// <summary>
/// A delta grown from the river mouth rather than drawn: a reduced-complexity delta model (DeltaRCM, Liang et al. 2015; pyDeltaRCM).
/// Water parcels walk from the mouth, each step to a neighbour chosen by depth and the flow's direction; their paths are the flow
/// field and set the water surface. Sand parcels walk the same way and drop their load where more passes than the flow carries;
/// mud settles in slack water; fast flow lifts bed. The bed changes each step, so the flow responds: the jet drops a mouth bar and
/// divides round it, levees build beside channels, a channel that builds itself up is abandoned for a lower way, lobes build out
/// and switch. Obstacles at the mouth split the flow by the same rule.
/// </summary>
public static class DeltaGrowth
{
    private static readonly int[] DX = { 1, 1, 0, -1, -1, -1, 0, 1 };
    private static readonly int[] DZ = { 0, 1, 1, 1, 0, -1, -1, -1 };
    private static readonly float[] Dist = { 1f, 1.4142f, 1f, 1.4142f, 1f, 1.4142f, 1f, 1.4142f };

    /// <summary>
    /// Grows the delta into <paramref name="height"/> from the sea cell at the river's mouth. Returns the sea cells it built above
    /// the sea. <paramref name="transitions"/> counts water parcel steps per cell and direction (8 per cell) over the last steps;
    /// <paramref name="flow"/> is each cell's flow per unit width as a share of the river's at the mouth; <paramref name="level"/>
    /// the water surface.
    /// </summary>
    public static int Grow(float[] height, bool[] sea, int start, Vector2 heading, float width, float depth, float seaLevel, int sizeX, int sizeZ,
        DeltaGrowthSettings p, out int[] transitions, out float[] flow, out float[] level, out string report)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        int n = sizeX * sizeZ;
        var original = (float[])height.Clone();
        depth = Mathf.Max(depth, 0.2f);
        width = Mathf.Max(width, 1f);
        transitions = new int[n * 8];
        flow = new float[n];
        var surface = level = new float[n];
        for (int i = 0; i < n; i++)
            surface[i] = seaLevel;
        var random = new System.Random(Rand.Int);

        // The inlet: a line of the channel's width across the mouth, kept open at the channel's depth.
        var across = new Vector2(-heading.y, heading.x);
        var inlet = new List<int>();
        int half = Mathf.Max(0, Mathf.RoundToInt(width * 0.5f));
        for (int k = -half; k <= half; k++)
        {
            int x = Mathf.FloorToInt(start % sizeX + 0.5f + across.x * k), z = Mathf.FloorToInt(start / sizeX + 0.5f + across.y * k);
            int i = z * sizeX + x;
            if (x >= 0 && z >= 0 && x < sizeX && z < sizeZ && !inlet.Contains(i))
                inlet.Add(i);
        }
        var isInlet = new bool[n];
        foreach (int i in inlet)
            isInlet[i] = true;
        void OpenInlet()
        {
            foreach (int i in inlet)
                height[i] = Mathf.Min(height[i], surface[i] - depth);
        }
        OpenInlet();

        float Depth(int i) => surface[i] - height[i];
        float floor = seaLevel - p.maxScour * depth;
        float volume = p.volumeWidths * width * width * depth;
        float perParcel = volume / Mathf.Max(1, p.steps * p.sedimentParcels);
        // Sand passing the mouth per cell of its width per step: the capacity at the mouth's velocity.
        float sandAtMouth = perParcel * p.sedimentParcels * p.sandShare / inlet.Count;
        int maxSteps = 2 * (sizeX + sizeZ);
        var qx = new float[n];
        var qz = new float[n];
        var sx = new float[n];
        var sz = new float[n];
        var dirX = new float[n];
        var dirZ = new float[n];
        // Before any parcel has walked, water spreads from the mouth: a faint field pointing away from it (out ahead of the
        // mouth, not back along the shore), which the parcels' own flow replaces within a few steps.
        var mouthAt = new Vector2(start % sizeX + 0.5f, start / sizeX + 0.5f) - heading;
        float faint = 0.05f * p.waterParcels / inlet.Count;
        for (int i = 0; i < n; i++)
        {
            Vector2 away = (new Vector2(i % sizeX + 0.5f, i / sizeX + 0.5f) - mouthAt).normalized;
            if (Vector2.Dot(away, heading) < 0f)
                away = (away + heading).normalized;
            qx[i] = away.x * faint;
            qz[i] = away.y * faint;
        }
        var sand = new float[n];
        var levelSum = new float[n];
        var levelCount = new int[n];
        var path = new List<int>();
        // Loop check (as DeltaRCM's): a parcel that comes back to a cell it passed this walk is circling in an eddy and ends.
        var visited = new int[n];
        int walk = 0;
        var weights = new float[8];
        int recordFrom = Mathf.FloorToInt(p.steps * (1f - p.recordShare));
        float deposited = 0f, eroded = 0f, lost = 0f;
        int stranded = 0, leaving = 0;
        // The active area (cells parcels reached), for the per-step passes.
        int minX = sizeX, minZ = sizeZ, maxX = 0, maxZ = 0;
        foreach (int i in inlet)
            Grow2(i);
        void Grow2(int i)
        {
            int x = i % sizeX, z = i / sizeX;
            if (x - 2 < minX) minX = Mathf.Max(0, x - 2);
            if (z - 2 < minZ) minZ = Mathf.Max(0, z - 2);
            if (x + 2 > maxX) maxX = Mathf.Min(sizeX - 1, x + 2);
            if (z + 2 > maxZ) maxZ = Mathf.Min(sizeZ - 1, z + 2);
        }

        // Velocity at a cell as a share of the mouth's: flow per unit width over depth, each against the mouth's.
        float Velocity(int i) => new Vector2(qx[i], qz[i]).magnitude * inlet.Count / p.waterParcels / Mathf.Max(Depth(i) / depth, 0.05f);

        // The routing direction at each cell: the surface's fall (gamma) and the flow field's direction.
        void Directions()
        {
            for (int z = minZ; z <= maxZ; z++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    int i = z * sizeX + x;
                    float gx = 0f, gz = 0f;
                    for (int d = 0; d < 8; d++)
                    {
                        int nx = x + DX[d], nz = z + DZ[d];
                        if (nx < 0 || nz < 0 || nx >= sizeX || nz >= sizeZ)
                            continue;
                        int nb = nz * sizeX + nx;
                        if (Depth(nb) <= p.dryDepth)
                            continue;
                        float fall = (surface[i] - surface[nb]) / Dist[d];
                        gx += fall * DX[d] / Dist[d];
                        gz += fall * DZ[d] / Dist[d];
                    }
                    var g = new Vector2(gx, gz).normalized;
                    var f = new Vector2(qx[i], qz[i]).normalized;
                    var dir = (g * p.surfaceSteering + f * (1f - p.surfaceSteering)).normalized;
                    dirX[i] = dir.x;
                    dirZ[i] = dir.y;
                }
            }
        }

        // The neighbour a parcel at c steps to (direction index), 8 = off the map, -1 if none is wet ahead.
        int Choose(int c, float theta)
        {
            int cx = c % sizeX, cz = c / sizeX;
            var along = new Vector2(dirX[c], dirZ[c]);
            bool aimless = along == Vector2.zero;
            if (aimless && isInlet[c])
            {
                along = heading;
                aimless = false;
            }
            float total = 0f;
            for (int d = 0; d < 8; d++)
            {
                weights[d] = 0f;
                float cos = aimless ? 1f : (DX[d] * along.x + DZ[d] * along.y) / Dist[d];
                if (cos <= 0f)
                    continue;
                int x = cx + DX[d], z = cz + DZ[d];
                // Off the map the water runs on as over open water.
                float h = x < 0 || z < 0 || x >= sizeX || z >= sizeZ ? p.deepStop * depth : Depth(z * sizeX + x);
                if (h <= p.dryDepth)
                    continue;
                total += weights[d] = Mathf.Pow(h, theta) * Mathf.Pow(cos, p.alignmentExponent) / Dist[d];
            }
            if (total <= 0f)
                return -1;
            float pick = (float)random.NextDouble() * total;
            for (int d = 0; d < 8; d++)
            {
                pick -= weights[d];
                if (pick <= 0f && weights[d] > 0f)
                    return d;
            }
            return -1;
        }
        bool Inside(int c, int d, out int next)
        {
            int x = c % sizeX + DX[d], z = c / sizeX + DZ[d];
            next = z * sizeX + x;
            return x >= 0 && z >= 0 && x < sizeX && z < sizeZ;
        }

        for (int step = 0; step < p.steps; step++)
        {
            Directions();

            // Water: the flow field and the water surface for this step.
            System.Array.Clear(sx, 0, n);
            System.Array.Clear(sz, 0, n);
            System.Array.Clear(levelSum, 0, n);
            System.Array.Clear(levelCount, 0, n);
            bool record = step >= recordFrom;
            for (int k = 0; k < p.waterParcels; k++)
            {
                int c = inlet[random.Next(inlet.Count)];
                path.Clear();
                walk++;
                for (int s = 0; s < maxSteps; s++)
                {
                    if (visited[c] == walk)
                        break;
                    visited[c] = walk;
                    path.Add(c);
                    int d = Choose(c, p.waterTheta);
                    if (d < 0)
                        break;
                    sx[c] += DX[d] / Dist[d];
                    sz[c] += DZ[d] / Dist[d];
                    if (record)
                        transitions[c * 8 + d]++;
                    if (!Inside(c, d, out c) || Depth(c) > p.deepStop * depth)
                        break;
                    Grow2(c);
                }
                // The surface along the path, from its end upstream: the sea's while the water is open (deep or slow), then rising
                // by surfaceSlope per cell.
                float rise = seaLevel;
                bool open = true;
                for (int j = path.Count - 1; j >= 0; j--)
                {
                    int at = path[j];
                    if (open && (Depth(at) > p.openWaterDepth * depth || Velocity(at) < p.openWaterVelocity) && !isInlet[at])
                        rise = seaLevel;
                    else
                    {
                        open = false;
                        if (j < path.Count - 1)
                        {
                            int next = path[j + 1];
                            rise += p.surfaceSlope * (at % sizeX != next % sizeX && at / sizeX != next / sizeX ? 1.4142f : 1f);
                        }
                    }
                    levelSum[at] += rise;
                    levelCount[at]++;
                }
            }
            if (p.flowBlur)
            {
                Blur(sx, sizeX, minX, minZ, maxX, maxZ);
                Blur(sz, sizeX, minX, minZ, maxX, maxZ);
            }
            for (int z = minZ; z <= maxZ; z++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    int i = z * sizeX + x;
                    qx[i] = p.flowUpdate * sx[i] + (1f - p.flowUpdate) * qx[i];
                    qz[i] = p.flowUpdate * sz[i] + (1f - p.flowUpdate) * qz[i];
                }
            }
            UpdateSurface(surface, levelSum, levelCount, height, seaLevel, p, sizeX, minX, minZ, maxX, maxZ);

            // Sediment: sand by capacity, mud by velocity thresholds; each parcel changes the bed by at most a share of the depth.
            System.Array.Clear(sand, 0, n);
            for (int k = 0; k < p.sedimentParcels; k++)
            {
                bool isSand = random.NextDouble() < p.sandShare;
                float load = perParcel;
                int c = inlet[random.Next(inlet.Count)];
                bool left = false;
                walk++;
                for (int s = 0; s < maxSteps && load > 1e-7f; s++)
                {
                    if (visited[c] == walk)
                        break;
                    visited[c] = walk;
                    int d = Choose(c, isSand ? p.sandTheta : p.mudTheta);
                    if (d < 0)
                    {
                        stranded++;
                        break;
                    }
                    if (!Inside(c, d, out c) || Depth(c) > p.deepStop * depth)
                    {
                        left = true;
                        break;
                    }
                    if (isInlet[c])
                        continue;
                    float h = Mathf.Max(0f, Depth(c));
                    float u = Velocity(c);
                    float most = p.bedChangeCap * h;
                    if (isSand)
                    {
                        sand[c] += load;
                        float capacity = sandAtMouth * Mathf.Pow(u, p.beta);
                        if (sand[c] > capacity)
                        {
                            float put = Mathf.Min(Mathf.Min(load, sand[c] - capacity), most);
                            height[c] += put;
                            load -= put;
                            sand[c] -= put;
                            deposited += put;
                        }
                        else if (u > p.sandErode)
                        {
                            float take = Mathf.Min(Mathf.Min(perParcel * (Mathf.Pow(u / p.sandErode, p.beta) - 1f), most), height[c] - floor);
                            if (take > 0f)
                            {
                                height[c] -= take;
                                load += take;
                                eroded += take;
                            }
                        }
                    }
                    else if (u < p.mudDeposit)
                    {
                        float put = Mathf.Min(load * (1f - Mathf.Pow(u / p.mudDeposit, p.beta)), most);
                        height[c] += put;
                        load -= put;
                        deposited += put;
                    }
                    else if (u > p.mudErode)
                    {
                        float take = Mathf.Min(Mathf.Min(perParcel * (Mathf.Pow(u / p.mudErode, p.beta) - 1f), most), height[c] - floor);
                        if (take > 0f)
                        {
                            height[c] -= take;
                            load += take;
                            eroded += take;
                        }
                    }
                }
                if (left)
                    leaving++;
                if (left || load > 0f)
                    lost += load;
            }

            if (p.bedDiffusion > 0f)
                Smooth(height, surface, p.bedDiffusion, p.dryDepth, sizeX, minX, minZ, maxX, maxZ);
            OpenInlet();
        }

        int built = 0, flats = 0;
        for (int i = 0; i < n; i++)
        {
            flow[i] = new Vector2(qx[i], qz[i]).magnitude * inlet.Count / p.waterParcels;
            // Intertidal flats: built up within half the tidal range below the sea, out of the channels.
            if (height[i] > seaLevel - p.tidalRange * 0.5f && height[i] <= seaLevel + 0.05f && flow[i] < p.channelShare && height[i] != original[i])
            {
                height[i] = seaLevel + 0.06f;
                flats++;
            }
            if (sea[i] && height[i] > seaLevel + 0.05f)
                built++;
        }
        report = $"{p.steps} steps x {p.waterParcels}+{p.sedimentParcels} parcels in {watch.ElapsedMilliseconds} ms over {maxX - minX + 1}x{maxZ - minZ + 1} cells; " +
                 $"mouth {width:F1} wide, {depth:F2} m deep; sediment {volume:F0} m-cells, deposited {deposited:F0}, eroded {eroded:F0}, carried off {lost:F0} ({leaving} parcels left, {stranded} stranded); " +
                 $"{built} cells of land built over the sea ({flats} intertidal flats)";
        return built;
    }

    /// <summary>
    /// The new water surface: where parcels passed, the mean of their path levels (else the last); smoothed over wet cells; the
    /// surface takes surfaceUpdate of it. Dry ground beside water lower than that water's surface floods to it. Never below the sea.
    /// </summary>
    private static void UpdateSurface(float[] surface, float[] sum, int[] count, float[] height, float seaLevel, DeltaGrowthSettings p,
        int sizeX, int minX, int minZ, int maxX, int maxZ)
    {
        int sizeZ = surface.Length / sizeX;
        var next = (float[])surface.Clone();
        for (int z = minZ; z <= maxZ; z++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                int i = z * sizeX + x;
                if (count[i] > 0)
                    next[i] = sum[i] / count[i];
            }
        }
        var copy = new float[surface.Length];
        for (int pass = 0; pass < p.surfaceSmoothing; pass++)
        {
            System.Array.Copy(next, copy, next.Length);
            for (int z = minZ; z <= maxZ; z++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    int i = z * sizeX + x;
                    if (copy[i] - height[i] <= p.dryDepth)
                        continue;
                    float total = 0f;
                    int wet = 0;
                    for (int d = 0; d < 8; d++)
                    {
                        int nx = x + DX[d], nz = z + DZ[d];
                        if (nx < 0 || nz < 0 || nx >= sizeX || nz >= sizeZ)
                            continue;
                        int nb = nz * sizeX + nx;
                        if (copy[nb] - height[nb] <= p.dryDepth)
                            continue;
                        total += copy[nb];
                        wet++;
                    }
                    if (wet > 0)
                        next[i] = (1f - p.smoothShare) * copy[i] + p.smoothShare * total / wet;
                }
            }
        }
        for (int z = minZ; z <= maxZ; z++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                int i = z * sizeX + x;
                surface[i] = Mathf.Max(seaLevel, p.surfaceUpdate * next[i] + (1f - p.surfaceUpdate) * surface[i]);
            }
        }
        // Flooding: dry ground lower than the water beside it fills to it.
        for (int z = minZ; z <= maxZ; z++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                int i = z * sizeX + x;
                if (surface[i] - height[i] > p.dryDepth)
                    continue;
                float highest = float.MinValue;
                for (int d = 0; d < 8; d++)
                {
                    int nx = x + DX[d], nz = z + DZ[d];
                    if (nx < 0 || nz < 0 || nx >= sizeX || nz >= sizeZ)
                        continue;
                    int nb = nz * sizeX + nx;
                    if (surface[nb] - height[nb] > p.dryDepth)
                        highest = Mathf.Max(highest, surface[nb]);
                }
                if (highest > height[i] + p.dryDepth)
                    surface[i] = highest;
            }
        }
    }

    private static void Blur(float[] v, int sizeX, int minX, int minZ, int maxX, int maxZ)
    {
        int sizeZ = v.Length / sizeX;
        var copy = (float[])v.Clone();
        for (int z = minZ; z <= maxZ; z++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                float total = 0f;
                int count = 0;
                for (int dz = -1; dz <= 1; dz++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, nz = z + dz;
                        if (nx < 0 || nz < 0 || nx >= sizeX || nz >= sizeZ)
                            continue;
                        total += copy[nz * sizeX + nx];
                        count++;
                    }
                }
                v[z * sizeX + x] = total / count;
            }
        }
    }

    /// <summary>Wet bed (and ground just above the water) moves a share of the way to its wet neighbours' mean.</summary>
    private static void Smooth(float[] height, float[] surface, float share, float dry, int sizeX, int minX, int minZ, int maxX, int maxZ)
    {
        int sizeZ = height.Length / sizeX;
        var copy = (float[])height.Clone();
        for (int z = minZ; z <= maxZ; z++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                int i = z * sizeX + x;
                if (copy[i] > surface[i] + dry)
                    continue;
                float total = 0f;
                int count = 0;
                for (int d = 0; d < 8; d += 2)
                {
                    int nx = x + DX[d], nz = z + DZ[d];
                    if (nx < 0 || nz < 0 || nx >= sizeX || nz >= sizeZ)
                        continue;
                    int nb = nz * sizeX + nx;
                    if (copy[nb] > surface[nb] + dry)
                        continue;
                    total += copy[nb];
                    count++;
                }
                if (count > 0)
                    height[i] += share * (total / count - copy[i]);
            }
        }
    }
}
