
using System.Net.Http.Json;
using PropaneDriver.Client.HelperClasses;
using PropaneDriver.Shared.Dtos;

namespace PropaneDriver.Client.ApiConsumers
{
    public class DeliveryTimeApiConsumer
    {
        private readonly HttpClient _http;

        public DeliveryTimeApiConsumer(HttpClient http)
        {
            _http = http;
        }

        public async Task<SaveDeliveryTimeResult> SaveDeliveryTimeAsync(DeliveryTimeApiDto deliveryTimeData)
        {
            try
            {
                var response = await _http.PostAsJsonAsync("api/delivery-times", deliveryTimeData);

                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync();
                    var msg = $"Server returned {(int)response.StatusCode} {response.ReasonPhrase}: {body}";
                    Console.WriteLine($"Failed to save delivery time: {msg}");

                    await ErrorLogApiConsumer.LogErrorAsync(
                        "DeliveryTimeApiService.SaveDeliveryTimeAsync",
                        $"DeliveryId={deliveryTimeData.DeliveryId} AddressId={deliveryTimeData.AddressId}: {msg}");

                    return new SaveDeliveryTimeResult { Success = false, ErrorMessage = msg };
                }

                return new SaveDeliveryTimeResult { Success = true };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to save delivery time: {ex.Message}");

                await ErrorLogApiConsumer.LogErrorAsync(
                    "DeliveryTimeApiService.SaveDeliveryTimeAsync",
                    $"Exception saving delivery time DeliveryId={deliveryTimeData.DeliveryId} AddressId={deliveryTimeData.AddressId}: {ex.Message}");

                return new SaveDeliveryTimeResult { Success = false, ErrorMessage = ex.Message };
            }
        }

        public async Task<DeliveryAverageResult> GetAverageTimeAsync(Guid addressId)
        {
            try
            {
                var result = await _http.GetFromJsonAsync<DeliveryAverageResult>($"api/delivery-times/average?addressId={addressId}");
                return result ?? new DeliveryAverageResult();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to get average time: {ex.Message}");
                return new DeliveryAverageResult();
            }
        }
    }
}
