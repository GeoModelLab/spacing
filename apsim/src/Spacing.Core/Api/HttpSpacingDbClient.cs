using System.Net.Http.Json;
using Newtonsoft.Json;
using Spacing.Core.Api.Dto;
using Spacing.Core.Domain;

namespace Spacing.Core.Api;

/// <summary>
/// Client HTTP che interroga la REST API del database Spacing (Django/DRF).
/// Autenticazione tramite header X-Api-Key.
/// </summary>
public class HttpSpacingDbClient : ISpacingDbClient
{
    private readonly HttpClient _http;

    public HttpSpacingDbClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<IEnumerable<GridCell>> GetCellsAsync(
        string nuts2 = null,
        CancellationToken cancellationToken = default)
    {
        var url = "cells/";
        if (!string.IsNullOrEmpty(nuts2))
            url += $"?nuts2={nuts2}";

        var response = await _http.GetStringAsync(url, cancellationToken);
        var dto = JsonConvert.DeserializeObject<CellsResponseDto>(response);
        return dto.results.Select(DtoMapper.ToGridCell);
    }

    public async Task<WeatherSeries> GetWeatherAsync(
        string cellId,
        DateOnly start,
        DateOnly end,
        CancellationToken cancellationToken = default)
    {
        var url = $"weather/{cellId}/?start={start:yyyy-MM-dd}&end={end:yyyy-MM-dd}&format=daily";
        var response = await _http.GetStringAsync(url, cancellationToken);
        var dto = JsonConvert.DeserializeObject<WeatherSeriesDto>(response);
        return DtoMapper.ToWeatherSeries(dto);
    }

    public async Task<IEnumerable<SoilProfile>> GetSoilProfilesAsync(
        string cellId,
        CancellationToken cancellationToken = default)
    {
        var url = $"soils/{cellId}/";
        var response = await _http.GetStringAsync(url, cancellationToken);
        var dto = JsonConvert.DeserializeObject<SoilsResponseDto>(response);
        return dto.profiles.Select(DtoMapper.ToSoilProfile);
    }
}
