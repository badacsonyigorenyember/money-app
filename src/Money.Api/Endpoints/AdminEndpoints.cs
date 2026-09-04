namespace Money.Api.Endpoints;

/// <summary>
/// Placeholder so the composition root (Task 26) compiles and routes ahead of Task 29, which
/// replaces this with the real admin routes (backup, export, integrity check).
/// </summary>
public static class AdminEndpoints
{
    public static RouteGroupBuilder MapAdminEndpoints(this RouteGroupBuilder group) => group;
}
