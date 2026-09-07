using EnergyBalance.Api.Dto;
using EnergyBalance.DataAccess.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace EnergyBalance.Api.Controllers;

[ApiController]
[Route("api/meters/{meterId:int}")]
public class ReadingsController : ControllerBase
{
    private readonly IReadingRepository _readingRepository;

    public ReadingsController(IReadingRepository readingRepository)
    {
        _readingRepository = readingRepository;
    }

    [HttpGet("readings/latest")]
    public async Task<ActionResult<ReadingDto>> GetLatest(int meterId, CancellationToken ct)
    {
        var reading = await _readingRepository.GetLatestReadingAsync(meterId, ct);
        if (reading is null)
        {
            return NotFound();
        }

        return Ok(new ReadingDto(
            reading.TimestampUtc,
            reading.ActiveEnergyTotal,
            reading.ActiveEnergyExportTotal,
            reading.VoltageA,
            reading.VoltageB,
            reading.VoltageC,
            reading.ActivePower,
            reading.PowerFactor,
            reading.Quality.ToString(),
            reading.TamperFlag));
    }

    [HttpGet("load-profile")]
    public async Task<ActionResult<IReadOnlyList<LoadProfilePointDto>>> GetLoadProfile(
        int meterId,
        [FromQuery] DateTime fromUtc,
        [FromQuery] DateTime toUtc,
        CancellationToken ct)
    {
        if (toUtc <= fromUtc)
        {
            return BadRequest("Параметр toUtc должен быть позже fromUtc.");
        }

        var profile = await _readingRepository.GetProfileAsync(meterId, fromUtc, toUtc, ct);
        var dtos = profile
            .Select(p => new LoadProfilePointDto(p.IntervalStartUtc, p.IntervalMinutes, p.ActiveEnergyDelta, p.ActiveEnergyExportDelta, p.Quality.ToString()))
            .ToList();

        return Ok(dtos);
    }
}
