namespace Hotelier.Events;

/// <summary>
/// Consumer-side DTO for UserDeleted.
/// </summary>
public record UserDeleted
{
    public Guid UserId { get; init; }
    public string UserType { get; init; } = string.Empty;
}
