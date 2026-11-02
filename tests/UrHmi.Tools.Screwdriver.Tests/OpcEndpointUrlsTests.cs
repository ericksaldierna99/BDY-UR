namespace UrHmi.Tools.Screwdriver.Tests;

public sealed class OpcEndpointUrlsTests
{
    [Theory]
    [InlineData("10.42.0.1", 4840, "opc.tcp://10.42.0.1:4840")]
    [InlineData("10.42.0.1:4840", 4840, "opc.tcp://10.42.0.1:4840")]
    [InlineData("opc.tcp://10.42.0.1:4840", 4840, "opc.tcp://10.42.0.1:4840")]
    [InlineData("opc.tcp://10.42.0.1:4840/", 4840, "opc.tcp://10.42.0.1:4840")]
    public void Canonical_host_and_port_keep_the_opc_tcp_scheme(string host, int port, string expected)
    {
        Assert.Equal(expected, OpcEndpointUrls.Canonical(host, port));
    }

    [Theory]
    [InlineData("opc.tcp://10.42.0.1:4840")]
    [InlineData("opc.tcp://10.42.0.1:4840/")]
    [InlineData("10.42.0.1:4840")]
    public void Canonical_url_never_drops_the_opc_tcp_scheme(string input)
    {
        Assert.Equal("opc.tcp://10.42.0.1:4840", OpcEndpointUrls.Canonical(input));
    }

    [Fact]
    public void Host_port_without_scheme_is_invalid_until_canonicalized()
    {
        Assert.Throws<UriFormatException>(() => new Uri("10.42.0.1:4840"));
        Assert.Equal("opc.tcp://10.42.0.1:4840", OpcEndpointUrls.Canonical("10.42.0.1:4840"));
    }

    [Fact]
    public void Application_uri_is_a_well_formed_absolute_uri()
    {
        Assert.True(Uri.IsWellFormedUriString(OpcEndpointUrls.ApplicationUri, UriKind.Absolute));
        Assert.NotEqual(OpcEndpointUrls.ApplicationName, OpcEndpointUrls.ApplicationUri);
        Assert.StartsWith("urn:", OpcEndpointUrls.ApplicationUri, StringComparison.Ordinal);
    }

    [Fact]
    public void Default_configuration_endpoint_is_the_canonical_hotspot_url()
    {
        Assert.Equal("opc.tcp://10.42.0.1:4840", ScrewdriverConfiguration.CreateDefault().OpcUaEndpoint);
    }
}
