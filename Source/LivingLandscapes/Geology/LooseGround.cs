using System;

namespace LivingLandscapes.Geology
{
    /// <summary>
    /// Loose ground (issue #186, G4): river deposits are unconsolidated sediment and erode far easier than the bed
    /// under them - loose sand starts moving at a critical shear stress of about 1 Pa, soil holds about 10 Pa and rock
    /// 100-1000 Pa (TerrainChannels.Impact.cs:27-31: sandResistance 1, soilResistance 10, rockResistance 1000). The
    /// exported erodibility therefore blends the rock K toward the loose K by the share of <c>looseDepth</c> the
    /// deposits have filled: lerp(rockK, kLoose, min(1, depositMetres / looseDepth)). Only the exported field uses
    /// this; the erosion model itself keeps cutting with the rock K. Game-free.
    /// </summary>
    public static class LooseGround
    {
        /// <summary>
        /// Erodibility K of ground covered by <paramref name="depositMetres"/> of river deposits: <paramref name="rockK"/>
        /// where there are none, <paramref name="kLoose"/> once they are at least <paramref name="looseDepth"/> thick,
        /// linear in between. Requires looseDepth &gt; 0.
        /// </summary>
        public static float Erodibility(float rockK, float depositMetres, float kLoose, float looseDepth)
        {
            float share = Math.Min(1f, depositMetres / looseDepth);
            return rockK + (kLoose - rockK) * share;
        }
    }
}
