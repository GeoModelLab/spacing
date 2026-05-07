using Spacing.Core.Domain;

namespace Spacing.Core.Api;

/// <summary>
/// Contratto per l'accesso al database Spacing (WP2, Django/PostGIS).
/// Implementazioni: HttpSpacingDbClient (produzione), MockSpacingDbClient (sviluppo/test).
/// </summary>
public interface ISpacingDbClient
{
    /// <summary>
    /// Restituisce le celle della griglia 0.1° per il territorio italiano.
    /// </summary>
    /// <param name="nuts2">Filtro opzionale per regione NUTS2 (es. "ITC1").</param>
    /// <param name="cancellationToken"></param>
    Task<IEnumerable<GridCell>> GetCellsAsync(
        string nuts2 = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Restituisce la serie meteo giornaliera per una cella.
    /// Le unità sono indicate in WeatherSeries.Meta.
    /// </summary>
    Task<WeatherSeries> GetWeatherAsync(
        string cellId,
        DateOnly start,
        DateOnly end,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Restituisce i profili suolo associati a una cella.
    /// Una cella può avere N profili (variabilità spaziale sub-0.1°).
    /// </summary>
    Task<IEnumerable<SoilProfile>> GetSoilProfilesAsync(
        string cellId,
        CancellationToken cancellationToken = default);
}
