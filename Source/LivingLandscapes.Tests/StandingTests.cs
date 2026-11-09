using LivingLandscapes.Geology;
using Xunit;

namespace LivingLandscapes.Tests;

/// <summary>
/// One standing-water test (#156): for a table of (filled, natural, depth) every caller (Confluences' exclusion, the budget's
/// base level, Paint's hand-over, the feedback passes) reads the same answer, and a hand-over implies base level.
/// </summary>
public class StandingTests
{
    private const float MinDepth = 0.15f, Tolerance = 0.05f, SpringDepth = 0.1f;

    // filled, natural, depth, expected standing
    public static TheoryData<float, float, float, bool> Table => new()
    {
        { 10.00f, 10.00f, 0.50f, false },  // no hollow
        { 10.30f, 10.00f, 0.50f, false },  // shallower than the channel
        { 10.54f, 10.00f, 0.50f, false },  // depth < filled - natural <= depth + tolerance: the old gap, now flowing for all callers
        { 10.56f, 10.00f, 0.50f, true },   // just past depth + tolerance
        { 10.18f, 10.00f, 0.02f, false },  // minDepth floors a thin channel: 0.18 < 0.15 + 0.05
        { 10.21f, 10.00f, 0.02f, true },
        { 12.00f, 10.00f, 1.00f, true },   // deep hollow
    };

    [Theory]
    [MemberData(nameof(Table))]
    public void EveryCallerAgrees(float filled, float natural, float depth, bool expected)
    {
        var standing = new Standing(new[] { filled }, null, MinDepth, Tolerance, SpringDepth);
        bool budget = standing.Is(0, natural, depth);
        bool junctions = standing.Is(0, natural, depth);
        bool paint = standing.HandedToLake(0, natural, depth, holdsLake: true);
        Assert.Equal(expected, budget);
        Assert.Equal(budget, junctions);
        Assert.Equal(expected, paint);
        // A cell handed to the lake model is always standing (invariant 8).
        Assert.True(!paint || budget);
        // A hollow the lake model refuses is not handed over, whatever its depth.
        Assert.False(standing.HandedToLake(0, natural, depth, holdsLake: false));
    }

    [Fact]
    public void SeaIsAlwaysStandingButNeverHandedToTheLakeModel()
    {
        var standing = new Standing(new[] { 5f }, new[] { true }, MinDepth, Tolerance, SpringDepth);
        Assert.True(standing.Is(0, 5f, 1f));
        Assert.False(standing.HandedToLake(0, 5f, 1f, true));
    }

    [Fact]
    public void SpringHollowUsesItsOwnShallowerThreshold()
    {
        var standing = new Standing(new[] { 10.12f }, null, MinDepth, Tolerance, SpringDepth);
        Assert.True(standing.InSpringHollow(0, 10f));
        Assert.False(standing.Is(0, 10f, 0.5f));
    }

    [Fact]
    public void ScourDepthIsNotAnInput()
    {
        // The decision reads the depth it is given; callers pass the pre-scour depth (amendment 1). A deeper (scoured) depth would flip it.
        var standing = new Standing(new[] { 10.40f }, null, MinDepth, Tolerance, SpringDepth);
        Assert.True(standing.Is(0, 10f, 0.2f));
        Assert.False(standing.Is(0, 10f, 0.2f * 2f));
    }

    // D5 (#186): room (accommodation) = the rise at which the cell stops standing, so a capped deposit never rises above the water.

    [Fact]
    public void Room_is_zero_when_not_standing()
    {
        // 10.30 - 10.00 = 0.30 <= max(0.50, 0.15) + 0.05: flowing, and there is nothing to fill.
        var standing = new Standing(new[] { 10.30f }, null, MinDepth, Tolerance, SpringDepth);
        Assert.False(standing.Is(0, 10.00f, 0.50f));
        Assert.Equal(0f, standing.Room(0, 10.00f, 0.50f));
    }

