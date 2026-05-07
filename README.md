# Spacing — CarbonFarming Italia

Piattaforma di simulazione agronomica per la stima del sequestro di carbonio in sistemi agricoli italiani. Utilizza [APSIM Next Generation](https://www.apsim.info/) come motore di simulazione e un algoritmo Multi-Start Simplex per la calibrazione dei parametri colturali.

## Struttura del repository

```
spacing/
└── apsim/
    ├── apsim source/          # Sorgente APSIM Next Generation (destinato a diventare git submodule)
    │   ├── APSIM.Core/
    │   ├── APSIM.Shared/
    │   ├── DeepCloner.Core/
    │   └── Models/
    ├── src/
    │   ├── Spacing.Core/      # Libreria principale: adattatori, dominio, simulazione, calibrazione
    │   ├── Spacing.Tool1/     # Tool batch: simulazioni su griglia nazionale
    │   ├── Spacing.Tool2/     # Tool on-demand: calibrazione + simulazione per coordinata
    │   ├── runner/            # Runner APSIM legacy (Breath)
    │   ├── optimizer/         # Implementazione Multi-Start Simplex
    │   └── source/            # Utilities e funzioni di supporto
    ├── examples/              # Esempi di file di richiesta (.json)
    ├── spacing.config.example.json   # Template configurazione (copiare in spacing.config.json)
    ├── spacing.sln            # Solution Visual Studio
    └── TR_3745_2825_2020.met  # File meteo di esempio (formato APSIM)
```

## Prerequisiti

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8)
- Visual Studio 2022+ oppure Rider / VS Code con estensione C#
- APSIM Next Generation runtime (incluso nel sorgente sotto `apsim source/`)
- API backend Spacing in esecuzione (oppure usare `--mock` per lo sviluppo locale)

## Setup rapido

```bash
# 1. Clonare il repo
git clone <url> && cd spacing

# 2. Copiare e personalizzare la configurazione
cp apsim/spacing.config.example.json apsim/spacing.config.json
# Editare spacing.config.json con DbApiBaseUrl, DbApiKey, path locali, ecc.

# 3. Compilare la solution
cd apsim
dotnet build spacing.sln
```

## Utilizzo

### Tool 1 — Simulazioni batch su griglia italiana

Esegue simulazioni su tutte le celle di una regione NUTS2 (o tutta Italia) per un insieme di scenari di rotazione predefiniti.

```bash
cd apsim
dotnet run --project src/Spacing.Tool1 -- \
  --config spacing.config.json \
  --nuts2 ITC4 \          # Lombardia; omettere per tutta Italia
  --mock                  # usa dati fittizi (sviluppo)
```

Output: `<OutputDir>/tool1_results.csv` con resa per unità di simulazione (cella × suolo × scenario).

### Tool 2 — Calibrazione e simulazione on-demand

Esegue la simulazione per una singola coordinata, con calibrazione opzionale dei parametri colturali tramite Multi-Start Simplex.

```bash
# Simulazione diretta
dotnet run --project src/Spacing.Tool2 -- \
  --lat 45.4 --lon 9.2 \
  --rotation "Maize" \
  --mock

# Con calibrazione su osservazioni proprie
dotnet run --project src/Spacing.Tool2 -- \
  --lat 45.4 --lon 9.2 \
  --rotation "Maize,Wheat" \
  --calibrate \
  --observations miei_dati.csv \  # Province,Crop,Year,DOY,LAI,Yield
  --yield-weight 0.6 \
  --lai-weight   0.4 \
  --simplexes    30

# Da file JSON di richiesta
dotnet run --project src/Spacing.Tool2 -- \
  --request examples/rotation_maize_irrigated.json \
  --calibrate \
  --mock
```

Output JSON su stdout con resa, biomassa, parametri calibrati e path dei CSV di confronto.

### Parametri di calibrazione

I range e i valori iniziali dei parametri da calibrare si trovano in `src/Spacing.Tool2/calibration_params.json`. Modificare questo file per aggiungere colture o cambiare i range senza ricompilare.

Colture attualmente supportate: **Maize**, **Sorghum**, **Wheat**, **Barley**, **Soybean**.

## Configurazione (`spacing.config.json`)

| Chiave | Descrizione | Default di esempio |
|---|---|---|
| `DbApiBaseUrl` | URL base del backend Spacing | `http://localhost:8000/api/v1` |
| `DbApiKey` | Chiave API per l'autenticazione | — |
| `TempWeatherDir` | Directory per i file `.met` temporanei | `/tmp/spacing/weather` |
| `OutputDir` | Directory per i risultati delle simulazioni | `/tmp/spacing/output` |
| `StartYear` / `EndYear` | Periodo di simulazione | `1990` / `2023` |
| `MaxParallelism` | Simulazioni APSIM in parallelo (Tool 1) | `4` |

> **Nota:** `spacing.config.json` è escluso da git (contiene la chiave API). Usare sempre `spacing.config.example.json` come template.

## Dipendenze APSIM

Il sorgente APSIM Next Generation è incluso in `apsim/apsim source/` come copia diretta. In una versione futura del repository sarà referenziato come git submodule puntando a `ApsimNG/ApsimX`.

## Licenza

Questo progetto è proprietario — © Spacing / CarbonFarming-IT. Il codice APSIM Next Generation incluso è soggetto alla [licenza APSIM](https://www.apsim.info/apsim-model-terms-and-conditions/).
