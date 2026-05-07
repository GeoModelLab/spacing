namespace Spacing.Core.Calibration;

/// <summary>
/// Carica dati di calibrazione da file CSV nel formato standard Spacing.
///
/// Formato CSV atteso (con header):
///   Province,Crop,Year,DOY,LAI,Yield
///
/// Convenzioni:
///   - Yield: in t/ha se valore &lt; 100, altrimenti in kg/ha. NaN/vuoto = assente.
///   - LAI  : m²/m². NaN/vuoto = assente.
///   - Una riga può avere solo LAI (riga di telerilevamento), solo Yield (riga
///     di raccolta da ISTAT), o entrambi.
///   - Province e Crop sono stringhe; DOY è intero (1–365).
///
/// Output:
///   - yieldObservations : una per (Crop, Year) dove Yield è presente
///   - laiObservations   : una per ogni riga dove LAI è presente
///
/// Esempio d'uso:
///   var (yields, lais) = CalibrationDataLoader.LoadFromCsv("testCalib.csv");
/// </summary>
public static class CalibrationDataLoader
{
    /// <summary>
    /// Carica le osservazioni da CSV.
    /// </summary>
    public static (List<ReferenceObservation> Yields, List<LaiObservation> Lais)
        LoadFromCsv(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"File calibrazione non trovato: {path}");

        var yields = new List<ReferenceObservation>();
        var lais   = new List<LaiObservation>();

        var lines = File.ReadAllLines(path);
        if (lines.Length < 2)
            throw new InvalidDataException("File vuoto o senza righe dati.");

        // Individua gli indici di colonna dalla riga header
        var header = lines[0].Split(',').Select(c => c.Trim().ToLowerInvariant()).ToList();
        int iProvince = IndexOf(header, "province");
        int iCrop     = IndexOf(header, "crop");
        int iYear     = IndexOf(header, "year");
        int iDoy      = IndexOf(header, "doy");
        int iLai      = IndexOf(header, "lai");
        int iYield    = IndexOf(header, "yield");

        if (iCrop < 0 || iYear < 0 || iDoy < 0)
            throw new InvalidDataException(
                "Header mancante: richieste colonne Crop, Year, DOY. " +
                $"Trovato: {lines[0]}");

        int lineNo = 1;
        foreach (var rawLine in lines.Skip(1))
        {
            lineNo++;
            if (string.IsNullOrWhiteSpace(rawLine)) continue;

            var cols = rawLine.Split(',');

            try
            {
                var crop = iCrop  < cols.Length ? cols[iCrop].Trim()  : null;
                if (string.IsNullOrEmpty(crop)) continue;

                int year = int.Parse(cols[iYear].Trim());
                int doy  = int.Parse(cols[iDoy].Trim());

                double lai   = iLai   >= 0 && iLai   < cols.Length
                               ? TryParseDouble(cols[iLai].Trim())   : double.NaN;
                double yield = iYield >= 0 && iYield < cols.Length
                               ? TryParseDouble(cols[iYield].Trim()) : double.NaN;

                // LAI observation
                if (!double.IsNaN(lai))
                    lais.Add(new LaiObservation
                    {
                        CropName = crop,
                        Year     = year,
                        DOY      = doy,
                        LAI      = lai
                    });

                // Yield observation
                if (!double.IsNaN(yield))
                {
                    // Se < 100 assumi t/ha → converti in kg/ha
                    double yieldKgHa = yield < 100.0 ? yield * 1000.0 : yield;
                    yields.Add(new ReferenceObservation
                    {
                        CropName       = crop,
                        Year           = year,
                        GrainYieldKgHa = yieldKgHa
                    });
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(
                    $"[CalibLoader] AVVISO riga {lineNo} ignorata: {ex.Message} ({rawLine})");
            }
        }

        Console.Error.WriteLine(
            $"[CalibLoader] Caricate {yields.Count} osservazioni resa, " +
            $"{lais.Count} osservazioni LAI da '{path}'.");

        return (yields, lais);
    }

    // ---- Helpers ----

    private static int IndexOf(List<string> header, string name) =>
        header.IndexOf(name);

    private static double TryParseDouble(string s)
    {
        if (string.IsNullOrEmpty(s) ||
            s.Equals("NaN", StringComparison.OrdinalIgnoreCase) ||
            s == "-")
            return double.NaN;

        return double.TryParse(s,
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out var d)
            ? d : double.NaN;
    }
}
