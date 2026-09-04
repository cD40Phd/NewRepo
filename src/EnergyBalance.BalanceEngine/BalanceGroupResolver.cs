using EnergyBalance.Domain.Entities;

namespace EnergyBalance.BalanceEngine;

/// <summary>
/// Работа с деревом балансовых групп: топологическая сортировка (для расчёта «снизу вверх»)
/// и вычисление «эффективного» состава группы — собственных точек учёта плюс унаследованных
/// от всех дочерних групп (расчёт по группе верхнего уровня автоматически включает все
/// подчинённые фидеры/подстанции).
/// </summary>
public class BalanceGroupResolver
{
    /// <summary>
    /// Возвращает группы в порядке, при котором дочерняя группа всегда предшествует своему родителю
    /// (порядок, пригодный для последовательного расчёта «снизу вверх»).
    /// </summary>
    public IReadOnlyList<BalanceGroup> GetBottomUpOrder(IReadOnlyCollection<BalanceGroup> allGroups)
    {
        var byParent = allGroups
            .Where(g => g.ParentGroupId.HasValue)
            .GroupBy(g => g.ParentGroupId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        var result = new List<BalanceGroup>();
        var visited = new HashSet<int>();

        void Visit(BalanceGroup group)
        {
            if (!visited.Add(group.Id))
            {
                return;
            }

            if (byParent.TryGetValue(group.Id, out var children))
            {
                foreach (var child in children)
                {
                    Visit(child);
                }
            }

            result.Add(group);
        }

        foreach (var group in allGroups)
        {
            Visit(group);
        }

        return result;
    }

    /// <summary>
    /// Возвращает состав участников группы с учётом всех вложенных (дочерних) групп.
    /// </summary>
    public IReadOnlyList<BalanceGroupMember> GetEffectiveMembers(BalanceGroup group, IReadOnlyCollection<BalanceGroup> allGroups)
    {
        var result = new List<BalanceGroupMember>(group.Members);

        var children = allGroups.Where(g => g.ParentGroupId == group.Id);
        foreach (var child in children)
        {
            result.AddRange(GetEffectiveMembers(child, allGroups));
        }

        return result;
    }
}
