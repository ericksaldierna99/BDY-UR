namespace UrHmi.Tools.Screwdriver.Tests;

internal sealed class FakeOpcUaChannel : IOpcUaChannel
{
    public List<string> ConnectCalls { get; } = [];

    public Func<string, CancellationToken, Task>? ConnectHandler { get; set; }

    public Dictionary<string, List<OpcBrowseNode>> Tree { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, object?> Values { get; } = new(StringComparer.Ordinal);

    public bool IsConnected { get; set; }

    public bool DisconnectRequested { get; private set; }

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

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public void Map(string parentId, params OpcBrowseNode[] children) => Tree[parentId] = [.. children];

    public static OpcBrowseNode Node(string id, string name) => new(id, name, name);
}
