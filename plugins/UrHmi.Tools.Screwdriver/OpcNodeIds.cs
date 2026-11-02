using Opc.Ua;

namespace UrHmi.Tools.Screwdriver;

/// <summary>
/// Browse results often carry ExpandedNodeId strings (<c>nsu=http://...;i=5003</c>).
/// <see cref="NodeId.Parse"/> rejects that form, so the channel always localizes
/// through the session namespace table before a later read or browse.
/// </summary>
internal static class OpcNodeIds
{
    public static NodeId Resolve(string text, NamespaceTable namespaces)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        ArgumentNullException.ThrowIfNull(namespaces);

        if (NodeId.TryParse(text, out var nodeId) && !nodeId.IsNull)
        {
            return nodeId;
        }

        if (!ExpandedNodeId.TryParse(text, out var expanded) || expanded.IsNull)
        {
            throw new InvalidOperationException($"Cannot parse OPC UA node id '{text}'.");
        }

        var local = ExpandedNodeId.ToNodeId(expanded, namespaces, true);
        if (local.IsNull)
        {
            throw new InvalidOperationException($"Cannot resolve OPC UA node id '{text}'.");
        }

        return local;
    }

    public static string Format(ExpandedNodeId id, NamespaceTable namespaces)
    {
        if (id.IsNull)
        {
            return string.Empty;
        }

        ArgumentNullException.ThrowIfNull(namespaces);
        var local = ExpandedNodeId.ToNodeId(id, namespaces, true);
        return local.IsNull ? string.Empty : local.ToString();
    }
}
