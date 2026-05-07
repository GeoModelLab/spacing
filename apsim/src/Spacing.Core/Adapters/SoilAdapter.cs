using Spacing.Core.Domain;

namespace Spacing.Core.Adapters;

/// <summary>
/// Converte un SoilProfile (da API Spacing DB) nel frammento JSON APSIM
/// che rappresenta il modello Soil con i suoi sottomodelli.
///
/// Struttura derivata da Sorghum.apsimx (APSIM Next Generation):
///   Soil
///     Physical (con SoilCrop per ogni coltura)
///     WaterBalance (Models.WaterModel.WaterBalance, ResourceName="WaterBalance")
///     Nutrient    (Models.Soils.Nutrients.Nutrient,  ResourceName="Nutrient")
///     Organic
///     Chemical
///     Water       (contenuto idrico iniziale, con Thickness)
///     CERESSoilTemperature
///     Solute NO3  (con Thickness)
///     Solute NH4  (con Thickness)
///     Solute Urea (con Thickness)
/// </summary>
public class SoilAdapter
{
    private static readonly string Fmt = "F4";
    private static readonly System.Globalization.CultureInfo Inv =
        System.Globalization.CultureInfo.InvariantCulture;

    /// <summary>
    /// Genera il frammento JSON APSIM per un SoilProfile.
    /// cropNames: nomi delle colture nella rotazione, usati per generare i
    /// nodi SoilCrop figli di Physical (LL, KL, XF).
    /// </summary>
    public string ToApsimJson(SoilProfile profile, IEnumerable<string> cropNames = null)
    {
        var layers = profile.Layers;
        var thick  = Arr(layers.Select(l => (l.DepthBottom - l.DepthTop) * 10.0));
        var thickJ = ArrJson(thick);

        var crops  = (cropNames ?? Enumerable.Empty<string>()).ToList();
        if (crops.Count == 0) crops.Add("Sorghum");  // default

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("{");
        sb.AppendLine("  \"$type\": \"Models.Soils.Soil, Models\",");
        sb.AppendLine($"  \"SoilType\": \"{Esc(profile.SoilType)}\",");
        sb.AppendLine($"  \"DataSource\": \"{Esc(profile.DataSource)}\",");
        sb.AppendLine("  \"Name\": \"Soil\",");
        sb.AppendLine("  \"Children\": [");

        // ---- 1. Physical ------------------------------------------------
        var soilCrops = string.Join(",\n", crops.Select(c => BuildSoilCrop(c, layers)));

        sb.AppendLine("    {");
        sb.AppendLine("      \"$type\": \"Models.Soils.Physical, Models\",");
        sb.AppendLine("      \"Name\": \"Physical\",");
        sb.AppendLine($"      \"Thickness\": {thickJ},");
        sb.AppendLine($"      \"BD\": {ValJson(layers.Select(l => l.BD))},");
        sb.AppendLine($"      \"AirDry\": {ValJson(layers.Select(l => l.LL15 * 0.5))},");
        sb.AppendLine($"      \"LL15\": {ValJson(layers.Select(l => l.LL15))},");
        sb.AppendLine($"      \"DUL\": {ValJson(layers.Select(l => l.DUL))},");
        sb.AppendLine($"      \"SAT\": {ValJson(layers.Select(l => l.SAT))},");
        sb.AppendLine("      \"KS\": null,");
        sb.AppendLine("      \"Children\": [");
        sb.AppendLine(soilCrops);
        sb.AppendLine("      ],");
        sb.AppendLine("      \"Enabled\": true,");
        sb.AppendLine("      \"ReadOnly\": false");
        sb.AppendLine("    },");

        // ---- 2. WaterBalance (SWCON model) --------------------------------
        sb.AppendLine("    {");
        sb.AppendLine("      \"$type\": \"Models.WaterModel.WaterBalance, Models\",");
        sb.AppendLine("      \"Name\": \"SoilWater\",");
        sb.AppendLine("      \"ResourceName\": \"WaterBalance\",");
        sb.AppendLine("      \"SummerDate\": \"1-Jun\",");
        sb.AppendLine("      \"SummerU\": 6.0,");
        sb.AppendLine("      \"SummerCona\": 3.5,");
        sb.AppendLine("      \"WinterDate\": \"1-Oct\",");
        sb.AppendLine("      \"WinterU\": 2.0,");
        sb.AppendLine("      \"WinterCona\": 2.5,");
        sb.AppendLine("      \"DiffusConst\": 40.0,");
        sb.AppendLine("      \"DiffusSlope\": 16.0,");
        sb.AppendLine("      \"Salb\": 0.13,");
        sb.AppendLine("      \"CN2Bare\": 73.0,");
        sb.AppendLine("      \"CNRed\": 20.0,");
        sb.AppendLine("      \"CNCov\": 0.8,");
        sb.AppendLine("      \"PSIDul\": -100.0,");
        sb.AppendLine($"      \"Thickness\": {thickJ},");
        sb.AppendLine($"      \"SWCON\": {ValJson(layers.Select(_ => 0.3))},");
        sb.AppendLine("      \"Children\": [],");
        sb.AppendLine("      \"Enabled\": true,");
        sb.AppendLine("      \"ReadOnly\": false");
        sb.AppendLine("    },");

        // ---- 3. Nutrient (ciclo azoto/carbonio) ----------------------------
        sb.AppendLine("    {");
        sb.AppendLine("      \"$type\": \"Models.Soils.Nutrients.Nutrient, Models\",");
        sb.AppendLine("      \"Name\": \"Nutrient\",");
        sb.AppendLine("      \"ResourceName\": \"Nutrient\",");
        sb.AppendLine("      \"Children\": [],");
        sb.AppendLine("      \"Enabled\": true,");
        sb.AppendLine("      \"ReadOnly\": false");
        sb.AppendLine("    },");

        // ---- 4. Organic ----------------------------------------------------
        sb.AppendLine("    {");
        sb.AppendLine("      \"$type\": \"Models.Soils.Organic, Models\",");
        sb.AppendLine("      \"Name\": \"Organic\",");
        sb.AppendLine("      \"FOMCNRatio\": 50.0,");
        sb.AppendLine($"      \"Thickness\": {thickJ},");
        sb.AppendLine($"      \"Carbon\": {ValJson(layers.Select(l => l.OC))},");
        sb.AppendLine($"      \"SoilCNRatio\": {ValJson(layers.Select(_ => 12.5))},");
        sb.AppendLine($"      \"FBiom\": {ValJson(layers.Select(l => EstimateFBiom(l)))},");
        sb.AppendLine($"      \"FInert\": {ValJson(layers.Select(l => EstimateFInert(l)))},");
        sb.AppendLine($"      \"FOM\": {ValJson(layers.Select((l, i) => 260.0 * Math.Exp(-0.02 * i * (l.DepthBottom - l.DepthTop))))},");
        sb.AppendLine("      \"Children\": [],");
        sb.AppendLine("      \"Enabled\": true,");
        sb.AppendLine("      \"ReadOnly\": false");
        sb.AppendLine("    },");

        // ---- 5. Chemical ---------------------------------------------------
        sb.AppendLine("    {");
        sb.AppendLine("      \"$type\": \"Models.Soils.Chemical, Models\",");
        sb.AppendLine("      \"Name\": \"Chemical\",");
        sb.AppendLine($"      \"Thickness\": {thickJ},");
        sb.AppendLine($"      \"PH\": {ValJson(layers.Select(l => l.PH))},");
        sb.AppendLine("      \"PHUnits\": 0,");
        sb.AppendLine("      \"Children\": [],");
        sb.AppendLine("      \"Enabled\": true,");
        sb.AppendLine("      \"ReadOnly\": false");
        sb.AppendLine("    },");

        // ---- 6. Water (contenuto idrico iniziale) --------------------------
        sb.AppendLine("    {");
        sb.AppendLine("      \"$type\": \"Models.Soils.Water, Models\",");
        sb.AppendLine("      \"Name\": \"Water\",");
        sb.AppendLine($"      \"Thickness\": {thickJ},");
        sb.AppendLine($"      \"InitialValues\": {ValJson(layers.Select(l => l.SW))},");
        sb.AppendLine("      \"RelativeTo\": null,");
        sb.AppendLine("      \"FilledFromTop\": false,");
        sb.AppendLine("      \"Children\": [],");
        sb.AppendLine("      \"Enabled\": true,");
        sb.AppendLine("      \"ReadOnly\": false");
        sb.AppendLine("    },");

        // ---- 7. Temperatura suolo -----------------------------------------
        sb.AppendLine("    {");
        sb.AppendLine("      \"$type\": \"Models.Soils.CERESSoilTemperature, Models\",");
        sb.AppendLine("      \"Name\": \"Temperature\",");
        sb.AppendLine("      \"Children\": [],");
        sb.AppendLine("      \"Enabled\": true,");
        sb.AppendLine("      \"ReadOnly\": false");
        sb.AppendLine("    },");

        // ---- 8. Soluto NO3 -------------------------------------------------
        sb.AppendLine("    {");
        sb.AppendLine("      \"$type\": \"Models.Soils.Solute, Models\",");
        sb.AppendLine("      \"Name\": \"NO3\",");
        sb.AppendLine($"      \"Thickness\": {thickJ},");
        sb.AppendLine($"      \"InitialValues\": {ValJson(layers.Select(l => l.NO3))},");
        sb.AppendLine("      \"InitialValuesUnits\": 0,");
        sb.AppendLine("      \"WaterTableConcentration\": 0.0,");
        sb.AppendLine("      \"D0\": 0.0,");
        sb.AppendLine("      \"Children\": [],");
        sb.AppendLine("      \"Enabled\": true,");
        sb.AppendLine("      \"ReadOnly\": false");
        sb.AppendLine("    },");

        // ---- 9. Soluto NH4 -------------------------------------------------
        sb.AppendLine("    {");
        sb.AppendLine("      \"$type\": \"Models.Soils.Solute, Models\",");
        sb.AppendLine("      \"Name\": \"NH4\",");
        sb.AppendLine($"      \"Thickness\": {thickJ},");
        sb.AppendLine($"      \"InitialValues\": {ValJson(layers.Select(l => l.NH4))},");
        sb.AppendLine("      \"InitialValuesUnits\": 0,");
        sb.AppendLine("      \"WaterTableConcentration\": 0.0,");
        sb.AppendLine("      \"D0\": 0.0,");
        sb.AppendLine("      \"Children\": [],");
        sb.AppendLine("      \"Enabled\": true,");
        sb.AppendLine("      \"ReadOnly\": false");
        sb.AppendLine("    },");

        // ---- 10. Soluto Urea -----------------------------------------------
        sb.AppendLine("    {");
        sb.AppendLine("      \"$type\": \"Models.Soils.Solute, Models\",");
        sb.AppendLine("      \"Name\": \"Urea\",");
        sb.AppendLine($"      \"Thickness\": {thickJ},");
        sb.AppendLine($"      \"InitialValues\": {ValJson(layers.Select(_ => 0.0))},");
        sb.AppendLine("      \"InitialValuesUnits\": 1,");
        sb.AppendLine("      \"WaterTableConcentration\": 0.0,");
        sb.AppendLine("      \"D0\": 0.0,");
        sb.AppendLine("      \"Children\": [],");
        sb.AppendLine("      \"Enabled\": true,");
        sb.AppendLine("      \"ReadOnly\": false");
        sb.AppendLine("    }");

        sb.AppendLine("  ],");
        sb.AppendLine("  \"Enabled\": true,");
        sb.AppendLine("  \"ReadOnly\": false");
        sb.Append("}");

        return sb.ToString();
    }

