namespace Spacing.Core.Domain;

/// <summary>
/// Cella della griglia 0.1° × 0.1° sul territorio italiano.
/// Unità geografica base per il recupero di meteo e suoli.
/// </summary>
public class GridCell
{
    /// <summary>Identificatore univoco della cella, es. "IT_450_75" (lat*10_lon*10).</summary>
    public string Id { get; set; }

    /// <summary>Latitudine del centroide (gradi decimali, WGS84).</summary>
    public double Lat { get; set; }

    /// <summary>Longitudine del centroide (gradi decimali, WGS84).</summary>
    public double Lon { get; set; }

    /// <summary>Codice regione NUTS2 (es. "ITC1" = Piemonte).</summary>
    public string Nuts2 { get; set; }

    /// <summary>Codice provincia NUTS3 (es. "ITC11" = Torino).</summary>
    public string Nuts3 { get; set; }

    public override string ToString() => $"{Id} ({Lat:F2}°N, {Lon:F2}°E) [{Nuts3}]";
}