    [Fact]
    public void Room_equals_the_rise_at_which_the_cell_stops_standing()
    {
        // L = 12.00 over a bed at 10.00 with a 1.00 m channel: standing needs 1.00 + 0.05, so the room is 2.00 - 1.05 = 0.95 m.
        var standing = new Standing(new[] { 12.00f }, null, MinDepth, Tolerance, SpringDepth);
        float room = standing.Room(0, 10.00f, 1.00f);
        Assert.Equal(0.95f, room, 4);
        // Just below the cap the cell is still standing water; just above it, it is not: the cap is the standing threshold itself.
        Assert.True(standing.Is(0, 10.00f + room * 0.99f, 1.00f));
        Assert.False(standing.Is(0, 10.00f + room * 1.01f, 1.00f));
    }

    [Fact]
    public void Room_is_zero_for_the_sea()
    {
        // The sea stands at its level whatever the bed: filling it in is the shore pass' work, not the deposits' (D5 v1).
        var standing = new Standing(new[] { 5f }, new[] { true }, MinDepth, Tolerance, SpringDepth);
        Assert.True(standing.Is(0, 0f, 1f));
        Assert.Equal(0f, standing.Room(0, 0f, 1f));
    }

    [Fact]
    public void Room_never_negative()
    {
        // No hollow at all, and a cell just under the standing threshold (the old gap): the max(0, ...) floors both at zero.
        var standing = new Standing(new[] { 10.00f, 10.54f }, null, MinDepth, Tolerance, SpringDepth);
        Assert.Equal(0f, standing.Room(0, 10.00f, 0.50f));
        Assert.Equal(0f, standing.Room(1, 10.00f, 0.50f));
    }

    // What a player would see (D5, #186): a lake a river keeps feeding shallows pass after pass. Below is the measured behaviour on
    // the two pure pieces the feedback pass uses (Standing.Room and the ledger's room cap), with the water surface held fixed as it
    // is in a closed depression - the spill over the sill does not depend on how high its bed has built up.

    private const float LakeLevel = 12.00f, LakeDepth = 1.00f, Share = 0.5f, Offered = 1.00f;

    /// <summary>The highest bed that is still standing water: the water surface less the channel depth and the standing tolerance.</summary>
    private const float StandingLimit = LakeLevel - (LakeDepth + Tolerance);

    /// <summary>The rounding guard the production cap adds to the room (TerrainChannels.Feedback.cs CrossingEpsilon), so the last
    /// rise carries the bed strictly across <see cref="StandingLimit"/> instead of freezing a float step below it.</summary>
    private const float CrossingEpsilon = 0.001f;

    /// <summary>
    /// One feedback pass over the lake cell, the way Aggrade does it (TerrainChannels.Feedback.cs:72-87): while the cell stands its
    /// rise is capped at its room plus the crossing epsilon, the bed goes up and the water surface does not. Returns the bed after
    /// the pass.
    /// </summary>
    private static float FillPass(DepositLedger ledger, Standing standing, float bed, out float rise)
    {
        // RoomBelowWater: the room plus CrossingEpsilon on standing land water, no cap (infinity) anywhere else.
        float[] roomCap = standing.Is(0, bed, LakeDepth) ? new[] { standing.Room(0, bed, LakeDepth) + CrossingEpsilon } : null;
        rise = ledger.Raise(new[] { Offered }, Share, roomCap, out _)[0];
        return bed + rise;
    }

    [Fact]
    public void Capped_deposits_never_lift_a_standing_bed_above_its_water()
    {
        var standing = new Standing(new[] { LakeLevel }, null, MinDepth, Tolerance, SpringDepth);
        var ledger = new DepositLedger();
        float bed = 10.00f, total = 0f;
        for (int pass = 0; pass < 8; pass++)
        {
            bool stoodBefore = standing.Is(0, bed, LakeDepth);
            float next = FillPass(ledger, standing, bed, out float rise);
            Assert.True(rise >= 0f, $"pass {pass}: a deposit lowered the bed");
            Assert.True(next >= bed, $"pass {pass}: the bed sank from {bed} to {next}");
            if (stoodBefore)
                Assert.True(next <= StandingLimit + CrossingEpsilon + 1e-4f,
                    $"pass {pass}: a standing bed rose to {next}, above the standing line ({StandingLimit}) plus the crossing epsilon");
            bed = next;
            total += rise;
        }
        // The ledger books the rise that happened, not the alluvium that was offered, so soil depth cannot count it twice (SD11).
        Assert.Equal(total, ledger.AggradedVolume, 4);
        Assert.True(ledger.AggradedVolume < 8 * Offered * Share, "the room refused nothing: the cap never bound");
    }

