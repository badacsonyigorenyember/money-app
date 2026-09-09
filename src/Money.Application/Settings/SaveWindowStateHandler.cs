using Money.Application.Abstractions;
using Money.Domain.Primitives;

namespace Money.Application.Settings;

/// <summary>
/// Writes the desktop host's window rectangle and nothing else: every business field is read
/// back from the stored row and written through unchanged.
/// </summary>
public sealed class SaveWindowStateHandler(ISettingsRepository settings, IUnitOfWork unitOfWork)
{
    public async Task<Result> HandleAsync(
        WindowState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (state.Width <= 0 || state.Height <= 0)
        {
            return new DomainError(
                "window.invalid_size", "A window's width and height must both be positive.");
        }

        var existing = await settings.GetAsync(cancellationToken) ?? AppSettings.Default;

        await settings.SaveAsync(
            existing with
            {
                WindowWidth = state.Width,
                WindowHeight = state.Height,
                WindowX = state.X,
                WindowY = state.Y
            },
            cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Ok();
    }
}
