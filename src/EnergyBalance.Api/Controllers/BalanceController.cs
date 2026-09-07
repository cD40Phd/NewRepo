using EnergyBalance.Api.Dto;
using EnergyBalance.Application;
using EnergyBalance.DataAccess;
using EnergyBalance.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnergyBalance.Api.Controllers;

[ApiController]
[Route("api/balance-groups")]
public class BalanceController : ControllerBase
{
    private readonly EnergyBalanceDbContext _db;
    private readonly BalanceCalculationOrchestrator _orchestrator;

    public BalanceController(EnergyBalanceDbContext db, BalanceCalculationOrchestrator orchestrator)
    {
        _db = db;
        _orchestrator = orchestrator;
    }

    [HttpGet("{groupId:int}/results")]
    public async Task<ActionResult<IReadOnlyList<BalanceCalculationResultDto>>> GetResults(
        int groupId,
        [FromQuery] DateTime? fromUtc,
        [FromQuery] DateTime? toUtc,
        CancellationToken ct)
    {
        var query = _db.BalanceCalculationResults
            .Include(r => r.BalanceGroup)
            .Where(r => r.BalanceGroupId == groupId);

        if (fromUtc is not null)
        {
            query = query.Where(r => r.PeriodStartUtc >= fromUtc);
        }

        if (toUtc is not null)
        {
            query = query.Where(r => r.PeriodEndUtc <= toUtc);
        }

        var results = await query
            .OrderByDescending(r => r.PeriodStartUtc)
            .ThenByDescending(r => r.Version)
            .Take(500)
            .ToListAsync(ct);

        return Ok(results.Select(ToDto).ToList());
    }

    /// <summary>Запускает внеплановый пересчёт баланса за указанный период по всем балансовым группам.</summary>
    [HttpPost("calculate")]
    public async Task<ActionResult<IReadOnlyList<BalanceCalculationResultDto>>> Calculate([FromBody] CalculateBalanceRequestDto request, CancellationToken ct)
    {
        if (request.PeriodEndUtc <= request.PeriodStartUtc)
        {
            return BadRequest("PeriodEndUtc должен быть позже PeriodStartUtc.");
        }

        var calculatedBy = User.Identity?.Name ?? "api-user";
        var results = await _orchestrator.CalculateAllAsync(request.PeriodStartUtc, request.PeriodEndUtc, calculatedBy, ct);

        return Ok(results.Select(ToDto).ToList());
    }

    private static BalanceCalculationResultDto ToDto(BalanceCalculationResult r) => new(
        r.Id,
        r.BalanceGroupId,
        r.BalanceGroup?.Name ?? string.Empty,
        r.PeriodStartUtc,
        r.PeriodEndUtc,
        r.Version,
        r.SupplyEnergyKwh,
        r.ConsumptionEnergyKwh,
        r.OwnNeedsEnergyKwh,
        r.NormativeLossKwh,
        r.ActualLossKwh,
        r.ImbalanceKwh,
        r.ImbalancePercent,
        r.Status.ToString(),
        r.MeteringPointsCount,
        r.MeteringPointsWithIssuesCount,
        r.CalculatedAtUtc,
        r.CalculatedBy,
        r.IsApproved);
}
