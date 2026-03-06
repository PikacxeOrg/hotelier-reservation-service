namespace ReservationService.Domain;

/// <summary>
/// Checks availability-service for date availability and pricing.
/// </summary>
public interface IAvailabilityServiceClient
{
    Task<AvailabilityCheckResult> CheckAvailabilityAsync(Guid accommodationId, DateOnly checkIn, DateOnly checkOut);
}

public class AvailabilityCheckResult
{
    public bool IsAvailable { get; set; }
    public AvailabilityPriceInfo? Price { get; set; }
}

public class AvailabilityPriceInfo
{
    public decimal PricePerNight { get; set; }
    public string PriceType { get; set; } = string.Empty;
    public int Nights { get; set; }
    public decimal TotalPrice { get; set; }
}
