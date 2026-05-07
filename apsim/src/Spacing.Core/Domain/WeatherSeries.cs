namespace Spacing.Core.Domain;

/// <summary>
/// Metadati sulle unità della serie meteo restituita dall'API.
/// Permette al WeatherAdapter di applicare le conversioni corrette
/// indipendentemente da come il DB decide di esporre i dati.
/// </summary>
public class WeatherSeriesMeta
{
    /// <summary>"J/m2/day" o "MJ/m2/day"</summary>
    public string RadnUnit { get; set; } = "MJ/m2/day";

    /// <summary>"dewpoint_C" o "hPa"</summary>
    public string VpUnit { get; set; } = "hPa";

    /// <summary>"C" (Celsius) — temperatura sempre in °C</summary>
    public string TempUnit { get; set; } = "C";

    /// <summary>"mm/day"</summary>
    public string RainUnit { get; set; } = "mm/day";
}

/// <summary>
/// Serie temporale meteo completa per una cella, restituita dall'API Spacing DB.
/// </summary>
public class WeatherSeries
{
    public string CellId { get; set; }
    public double Lat { get; set; }
    public double Lon { get; set; }
    public WeatherSeriesMeta Meta { get; set; } = new();
    public List<WeatherRecord> Data { get; set; } = new();
}
