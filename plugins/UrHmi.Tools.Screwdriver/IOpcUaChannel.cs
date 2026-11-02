namespace UrHmi.Tools.Screwdriver;

public sealed record OpcBrowseNode(string NodeId, string BrowseName, string DisplayName);

/// <summary>
/// Narrow OPC UA session surface owned by the screwdriver plugin. Tests fake this
/// so they never open sockets to SpinBridge.
/// </summary>
public interface IOpcUaChannel : IAsyncDisposable
{
    bool IsConnected { get; }

    Task ConnectAsync(string endpointUrl, CancellationToken cancellationToken);

    Task DisconnectAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OpcBrowseNode>> BrowseChildrenAsync(string nodeId, CancellationToken cancellationToken);

    Task<object?> ReadValueAsync(string nodeId, CancellationToken cancellationToken);

    /// <summary>
    /// Subscribe to ResultReadyEventType / JoiningSystemResultReadyEventType on
    /// ResultManagement. <paramref name="onResult"/> receives the event Result
    /// field (JoiningResultDataType, or ResultDataType with ResultContent).
    /// </summary>
    Task SubscribeResultReadyAsync(
        string sourceNodeId,
        Action<object?> onResult,
        CancellationToken cancellationToken);

    /// <summary>
    /// Data-change monitor for LatestResult (Spin's poll alternative in UAExpert
    /// Data Access View).
    /// </summary>
    Task SubscribeDataChangeAsync(
        string nodeId,
        Action<object?> onValue,
        CancellationToken cancellationToken);
}
