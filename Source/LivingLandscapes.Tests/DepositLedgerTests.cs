using System;
using System.Collections.Generic;
using LivingLandscapes.Geology;
using UnityEngine;
using Xunit;

namespace LivingLandscapes.Tests;

/// <summary>
/// Step S4 of the hydrology unification (#186): the deposit ledger. Guards: the sediment budget conserves mass (supply =
/// deposited + exits), the same deposit is the same thickness in every call (one kappa), the kernel loses nothing, and the
/// aggradation pass raises the ground by exactly its share of the layer.
/// </summary>
public class DepositLedgerTests
{
    private const int SizeX = 24, SizeZ = 11, Row = 5;

    /// <summary>A channel along one row, falling in a concave profile (so its capacity eases downstream) with a graded river entering at the top.</summary>
    private sealed class Channel
    {
        public readonly List<int> Cells = new List<int>();
        public readonly int[] Receiver = new int[SizeX * SizeZ];
        public readonly float[] Discharge = new float[SizeX * SizeZ];
        public readonly float[] Filled = new float[SizeX * SizeZ];
        public readonly float[] Inflow = new float[SizeX * SizeZ];
        public readonly float[] Width = new float[SizeX * SizeZ];
        public readonly bool[] BaseLevel = new bool[SizeX * SizeZ];

        public Channel(bool endsInBaseLevel)
        {
            for (int i = 0; i < Receiver.Length; i++)
            {
                Receiver[i] = -1;
                Width[i] = 2f;
            }
            for (int z = 0; z < SizeZ; z++)
            {
                for (int x = 0; x < SizeX; x++)
                    Filled[z * SizeX + x] = 6f * (float)Math.Exp(-x / 5.0);
            }
            for (int x = 0; x < SizeX; x++)
            {
                int c = Row * SizeX + x;
                Cells.Add(c);
                Discharge[c] = 10f;
                Receiver[c] = x < SizeX - 1 ? c + 1 : -1;
            }
            Inflow[Row * SizeX] = 10f;
            BaseLevel[Row * SizeX + SizeX - 1] = endsInBaseLevel;
        }

        public void Deposits(SedimentSettings p, out float[] deposit, out float[] grain, out float total, out float atBase, out float eroded, out float exits) =>
            Sediment.Deposits(Cells, Receiver, Discharge, 1f, Filled, Inflow, null, BaseLevel, SizeX, p, out deposit, out grain, out total, out atBase, out eroded,
                out exits);

        public LayTotals Spread(float[] deposit, float[] grain, SedimentSettings p, DepositLedger ledger, out float[] alluvium, out float[] grainSize) =>
            Sediment.Spread(Cells, Receiver, null, deposit, grain, Width, Filled, Filled, SizeX, SizeZ, p, ledger, out alluvium, out grainSize);
    }