    // ---- SoilCrop (figlio di Physical, per ogni coltura) ------------------

    private static string BuildSoilCrop(string cropName, List<SoilLayer> layers)
    {
        var ll  = ValJson(layers.Select(l => l.LL15));
        var kl  = ValJson(layers.Select((l, i) => KL(i, layers.Count)));
        var xf  = ValJson(layers.Select(_ => 1.0));

        return
            "        {\n" +
            "          \"$type\": \"Models.Soils.SoilCrop, Models\",\n" +
            $"          \"Name\": \"{cropName}Soil\",\n" +
            $"          \"LL\": {ll},\n" +
            $"          \"KL\": {kl},\n" +
            $"          \"XF\": {xf},\n" +
            "          \"Children\": [],\n" +
            "          \"Enabled\": true,\n" +
            "          \"ReadOnly\": false\n" +
            "        }";
    }

    // ---- Parametri derivati -----------------------------------------------

    private static double KL(int layerIndex, int totalLayers)
    {
        // Diminuisce con la profondita': 0.07 (sup) -> 0.04 (fondo)
        double frac = totalLayers > 1 ? (double)layerIndex / (totalLayers - 1) : 0.0;
        return 0.07 - frac * 0.03;
    }

    private static double EstimateFBiom(SoilLayer l) =>
        l.DepthTop < 15 ? 0.05 : l.DepthTop < 30 ? 0.02 : 0.01;

    private static double EstimateFInert(SoilLayer l) =>
        l.DepthTop < 15 ? 0.45 : l.DepthTop < 30 ? 0.60 : 0.90;

    // ---- Helpers JSON -------------------------------------------------------

    private static double[] Arr(IEnumerable<double> src) => src.ToArray();

    private static string ArrJson(double[] values) =>
        "[\n        " +
        string.Join(",\n        ", values.Select(v => v.ToString(Fmt, Inv))) +
        "\n      ]";

    private static string ValJson(IEnumerable<double> values) =>
        "[\n        " +
        string.Join(",\n        ", values.Select(v => v.ToString(Fmt, Inv))) +
        "\n      ]";

    private static string Esc(string s) => (s ?? "").Replace("\"", "\\\"");
}
