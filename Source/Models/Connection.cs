namespace TraceTiger.Models;

/// <summary>A named Azure target to run KQL against. No secrets — access is the signed-in
/// user's RBAC; resource/workspace ids are not sensitive.</summary>
public sealed class Connection
{
    public string Name { get; set; } = "";
    public ConnectionKind Kind { get; set; } = ConnectionKind.AppInsightsResource;

    /// <summary>Full ARM resource id (App Insights component or Log Analytics workspace).</summary>
    public string ResourceId { get; set; } = "";

    /// <summary>Log Analytics workspace GUID (alternative to <see cref="ResourceId"/> for workspaces).</summary>
    public string WorkspaceId { get; set; } = "";

    /// <summary>Optional Entra tenant to pin sign-in to (guest tenants, multi-tenant accounts).</summary>
    public string TenantId { get; set; } = "";

    /// <summary>Badge color shown in pickers and history cards.</summary>
    public string ColorHex { get; set; } = "#58A6FF";

    public Connection Clone() => (Connection)MemberwiseClone();
}
