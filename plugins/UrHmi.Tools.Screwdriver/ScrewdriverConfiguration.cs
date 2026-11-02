using System.Net;

namespace UrHmi.Tools.Screwdriver;

public sealed record ScrewdriverConfiguration(
    string ToolModel,
    string SpinBridgeIp,
    int OpcUaPort,
    int StaleTimeoutMs)
{
    public const string DefaultToolModel = "SDV35";
    public const string DefaultSpinBridgeIp = "192.168.37.2";
    public const int DefaultOpcUaPort = 4840;
    public const int DefaultStaleTimeoutMs = 2000;
    public const string FileName = "screwdriver.json";

    public string OpcUaEndpoint => $"opc.tcp://{SpinBridgeIp}:{OpcUaPort}";

    public static ScrewdriverConfiguration CreateDefault() =>
        new(DefaultToolModel, DefaultSpinBridgeIp, DefaultOpcUaPort, DefaultStaleTimeoutMs);

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

        if (StaleTimeoutMs <= 0)
        {
            errors.Add("spinBridge.staleTimeoutMs must be greater than 0.");
        }

        return errors;
    }
}
