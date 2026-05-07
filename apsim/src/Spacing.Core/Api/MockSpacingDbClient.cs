using Spacing.Core.Domain;

namespace Spacing.Core.Api;

/// <summary>
/// Implementazione mock dell'ISpacingDbClient per sviluppo e test locali.
/// Genera dati sintetici plausibili per una cella della Pianura Padana
/// (Lombardia, ~45.4°N 9.2°E) senza necessità di connessione al DB reale.
///
/// Dati meteo: media climatologica Pianura Padana + variabilità stagionale sintetica.
/// Dati suolo: Cambisol tipico della pianura irrigua lombarda.
/// </summary>
public class MockSpacingDbClient : ISpacingDbClient
{
    private static readonly GridCell MockCell = new()
    {
        Id    = "IT_454_092",
        Lat   = 45.45,
        Lon   = 9.25,
        Nuts2 = "ITC4",
        Nuts3 = "ITC45"   // Milano
    };

    public Task<IEnumerable<GridCell>> GetCellsAsync(
        string nuts2 = null,
        CancellationToken cancellationToken = default)
    {
        IEnumerable<GridCell> cells = new[] { MockCell };
        return Task.FromResult(cells);
    }

    public Task<WeatherSeries> GetWeatherAsync(
        string cellId,
        DateOnly start,
        DateOnly end,
        CancellationToken cancellationToken = default)
    {
        var records = new List<WeatherRecord>();
        var rng = new Random(42);  // seed fisso per riproducibilità

        for (var date = start; date <= end; date = date.AddDays(1))
        {
            // Stagionalità sintetica basata su DOY (day of year)
            // seasonAngle = 0 al solstizio estivo (DOY 172) → cos(0) = +1 = massimo
            var doy = date.DayOfYear;
            var seasonAngle = 2.0 * Math.PI * (doy - 172) / 365.0;

            // Temperatura: range annuo tipico Pianura Padana
            // Estate (DOY 172): tmean ≈ 24°C | Inverno (DOY 355): tmean ≈ 1°C
            var tmean = 12.5 + 11.5 * Math.Cos(seasonAngle);
            var tmax  = tmean + 6.0 + rng.NextDouble() * 2.0;
            var tmin  = tmean - 6.0 - rng.NextDouble() * 2.0;

            // Radiazione: 3 MJ/m²/day (inverno) → 22 MJ/m²/day (estate)
            var radn = 12.5 + 9.5 * Math.Cos(seasonAngle) + rng.NextDouble() * 2.0;
            radn = Math.Max(0.5, radn);

            // Pioggia: distribuzione di Poisson + stagionalità lieve
            var rainProb = 0.30 + 0.08 * Math.Sin(2.0 * Math.PI * (doy - 90) / 365.0);
            var rain = rng.NextDouble() < rainProb
                ? 2.0 + rng.NextDouble() * 18.0
                : 0.0;

            // Vento: media 2.5 m/s
            var wind = 1.0 + rng.NextDouble() * 3.0;

            // Pressione di vapore (hPa): segue la temperatura
            var vp = 6.1078 * Math.Exp(17.27 * tmin / (tmin + 237.3));

            records.Add(new WeatherRecord
            {
                Date = date,
                Tmax = Math.Round(tmax, 1),
                Tmin = Math.Round(tmin, 1),
                Rain = Math.Round(rain, 1),
                Radn = Math.Round(radn, 2),
                Wind = Math.Round(wind, 1),
                Vp   = Math.Round(vp, 2)
            });
        }

        var series = new WeatherSeries
        {
            CellId = cellId,
            Lat    = MockCell.Lat,
            Lon    = MockCell.Lon,
            Meta   = new WeatherSeriesMeta
            {
                RadnUnit = "MJ/m2/day",  // già convertita
                VpUnit   = "hPa",        // già in hPa
                TempUnit = "C",
                RainUnit = "mm/day"
            },
            Data = records
        };

        return Task.FromResult(series);
    }

    public Task<IEnumerable<SoilProfile>> GetSoilProfilesAsync(
        string cellId,
        CancellationToken cancellationToken = default)
    {
        // Cambisol tipico Pianura Padana lombarda (irriguo)
        var profile = new SoilProfile
        {
            ProfileId    = $"{cellId}_S1",
            CellId       = cellId,
            SoilType     = "Haplic Cambisol (Loamy)",
            DataSource   = "Mock (Pianura Padana tipica)",
            AreaFraction = 1.0,
            Layers = new List<SoilLayer>
            {
                Layer(  0,  15, sand:30, silt:50, clay:20, oc:2.1, bd:1.25, ph:6.8, dul:0.32, ll15:0.13, sat:0.45),
                Layer( 15,  30, sand:28, silt:48, clay:24, oc:1.5, bd:1.30, ph:6.9, dul:0.30, ll15:0.14, sat:0.44),
                Layer( 30,  60, sand:25, silt:45, clay:30, oc:0.9, bd:1.35, ph:7.0, dul:0.29, ll15:0.15, sat:0.43),
                Layer( 60,  90, sand:22, silt:43, clay:35, oc:0.5, bd:1.40, ph:7.1, dul:0.28, ll15:0.16, sat:0.42),
                Layer( 90, 120, sand:20, silt:40, clay:40, oc:0.3, bd:1.45, ph:7.2, dul:0.27, ll15:0.17, sat:0.41),
                Layer(120, 180, sand:20, silt:38, clay:42, oc:0.1, bd:1.48, ph:7.3, dul:0.26, ll15:0.18, sat:0.40),
            }
        };

        IEnumerable<SoilProfile> profiles = new[] { profile };
        return Task.FromResult(profiles);
    }

    private static SoilLayer Layer(
        double top, double bot,
        double sand, double silt, double clay,
        double oc, double bd, double ph,
        double dul, double ll15, double sat) => new()
    {
        DepthTop    = top,
        DepthBottom = bot,
        Sand = sand, Silt = silt, Clay = clay,
        OC = oc, BD = bd, PH = ph,
        DUL = dul, LL15 = ll15, SAT = sat,
        SW  = dul,      // inizializzato a field capacity
        NO3 = 5.0,
        NH4 = 0.5
    };
}
