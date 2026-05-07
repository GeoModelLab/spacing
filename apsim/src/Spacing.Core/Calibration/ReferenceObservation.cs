namespace Spacing.Core.Calibration;

/// <summary>
/// Dato di riferimento osservato per la calibrazione.
/// Corrisponde a un'osservazione di campo (ELISA, esperimento, MODIS).
/// </summary>
public class ReferenceObservation
{
    /// <summary>Anno dell'osservazione.</summary>
    public int Year { get; init; }

    /// <summary>Nome della coltura (deve corrispondere a RotationCrop.CropName).</summary>
    public string CropName { get; init; }

    /// <summary>Resa granellare osservata, kg/ha. NaN se non disponibile.</summary>
    public double GrainYieldKgHa { get; init; } = double.NaN;

    /// <summary>Biomassa epigea osservata, kg/ha. NaN se non disponibile.</summary>
    public double BiomassKgHa { get; init; } = double.NaN;

    /// <summary>LAI massimo osservato (da MODIS/Sentinel). NaN se non disponibile.</summary>
    public double MaxLAI { get; init; } = double.NaN;

    public override string ToString() =>
        $"{CropName} {Year}: yield={GrainYieldKgHa:F0} kg/ha, LAI={MaxLAI:F2}";
}
