using APSIM.Core;
using APSIM.Shared.Utilities;
using Models.Core;
using Models.Core.Run;
using Models.Storage;
using Spacing.Core.Domain;
using System.Data;
using System.Text;

namespace Spacing.Core.Simulation;

/// <summary>
/// Esegue una SimulationUnit tramite il motore APSIM Next Generation
/// e legge i risultati dal DataStore SQLite.
/// </summary>
public class SimulationRunner
{
    private readonly string _outputDir;

    public SimulationRunner(string outputDir)
    {
        _outputDir = outputDir;
        Directory.CreateDirectory(_outputDir);
    }

    /// <summary>
    /// Esegue la simulazione in modo sincrono e restituisce il risultato.
    /// <param name="saveOutputFiles">
    ///   Se false, salta la scrittura dei CSV di output (usato durante la calibrazione
    ///   per massimizzare la velocità: APSIM gira veloce, solo i dati in-memory servono
    ///   per il calcolo della loss function).
    /// </param>
    /// </summary>
    public SimulationResult Run(SimulationUnit unit, string apsimxPath,
        bool saveOutputFiles = true)
    {
        var result = new SimulationResult { SimulationUnitId = unit.Id };

        Console.Error.WriteLine($"[Runner] Caricamento: {apsimxPath}");

        try
        {
            var simulations = FileFormat
                .ReadFromFile<Simulations>(
                    apsimxPath,
                    e => throw new Exception($"Errore caricamento: {e.Message}", e))
                .Model as Simulations;

            if (simulations == null)
                throw new Exception("FileFormat ha restituito null: controllare la struttura del .apsimx");

            // Diagnostica: verifica il percorso .db che APSIM intende usare
            var dsNode = simulations.Node.FindChild<IDataStore>();
            if (dsNode is DataStore ds)
                Console.Error.WriteLine($"[Runner] DataStore.FileName (pre-run) = '{ds.FileName}'");
            else
                Console.Error.WriteLine("[Runner] AVVISO: DataStore non trovato prima del run");

            Console.Error.WriteLine($"[Runner] Avvio simulazione APSIM...");
            var runner = new Runner(simulations);
            var errors = runner.Run();

            if (errors != null && errors.Count > 0)
            {
                result.Success = false;
                result.ErrorMessage = string.Join("\n", errors.Select(FullMessage));
                Console.Error.WriteLine($"[Runner] ERRORI ({errors.Count}):");
                foreach (var e in errors)
                    Console.Error.WriteLine($"  {FullMessage(e)}");
                return result;
            }

            Console.Error.WriteLine("[Runner] Simulazione completata senza errori.");
            result.Success = true;
            result.DataStorePath = Path.ChangeExtension(apsimxPath, ".db");

            // Dopo runner.Run() il DataStore chiude la connessione: bisogna
            // riaprire il file .db direttamente tramite SQLite + DataStoreReader.
            if (!File.Exists(result.DataStorePath))
            {
                Console.Error.WriteLine($"[Runner] AVVISO: file DataStore non trovato: {result.DataStorePath}");
                return result;
            }

            var db = new SQLite();
            try
            {
                db.OpenDatabase(result.DataStorePath, readOnly: true);
                var reader = new DataStoreReader();
                reader.SetConnection(db);

                // Stampa _Messages per diagnostica semina/raccolta
                DumpMessages(db);

                ReadResults(unit, reader, result);

                // Legge DailyReport e lo salva nel result per la scrittura CSV
                result.RawDailyReport = reader.GetData("DailyReport");
                if (result.RawDailyReport != null)
                    Console.Error.WriteLine($"[Runner] DailyReport: {result.RawDailyReport.Rows.Count} righe giornaliere");
                else
                    Console.Error.WriteLine("[Runner] AVVISO: tabella 'DailyReport' non trovata.");
            }
            finally
            {
                db.CloseDatabase();
            }

            // Scrive i CSV di output (solo se richiesto — durante la calibrazione
            // questo passaggio viene saltato per non appesantire ogni simulazione)
            if (saveOutputFiles)
            {
                WriteControlCsv(unit, result);
                WriteDailyCsv(unit, result);
                WriteCropSummaryCsv(unit, result);
            }
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = FullMessage(ex);
            Console.Error.WriteLine($"[Runner] ECCEZIONE: {result.ErrorMessage}");
        }

        return result;
    }

