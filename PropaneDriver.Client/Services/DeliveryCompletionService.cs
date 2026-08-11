using PropaneDriver.Shared.Dtos;
using PropaneDriver.Shared.Interfaces;

namespace PropaneDriver.Client.Services
{
    // Finishes a delivery once its elapsed time is known: persists the
    // DeliveryTime row, marks the delivery complete, and announces both.
    // Takes elapsed seconds as a parameter so it doesn't care whether the
    // geofence or the driver timed the stop.
    public class DeliveryCompletionService
    {
        private const int COMPLETED_STATUS = 2;

        // Floor on recorded time. Stops shorter than this skew the
        // address-average stats that drive route estimates.
        private const double MINIMUM_DELIVERY_SECONDS = 5 * 60;

        private readonly DeliveryTimeApiService _deliveryTimeApi;
        private readonly DeliveryApiService _deliveryApi;

        public event Action<SaveDeliveryTimeResult>? OnSaveResult;
        public event Action<IDelivery, double>? OnDeliveryCompleted;

        public DeliveryCompletionService(
            DeliveryTimeApiService deliveryTimeApi,
            DeliveryApiService deliveryApi)
        {
            _deliveryTimeApi = deliveryTimeApi;
            _deliveryApi = deliveryApi;
        }

        // Returns true when the delivery ended up complete, so callers can tell
        // a finished stop from one that needs another attempt.
        public async Task<bool> CompleteAsync(IDelivery? delivery, double rawElapsedSeconds)
        {
            if (delivery is null)
            {
                await ErrorLogService.LogErrorAsync(
                    "DeliveryCompletionService.CompleteAsync", "delivery is null");
                return false;
            }

            if (rawElapsedSeconds <= 0)
            {
                await ErrorLogService.LogErrorAsync(
                    "DeliveryCompletionService.CompleteAsync", "rawElapsedSeconds <= 0");
                return false;
            }

            // No address row means no stats to attach the time to. Bail before
            // marking complete so the stop can be retried.
            if (delivery.Address.Id == Guid.Empty)
            {
                await ErrorLogService.LogErrorAsync(
                    "DeliveryCompletionService.CompleteAsync",
                    $"Delivery '{delivery.Id}' has no AddressId — cannot save time");
                return false;
            }

            var elapsedSeconds = Math.Max(rawElapsedSeconds, MINIMUM_DELIVERY_SECONDS);

            await SaveDeliveryTimeAsync(delivery, elapsedSeconds);

            if (delivery.Status != COMPLETED_STATUS)
            {
                await MarkCompleteAsync(delivery);
                OnDeliveryCompleted?.Invoke(delivery, elapsedSeconds);
            }

            return true;
        }

        private async Task SaveDeliveryTimeAsync(IDelivery delivery, double elapsedSeconds)
        {
            var deliveryTime = new DeliveryTimeDto
            {
                DeliveryId = delivery.Id,
                AddressId = delivery.Address.Id,
                TimeIntervalSeconds = elapsedSeconds
            };

            try
            {
                var saveResult = await _deliveryTimeApi.SaveDeliveryTimeAsync(deliveryTime);
                OnSaveResult?.Invoke(saveResult);
            }
            catch (Exception ex)
            {
                await ErrorLogService.LogErrorAsync(
                    "DeliveryCompletionService.SaveDeliveryTimeAsync", $"Saving delivery time failed: {ex.Message}");
                OnSaveResult?.Invoke(new SaveDeliveryTimeResult { Success = false, ErrorMessage = ex.Message });
            }
        }

        private async Task MarkCompleteAsync(IDelivery delivery)
        {
            delivery.Status = COMPLETED_STATUS;

            try
            {
                await _deliveryApi.UpdateStatusAsync(delivery.Id, COMPLETED_STATUS);
            }
            catch (Exception ex)
            {
                await ErrorLogService.LogErrorAsync(
                    "DeliveryCompletionService.MarkCompleteAsync", $"Updating delivery status failed: {ex.Message}");
            }
        }
    }
}
