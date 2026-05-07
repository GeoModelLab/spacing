using Spacing.Core.Adapters;
using Spacing.Core.Domain;
using Spacing.Core.Simulation;
using System.Data;
using System.Text;
using UNIMI.optimizer;

namespace Spacing.Core.Calibration;

/// <summary>
/// Esegue la calibrazione dei parametri APSIM per una SimulationUnit
/// usando il Multi-Start Nelder-Mead Simplex.
///
/// Output prodotti (cartella OutputDir):
///
///   calibrated/{CropName}_{CellId}.json
///     → parametri calibrati per questa cella, un file per cella.
///        Tutti i file in calibrated/ hanno lo stesso schema → facile costruire
///        distribuzioni provinciali (leggi tutti i file, raggruppa per CropName).
///
///   timeseries_{CellId}.csv
///     → serie giornaliera LAI simulato + LAI osservato (affiancati) per tutta la durata
///        della simulazione finale. Righe senza osservazione: LAI_obs = vuoto.
///
///   harvest_{CellId}.csv
///     → una riga per ogni raccolto: Yield_sim_kgha vs Yield_obs_kgha con errori.
///
/// File temporanei (creati durante l'ottimizzazione):
///   _cal_{n}.apsimx / _cal_{n}.db → cancellati automaticamente dopo ogni run.
/// </summary>
public class SpacingCalibrator
{
    private readonly SoilAdapter      _soilAdapter;
    private readonly RotationBuilder  _rotBuilder;
    private readonly SimulationRunner _runner;

    public int    NofSimplexes  { get; set; } = 30;
    public int    MaxIterations { get; set; } = 200;
    public double Ftol          { get; set; } = 1e-5;
    public double YieldWeight   { get; set; } = 0.5;
    public double LaiWeight     { get; set; } = 0.5;

    /// <summary>
    /// Cartella base degli output. Vengono creati:
    ///   {OutputDir}/calibrated/   → un JSON per cella
    ///   {OutputDir}/              → timeseries_*.csv, harvest_*.csv
    /// </summary>
    public string OutputDir { get; set; } = null;

    public SpacingCalibrator(
        SoilAdapter      soilAdapter,
        RotationBuilder  rotBuilder,
        SimulationRunner runner)
    {
        _soilAdapter = soilAdapter;
        _rotBuilder  = rotBuilder;
        _runner      = runner;
    }

    public (Dictionary<string, Dictionary<string, double>> BestParams,
            SimulationResult FinalResult,
            string CalibratedJsonPath,
            string TimeSeriesCsvPath,
            string HarvestCsvPath)
        Calibrate(
            SimulationUnit                                  unit,
            string                                          weatherMetPath,
            Dictionary<string, List<CalibrationParameter>> cropParams,
            List<ReferenceObservation>                      yieldObservations,
            List<LaiObservation>                            laiObservations = null)
    {
        var objFunc = new ApsimObjectiveFunction(
            unitTemplate:      unit,
            weatherMetPath:    weatherMetPath,
            soilAdapter:       _soilAdapter,
            rotBuilder:        _rotBuilder,
            runner:            _runner,
            yieldObservations: yieldObservations)
        {
            CropParameters  = cropParams,
            LaiObservations = laiObservations ?? new List<LaiObservation>(),
            YieldWeight     = YieldWeight,
            LaiWeight       = LaiWeight
        };

        int nParam = objFunc.TotalParams;
        var limits = objFunc.BuildLimits();

        var optimizer = new MultiStartSimplex
        {
            NofSimplexes = NofSimplexes,
            Itmax        = MaxIterations,
            Ftol         = Ftol
        };

        Console.Error.WriteLine();
        Console.Error.WriteLine($"[Calibrator] Cella: {unit.Cell.Id}  |  " +
                                $"Simplex: {NofSimplexes}  |  Parametri: {nParam}");
        Console.Error.WriteLine($"[Calibrator] Obs resa: {yieldObservations.Count}  |  " +
                                $"Obs LAI: {(laiObservations?.Count ?? 0)}  |  " +
                                $"Pesi: yield={YieldWeight:F2} LAI={LaiWeight:F2}");
        Console.Error.WriteLine($"[Calibrator] File temp: cancellati dopo ogni run");
        Console.Error.WriteLine();

        var allParams = cropParams.Values.SelectMany(l => l).ToList();
        for (int i = 0; i < allParams.Count; i++)
            Console.Error.WriteLine($"  [{i}] {allParams[i]}");
        Console.Error.WriteLine();

        optimizer.Multistart(objFunc, nParam, limits, out var results);

        Console.Error.WriteLine();
        Console.Error.WriteLine($"[Calibrator] Completato — {objFunc.ncompute} simulazioni eseguite");

        var bestCoef = new double[nParam];
        for (int i = 0; i < nParam; i++)
            bestCoef[i] = results[0, i];

        double bestLoss = results[0, nParam];
        Console.Error.WriteLine($"[Calibrator] Best loss = {bestLoss:F5}");
        for (int i = 0; i < nParam; i++)
            Console.Error.WriteLine($"  {allParams[i].Name} = {bestCoef[i]:G6}");

        var bestOverrides = objFunc.BuildOverrides(bestCoef);

        // ---- Salva parametri calibrati in calibrated/{crop}_{cell}.json ----
        string calibratedJsonPath = null;
        if (OutputDir != null)
            calibratedJsonPath = SaveCalibratedJson(
                unit, bestOverrides, bestLoss,
                yieldObservations.Count, laiObservations?.Count ?? 0,
                OutputDir);

        // ---- Simulazione finale (scrive i CSV di output standard) ----
        Console.Error.WriteLine();
        Console.Error.WriteLine("[Calibrator] Simulazione finale con parametri calibrati...");
        var finalApsimx = _rotBuilder.BuildApsimxFile(unit, weatherMetPath, bestOverrides);
        var finalResult = _runner.Run(unit, finalApsimx, saveOutputFiles: true);

        // ---- CSV time series + harvest (osservato vs simulato) ----
        string timeSeriesCsvPath = null;
        string harvestCsvPath    = null;
        if (OutputDir != null && finalResult.Success)
        {
            timeSeriesCsvPath = WriteTimeSeriesCsv(
                unit, finalResult, laiObservations, OutputDir);
            harvestCsvPath = WriteHarvestCsv(
                unit, finalResult, yieldObservations, OutputDir);
        }

        return (bestOverrides, finalResult, calibratedJsonPath, timeSeriesCsvPath, harvestCsvPath);
    }

