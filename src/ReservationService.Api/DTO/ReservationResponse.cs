using ReservationService.Domain;

namespace ReservationService.Api;

public class ReservationResponse
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid AccommodationId { get; set; }
    public Guid HostId { get; set; }
    public DateTime FromDate { get; set; }
    public DateTime ToDate { get; set; }
    public int NumOfGuests { get; set; }
    public ReservationStatus Status { get; set; }
    public DateTime CreatedTimestamp { get; set; }
}