    /// <summary>Versione asincrona per uso nel batch Tool1.</summary>
    public Task<SimulationResult> RunAsync(
        SimulationUnit unit,
        string apsimxPath,
        bool saveOutputFiles = true,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => Run(unit, apsimxPath, saveOutputFiles), cancellationToken);

    // -------------------------------------------------------------------------
    // Lettura e aggregazione risultati
    // -------------------------------------------------------------------------

    private static void ReadResults(
        SimulationUnit unit,
        DataStoreReader reader,
        SimulationResult result)
    {
        try
        {
            // Elenca tutte le tabelle presenti nel DataStore per diagnostica
            try
            {
                // TableNames filtra le tabelle con _ → usiamo GetTableNames() direttamente
                var allTables = reader.Connection.GetTableNames();
                Console.Error.WriteLine($"[Runner] Tabelle nel DataStore (tutte): {string.Join(", ", allTables)}");
                Console.Error.WriteLine($"[Runner] Tabelle utente: {string.Join(", ", reader.TableNames)}");
            }
            catch (Exception tex) { Console.Error.WriteLine($"[Runner] AVVISO listing tabelle: {tex.Message}"); }

            var table = reader.GetData("Report");

            Console.Error.WriteLine(table == null
                ? "[Runner] AVVISO: tabella 'Report' non trovata nel DataStore."
                : $"[Runner] Report: {table.Rows.Count} righe, colonne: {string.Join(", ", table.Columns.Cast<DataColumn>().Select(c => c.ColumnName))}");

            if (table == null || table.Rows.Count == 0) return;

            result.RawReport = table;

            foreach (var crop in unit.Rotation.Crops)
            {
                var yieldCol   = $"{crop.CropName}_GrainYield_kgha";
                var biomassCol = $"{crop.CropName}_Biomass_kgha";

                if (table.Columns.Contains(yieldCol))
                {
                    var yields = table.AsEnumerable()
                        .Select(r => SafeDouble(r, yieldCol))
                        .Where(v => v > 0)
                        .ToList();
                    if (yields.Count > 0)
                    {
                        result.MeanYieldKgHa[crop.CropName] = yields.Average();
                        Console.Error.WriteLine($"[Runner] {crop.CropName}: {yields.Count} raccolti, resa media = {result.MeanYieldKgHa[crop.CropName]:F0} kg/ha");
                    }
                    else
                    {
                        Console.Error.WriteLine($"[Runner] AVVISO: {crop.CropName} non ha mai prodotto resa > 0 (mai seminato?)");
                    }
                }

                if (table.Columns.Contains(biomassCol))
                {
                    var bio = table.AsEnumerable()
                        .Select(r => SafeDouble(r, biomassCol))
                        .Where(v => v > 0)
                        .ToList();
                    if (bio.Count > 0)
                        result.MeanAbovegroundBiomassKgHa[crop.CropName] = bio.Average();
                }
            }

            if (table.Columns.Contains("SoilNO3_kgha") && table.Rows.Count > 0)
                result.FinalSoilCarbonKgHa = SafeDouble(table.Rows[table.Rows.Count - 1], "SoilNO3_kgha");
        }
        catch (Exception ex)
        {
            var msg = $" [ReadResults: {FullMessage(ex)}]";
            result.ErrorMessage = (result.ErrorMessage ?? "") + msg;
            Console.Error.WriteLine($"[Runner] ERRORE lettura risultati:{msg}");
        }
    }

