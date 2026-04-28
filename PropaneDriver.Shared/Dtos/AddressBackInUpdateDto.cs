namespace PropaneDriver.Shared.Dtos
{
    // Request body for PUT api/addresses/{id}/back-in. Toggles the
    // BackIn flag on an Address row so the driver client can warn the
    // driver that the truck needs to back into this driveway.
    public class AddressBackInUpdateDto
    {
        public bool BackIn { get; set; }
    }
}
