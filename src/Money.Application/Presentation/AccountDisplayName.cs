namespace Money.Application.Presentation;

/// <summary>
/// The one place a deleted account's name is marked up for a reader. A deleted account survives
/// only inside history the user already recorded, so an old entry still says what it was for -
/// with the name flagged, so nobody goes looking for a category that no longer exists.
/// </summary>
public static class AccountDisplayName
{
    public const string DeletedSuffix = " (deleted)";

    public static string For(string name, bool isDeleted) =>
        isDeleted ? name + DeletedSuffix : name;
}
