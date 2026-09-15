using PropaneDriver.Shared.Dtos;
using PropaneDriver.Shared.Interfaces;

namespace PropaneDriver.Client.Services
{
    public class DeliveryCompletionService
    {
        private const int COMPLETED_STATUS = 2;
        private const double MINIMUM_DELIVERY_SECONDS = 5 * 60;

        private readonly DeliveryTimeApiService _deliveryTimeApi;
        private readonly DeliveryApiService _deliveryApi;

        public event Action<SaveDeliveryTimeResult>? OnSaveResult;
        public event Action<IDelivery, double>? OnDeliveryCompleted;

        public DeliveryCompletionService(DeliveryTimeApiService deliveryTimeApi, DeliveryApiService deliveryApi)
        {
            _deliveryTimeApi = deliveryTimeApi;
            _deliveryApi = deliveryApi;
        }

        public async Task<bool> CompleteDeliveryAsync(IDelivery? delivery, double rawElapsedSeconds, bool enforceMinimumDuration = true)
        {
            if (delivery is null)
            {
                await ErrorLogService.LogErrorAsync(
                    "DeliveryCompletionService.CompleteAsync", "delivery is null");
                return false;
            }

            if (delivery.Address is null)
            {
                await ErrorLogService.LogErrorAsync(
                    "DeliveryCompletionService.CompleteAsync", $"delivery.Address is null");
                return false;
            }

            if (delivery.Address!.Id == Guid.Empty)
            {
                await ErrorLogService.LogErrorAsync(
                    "DeliveryCompletionService.CompleteAsync",
                    $"Delivery '{delivery.Id}' has no AddressId — cannot save time");
                return false;
            }

            if (rawElapsedSeconds <= 0)
            {
                await ErrorLogService.LogErrorAsync(
                    "DeliveryCompletionService.CompleteAsync", "rawElapsedSeconds <= 0");
                return false;
            }

            if (enforceMinimumDuration && rawElapsedSeconds < MINIMUM_DELIVERY_SECONDS)
            {
                await ErrorLogService.LogErrorAsync(
                    "DeliveryCompletionService.CompleteAsync",
                    $"Ignoring {rawElapsedSeconds:F0}s stop for delivery '{delivery.Id}' — " +
                    "under the 5-minute minimum, likely a fence blip");
                return false;
            }

            await SaveDeliveryTimeAsync(delivery, rawElapsedSeconds);

            if (delivery.Status != COMPLETED_STATUS)
            {
                await MarkCompleteAsync(delivery);
                OnDeliveryCompleted?.Invoke(delivery, rawElapsedSeconds);
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
