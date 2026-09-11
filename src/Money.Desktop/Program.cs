using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Money.Api;
using Money.Application.Settings;
using Money.Infrastructure.Persistence;
using Rectangle = System.Drawing.Rectangle;
using WinForms = System.Windows.Forms;

namespace Money.Desktop;

internal static class Program
{
    private const string WebView2DownloadUrl = "https://developer.microsoft.com/microsoft-edge/webview2/";

    private const string HeadlessFlag = "--headless";

    [STAThread]
    private static int Main(string[] args)
    {
        // A WinExe has no console and no default error dialog, so anything unhandled exits
        // silently: no window, no message, nothing to report. Every failure gets a dialog -
        // these two hooks for after the message loop starts, the try/catch for before.
        WinForms.Application.ThreadException += (_, e) => Fatal(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Fatal(e.ExceptionObject as Exception);

        try
        {
            return Run(args);
        }
        catch (Exception ex)
        {
            ShowError($"Money could not start.\n\n{ex}");
            return 1;
        }
    }

    private static int Run(string[] args)
    {
        var dataDirectory = DataDirectory.Resolve(
            Environment.GetEnvironmentVariable(DataDirectory.EnvironmentVariable),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));

        // --headless is this host's own flag, and it is stripped before the arguments reach
        // configuration: the command-line provider has no notion of a valueless flag, so it reads
        // --headless --urls http://127.0.0.1:5099 as the key --headless with the value --urls, and
        // the real address pair vanishes. Headless means no window and no WebView2 runtime check,
        // because neither applies to a process nobody is looking at. CI's smoke test runs the
        // published exe this way, on a runner that has no browser runtime at all. The address
        // arrives through configuration, not through app.Urls.
        var headless = args.Contains(HeadlessFlag, StringComparer.OrdinalIgnoreCase);
        args = [.. args.Where(arg => !HeadlessFlag.Equals(arg, StringComparison.OrdinalIgnoreCase))];

        // Two processes writing one SQLite file is the failure this guard exists to prevent, and
        // a headless process writes exactly as hard as a window does: a WAL appended to by two
        // processes at once is how a ledger turns into "database disk image is malformed". So the
        // guard covers every start, and --headless changes only how it says no. It used to be
        // skipped here, on the reasoning that a process nobody is looking at has no window to
        // raise - true, and beside the point, because the file it opens is the same file.
        //
        // The name is scoped to the data directory rather than the machine: MONEYAPP_DATA_DIR
        // exists precisely so a throwaway instance can run against scratch data side by side with
        // the real one (docs/running-locally.md), and a global name would forbid that.
        using var singleInstance = new Mutex(true, MutexNameFor(dataDirectory), out var isOnlyInstance);
        if (!isOnlyInstance)
        {
            if (headless)
            {
                Console.Error.WriteLine($"Money is already running against {dataDirectory}.");
                return 1;
            }

            // Saying so, rather than vanishing. A silent exit here is indistinguishable from
            // "the new build opened and is still broken": you publish, double-click, the new
            // process leaves without a word, and the window you are looking at is the old one -
            // running the old code, against the old schema, showing the old error. That cost an
            // afternoon once.
            //
            // ponytail: a message, not a raise. Raising the other window means FindWindow on a
            // title that is not unique, and SetForegroundWindow is refused to a process that does
            // not own the foreground - focusing the wrong window is worse than saying nothing.
            // Upgrade path: have the owner listen on a named pipe and raise itself when a
            // newcomer knocks.
            ShowError(
                "Money is already open, and one window is the limit - two of them would be two "
                + "programs writing one ledger file.\n\nThis is the copy you just started, and it "
                + "is closing. Close the window that is already open before starting a new build, "
                + "or you will keep looking at the old one.");
            return 0;
        }

        if (headless)
        {
            BuildApp(args).RunAsync().GetAwaiter().GetResult();
            return 0;
        }

        // Without this WebView2 writes its cache beside the exe, which may sit in a read-only or
        // OneDrive-synced folder.
        Environment.SetEnvironmentVariable(
            "WEBVIEW2_USER_DATA_FOLDER", System.IO.Path.Combine(dataDirectory, "webview2"));

        // Asking before we build a window: constructing the control without the runtime throws
        // from inside WinForms, which is a stack trace where a download link belongs.
        try
        {
            CoreWebView2Environment.GetAvailableBrowserVersionString();
        }
        catch (Exception)
        {
            ShowError(
                "Money needs the Microsoft Edge WebView2 Runtime, and it is not available on this "
                + "computer.\n\nInstall the Evergreen bootstrapper from\n"
                + WebView2DownloadUrl
                + "\n\nthen start Money again.");
            return 1;
        }

        // A WinExe has no console, so every line the logging pipeline writes - including the
        // stack trace behind a 500 in the window - goes nowhere, and the only thing on screen is
        // a trace id that leads to no trace. Point Console.Out at a file beside the ledger so the
        // window can be asked what went wrong.
        //
        // After the single-instance guard, never before it: this truncates the file, and a second
        // instance that opens it on its way to discovering it is not wanted would blank the log of
        // the instance actually on screen - or fail outright on the writer that one still holds.

        //
        // ponytail: one file, overwritten each start, so it cannot grow without bound and always
        // describes the session you are looking at. Upgrade path: roll by date if a failure ever
        // needs the run before this one.
        System.IO.Directory.CreateDirectory(dataDirectory);
        Console.SetOut(new System.IO.StreamWriter(System.IO.Path.Combine(dataDirectory, "money.log"))
        {
            AutoFlush = true
        });

        // Which ledger this window is about to open. The host logs its content root already, and
        // that is the one path that does not matter; the one that does was never written down, so
        // "the database is corrupt" could never be checked against "which database".
        Console.WriteLine($"info: Money.Desktop[0]{Environment.NewLine}      Ledger: "
            + DataDirectory.DatabasePathIn(dataDirectory));

        var app = BuildApp(args);

        // Loopback only, on whatever port the OS hands out: never reachable from the network,
        // never fighting another instance for a fixed port.
        app.Urls.Clear();
        app.Urls.Add("http://127.0.0.1:0");
        app.StartAsync().GetAwaiter().GetResult();

        var url = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses.First();

        WinForms.Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        using var form = CreateForm(url, SavedBounds(app));

        // RestoreBounds while maximised or minimised, so closing full-screen does not persist a
        // full-screen rectangle as the normal size. Bounds otherwise, because RestoreBounds only
        // holds a complete rectangle once the window has actually left the normal state - before
        // that it reports whichever components were set in code, which here would be a width and
        // a height with x and y still at their -1 sentinel.
        var closingBounds = Rectangle.Empty;
        form.FormClosing += (_, _) => closingBounds =
            form.WindowState == FormWindowState.Normal ? form.Bounds : form.RestoreBounds;

        // A restore stages the new file and stops the host; the swap itself happens at the next
        // start, before anything opens the database. So the app has to come back by itself.
        var restoring = false;
        app.Lifetime.ApplicationStopping.Register(() =>
        {
            restoring = true;
            if (form.IsHandleCreated && !form.IsDisposed)
            {
                form.BeginInvoke(form.Close);
            }
        });

        WinForms.Application.Run(form);

        // Read before StopAsync, which trips ApplicationStopping itself and would make every
        // ordinary close look like a restore.
        var relaunch = restoring;
        if (!relaunch)
        {
            // Skipped when restoring: the database this would write to is about to be replaced.
            SaveBounds(app, closingBounds);
        }

        app.StopAsync().GetAwaiter().GetResult();

        if (relaunch)
        {
            // Release before starting the successor, or it finds this process still holding the
            // single-instance mutex and exits immediately.
            singleInstance.ReleaseMutex();
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true });
        }

        return 0;
    }

    private static WebApplication BuildApp(string[] args) =>
        MoneyWebApp.CreateAsync(new WebApplicationOptions
        {
            Args = args,
            ApplicationName = typeof(MoneyWebApp).Assembly.GetName().Name,
            ContentRootPath = AppContext.BaseDirectory
        }).GetAwaiter().GetResult();

    private static Form CreateForm(string url, Rectangle? savedBounds)
    {
        var form = new Form
        {
            Text = "Money",
            Controls = { new WebView2 { Dock = DockStyle.Fill, Source = new Uri(url) } }
        };

        if (savedBounds is { } bounds)
        {
            form.StartPosition = FormStartPosition.Manual;
            form.Bounds = bounds;
        }
        else
        {
            form.Width = 1280;
            form.Height = 860;
            form.StartPosition = FormStartPosition.CenterScreen;
        }

        return form;
    }

    /// <returns>
    /// The saved window rectangle, or null when nothing is saved or the rectangle no longer
    /// touches an attached monitor - a screen that has been unplugged since must not put the
    /// window somewhere the user cannot reach it.
    /// </returns>
    private static Rectangle? SavedBounds(WebApplication app)
    {
        // The window state handlers are scoped, like everything that touches the database.
        using var scope = app.Services.CreateScope();
        var saved = scope.ServiceProvider.GetRequiredService<GetWindowStateHandler>()
            .HandleAsync().GetAwaiter().GetResult();

        if (saved is null)
        {
            return null;
        }

        var bounds = new Rectangle(saved.X, saved.Y, saved.Width, saved.Height);
        return Screen.AllScreens.Any(screen => screen.WorkingArea.IntersectsWith(bounds))
            ? bounds
            : null;
    }

    private static void SaveBounds(WebApplication app, Rectangle bounds)
    {
        using var scope = app.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<SaveWindowStateHandler>()
            .HandleAsync(new WindowState(bounds.Width, bounds.Height, bounds.X, bounds.Y))
            .GetAwaiter().GetResult();
    }

    /// <summary>
    /// A stable per-data-directory mutex name. Hashed because a Windows path contains
    /// backslashes, which are the namespace separator in a kernel object name.
    /// </summary>
    private static string MutexNameFor(string dataDirectory)
    {
        var normalised = System.IO.Path.TrimEndingDirectorySeparator(
            System.IO.Path.GetFullPath(dataDirectory)).ToUpperInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalised)));

        // Local\ rather than Global\: one user's instance has no business blocking another's.
        return $@"Local\MoneyApp-{hash[..16]}";
    }

    private static void Fatal(Exception? exception)
    {
        ShowError($"Money hit an unexpected error and has to close.\n\n{exception}");
        Environment.Exit(1);
    }

    private static void ShowError(string message) =>
        WinForms.MessageBox.Show(message, "Money", MessageBoxButtons.OK, MessageBoxIcon.Error);
}