    private static float Sum(float[] values)
    {
        float s = 0f;
        foreach (float v in values)
            s += v;
        return s;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SupplyIsDepositsPlusExits(bool endsInBaseLevel)
    {
        var channel = new Channel(endsInBaseLevel);
        channel.Deposits(new SedimentSettings(), out float[] deposit, out _, out float total, out float atBase, out float eroded, out float exits);
        Assert.True(eroded > 0f);
        Assert.True(total > 0f, "the profile eases downstream, so the graded load settles somewhere");
        Assert.Equal(Sum(deposit), total, 3);
        Assert.Equal(eroded, total + exits, 2);
        if (endsInBaseLevel)
        {
            Assert.Equal(0f, exits, 3);
            Assert.True(atBase > 0f);
        }
        else
            Assert.True(exits > 0f, "a channel with no receiver and no base level lets its load leave the map");
    }

    [Fact]
    public void SameDepositIsTheSameThicknessInEveryCall()
    {
        var channel = new Channel(true);
        var p = new SedimentSettings();
        channel.Deposits(p, out float[] deposit, out float[] grain, out _, out _, out _, out _);
        var ledger = new DepositLedger();
        channel.Spread(deposit, grain, p, ledger, out float[] first, out _);
        Assert.True(ledger.IsFixed);
        float kappa = ledger.Kappa;
        // A second call with the same deposit gives the same thickness, cell for cell.
        channel.Spread(deposit, grain, p, ledger, out float[] again, out _);
        Assert.Equal(first, again);
        // A call with a quarter of the deposit is a quarter of the thickness: nothing is renormalised per call.
        var small = new float[deposit.Length];
        for (int i = 0; i < small.Length; i++)
            small[i] = deposit[i] * 0.25f;
        channel.Spread(small, grain, p, ledger, out float[] quarter, out _);
        Assert.Equal(kappa, ledger.Kappa);
        for (int i = 0; i < first.Length; i++)
        {
            if (first[i] > 0f && first[i] < p.maxThickness && quarter[i] >= p.minThickness)
                Assert.Equal(first[i] * 0.25f, quarter[i], 4);
        }
    }

    [Fact]
    public void FirstPassFixesKappaSoTheNinetiethPercentileIsTheTypicalThickness()
    {
        var channel = new Channel(true);
        var p = new SedimentSettings { typicalThickness = 1f, maxThickness = 100f, minThickness = 0f };
        channel.Deposits(p, out float[] deposit, out float[] grain, out _, out _, out _, out _);
        var ledger = new DepositLedger();
        channel.Spread(deposit, grain, p, ledger, out float[] alluvium, out _);
        var covered = new List<float>();
        foreach (float a in alluvium)
        {
            if (a > 0f)
                covered.Add(a);
        }
        covered.Sort();
        Assert.Equal(p.typicalThickness, covered[(int)(covered.Count * 0.9f)], 3);
    }

    [Fact]
    public void VolumeIsLaidClippedOrUnplacedNeverLost()
    {
        var channel = new Channel(true);
        var p = new SedimentSettings { maxThickness = 0.8f, minThickness = 0.05f };
        channel.Deposits(p, out float[] deposit, out float[] grain, out _, out _, out _, out _);
        LayTotals totals = channel.Spread(deposit, grain, p, new DepositLedger(), out float[] alluvium, out _);
        Assert.Equal(Sum(alluvium), totals.Laid, 3);
        Assert.True(totals.Clipped > 0f, "the thick deposits at the base level are cut at maxThickness");
        Assert.Equal(totals.Offered, totals.Laid + totals.Clipped + totals.Unplaced, 2);
    }

    [Fact]
    public void ADepositWithNoGroundToSettleOnIsBookedAsUnplaced()
    {
        var channel = new Channel(true);
        var p = new SedimentSettings();
        channel.Deposits(p, out float[] deposit, out float[] grain, out _, out _, out _, out _);
        // Every cell of the map lies far above its channel's water: the floodplain kernel has no ground at all.
        var high = new float[channel.Filled.Length];
        for (int i = 0; i < high.Length; i++)
            high[i] = 100f;
        LayTotals totals = Sediment.Spread(channel.Cells, channel.Receiver, null, deposit, grain, channel.Width, channel.Filled, high, SizeX, SizeZ, p,
            new DepositLedger(), out float[] alluvium, out _);
        Assert.Equal(0f, Sum(alluvium));
        Assert.True(totals.Unplaced > 0f);
        Assert.Equal(totals.Offered, totals.Unplaced, 3);
    }

    [Fact]
    public void AggradationIsTheShareOfTheLayerSum()
    {
        var channel = new Channel(true);
        var p = new SedimentSettings();
        channel.Deposits(p, out float[] deposit, out float[] grain, out _, out _, out _, out _);
        var ledger = new DepositLedger();
        channel.Spread(deposit, grain, p, ledger, out float[] alluvium, out _);
        float[] rise = ledger.Raise(alluvium, 0.6f, out float volume);
        Assert.Equal(0.6f * Sum(alluvium), volume, 3);
        Assert.Equal(volume, Sum(rise), 3);
        // A second pass accumulates in the ledger's own copy; the layer itself is not touched.
        ledger.Raise(alluvium, 0.6f, out float again);
        Assert.Equal(volume + again, Sum(ledger.Aggraded), 3);
        Assert.Equal(volume + again, ledger.AggradedVolume, 3);
    }

    [Fact]
    public void Raise_with_room_caps_and_books_the_capped_rise()
    {
        // D5 (issue #186): a standing cell cannot rise past its room, and the ledger must book what actually rose, not what was offered.
        var ledger = new DepositLedger();
        float[] alluvium = { 1f, 2f, 4f, 0f };
        float[] room = { 0.2f, 10f, 1.5f, 0f };
        float[] rise = ledger.Raise(alluvium, 0.5f, room, out float volume);
        Assert.Equal(new float[] { 0.2f, 1f, 1.5f, 0f }, rise);
        Assert.Equal(2.7f, volume, 4);
        Assert.Equal(volume, Sum(ledger.Aggraded), 4);
        Assert.Equal(volume, ledger.AggradedVolume, 4);
    }

    [Fact]
    public void Raise_without_room_is_unchanged()
    {
        float[] alluvium = { 1f, 2f, 4f, 0f };
        var capped = new DepositLedger();
        var plain = new DepositLedger();
        float[] rise = capped.Raise(alluvium, 0.5f, null, out float volume);
        float[] asBefore = plain.Raise(alluvium, 0.5f, out float volumeBefore);
        Assert.Equal(asBefore, rise);
        Assert.Equal(volumeBefore, volume, 4);
        Assert.Equal(0.5f * Sum(alluvium), volume, 4);
        Assert.Equal(Sum(asBefore), Sum(plain.Aggraded), 4);
    }

    [Fact]
    public void Raise_with_zero_room_books_nothing()
    {
        var ledger = new DepositLedger();
        float[] alluvium = { 1f, 2f, 4f };
        float[] rise = ledger.Raise(alluvium, 0.5f, new float[3], out float volume);
        Assert.Equal(0f, volume);
        Assert.Equal(0f, Sum(rise));
        Assert.Equal(0f, ledger.AggradedVolume);
        Assert.Equal(0f, Sum(ledger.Aggraded));
    }

    [Fact]
    public void NoDepositsLeavesKappaAtOneWithoutNaN()
    {
        var channel = new Channel(true);
        var p = new SedimentSettings();
        var none = new float[channel.Filled.Length];
        var ledger = new DepositLedger();
        LayTotals totals = channel.Spread(none, none, p, ledger, out float[] alluvium, out float[] grainSize);
        Assert.False(ledger.IsFixed);
        Assert.Equal(1f, ledger.Kappa);
        Assert.Equal(0f, Sum(alluvium));
        Assert.Equal(0f, Sum(grainSize));
        Assert.Equal(0f, totals.Offered);
    }

    [Fact]
    public void KappaIsClampedToItsRange()
    {
        var p = new SedimentSettings { kappaMin = 0.5f, kappaMax = 2f };
        var tiny = new float[] { 1e-6f, 2e-6f, 3e-6f };
        var ledger = new DepositLedger();
        ledger.FixFrom(tiny, p);
        Assert.Equal(2f, ledger.Kappa);
        Assert.True(ledger.KappaClamped);
        // Held: a later, much larger deposit does not move it.
        ledger.FixFrom(new float[] { 100f, 200f }, p);
        Assert.Equal(2f, ledger.Kappa);
    }
}
