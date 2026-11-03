using UrHmi.Tools.Abstractions;

namespace UrHmi.Tools.Screwdriver;

public sealed class ScrewdriverToolModule : IToolModule
{
    private readonly Func<ScrewdriverConfiguration, ISpinBridgeClient> _createClient;
    private ISpinBridgeClient? _client;

    public ScrewdriverToolModule()
        : this(CreateLiveClient)
    {
    }

    internal ScrewdriverToolModule(Func<ScrewdriverConfiguration, ISpinBridgeClient> createClient)
    {
        _createClient = createClient ?? CreateLiveClient;
    }

    public string Id => "screwdriver";

    public string DisplayName => "Atornillado";

    public IReadOnlyList<string> RequiredPlcTags => ScrewdriverTags.Required;

    public object CreateViewModel(IToolHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        var configuration = ScrewdriverConfigStore.LoadOrCreate();
        _client ??= _createClient(configuration);
        return new ScrewdriverViewModel(host, configuration, _client);
    }

    internal static ISpinBridgeClient CreateLiveClient(ScrewdriverConfiguration configuration)
    {
        var client = new SpinBridgeOpcUaClient(configuration);
        client.Start();
        return client;
    }
}
