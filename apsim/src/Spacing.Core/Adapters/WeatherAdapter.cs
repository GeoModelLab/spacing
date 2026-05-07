using Spacing.Core.Domain;

namespace Spacing.Core.Adapters;

/// <summary>
/// Converte una WeatherSeries (da API Spacing DB) nel formato .met di APSIM Next Generation.
///
/// Gestisce le conversioni di unità leggendo WeatherSeries.Meta, quindi funziona
/// sia se il DB espone dati raw ERA5 sia se li ha già convertiti:
///   - radn: J/m²/day  → MJ/m²/day  (÷ 1_000_000)
///   - vp:   dewpoint °C → hPa       (formula Magnus)
///
/// Il file .met viene scritto in una cartella temporanea e il path viene restituito.
/// APSIM legge il file al momento dell'esecuzione.
/// </summary>
public class WeatherAdapter
{
    private readonly string _tempDir;

    public WeatherAdapter(string tempDir)
    {
        _tempDir = tempDir;
        Directory.CreateDirectory(_tempDir);
    }

    /// <summary>
    /// Genera il file .met APSIM dalla serie meteo e restituisce il percorso assoluto.
    /// Il nome del file include cell_id e intervallo di date per evitare collisioni.
    /// </summary>
    public string WriteMetFile(WeatherSeries series, int startYear, int endYear)
    {
        var records = series.Data
            .Where(r => r.Date.Year >= startYear && r.Date.Year <= endYear)
            .OrderBy(r => r.Date)
            .ToList();

        if (records.Count == 0)
            throw new InvalidOperationException(
                $"Nessun dato meteo disponibile per {series.CellId} nel range {startYear}-{endYear}.");

        // Calcola tav e amp dalla serie filtrata (richiesti da APSIM)
        var (tav, amp) = ComputeTavAmp(records);

        var fileName = $"{series.CellId}_{startYear}_{endYear}.met";
        var filePath = Path.Combine(_tempDir, fileName);

        using var writer = new StreamWriter(filePath, append: false);

        // Header APSIM .met
        writer.WriteLine("[weather.met.weather]");
        writer.WriteLine($"Latitude  = {series.Lat:F4}  ! decimal degrees");
        writer.WriteLine($"Longitude = {series.Lon:F4}  ! decimal degrees");
        writer.WriteLine($"tav = {tav:F2}  ! annual mean temperature (oC)");
        writer.WriteLine($"amp = {amp:F2}  ! annual amplitude of temperature (oC)");
        writer.WriteLine();
        writer.WriteLine("year  day   radn   maxt   mint   rain   wind    vp");
        writer.WriteLine("  ()   () (MJ/m2)  (oC)   (oC)   (mm)  (m/s)  (hPa)");

        foreach (var r in records)
        {
            var radn = ConvertRadn(r.Radn, series.Meta.RadnUnit);
            var vp   = ConvertVp(r.Vp,   series.Meta.VpUnit);
            var doy  = r.Date.DayOfYear;

            writer.WriteLine(
                $"{r.Date.Year,5} {doy,4} {radn,7:F2} {r.Tmax,6:F1} {r.Tmin,6:F1} {r.Rain,6:F1} {r.Wind,6:F1} {vp,6:F2}");
        }

        return filePath;
    }

    // --- Conversioni unità ---

    private static double ConvertRadn(double value, string unit) =>
        unit.ToLowerInvariant() switch
        {
            "j/m2/day"  => value / 1_000_000.0,
            "mj/m2/day" => value,
            _ => throw new NotSupportedException($"Unità radiazione non supportata: {unit}")
        };

    private static double ConvertVp(double value, string unit) =>
        unit.ToLowerInvariant() switch
        {
            "hpa"        => value,
            "dewpoint_c" => DewpointToVaporPressure(value),
            _ => throw new NotSupportedException($"Unità pressione vapore non supportata: {unit}")
        };

    /// <summary>
    /// Formula di Magnus: converte dewpoint (°C) in pressione di vapore (hPa).
    /// e = 6.1078 × exp(17.27 × Td / (Td + 237.3))
    /// </summary>
    private static double DewpointToVaporPressure(double dewpointC) =>
        6.1078 * Math.Exp(17.27 * dewpointC / (dewpointC + 237.3));

    /// <summary>
    /// Calcola tav (temperatura media annua) e amp (escursione termica annua media)
    /// come richiesto dal formato .met di APSIM.
    /// amp = media delle escursioni mensili medie = (Tmax_mese - Tmin_mese) / 2, mediata sui mesi.
    /// </summary>
    private static (double tav, double amp) ComputeTavAmp(List<WeatherRecord> records)
    {
        var tav = records.Average(r => (r.Tmax + r.Tmin) / 2.0);

        var monthlyAmp = records
            .GroupBy(r => new { r.Date.Year, r.Date.Month })
            .Select(g => (g.Max(r => r.Tmax) - g.Min(r => r.Tmin)) / 2.0)
            .Average();

        return (tav, monthlyAmp);
    }
}
