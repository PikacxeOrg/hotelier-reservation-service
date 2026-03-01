using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.Extensions.Logging;

using ReservationService.Domain;

namespace ReservationService.Infrastructure;

public class AccommodationServiceClient(
    HttpClient httpClient,
    ILogger<AccommodationServiceClient> logger)
    : IAccommodationServiceClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<AccommodationInfo?> GetAccommodationAsync(Guid accommodationId)
    {
        try
        {
            var url = $"/api/accommodation/{accommodationId}";
            logger.LogDebug("Fetching accommodation: {Url}", url);

            var response = await httpClient.GetAsync(url);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return null;

            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<AccommodationInfo>(JsonOptions);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not fetch accommodation {Id}", accommodationId);
            return null;
        }
    }
}
