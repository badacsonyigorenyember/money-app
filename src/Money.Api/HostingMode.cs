namespace Money.Api;

/// <summary>
/// Read once at startup. Everything below the API layer is identical in both shapes, so moving
/// to server mode (phase 9) is a registration change, not a rewrite (spec section 11).
/// </summary>
public enum HostingMode
{
    Desktop = 1,
    Server = 2
}
