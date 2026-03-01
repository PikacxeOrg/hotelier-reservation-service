using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.Extensions.Logging;

using ReservationService.Domain;

namespace ReservationService.Infrastructure;

public class AvailabilityServiceClient(
    HttpClient httpClient,
    ILogger<AvailabilityServiceClient> logger)
    : IAvailabilityServiceClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<AvailabilityCheckResult> CheckAvailabilityAsync(
        Guid accommodationId, DateTime checkIn, DateTime checkOut)
    {
        try
        {
            var url = $"/api/availability/internal/check" +
                      $"?accommodationId={accommodationId}" +
                      $"&checkIn={checkIn:O}" +
                      $"&checkOut={checkOut:O}";

            logger.LogDebug("Checking availability: {Url}", url);

            var response = await httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadFromJsonAsync<AvailabilityCheckResult>(JsonOptions)
                   ?? new AvailabilityCheckResult { IsAvailable = false };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Could not reach availability-service. Allowing reservation creation as fallback.");
            // Fail-open: allow reservation to be created; host can reject manually
            return new AvailabilityCheckResult { IsAvailable = true };
        }
    }
}
