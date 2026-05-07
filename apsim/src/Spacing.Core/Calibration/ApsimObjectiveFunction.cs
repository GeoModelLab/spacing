using Spacing.Core.Adapters;
using Spacing.Core.Domain;
using Spacing.Core.Simulation;
using UNIMI.optimizer;
using System.Data;
#pragma warning disable CS0649  // field never assigned (optimizer files decompilati)

namespace Spacing.Core.Calibration;

/// <summary>
/// Funzione obiettivo APSIM per il Multi-Start Simplex.
///
/// Ad ogni chiamata di ObjfuncVal:
///  1. Traduce il vettore dei coefficienti nei parametri APSIM (cultivar override)
///  2. Costruisce e lancia la simulazione
///  3. Confronta il risultato con le osservazioni di riferimento
///  4. Restituisce loss combinata (da minimizzare)
///
/// Funzione obiettivo:
///   loss = YieldWeight * NRMSE_yield + LaiWeight * NRMSE_lai
///
///   NRMSE_yield = RMSE_yield / mean_obs_yield  (adimensionale, normalizzato)
///   NRMSE_lai   = RMSE_lai   / mean_obs_lai    (adimensionale, normalizzato)
///
/// I parametri sono ordinati esattamente come in CropParameters[].
/// Il dizionario CropParameters mappa CropName → lista di CalibrationParameter.
/// </summary>
public class ApsimObjectiveFunction : IOBJfunc
{
    // ---- IOBJfunc counters ----
    public int neval    { get; set; }
    public int ncompute { get; set; }

    // ---- Dipendenze ----
    private readonly SimulationUnit             _unitTemplate;
    private readonly string                     _weatherMetPath;
    private readonly SoilAdapter                _soilAdapter;
    private readonly RotationBuilder            _rotBuilder;
    private readonly SimulationRunner           _runner;
    private readonly List<ReferenceObservation> _yieldObservations;

    // ---- Configurazione ----

    /// <summary>CropName → lista di parametri da calibrare.</summary>
    public Dictionary<string, List<CalibrationParameter>> CropParameters { get; init; } = new();

    /// <summary>
    /// Osservazioni LAI puntuale (Year × DOY × Crop) da telerilevamento.
    /// Se vuota, la componente LAI viene ignorata nella loss function.
    /// </summary>
    public List<LaiObservation> LaiObservations { get; init; } = new();

    /// <summary>Peso della componente resa nella loss (default 0.5).</summary>
    public double YieldWeight { get; init; } = 0.5;

    /// <summary>Peso della componente LAI nella loss (default 0.5).</summary>
    public double LaiWeight { get; init; } = 0.5;

    public ApsimObjectiveFunction(
        SimulationUnit             unitTemplate,
        string                     weatherMetPath,
        SoilAdapter                soilAdapter,
        RotationBuilder            rotBuilder,
        SimulationRunner           runner,
        List<ReferenceObservation> yieldObservations)
    {
        _unitTemplate       = unitTemplate;
        _weatherMetPath     = weatherMetPath;
        _soilAdapter        = soilAdapter;
        _rotBuilder         = rotBuilder;
        _runner             = runner;
        _yieldObservations  = yieldObservations;
    }

    // ---- IOBJfunc ----

