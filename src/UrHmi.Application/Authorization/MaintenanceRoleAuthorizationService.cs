namespace UrHmi.Application.Authorization;

/// <summary>
/// Degraded single-role stand-in until the permission mechanism is chosen.
/// </summary>
public sealed class MaintenanceRoleAuthorizationService : IAuthorizationService
{
    public bool HasProcessChangePermission() => true;
}
