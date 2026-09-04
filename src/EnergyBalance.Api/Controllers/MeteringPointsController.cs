using EnergyBalance.Api.Dto;
using EnergyBalance.DataAccess.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace EnergyBalance.Api.Controllers;

[ApiController]
[Route("api/metering-points")]
public class MeteringPointsController : ControllerBase
{
    private readonly IMeteringPointRepository _repository;

    public MeteringPointsController(IMeteringPointRepository repository)
    {
        _repository = repository;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<MeteringPointDto>>> GetAll(CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var points = await _repository.GetAllWithActiveMetersAsync(ct);

        var dtos = points.Select(mp =>
        {
            var meter = mp.GetActiveMeter(today);
            return new MeteringPointDto(
                mp.Id,
                mp.Code,
                mp.Name,
                mp.Feeder?.Name,
                mp.TransformationRatio,
                meter?.ModelName,
                meter?.SerialNumber);
        }).ToList();

        return Ok(dtos);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<MeteringPointDto>> GetById(int id, CancellationToken ct)
    {
        var mp = await _repository.GetByIdAsync(id, ct);
        if (mp is null)
        {
            return NotFound();
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var meter = mp.GetActiveMeter(today);

        return Ok(new MeteringPointDto(mp.Id, mp.Code, mp.Name, mp.Feeder?.Name, mp.TransformationRatio, meter?.ModelName, meter?.SerialNumber));
    }
}
