
namespace PropaneDriver.Shared.Enums
{
    public enum ProductType
    {
        Propane = 0,
        FuelOil = 1
    }

    public static class ProductTypeExtensions
    {
        public static string ToDisplayName(this ProductType productType) => productType switch
        {
            ProductType.FuelOil => "Fuel Oil",
            _ => productType.ToString()
        };
    }
}
