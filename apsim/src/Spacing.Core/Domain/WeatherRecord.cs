namespace Spacing.Core.Domain;

/// <summary>
/// Dati meteo giornalieri per una cella, nelle unità native dell'API Spacing DB.
/// Il WeatherAdapter si occupa della conversione nelle unità APSIM.
/// </summary>
public class WeatherRecord
{
    public DateOnly Date { get; set; }

    // --- Variabili in unità native (possibilmente ERA5 raw o già convertite) ---
    // Il campo meta nella risposta API indica le unità effettive.

    /// <summary>Temperatura massima giornaliera. Unità: vedi WeatherSeriesMeta.</summary>
    public double Tmax { get; set; }

    /// <summary>Temperatura minima giornaliera. Unità: vedi WeatherSeriesMeta.</summary>
    public double Tmin { get; set; }

    /// <summary>Precipitazione giornaliera totale. Unità: vedi WeatherSeriesMeta.</summary>
    public double Rain { get; set; }

    /// <summary>Radiazione solare. Unità: vedi WeatherSeriesMeta (J/m²/day o MJ/m²/day).</summary>
    public double Radn { get; set; }

    /// <summary>Velocità del vento a 10m. Unità: m/s.</summary>
    public double Wind { get; set; }

    /// <summary>
    /// Pressione di vapore o dewpoint, a seconda di WeatherSeriesMeta.VpUnit.
    /// Se VpUnit == "dewpoint_C" → dewpoint in °C, WeatherAdapter converte in hPa.
    /// Se VpUnit == "hPa"       → già pressione di vapore in hPa.
    /// </summary>
    public double Vp { get; set; }
}
