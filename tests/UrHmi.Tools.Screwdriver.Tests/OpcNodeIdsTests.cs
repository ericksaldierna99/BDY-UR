using Opc.Ua;

namespace UrHmi.Tools.Screwdriver.Tests;

public sealed class OpcNodeIdsTests
{
    [Fact]
    public void Nsu_browse_ids_round_trip_to_local_node_ids()
    {
        var table = new NamespaceTable();
        table.GetIndexOrAppend(SpinBridgeNodes.NamespaceUri);

        var local = OpcNodeIds.Resolve(SpinBridgeNodes.TighteningSystemAbsolute, table);
        Assert.Equal("ns=1;i=5003", local.ToString());

        var formatted = OpcNodeIds.Format(
            new ExpandedNodeId(5003u, SpinBridgeNodes.NamespaceUri),
            table);
        Assert.Equal("ns=1;i=5003", formatted);
    }

    [Fact]
    public void Numeric_node_ids_parse_without_the_namespace_table_uri()
    {
        var local = OpcNodeIds.Resolve("ns=1;i=5011", new NamespaceTable());
        Assert.Equal("ns=1;i=5011", local.ToString());
    }
}
