namespace Hotelier.Events;

/// <summary>
/// Consumer-side DTO for AccommodationDeleted.
/// </summary>
public record AccommodationDeleted
{
    public Guid AccommodationId { get; init; }
    public Guid HostId { get; init; }
}
