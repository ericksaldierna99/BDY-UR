namespace UrHmi.Tools.Screwdriver.Tests;

internal sealed class FakeOpcUaChannel : IOpcUaChannel
{
    public List<string> ConnectCalls { get; } = [];

    public List<string> ResultReadySources { get; } = [];

    public List<string> DataChangeSources { get; } = [];

    public Func<string, CancellationToken, Task>? ConnectHandler { get; set; }

    public Dictionary<string, List<OpcBrowseNode>> Tree { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, object?> Values { get; } = new(StringComparer.Ordinal);

    public bool IsConnected { get; set; }

    public bool DisconnectRequested { get; private set; }

    public Func<string, Action<object?>, CancellationToken, Task>? SubscribeResultReadyHandler { get; set; }

    public Func<string, Action<object?>, CancellationToken, Task>? SubscribeDataChangeHandler { get; set; }

    private Action<object?>? _onResultReady;
    private Action<object?>? _onDataChange;

    public Task ConnectAsync(string endpointUrl, CancellationToken cancellationToken)
    {
        ConnectCalls.Add(endpointUrl);
        if (ConnectHandler is not null)
        {
            return ConnectHandler(endpointUrl, cancellationToken);
        }

        IsConnected = true;
        DisconnectRequested = false;
        return Task.CompletedTask;
    }

    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        IsConnected = false;
        DisconnectRequested = true;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<OpcBrowseNode>> BrowseChildrenAsync(string nodeId, CancellationToken cancellationToken)
    {
        if (Tree.TryGetValue(nodeId, out var children))
        {
            return Task.FromResult<IReadOnlyList<OpcBrowseNode>>(children);
        }

        return Task.FromResult<IReadOnlyList<OpcBrowseNode>>([]);
    }

    public Task<object?> ReadValueAsync(string nodeId, CancellationToken cancellationToken)
    {
        Values.TryGetValue(nodeId, out var value);
        return Task.FromResult(value);
    }

    public Task SubscribeResultReadyAsync(
        string sourceNodeId,
        Action<object?> onResult,
        CancellationToken cancellationToken)
    {
        ResultReadySources.Add(sourceNodeId);
        _onResultReady = onResult;
        if (SubscribeResultReadyHandler is not null)
        {
            return SubscribeResultReadyHandler(sourceNodeId, onResult, cancellationToken);
        }

        return Task.CompletedTask;
    }

    public Task SubscribeDataChangeAsync(
        string nodeId,
        Action<object?> onValue,
        CancellationToken cancellationToken)
    {
        DataChangeSources.Add(nodeId);
        _onDataChange = onValue;
        if (SubscribeDataChangeHandler is not null)
        {
            return SubscribeDataChangeHandler(nodeId, onValue, cancellationToken);
        }

        return Task.CompletedTask;
    }

    public void RaiseResultReady(object? payload) => _onResultReady?.Invoke(payload);

    public void RaiseDataChange(object? payload) => _onDataChange?.Invoke(payload);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public void Map(string parentId, params OpcBrowseNode[] children) => Tree[parentId] = [.. children];

    public static OpcBrowseNode Node(string id, string name) => new(id, name, name);
}
