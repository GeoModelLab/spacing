namespace Spacing.Core.Calibration;

/// <summary>
/// Descrive un singolo parametro da calibrare con l'ottimizzatore.
/// Il Name usa la sintassi Cultivar Command di APSIM:
///   es. "Phenology.TTEndJuvToFI.Target.FixedValue"
///   che diventa "[Sorghum].Phenology.TTEndJuvToFI.Target.FixedValue = value"
/// </summary>
public class CalibrationParameter
{
    /// <summary>Path del parametro APSIM (senza il prefisso [Crop]).</summary>
    public string Name { get; init; }

    /// <summary>Limite inferiore del dominio di ricerca.</summary>
    public double LowerBound { get; init; }

    /// <summary>Limite superiore del dominio di ricerca.</summary>
    public double UpperBound { get; init; }

    /// <summary>Valore iniziale (punto di partenza dell'ottimizzatore).</summary>
    public double InitialValue { get; init; }

    public override string ToString() =>
        $"{Name} in [{LowerBound:G4}, {UpperBound:G4}] (init={InitialValue:G4})";
}
