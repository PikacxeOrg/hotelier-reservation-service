namespace ReservationService.Domain;

/// <summary>
/// Fetches accommodation details from accommodation-service.
/// Needed to resolve HostId and AutoApproval flag for new reservations.
/// </summary>
public interface IAccommodationServiceClient
{
    Task<AccommodationInfo?> GetAccommodationAsync(Guid accommodationId);
}

public class AccommodationInfo
{
    public Guid Id { get; set; }
    public Guid HostId { get; set; }
    public bool AutoApproval { get; set; }
    public int MinGuests { get; set; }
    public int MaxGuests { get; set; }
}
