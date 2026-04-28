using PropaneDriver.Shared.Dtos;
using PropaneDriver.Shared.Interfaces;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PropaneDriver.Shared.JsonConverters
{
    // System.Text.Json can't deserialize an interface property on its own —
    // it has no way to know which concrete type to instantiate. Until there's
    // a second IDelivery implementation, every IDelivery on the wire is a
    // PropaneDelivery, so we route reads through that type. When other
    // implementations land, this converter is the place to add a discriminator.
    public class DeliveryJsonConverter : JsonConverter<IDelivery>
    {
        public override IDelivery? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => JsonSerializer.Deserialize<PropaneDelivery>(ref reader, options);

        public override void Write(Utf8JsonWriter writer, IDelivery value, JsonSerializerOptions options)
            => JsonSerializer.Serialize(writer, value, value.GetType(), options);
    }
}
