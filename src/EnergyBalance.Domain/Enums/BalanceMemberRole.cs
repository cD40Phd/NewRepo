namespace EnergyBalance.Domain.Enums;

/// <summary>
/// Роль точки учёта в балансовой группе: приход (отпуск в группу) или расход (потребление из группы).
/// </summary>
public enum BalanceMemberRole
{
    /// <summary>Точка учёта отпуска (приход электроэнергии в границы группы).</summary>
    Supply = 1,

    /// <summary>Точка учёта потребления (расход электроэнергии из группы).</summary>
    Consumption = 2,
}

/// <summary>Статус результата расчёта баланса.</summary>
public enum BalanceStatus
{
    /// <summary>Небаланс в пределах допустимого норматива.</summary>
    Normal = 1,

    /// <summary>Небаланс превышает предупредительный порог.</summary>
    Warning = 2,

    /// <summary>Небаланс превышает критический порог — требует расследования.</summary>
    Critical = 3,
}

/// <summary>Признак качества (достоверности) показания.</summary>
public enum ReadingQuality
{
    /// <summary>Показание получено штатно и прошло проверку диапазона/CRC.</summary>
    Good = 1,

    /// <summary>Показание восстановлено интерполяцией/по аналогии из-за пропуска.</summary>
    Restored = 2,

    /// <summary>Показание введено вручную оператором.</summary>
    Manual = 3,

    /// <summary>Показание сомнительно (вне диапазона, «заморожено», признак вмешательства).</summary>
    Suspect = 4,
}

/// <summary>Уровень серьёзности события.</summary>
public enum AlarmSeverity
{
    Info = 1,
    Warning = 2,
    Critical = 3,
}