    [Fact]
    public void Capped_deposits_fill_a_fed_lake_across_its_standing_line()
    {
        // What the cap does with the crossing epsilon (D5 #186, architect 2026-11-09): the bed climbs pass after pass and the last
        // rise carries it strictly across the standing-water line, so the cell stops being standing water and the normal land rule
        // (the uncapped share, Deposits_on_ground_are_not_capped...) takes it on to marsh and ground. Without the epsilon the bed
        // froze a float step below the line, still "standing" with zero room forever (measured in round 12).
        var standing = new Standing(new[] { LakeLevel }, null, MinDepth, Tolerance, SpringDepth);
        var ledger = new DepositLedger();
        float bed = 10.00f;
        int pass = 0;
        while (standing.Is(0, bed, LakeDepth) && pass < 8)
        {
            bed = FillPass(ledger, standing, bed, out _);
            pass++;
        }

        Assert.False(standing.Is(0, bed, LakeDepth), $"the lake is still standing at bed {bed}; the fill never crossed the line");
        Assert.True(bed > StandingLimit, $"the bed ended at {bed}, not above the standing line {StandingLimit}");
        // The rise delivered while the cell stood is exactly the room the lake had at the start plus the crossing epsilon:
        // 12.00 - 10.00 - (1.00 + 0.05) + 0.001 = 0.951 m, no more (the cap is a ceiling, not a pump).
        Assert.Equal(LakeLevel - 10.00f - (LakeDepth + Tolerance) + CrossingEpsilon, ledger.AggradedVolume, 6);
    }

    [Fact]
    public void Zero_room_and_no_deposit_leaves_the_bed_alone()
    {
        // The crossing epsilon is a ceiling adjustment, not a rise: a standing cell whose room is already zero and that receives
        // no alluvium does not move, and nothing is booked.
        var standing = new Standing(new[] { LakeLevel }, null, MinDepth, Tolerance, SpringDepth);
        var ledger = new DepositLedger();
        float bed = StandingLimit; // exactly on the line: room 0 (+ epsilon still standing by the strict test)
        Assert.True(standing.Is(0, bed, LakeDepth));
        float[] roomCap = { standing.Room(0, bed, LakeDepth) + CrossingEpsilon };
        float rise = ledger.Raise(new[] { 0f }, Share, roomCap, out float volume)[0];
        Assert.Equal(0f, rise);
        Assert.Equal(0f, volume);
        Assert.True(standing.Is(0, bed + rise, LakeDepth));
    }

    [Fact]
    public void Deposits_on_ground_are_not_capped_and_carry_the_bed_above_the_water()
    {
        // The other half of the rule: a cell that is not standing water keeps the plain land rule (no cap, no second threshold),
        // so the same deposits carry a filled hollow up through the water surface and out of it.
        var standing = new Standing(new[] { LakeLevel }, null, MinDepth, Tolerance, SpringDepth);
        var ledger = new DepositLedger();
        float bed = 11.00f; // 1.00 m below the surface, less than depth + tolerance: already not standing water
        Assert.False(standing.Is(0, bed, LakeDepth));
        for (int pass = 0; pass < 3; pass++)
            bed = FillPass(ledger, standing, bed, out _);
        Assert.Equal(12.50f, bed, 4); // three uncapped passes of 0.5 m: the bed stands above the water surface it used to hold
        Assert.Equal(1.50f, ledger.AggradedVolume, 4);
    }
}
