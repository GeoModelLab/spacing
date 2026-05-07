namespace Spacing.Core.Config;

/// <summary>
/// Configurazione principale del sistema Spacing.
/// Caricata da appsettings.json o da variabili d'ambiente.
/// </summary>
public class SpacingConfig
{
    /// <summary>URL base della REST API del database Spacing (WP2).</summary>
    public string DbApiBaseUrl { get; set; } = "http://localhost:8000/api/v1";

    /// <summary>API key per autenticazione (header X-Api-Key).</summary>
    public string DbApiKey { get; set; } = "";

    /// <summary>Cartella temporanea per i file .met generati da WeatherAdapter.</summary>
    public string TempWeatherDir { get; set; } = Path.Combine(Path.GetTempPath(), "spacing", "weather");

    /// <summary>Cartella dove vengono scritti i risultati delle simulazioni (DataStore SQLite).</summary>
    public string OutputDir { get; set; } = Path.Combine(Path.GetTempPath(), "spacing", "output");

    /// <summary>Anno di inizio delle simulazioni.</summary>
    public int StartYear { get; set; } = 1990;

    /// <summary>Anno di fine delle simulazioni.</summary>
    public int EndYear { get; set; } = 2023;

    /// <summary>Numero di thread paralleli per il batch Tool1.</summary>
    public int MaxParallelism { get; set; } = Environment.ProcessorCount;

    public static SpacingConfig Load(string jsonPath = "spacing.config.json")
    {
        if (!File.Exists(jsonPath))
            return new SpacingConfig();

        var json = File.ReadAllText(jsonPath);
        return Newtonsoft.Json.JsonConvert.DeserializeObject<SpacingConfig>(json) ?? new SpacingConfig();
    }

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(TempWeatherDir);
        Directory.CreateDirectory(OutputDir);
    }
}
