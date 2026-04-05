using PropaneDriver.Shared.Dtos;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddRazorPages();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}
else
{
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseBlazorFrameworkFiles();
app.UseStaticFiles();
app.UseRouting();

app.MapRazorPages();
app.MapControllers();

// Stub auth endpoint - TODO: implement real authentication
app.MapPost("api/Authenticate", (CredsDto creds) =>
{
    return Results.Ok(new AuthResponseDto
    {
        IsAuthenticated = true,
        Role = 1,
        UserId = Guid.NewGuid(),
        StatusMessage = "Stub - implement real auth"
    });
});

// Stub driver endpoint - TODO: implement real driver lookup
app.MapGet("driver/{id:guid}", (Guid id) =>
{
    return Results.Ok(new DriverDto
    {
        Id = id.ToString(),
        UserName = "stub_driver",
        Role = "driver",
        FirstName = "Stub",
        LastName = "Driver"
    });
});

app.MapFallbackToFile("index.html");

app.Run();
