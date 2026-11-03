namespace UrHmi.Tools.Screwdriver.Tests;

internal sealed class FakeOpcPortProbe : IOpcPortProbe
{
    public HashSet<int> OpenPorts { get; } = [];

    public List<(string Host, int Port)> Calls { get; } = [];

    public Task<bool> IsOpenAsync(string host, int port, TimeSpan timeout, CancellationToken cancellationToken)
    {
        Calls.Add((host, port));
        return Task.FromResult(OpenPorts.Contains(port));
    }
}
