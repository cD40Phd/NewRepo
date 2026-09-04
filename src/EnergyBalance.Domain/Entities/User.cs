namespace EnergyBalance.Domain.Entities;

/// <summary>Роль пользователя системы (RBAC).</summary>
public enum UserRole
{
    Administrator = 1,
    MeteringEngineer = 2,
    Dispatcher = 3,
    ReadOnly = 4,
}

/// <summary>Учётная запись пользователя Web-интерфейса.</summary>
public class User
{
    public int Id { get; set; }

    public string Login { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Хэш пароля (PBKDF2/BCrypt) — пароль в открытом виде никогда не хранится.</summary>
    public string PasswordHash { get; set; } = string.Empty;

    public UserRole Role { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; }
    public DateTime? LastLoginAtUtc { get; set; }
}