    /// <summary>
    /// Calcola il valore della funzione obiettivo per un dato vettore di parametri.
    /// Restituisce 1e300 se fuori dominio o la simulazione fallisce.
    /// </summary>
    public double ObjfuncVal(double[] Coefficient, double[,] limits)
    {
        neval++;

        // Verifica dominio — usa limits.GetLength(0) = nParam come bound corretto.
        // Coefficient.Length può essere nParam+1 per un bug nel Simplex decompilato.
        int nParams = limits.GetLength(0);
        for (int j = 0; j < nParams; j++)
            if (Coefficient[j] < limits[j, 0] || Coefficient[j] > limits[j, 1])
                return 1e300;

        ncompute++;

        string apsimxPath = null;
        try
        {
            var overrides = BuildOverrides(Coefficient);

            // ID corto per non sporcare la cartella output con nomi lunghissimi.
            // Il file viene cancellato dopo ogni run (vedi finally sotto).
            var tmpUnit = new SimulationUnit
            {
                Cell        = _unitTemplate.Cell,
                SoilProfile = _unitTemplate.SoilProfile,
                Rotation    = new CropRotation
                {
                    Id     = $"_cal_{ncompute}",
                    Name   = _unitTemplate.Rotation.Name,
                    Crops  = _unitTemplate.Rotation.Crops,
                    Cycles = _unitTemplate.Rotation.Cycles
                },
                StartYear = _unitTemplate.StartYear,
                EndYear   = _unitTemplate.EndYear
            };

            apsimxPath = _rotBuilder.BuildApsimxFile(tmpUnit, _weatherMetPath, overrides);
            // saveOutputFiles=false: nessun CSV scritto durante la calibrazione
            var result = _runner.Run(tmpUnit, apsimxPath, saveOutputFiles: false);

            if (!result.Success)
                return 1e300;

            return ComputeLoss(result);
        }
        catch
        {
            return 1e300;
        }
        finally
        {
            // Pulizia file temporanei: .apsimx + .db + .db-wal + .db-shm vengono
            // cancellati dopo ogni iterazione dell'ottimizzatore.
            // GC.Collect assicura che SQLiteConnection sia rilasciata prima del delete.
            if (apsimxPath != null)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                var dir  = Path.GetDirectoryName(apsimxPath) ?? ".";
                var stem = Path.GetFileNameWithoutExtension(apsimxPath);
                foreach (var f in Directory.GetFiles(dir, $"{stem}*"))
                    try { File.Delete(f); } catch { }
            }
        }
    }

    // ---- Helpers pubblici ----

    public int TotalParams =>
        CropParameters.Values.Sum(list => list.Count);

    public double[,] BuildLimits()
    {
        var allParams = AllParams();
        var limits    = new double[allParams.Count, 2];
        for (int i = 0; i < allParams.Count; i++)
        {
            limits[i, 0] = allParams[i].LowerBound;
            limits[i, 1] = allParams[i].UpperBound;
        }
        return limits;
    }

    public Dictionary<string, Dictionary<string, double>> BuildOverrides(double[] values)
    {
        var result = new Dictionary<string, Dictionary<string, double>>();
        int idx    = 0;
        foreach (var (cropName, paramList) in CropParameters)
        {
            var cropDict = new Dictionary<string, double>();
            foreach (var p in paramList)
                cropDict[p.Name] = values[idx++];
            result[cropName] = cropDict;
        }
        return result;
    }

    // ---- Funzione obiettivo ----

    private double ComputeLoss(SimulationResult result)
    {
        // ---- Componente 1: resa per anno ----
        double yieldSse = 0;
        int    yieldN   = 0;

        var yieldObs = _yieldObservations.Where(o => !double.IsNaN(o.GrainYieldKgHa)).ToList();

        if (yieldObs.Count > 0 && result.RawReport != null && result.RawReport.Rows.Count > 0)
        {
            // Scala di normalizzazione = media osservata (rende NRMSE adimensionale)
            double meanObsYield = yieldObs.Average(o => o.GrainYieldKgHa / 1000.0); // t/ha
            double normScale    = Math.Max(meanObsYield, 1.0);

            foreach (var obs in yieldObs)
            {
                var yieldCol = $"{obs.CropName}_GrainYield_kgha";
                if (!result.RawReport.Columns.Contains(yieldCol)) continue;

                var harvestRow = result.RawReport.AsEnumerable()
                    .FirstOrDefault(r => SafeInt(r, "Year") == obs.Year);
                if (harvestRow == null) continue;

                double simYield = SafeDouble(harvestRow, yieldCol) / 1000.0; // kg/ha → t/ha
                double obsYield = obs.GrainYieldKgHa / 1000.0;

                yieldSse += Math.Pow((simYield - obsYield) / normScale, 2);
                yieldN++;
            }
        }

        // ---- Componente 2: LAI time series ----
        double laiSse = 0;
        int    laiN   = 0;

        if (LaiObservations.Count > 0 && result.RawDailyReport != null)
        {
            // Indica il DailyReport una sola volta per (Year, DOY) → O(1) lookup
            var dailyIndex = result.RawDailyReport.AsEnumerable()
                .ToLookup(r => (Year: SafeInt(r, "Year"), DOY: SafeInt(r, "DOY")));

            double meanObsLai = LaiObservations.Average(l => l.LAI);
            double laiScale   = Math.Max(meanObsLai, 0.5); // evita divisione per zero

            foreach (var laiObs in LaiObservations)
            {
                var laiCol = $"{laiObs.CropName}_LAI";
                if (!result.RawDailyReport.Columns.Contains(laiCol)) continue;

                var group = dailyIndex[(laiObs.Year, laiObs.DOY)];
                var row   = group.FirstOrDefault();
                if (row == null) continue;

                double simLai = SafeDouble(row, laiCol);
                laiSse += Math.Pow((simLai - laiObs.LAI) / laiScale, 2);
                laiN++;
            }
        }

        // ---- Combina ----
        double totalLoss = 0;
        int    nTerms    = 0;
        double nrmseYield = 0, nrmseLai = 0;

        if (yieldN > 0)
        {
            nrmseYield = Math.Sqrt(yieldSse / yieldN);
            totalLoss += YieldWeight * nrmseYield;
            nTerms++;
        }
        if (laiN > 0)
        {
            nrmseLai   = Math.Sqrt(laiSse / laiN);
            totalLoss += LaiWeight * nrmseLai;
            nTerms++;
        }

        if (nTerms == 0) return 1e300;

        // Stampa avanzamento su stderr (sovrascrive la riga corrente)
        var yieldStr = yieldN > 0 ? $"NRMSEy={nrmseYield:F3}" : "NRMSEy=n/a";
        var laiStr   = laiN   > 0 ? $"NRMSEl={nrmseLai:F3}"   : "NRMSEl=n/a";
        var yieldSim = string.Join(" ", CropParameters.Keys.Select(c =>
            result.MeanYieldKgHa.TryGetValue(c, out var y) ? $"{c}={y/1000:F1}t/ha" : $"{c}=?"));

        Console.Error.Write(
            $"\r  eval={ncompute:D4}  loss={totalLoss:F4}  {yieldStr}  {laiStr}  [{yieldSim}]  ");

        return totalLoss;
    }

    // ---- Privati ----

    private List<CalibrationParameter> AllParams() =>
        CropParameters.Values.SelectMany(l => l).ToList();

    private static double SafeDouble(DataRow row, string col)
    {
        try { return Convert.ToDouble(row[col]); } catch { return 0.0; }
    }

    private static int SafeInt(DataRow row, string col)
    {
        try { return Convert.ToInt32(row[col]); } catch { return -1; }
    }
}
