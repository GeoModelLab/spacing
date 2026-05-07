namespace Spacing.Core.Domain;

/// <summary>
/// Strato di suolo con proprietà fisiche, idrologiche e chimiche.
/// Unità di misura: SI standard compatibili con APSIM.
/// </summary>
public class SoilLayer
{
    public double DepthTop { get; set; }     // cm
    public double DepthBottom { get; set; }  // cm

    // Texture
    public double Sand { get; set; }         // %
    public double Silt { get; set; }         // %
    public double Clay { get; set; }         // %

    // Proprietà idrologiche (valori volumetrici)
    public double DUL { get; set; }          // Drained Upper Limit (field capacity), m³/m³
    public double LL15 { get; set; }         // Lower Limit at 15 bar (wilting point), m³/m³
    public double SAT { get; set; }          // Saturazione, m³/m³
    public double SW { get; set; }           // Contenuto idrico iniziale, m³/m³ (default = DUL)

    // Proprietà chimiche
    public double OC { get; set; }           // Carbonio organico, %
    public double BD { get; set; }           // Bulk density, g/cm³
    public double PH { get; set; }           // pH in acqua

    // Azoto
    public double NO3 { get; set; } = 1.0;  // Nitrato iniziale, kg/ha
    public double NH4 { get; set; } = 0.1;  // Ammonio iniziale, kg/ha
}

/// <summary>
/// Profilo suolo completo associato a una cella.
/// Una cella può avere più profili (variabilità spaziale sub-cella).
/// L'unità di simulazione è GridCell × SoilProfile.
/// </summary>
public class SoilProfile
{
    public string ProfileId { get; set; }
    public string CellId { get; set; }
    public string SoilType { get; set; }
    public string DataSource { get; set; }  // Es. "ISRIC SoilGrids", "ARPAE", "experimental"

    /// <summary>Peso per aggregazione risultati a scala cella (0-1, somma = 1 per cella).</summary>
    public double AreaFraction { get; set; } = 1.0;

    public List<SoilLayer> Layers { get; set; } = new();
}
