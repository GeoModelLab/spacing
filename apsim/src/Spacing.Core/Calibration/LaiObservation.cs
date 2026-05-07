namespace Spacing.Core.Calibration;

/// <summary>
/// Osservazione puntuale di LAI (Leaf Area Index) da telerilevamento o campo.
/// A differenza di ReferenceObservation (aggregata per anno), questa classe
/// rappresenta un singolo punto della serie temporale (anno × DOY × LAI).
///
/// Fonte tipica: MODIS MOD15A2H (8-day LAI/FPAR) o Sentinel-2 via GEE.
/// Risoluzione temporale: ogni 8 giorni (DOY = 1, 9, 17, ...).
/// </summary>
public class LaiObservation
{
    /// <summary>Nome della coltura APSIM (es. "Maize").</summary>
    public string CropName { get; init; }

    /// <summary>Anno dell'osservazione.</summary>
    public int Year { get; init; }

    /// <summary>Giorno dell'anno (1-365).</summary>
    public int DOY { get; init; }

    /// <summary>LAI osservato (m²/m²).</summary>
    public double LAI { get; init; }

    public override string ToString() =>
        $"{CropName} {Year} DOY={DOY}: LAI={LAI:F2}";
}
