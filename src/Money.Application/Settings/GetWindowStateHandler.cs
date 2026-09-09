using Money.Application.Abstractions;

namespace Money.Application.Settings;

/// <summary>The desktop host's saved window rectangle, in screen pixels.</summary>
public sealed record WindowState(int Width, int Height, int X, int Y);

/// <summary>
/// Reads the saved window rectangle for the desktop host, so the host never touches EF.
/// Null means "nothing saved yet" — the host then picks its own default.
/// </summary>
public sealed class GetWindowStateHandler(ISettingsRepository settings)
{
    public async Task<WindowState?> HandleAsync(CancellationToken cancellationToken = default)
    {
        // All four columns are written together, so a partial rectangle is not a real state.
        return await settings.GetAsync(cancellationToken) is
            { WindowWidth: { } width, WindowHeight: { } height, WindowX: { } x, WindowY: { } y }
            ? new WindowState(width, height, x, y)
            : null;
    }
}