    // -------------------------------------------------------------------------
    // CSV di controllo
    // -------------------------------------------------------------------------

    private void WriteDailyCsv(SimulationUnit unit, SimulationResult result)
    {
        try
        {
            if (result.RawDailyReport == null || result.RawDailyReport.Rows.Count == 0)
            {
                Console.Error.WriteLine("[Runner] Nessun dato giornaliero da scrivere.");
                return;
            }

            var csvPath = Path.Combine(
                _outputDir,
                $"daily_{unit.Id.Replace("__", "_")}.csv");

            WriteCsvFromTable(result.RawDailyReport, csvPath);
            Console.Error.WriteLine($"[Runner] CSV giornaliero: {csvPath}");
            result.DailyCsvPath = csvPath;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Runner] AVVISO: impossibile scrivere daily CSV ({ex.Message})");
        }
    }

    private void WriteControlCsv(SimulationUnit unit, SimulationResult result)
    {
        try
        {
            if (result.RawReport == null || result.RawReport.Rows.Count == 0)
            {
                Console.Error.WriteLine("[Runner] Nessun dato da scrivere nel CSV di controllo.");
                return;
            }
            var csvPath = Path.Combine(_outputDir, $"report_{unit.Id.Replace("__", "_")}.csv");
            WriteCsvFromTable(result.RawReport, csvPath);
            Console.Error.WriteLine($"[Runner] CSV di controllo: {csvPath}");
            result.ControlCsvPath = csvPath;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Runner] AVVISO: impossibile scrivere CSV ({ex.Message})");
        }
    }

    private static void WriteCsvFromTable(System.Data.DataTable table, string path)
    {
        var sb = new StringBuilder();
        var cols = table.Columns.Cast<DataColumn>().Select(c => c.ColumnName).ToList();
        sb.AppendLine(string.Join(",", cols));
        foreach (DataRow row in table.Rows)
        {
            var values = cols.Select(c =>
            {
                var v = row[c]?.ToString() ?? "";
                return v.Contains(',') ? $"\"{v}\"" : v;
            });
            sb.AppendLine(string.Join(",", values));
        }
        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
    }

    // -------------------------------------------------------------------------
    // CSV riassuntivo per coltura (post-processing del DailyReport)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Produce crop_summary_*.csv con una riga per ogni raccolto:
    ///   Crop, SowDate, HarvestDate, Yield_kgha, DeltaSOC_kgCha,
    ///   N2O_kgNha, CO2C_kgCha, LeachNO3_kgNha
    ///
    /// L'algoritmo:
    ///   1. Scansiona il DailyReport cercando le transizioni IsAlive False→True (semina)
    ///      e True→False (raccolta) per ogni coltura.
    ///   2. Aggrega le variabili GHG e idrologiche nel periodo vegetativo.
    ///   3. Calcola ΔSOC come differenza di SoilCtot tra fine e inizio stagione.
    ///   4. La resa viene presa dal Report harvest (trigger su [CropName].Harvesting).
    ///
    /// Questo approccio non richiede Manager aggiuntivi nell'APSIM XML.
    /// Le colonne GHG necessarie nel DailyReport sono:
    ///   N2O_total_kgNha, CO2C_total_kgCha, LeachNO3_kgNha, SoilCtot_kgCha,
    ///   {CropName}_IsAlive
    /// </summary>
    private void WriteCropSummaryCsv(SimulationUnit unit, SimulationResult result)
    {
        try
        {
            var daily   = result.RawDailyReport;
            var harvest = result.RawReport;

            if (daily == null || daily.Rows.Count == 0)
            {
                Console.Error.WriteLine("[Runner] CropSummary: nessun DailyReport disponibile.");
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine("Crop,SowDate,HarvestDate,Yield_kgha,DeltaSOC_kgCha," +
                          "N2O_kgNha,CO2C_kgCha,LeachNO3_kgNha");

            foreach (var crop in unit.Rotation.Crops)
            {
                var name      = crop.CropName;
                var aliveCol  = $"{name}_IsAlive";
                var yieldCol  = $"{name}_GrainYield_kgha";

                if (!daily.Columns.Contains(aliveCol))
                {
                    Console.Error.WriteLine($"[Runner] CropSummary: colonna '{aliveCol}' non trovata nel DailyReport.");
                    continue;
                }

                // Individua i periodi vegetativi (False→True = semina, True→False = raccolta)
                var seasons = FindGrowingSeasons(daily, aliveCol);
                Console.Error.WriteLine($"[Runner] CropSummary {name}: {seasons.Count} stagioni trovate.");

                foreach (var (sowDate, harvestDate, rows) in seasons)
                {
                    // Aggregazioni GHG stagionali
                    double cumN2O   = rows.Sum(r => SafeDouble(r, "N2O_total_kgNha"));
                    double cumCO2   = rows.Sum(r => SafeDouble(r, "CO2C_total_kgCha"));
                    double cumLeach = rows.Sum(r => SafeDouble(r, "LeachNO3_kgNha"));

                    // ΔSOC: differenza stock C organico tra fine e inizio stagione (kg C/ha)
                    double socStart = SafeDouble(rows.First(), "SoilCtot_kgCha");
                    double socEnd   = SafeDouble(rows.Last(),  "SoilCtot_kgCha");
                    double deltaSOC = socEnd - socStart;

                    // Resa dall'harvest Report: cerca la riga con anno == harvestDate.Year
                    double yield = 0;
                    if (harvest != null && harvest.Columns.Contains(yieldCol))
                    {
                        var harvestYear = harvestDate.Year;
                        var harvestRow = harvest.AsEnumerable()
                            .FirstOrDefault(r =>
                            {
                                try { return Convert.ToInt32(r["Year"]) == harvestYear; }
                                catch { return false; }
                            });
                        if (harvestRow != null)
                            yield = SafeDouble(harvestRow, yieldCol);
                    }

                    sb.AppendLine(
                        $"{name},{sowDate:yyyy-MM-dd},{harvestDate:yyyy-MM-dd}," +
                        $"{yield:F0},{deltaSOC:F1}," +
                        $"{cumN2O:F4},{cumCO2:F1},{cumLeach:F3}");
                }
            }

            var csvPath = Path.Combine(_outputDir, $"crop_summary_{unit.Id.Replace("__", "_")}.csv");
            File.WriteAllText(csvPath, sb.ToString(), Encoding.UTF8);
            Console.Error.WriteLine($"[Runner] CSV crop summary: {csvPath}");
            result.CropSummaryCsvPath = csvPath;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Runner] AVVISO: impossibile scrivere crop summary ({ex.Message})");
        }
    }

    /// <summary>
    /// Individua i periodi vegetativi di una coltura nel DailyReport.
    /// Restituisce una lista di (SowDate, HarvestDate, righe del periodo).
    /// IsAlive è scritto da APSIM come stringa "True"/"False".
    /// </summary>
    private static List<(DateTime Sow, DateTime Harvest, List<DataRow> Rows)> FindGrowingSeasons(
        DataTable daily, string aliveCol)
    {
        var result = new List<(DateTime, DateTime, List<DataRow>)>();

        bool   prevAlive    = false;
        DateTime sowDate    = default;
        var    currentRows  = new List<DataRow>();

        foreach (DataRow row in daily.Rows)
        {
            bool alive = row[aliveCol]?.ToString()
                            .Equals("True", StringComparison.OrdinalIgnoreCase) ?? false;

            // Parsing data: APSIM scrive ISO o formato locale — proviamo entrambi
            DateTime date = default;
            try { date = Convert.ToDateTime(row["Clock.Today"]); }
            catch
            {
                try { date = DateTime.Parse(row["Clock.Today"]?.ToString() ?? ""); }
                catch { /* skip */ }
            }

            if (alive && !prevAlive)
            {
                // Transizione False→True: semina
                sowDate     = date;
                currentRows = new List<DataRow>();
            }

            if (alive)
                currentRows.Add(row);

            if (!alive && prevAlive && sowDate != default && currentRows.Count > 0)
            {
                // Transizione True→False: raccolta (il giorno corrente è il primo dopo EndCrop)
                // harvestDate = ultimo giorno in cui era viva
                var harvestDate = currentRows.Count > 0
                    ? Convert.ToDateTime(currentRows.Last()["Clock.Today"])
                    : date;
                result.Add((sowDate, harvestDate, currentRows));
            }

            prevAlive = alive;
        }

        // Gestisce il caso in cui la simulazione termina a coltura ancora viva
        if (prevAlive && currentRows.Count > 0)
        {
            var lastDate = Convert.ToDateTime(currentRows.Last()["Clock.Today"]);
            result.Add((sowDate, lastDate, currentRows));
        }

        return result;
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>Stampa su stderr i messaggi APSIM più rilevanti dal DataStore.</summary>
    private static void DumpMessages(SQLite db)
    {
        try
        {
            if (!db.TableExists("_Messages")) return;

            // Prima legge lo schema reale della tabella
            var cols = db.GetColumns("_Messages");
            var colNames = cols.Select(c => c.Item1).ToList();
            Console.Error.WriteLine($"[Runner] _Messages colonne: {string.Join(", ", colNames)}");

            var dt = db.ExecuteQuery("SELECT * FROM [_Messages] ORDER BY rowid");
            Console.Error.WriteLine($"[Runner] === _Messages ({dt.Rows.Count} righe) ===");

            // Indovina i nomi di colonna in modo flessibile
            string ColOf(DataRow r, params string[] candidates)
            {
                foreach (var c in candidates)
                    if (dt.Columns.Contains(c)) return r[c]?.ToString() ?? "";
                return "";
            }

            foreach (DataRow r in dt.Rows)
            {
                var msg  = ColOf(r, "Message", "MessageText", "Text", "Msg");
                var comp = ColOf(r, "ComponentName", "Component", "Source", "ModelName");
                var date = ColOf(r, "Date", "SimulationDate", "Clock.Today");
                var type = ColOf(r, "MessageType", "Severity", "Type");

                bool isError   = type == "2" || type.Contains("error", StringComparison.OrdinalIgnoreCase);
                bool isWarning = type == "1" || type.Contains("warn",  StringComparison.OrdinalIgnoreCase);
                bool isKey     = msg.Contains("Sow",     StringComparison.OrdinalIgnoreCase)
                              || msg.Contains("Harvest", StringComparison.OrdinalIgnoreCase)
                              || msg.Contains("Plant",   StringComparison.OrdinalIgnoreCase)
                              || msg.Contains("Error",   StringComparison.OrdinalIgnoreCase)
                              || msg.Contains("Warning", StringComparison.OrdinalIgnoreCase)
                              || msg.Contains("cannot",  StringComparison.OrdinalIgnoreCase)
                              || msg.Contains("failed",  StringComparison.OrdinalIgnoreCase);

                // In mancanza di filtro utile, stampa tutto (max 100 righe)
                if (isError || isWarning || isKey || dt.Rows.Count <= 100)
                    Console.Error.WriteLine($"  [{date}][{comp}][{type}] {msg}");
            }
            Console.Error.WriteLine("[Runner] === fine _Messages ===");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Runner] Impossibile leggere _Messages: {ex.Message}");
        }
    }

    private static string FullMessage(Exception ex)
    {
        var sb = new StringBuilder();
        sb.Append(ex.Message);
        var inner = ex.InnerException;
        while (inner != null)
        {
            sb.Append($" --> {inner.Message}");
            inner = inner.InnerException;
        }
        return sb.ToString();
    }

    private static double SafeDouble(DataRow row, string col)
    {
        try { return Convert.ToDouble(row[col]); }
        catch { return 0.0; }
    }
}