    // =========================================================================
    // Salvataggio parametri calibrati
    // =========================================================================

    /// <summary>
    /// Salva i parametri calibrati in:
    ///   {OutputDir}/calibrated/{CropName}_{CellId}.json
    ///
    /// Tutti i file in calibrated/ hanno lo stesso schema JSON → si possono
    /// leggere con glob + deserializzazione per costruire distribuzioni provinciali.
    ///
    /// Schema:
    /// {
    ///   "CropName":   "Maize",
    ///   "CellId":     "CELL_454_092",
    ///   "Lat":        45.4,
    ///   "Lon":         9.2,
    ///   "CalibratedAt": "2026-04-30T...",
    ///   "BestLoss":   0.1234,
    ///   "NObs":       { "Yield": 2, "LAI": 30 },
    ///   "Parameters": { "Phenology.Juvenile.Target.FixedValue": 245.3, ... }
    /// }
    /// </summary>
    private static string SaveCalibratedJson(
        SimulationUnit                                 unit,
        Dictionary<string, Dictionary<string, double>> bestOverrides,
        double                                         bestLoss,
        int                                            nObsYield,
        int                                            nObsLai,
        string                                         outputDir)
    {
        try
        {
            var calibDir = Path.Combine(outputDir, "calibrated");
            Directory.CreateDirectory(calibDir);

            foreach (var (cropName, parameters) in bestOverrides)
            {
                // Nome file univoco per cella: Maize_CELL_454_092.json
                // Sovrascrive se ricalibrato (comportamento desiderato)
                var fileName = $"{cropName}_{unit.Cell.Id}.json";
                var filePath = Path.Combine(calibDir, fileName);

                var payload = new
                {
                    CropName     = cropName,
                    CellId       = unit.Cell.Id,
                    Lat          = unit.Cell.Lat,
                    Lon          = unit.Cell.Lon,
                    CalibratedAt = DateTime.UtcNow.ToString("o"),
                    BestLoss     = bestLoss,
                    NObs         = new { Yield = nObsYield, LAI = nObsLai },
                    Parameters   = parameters
                };

                File.WriteAllText(
                    filePath,
                    Newtonsoft.Json.JsonConvert.SerializeObject(payload, Newtonsoft.Json.Formatting.Indented),
                    Encoding.UTF8);

                Console.Error.WriteLine($"[Calibrator] Parametri → {filePath}");
            }

            // Restituisce il percorso del primo file (di solito c'è un solo crop)
            var firstCrop = bestOverrides.Keys.First();
            return Path.Combine(calibDir, $"{firstCrop}_{unit.Cell.Id}.json");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Calibrator] AVVISO: impossibile salvare JSON calibrati ({ex.Message})");
            return null;
        }
    }

    // =========================================================================
    // Time series LAI: simulato + osservato affiancati
    // =========================================================================

    /// <summary>
    /// Produce {OutputDir}/timeseries_{CellId}.csv con una riga per ogni giorno
    /// della simulazione finale, per ogni coltura nella rotazione.
    ///
    /// Colonne:
    ///   Date, Year, DOY, CropName, IsAlive, Stage,
    ///   LAI_sim,          ← sempre presente (0 quando la coltura non è in campo)
    ///   LAI_obs,          ← valore osservato solo nelle date di osservazione, altrimenti vuoto
    ///   Biomass_kgha, GrainWt_kgha
    ///
    /// Questo formato è direttamente plottabile (es. in Python/R/Excel):
    ///   df.plot(x="Date", y=["LAI_sim","LAI_obs"], style=["b-","ro"])
    /// </summary>
    private static string WriteTimeSeriesCsv(
        SimulationUnit       unit,
        SimulationResult     result,
        List<LaiObservation> laiObs,
        string               outputDir)
    {
        try
        {
            var daily = result.RawDailyReport;
            if (daily == null || daily.Rows.Count == 0)
            {
                Console.Error.WriteLine("[Calibrator] Nessun DailyReport per timeseries.");
                return null;
            }

            // Indice osservazioni LAI: (CropName, Year, DOY) → valore
            var obsIndex = (laiObs ?? new List<LaiObservation>())
                .GroupBy(o => (o.CropName, o.Year, o.DOY))
                .ToDictionary(g => g.Key, g => g.First().LAI);

            var inv = System.Globalization.CultureInfo.InvariantCulture;
            var sb  = new StringBuilder();

            // Intestazione: colonne fisse + una riga per crop
            sb.AppendLine("Date,Year,DOY,CropName,IsAlive,Stage,LAI_sim,LAI_obs,Biomass_kgha,GrainWt_kgha");

            foreach (var crop in unit.Rotation.Crops)
            {
                var name = crop.CropName;

                // Colonne nel DailyReport
                bool hasLai      = daily.Columns.Contains($"{name}_LAI");
                bool hasAlive    = daily.Columns.Contains($"{name}_IsAlive");
                bool hasStage    = daily.Columns.Contains($"{name}_Stage");
                bool hasBio      = daily.Columns.Contains($"{name}_Biomass_kgha");
                bool hasGrain    = daily.Columns.Contains($"{name}_GrainWt_kgha");

                foreach (DataRow row in daily.Rows)
                {
                    int year = SafeInt(row, "Year");
                    int doy  = SafeInt(row, "DOY");

                    string date    = row["Clock.Today"]?.ToString() ?? "";
                    string isAlive = hasAlive ? (row[$"{name}_IsAlive"]?.ToString() ?? "") : "";
                    string stage   = hasStage  ? SafeDouble(row, $"{name}_Stage").ToString("F2", inv) : "";
                    string laiSim  = hasLai    ? SafeDouble(row, $"{name}_LAI").ToString("F4", inv) : "";
                    string bio     = hasBio    ? SafeDouble(row, $"{name}_Biomass_kgha").ToString("F1", inv) : "";
                    string grain   = hasGrain  ? SafeDouble(row, $"{name}_GrainWt_kgha").ToString("F1", inv) : "";

                    // LAI osservato: presente solo nelle date di misura
                    string laiObsStr = obsIndex.TryGetValue((name, year, doy), out var obsVal)
                        ? obsVal.ToString("F4", inv)
                        : "";

                    sb.AppendLine($"{date},{year},{doy},{name},{isAlive},{stage},{laiSim},{laiObsStr},{bio},{grain}");
                }
            }

            var cellTag  = unit.Cell.Id.Replace(" ", "_");
            var filePath = Path.Combine(outputDir, $"timeseries_{cellTag}.csv");
            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
            Console.Error.WriteLine($"[Calibrator] Time series → {filePath}");

            // Mini-sommario: quante righe con osservazione LAI trovate
            int matched = (laiObs ?? new List<LaiObservation>())
                .Count(o => daily.Columns.Contains($"{o.CropName}_LAI"));
            Console.Error.WriteLine($"[Calibrator]   {matched} osservazioni LAI matchate nella serie giornaliera");

            return filePath;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Calibrator] AVVISO: impossibile scrivere timeseries ({ex.Message})");
            return null;
        }
    }

    // =========================================================================
    // Harvest: resa simulata vs osservata
    // =========================================================================

    /// <summary>
    /// Produce {OutputDir}/harvest_{CellId}.csv con una riga per ogni anno
    /// in cui è presente un'osservazione di resa.
    ///
    /// Colonne:
    ///   CropName, Year, Yield_sim_kgha, Yield_obs_kgha, Error_kgha, RelError_pct
    ///
    /// Se il simulatore non ha prodotto resa per quell'anno (semina fallita, ecc.)
    /// Yield_sim_kgha = 0 e RelError_pct = 9999 (flag evidente).
    /// </summary>
    private static string WriteHarvestCsv(
        SimulationUnit             unit,
        SimulationResult           result,
        List<ReferenceObservation> yieldObs,
        string                     outputDir)
    {
        try
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            var sb  = new StringBuilder();
            sb.AppendLine("CropName,Year,Yield_sim_kgha,Yield_obs_kgha,Error_kgha,RelError_pct");

            foreach (var obs in yieldObs.Where(o => !double.IsNaN(o.GrainYieldKgHa))
                                        .OrderBy(o => o.CropName).ThenBy(o => o.Year))
            {
                double simYield = 0;
                var yieldCol    = $"{obs.CropName}_GrainYield_kgha";

                if (result.RawReport != null && result.RawReport.Columns.Contains(yieldCol))
                {
                    var row = result.RawReport.AsEnumerable()
                        .FirstOrDefault(r => SafeInt(r, "Year") == obs.Year);
                    if (row != null)
                        simYield = SafeDouble(row, yieldCol);
                }

                double error    = simYield - obs.GrainYieldKgHa;
                double relError = obs.GrainYieldKgHa > 0
                    ? error / obs.GrainYieldKgHa * 100.0
                    : 9999.0;

                sb.AppendLine(
                    $"{obs.CropName},{obs.Year}," +
                    $"{simYield.ToString("F0", inv)}," +
                    $"{obs.GrainYieldKgHa.ToString("F0", inv)}," +
                    $"{error.ToString("F0", inv)}," +
                    $"{relError.ToString("F1", inv)}");
            }

            // Stampa a console
            Console.Error.WriteLine();
            Console.Error.WriteLine("[Calibrator] === Confronto resa osservata vs simulata ===");
            Console.Error.WriteLine($"  {"Crop",-10} {"Year",-5} {"Sim (t/ha)",10} {"Obs (t/ha)",10} {"Err%",8}");
            Console.Error.WriteLine("  " + new string('-', 48));
            foreach (var obs in yieldObs.Where(o => !double.IsNaN(o.GrainYieldKgHa)))
            {
                double sim = 0;
                var col    = $"{obs.CropName}_GrainYield_kgha";
                if (result.RawReport?.Columns.Contains(col) == true)
                {
                    var row = result.RawReport.AsEnumerable()
                        .FirstOrDefault(r => SafeInt(r, "Year") == obs.Year);
                    if (row != null) sim = SafeDouble(row, col);
                }
                double rel = obs.GrainYieldKgHa > 0
                    ? (sim - obs.GrainYieldKgHa) / obs.GrainYieldKgHa * 100.0 : double.NaN;
                Console.Error.WriteLine(
                    $"  {obs.CropName,-10} {obs.Year,-5} " +
                    $"{sim / 1000,10:F2} {obs.GrainYieldKgHa / 1000,10:F2} " +
                    $"{(double.IsNaN(rel) ? "n/a" : rel.ToString("F1") + "%"),8}");
            }
            Console.Error.WriteLine();

            var cellTag  = unit.Cell.Id.Replace(" ", "_");
            var filePath = Path.Combine(outputDir, $"harvest_{cellTag}.csv");
            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
            Console.Error.WriteLine($"[Calibrator] Harvest → {filePath}");

            return filePath;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Calibrator] AVVISO: impossibile scrivere harvest CSV ({ex.Message})");
            return null;
        }
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private static double SafeDouble(DataRow row, string col)
    {
        try { return Convert.ToDouble(row[col]); } catch { return double.NaN; }
    }

    private static int SafeInt(DataRow row, string col)
    {
        try { return Convert.ToInt32(row[col]); } catch { return -1; }
    }
}
