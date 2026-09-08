using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Web.WebView2.WinForms;
using Money.Api;
using WinForms = System.Windows.Forms;
using Money.Infrastructure.Persistence;

namespace Money.Desktop;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var dataDirectory = DataDirectory.Resolve(
            Environment.GetEnvironmentVariable(DataDirectory.EnvironmentVariable),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));

        // Without this WebView2 writes its cache beside the exe, which may sit in a read-only or
        // OneDrive-synced folder.
        Environment.SetEnvironmentVariable(
            "WEBVIEW2_USER_DATA_FOLDER", System.IO.Path.Combine(dataDirectory, "webview2"));

        var app = MoneyWebApp.CreateAsync(new WebApplicationOptions
        {
            Args = args,
            ApplicationName = typeof(MoneyWebApp).Assembly.GetName().Name,
            ContentRootPath = AppContext.BaseDirectory
        }).GetAwaiter().GetResult();

        // Loopback only, on whatever port the OS hands out: never reachable from the network,
        // never fighting another instance for a fixed port.
        app.Urls.Clear();
        app.Urls.Add("http://127.0.0.1:0");
        app.StartAsync().GetAwaiter().GetResult();

        var url = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses.First();

        WinForms.Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        using var form = new Form
        {
            Text = "Money",
            Width = 1280,
            Height = 860,
            StartPosition = FormStartPosition.CenterScreen,
            Controls = { new WebView2 { Dock = DockStyle.Fill, Source = new Uri(url) } }
        };
        WinForms.Application.Run(form);

        app.StopAsync().GetAwaiter().GetResult();
    }
}
