using Money.Application.Recurring;
using Money.Infrastructure.Persistence;

namespace Money.Api;

/// <summary>
/// The whole app in one place so both entry points - the WebView2 host in Money.Desktop and the
/// test host - boot the identical pipeline. It serves the Razor Pages the desktop window renders
/// and nothing else: there is no JSON API, because nothing but that window ever calls it.
/// </summary>
public static class MoneyWebApp
{
    /// <param name="options">
    /// Money.Desktop is the entry assembly when hosted in WebView2, so it has to name Money.Api
    /// explicitly - that is where the compiled Razor Pages live - and pin the content root to the
    /// exe folder rather than whatever directory the shortcut happened to launch from.
    /// </param>
    public static async Task<WebApplication> CreateAsync(WebApplicationOptions options)
    {
        var builder = WebApplication.CreateBuilder(options);

        builder.Services.AddMoneyApp();
        builder.Services.AddRazorPages();
        // HTMX sends the antiforgery token as a header (see Pages/Shared/_Layout.cshtml's hx-headers) for
        // the requests that are not a plain <form> post - the Remove button has no enclosing form.
        builder.Services.AddAntiforgery(options => options.HeaderName = "RequestVerificationToken");
        builder.Services.AddProblemDetails();

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

        // The one route that is not a page: CI starts the published exe headless and waits for
        // this before calling the single-file publish good (spec section 14).
        app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

        app.MapRazorPages();

        return app;
    }
}
