namespace Spacing.Core.Api.Dto;

// DTO che rispecchiano esattamente la struttura JSON dell'API Django/DRF

public class CellDto
{
    public string id { get; set; }
    public double lat { get; set; }
    public double lon { get; set; }
    public string nuts2 { get; set; }
    public string nuts3 { get; set; }
}

public class CellsResponseDto
{
    public int count { get; set; }
    public List<CellDto> results { get; set; } = new();
}

public class WeatherMetaDto
{
    public string radn_unit { get; set; } = "MJ/m2/day";
    public string vp_unit { get; set; } = "hPa";
    public string temp_unit { get; set; } = "C";
    public string rain_unit { get; set; } = "mm/day";
}

public class WeatherRecordDto
{
    public string date { get; set; }
    public double tmax { get; set; }
    public double tmin { get; set; }
    public double rain { get; set; }
    public double radn { get; set; }
    public double wind { get; set; }
    public double vp { get; set; }
}

public class WeatherSeriesDto
{
    public string cell_id { get; set; }
    public double lat { get; set; }
    public double lon { get; set; }
    public WeatherMetaDto meta { get; set; } = new();
    public List<WeatherRecordDto> data { get; set; } = new();
}

public class SoilLayerDto
{
    public double depth_top { get; set; }
    public double depth_bottom { get; set; }
    public double sand { get; set; }
    public double silt { get; set; }
    public double clay { get; set; }
    public double dul { get; set; }
    public double ll15 { get; set; }
    public double sat { get; set; }
    public double oc { get; set; }
    public double bd { get; set; }
    public double ph { get; set; }
}

public class SoilProfileDto
{
    public string profile_id { get; set; }
    public string cell_id { get; set; }
    public string soil_type { get; set; }
    public string data_source { get; set; }
    public double area_fraction { get; set; } = 1.0;
    public List<SoilLayerDto> layers { get; set; } = new();
}

public class SoilsResponseDto
{
    public string cell_id { get; set; }
    public List<SoilProfileDto> profiles { get; set; } = new();
}
