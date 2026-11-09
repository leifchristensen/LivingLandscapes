namespace LivingLandscapes.Geology;

/// <summary>
/// What the erosion model computed beyond the landform, cut to the map's cells: handed from the generator to the layer workers
/// (<see cref="Layers.ErodibilityWorker"/>, <see cref="Layers.DepositionWorker"/>), which export them as layers.
/// </summary>
public class ErosionFields
{
    /// <summary>Erodibility K the model cut with (P2): the landform's factor times the exposed bed's. 1 is average rock.</summary>
    public float[] Erodibility;

    /// <summary>Metres of fluvial deposit the model left standing (Davy and Lague deposition, less what later incision removed).</summary>
    public float[] Deposition;

    /// <summary>
    /// Grain (0..1, 1 coarsest) of the fluvial deposit standing at each map cell (issue #186, G1); null unless
    /// erosionKeeps.depositGrain. The shore pass builds its beaches from this grain.
    /// </summary>
    public float[] DepositionGrain;

    /// <summary>
    /// Metres of sediment the model delivered to the sea at each model-grid cell, summed over the run (issue #186, G2);
    /// null unless erosionKeeps.seaSupply. Covers the whole region, not just the map window (see the grid fields).
    /// </summary>
    public float[] SeaSupply;

    /// <summary>Grain (0..1) of the sediment delivered to the sea at each model-grid cell; null unless erosionKeeps.seaSupply.</summary>
    public float[] SeaSupplyGrain;

    /// <summary>Size of the model grid <see cref="SeaSupply"/> covers, and how it maps to the map cells above
    /// (model cell (x, z) holds map cell (OriginX + x * MapCellsPerModelCell, OriginZ + z * MapCellsPerModelCell)).</summary>
    public int GridX, GridZ;
    public float OriginX, OriginZ, MapCellsPerModelCell;
}
