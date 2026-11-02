using System.Net;

namespace UrHmi.Tools.Screwdriver;

public sealed record ScrewdriverConfiguration(
    string ToolModel,
    string SpinBridgeIp,
    int OpcUaPort,
    int StaleTimeoutMs,
    int HttpPort = 80)
{
    public const string DefaultToolModel = "SDV35";
    public const string DefaultSpinBridgeIp = "10.42.0.1";
    public const int DefaultOpcUaPort = 4840;
    public const int DefaultHttpPort = 80;
    public const int DefaultStaleTimeoutMs = 2000;
    public const string FileName = "screwdriver.json";

    public static readonly int[] DefaultOpcUaProbePorts = [4840, 4841, 48010, 49320, 62541];

    public string OpcUaEndpoint => $"opc.tcp://{SpinBridgeIp}:{OpcUaPort}";

    public string HttpBase => $"http://{SpinBridgeIp}:{HttpPort}";

    public IReadOnlyList<int> OpcUaPortsToProbe
    {
        get
        {
            var ports = new List<int> { OpcUaPort };
            foreach (var port in DefaultOpcUaProbePorts)
            {
                if (!ports.Contains(port))
                {
                    ports.Add(port);
                }
            }

            return ports;
        }
    }

    public static ScrewdriverConfiguration CreateDefault() =>
        new(DefaultToolModel, DefaultSpinBridgeIp, DefaultOpcUaPort, DefaultStaleTimeoutMs, DefaultHttpPort);

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(ToolModel))
        {
            errors.Add("toolModel is required.");
        }

        if (string.IsNullOrWhiteSpace(SpinBridgeIp) || !IPAddress.TryParse(SpinBridgeIp, out _))
        {
            errors.Add("spinBridge.ip is not a valid IP address.");
        }

        if (OpcUaPort is < 1 or > 65535)
        {
            errors.Add("spinBridge.opcUaPort must be between 1 and 65535.");
        }

        if (HttpPort is < 1 or > 65535)
        {
            errors.Add("spinBridge.httpPort must be between 1 and 65535.");
        }

        if (StaleTimeoutMs <= 0)
        {
            errors.Add("spinBridge.staleTimeoutMs must be greater than 0.");
        }

        return errors;
    }
}
