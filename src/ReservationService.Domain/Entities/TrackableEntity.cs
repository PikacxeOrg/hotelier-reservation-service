namespace ReservationService.Domain;

public abstract class TrackableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string? CreatedBy { get; set; }
    public string? ModifiedBy { get; set; }
    public DateTime CreatedTimestamp { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedTimestamp { get; set; } = DateTime.UtcNow;
}
