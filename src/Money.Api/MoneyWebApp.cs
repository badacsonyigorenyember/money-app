using Money.Api.Endpoints;
using Money.Application.Recurring;
using Money.Infrastructure.Persistence;

namespace Money.Api;

/// <summary>
/// The whole web app in one place so both entry points - <c>dotnet run</c> on Money.Api and the
/// WebView2 host in Money.Desktop - boot the identical pipeline.
/// </summary>
public static class MoneyWebApp
{
    private const string ServerFlag = "--server";

    /// <summary>
    /// The arguments configuration is allowed to see. <c>--server</c> is ours, and the command-line
    /// configuration provider has no notion of a valueless flag: it reads
    /// <c>--server --urls http://127.0.0.1:5099</c> as the key <c>--server</c> with the value
    /// <c>--urls</c>, so the real address pair vanishes and Kestrel silently falls back to its
    /// default port. Strip the flag here, once, rather than in each entry point.
    /// </summary>
    public static string[] ConfigurationArgs(string[] args) =>
        [.. args.Where(arg => !ServerFlag.Equals(arg, StringComparison.OrdinalIgnoreCase))];

    /// <param name="options">
    /// Money.Desktop is the entry assembly when hosted in WebView2, so it has to name Money.Api
    /// explicitly - that is where the compiled Razor Pages live - and pin the content root to the
    /// exe folder rather than whatever directory the shortcut happened to launch from.
    /// </param>
    public static async Task<WebApplication> CreateAsync(WebApplicationOptions options)
    {
        var args = options.Args ?? [];

        var mode = args.Contains(ServerFlag, StringComparer.OrdinalIgnoreCase)
            ? HostingMode.Server
            : HostingMode.Desktop;

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = ConfigurationArgs(args),
            EnvironmentName = options.EnvironmentName,
            ApplicationName = options.ApplicationName,
            ContentRootPath = options.ContentRootPath,
            WebRootPath = options.WebRootPath
        });

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

            // Recurring rules catch up at startup and again whenever the transactions screen is
            // opened (spec 5.8). Both are safe to repeat: a run with nothing due writes nothing.
            await scope.ServiceProvider.GetRequiredService<RecurringMaterialiser>()
                                       .RunAsync(CancellationToken.None);
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
        api.MapImportEndpoints();
        api.MapRecurringEndpoints();

        app.MapRazorPages();

        return app;
    }
}
