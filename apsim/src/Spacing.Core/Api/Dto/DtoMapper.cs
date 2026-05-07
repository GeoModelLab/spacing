using Spacing.Core.Domain;

namespace Spacing.Core.Api.Dto;

/// <summary>
/// Converte i DTO della REST API nei domain model di Spacing.Core.
/// </summary>
public static class DtoMapper
{
    public static GridCell ToGridCell(CellDto dto) => new()
    {
        Id    = dto.id,
        Lat   = dto.lat,
        Lon   = dto.lon,
        Nuts2 = dto.nuts2,
        Nuts3 = dto.nuts3
    };

    public static WeatherSeries ToWeatherSeries(WeatherSeriesDto dto) => new()
    {
        CellId = dto.cell_id,
        Lat    = dto.lat,
        Lon    = dto.lon,
        Meta   = new WeatherSeriesMeta
        {
            RadnUnit = dto.meta?.radn_unit ?? "MJ/m2/day",
            VpUnit   = dto.meta?.vp_unit   ?? "hPa",
            TempUnit = dto.meta?.temp_unit ?? "C",
            RainUnit = dto.meta?.rain_unit ?? "mm/day"
        },
        Data = dto.data.Select(r => new WeatherRecord
        {
            Date = DateOnly.Parse(r.date),
            Tmax = r.tmax,
            Tmin = r.tmin,
            Rain = r.rain,
            Radn = r.radn,
            Wind = r.wind,
            Vp   = r.vp
        }).ToList()
    };

    public static SoilProfile ToSoilProfile(SoilProfileDto dto) => new()
    {
        ProfileId    = dto.profile_id,
        CellId       = dto.cell_id,
        SoilType     = dto.soil_type,
        DataSource   = dto.data_source,
        AreaFraction = dto.area_fraction,
        Layers       = dto.layers.Select(l => new SoilLayer
        {
            DepthTop    = l.depth_top,
            DepthBottom = l.depth_bottom,
            Sand        = l.sand,
            Silt        = l.silt,
            Clay        = l.clay,
            DUL         = l.dul,
            LL15        = l.ll15,
            SAT         = l.sat,
            SW          = l.dul,   // inizializzato a field capacity
            OC          = l.oc,
            BD          = l.bd,
            PH          = l.ph
        }).ToList()
    };
}
