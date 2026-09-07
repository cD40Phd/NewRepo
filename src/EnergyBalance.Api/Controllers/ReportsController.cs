using System.Globalization;
using System.Text;
using EnergyBalance.DataAccess;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnergyBalance.Api.Controllers;

/// <summary>
/// Экспорт регламентной отчётности. Формат CSV выбран как минимальная зависимость для
/// программного экспорта в 1С/биллинг; экспорт в Excel/PDF по унифицированным формам
/// балансовых актов подключается на следующем этапе (ClosedXML/QuestPDF, см. п. 7.4 ТЗ).
/// </summary>
[ApiController]
[Route("api/reports")]
public class ReportsController : ControllerBase
{
    private readonly EnergyBalanceDbContext _db;

    public ReportsController(EnergyBalanceDbContext db)
    {
        _db = db;
    }

    [HttpGet("balance-summary.csv")]
    public async Task<IActionResult> GetBalanceSummaryCsv([FromQuery] DateTime fromUtc, [FromQuery] DateTime toUtc, CancellationToken ct)
    {
        var candidates = await _db.BalanceCalculationResults
            .Include(r => r.BalanceGroup)
            .Where(r => r.PeriodStartUtc >= fromUtc && r.PeriodEndUtc <= toUtc)
            .ToListAsync(ct);

        // Последняя версия расчёта на группу выбирается на стороне клиента (LINQ-to-Objects),
        // так как группировка с выбором "первой по версии записи в группе" плохо транслируется в SQL.
        var results = candidates
            .GroupBy(r => r.BalanceGroupId)
            .Select(g => g.OrderByDescending(r => r.Version).First())
            .OrderBy(r => r.BalanceGroup?.Name)
            .ToList();

        var csv = new StringBuilder();
        csv.AppendLine("Группа;Код;Период с;Период по;Отпуск, кВт.ч;Потребление, кВт.ч;Собственные нужды, кВт.ч;Норм. потери, кВт.ч;Факт. потери, кВт.ч;Небаланс, кВт.ч;Небаланс, %;Статус;Утверждено");

        foreach (var r in results)
        {
            csv.AppendLine(string.Join(';', new[]
            {
                r.BalanceGroup?.Name ?? string.Empty,
                r.BalanceGroup?.Code ?? string.Empty,
                r.PeriodStartUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                r.PeriodEndUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                r.SupplyEnergyKwh.ToString(CultureInfo.InvariantCulture),
                r.ConsumptionEnergyKwh.ToString(CultureInfo.InvariantCulture),
                r.OwnNeedsEnergyKwh.ToString(CultureInfo.InvariantCulture),
                r.NormativeLossKwh.ToString(CultureInfo.InvariantCulture),
                r.ActualLossKwh.ToString(CultureInfo.InvariantCulture),
                r.ImbalanceKwh.ToString(CultureInfo.InvariantCulture),
                r.ImbalancePercent.ToString(CultureInfo.InvariantCulture),
                r.Status.ToString(),
                r.IsApproved ? "да" : "нет",
            }));
        }

        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
        return File(bytes, "text/csv", $"balance-summary_{fromUtc:yyyyMMdd}_{toUtc:yyyyMMdd}.csv");
    }
}
