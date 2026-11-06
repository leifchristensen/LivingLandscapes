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
}
