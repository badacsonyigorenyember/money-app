using Money.Api;
using Money.Api.Endpoints;
using Money.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

var mode = args.Contains("--server", StringComparer.OrdinalIgnoreCase)
    ? HostingMode.Server
    : HostingMode.Desktop;

builder.Services.AddMoneyApp(builder.Configuration, mode);
builder.Services.AddRazorPages();
// HTMX sends the antiforgery token as a header (see Pages/Shared/_Layout.cshtml's hx-headers) for
// the requests that are not a plain <form> post - the Remove button has no enclosing form.
builder.Services.AddAntiforgery(options => options.HeaderName = "RequestVerificationToken");
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi("v1");

var app = builder.Build();

// Migrations run after an automatic pre-migration backup (spec section 8).
using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().InitialiseAsync();
}

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseStaticFiles();

app.MapOpenApi("/openapi/{documentName}.json");
app.MapGet("/health", () => Results.Ok(new { status = "ok" })).ExcludeFromDescription();

var api = app.MapGroup("/api/v1");
api.MapAccountEndpoints();
api.MapCategoryEndpoints();
api.MapTransactionEndpoints();
api.MapSettingsEndpoints();
api.MapAdminEndpoints();

app.MapRazorPages();

await app.RunAsync();

/// <summary>Exposed so WebApplicationFactory&lt;Program&gt; can host the app in tests.</summary>
public partial class Program;
