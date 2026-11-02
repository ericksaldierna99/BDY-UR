using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Configuration;

namespace UrHmi.Tools.Screwdriver;

/// <summary>
/// OPC Foundation session for the local SpinBridge. Anonymous, no security,
/// AutoAcceptUntrustedCertificates for this cell.
/// </summary>
public sealed class OpcUaChannel : IOpcUaChannel
{
    private const uint SessionTimeoutMs = 60_000;
    private const int DiscoverTimeoutMs = 5_000;
    private static readonly string PkiRoot = Path.Combine(Path.GetTempPath(), "urhmi-screwdriver-opcua");

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ITelemetryContext _telemetry = DefaultTelemetry.Create(static _ => { });
    private ISession? _session;
    private int _disposed;

    public bool IsConnected => _session?.Connected == true;

    public async Task ConnectAsync(string endpointUrl, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpointUrl);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await CloseSessionAsync().ConfigureAwait(false);

            var application = new ApplicationInstance(_telemetry)
            {
                ApplicationName = "UrHmi.Screwdriver",
                ApplicationType = ApplicationType.Client
            };

#pragma warning disable CS0618
            var configuration = await application
                .Build("UrHmi.Screwdriver", "urn:bdy:urhmi:screwdriver")
                .AsClient()
                .AddSecurityConfiguration("CN=UrHmi.Screwdriver", PkiRoot, PkiRoot, Path.Combine(PkiRoot, "rejected"))
#pragma warning restore CS0618
                .SetAutoAcceptUntrustedCertificates(true)
                .SetRejectSHA1SignedCertificates(false)
                .SetMinimumCertificateKeySize(1024)
                .CreateAsync(cancellationToken)
                .ConfigureAwait(false);

            await application.CheckApplicationInstanceCertificatesAsync(true, 2048, cancellationToken)
                .ConfigureAwait(false);

            if (configuration.CertificateManager is ICertificateValidatorEx validator)
            {
                validator.AcceptError = static (_, _) => true;
            }

            var description = await CoreClientUtils.SelectEndpointAsync(
                    configuration,
                    endpointUrl,
                    useSecurity: false,
                    DiscoverTimeoutMs,
                    _telemetry,
                    cancellationToken)
                .ConfigureAwait(false)
                ?? throw new InvalidOperationException($"No OPC UA endpoint was found at {endpointUrl}.");
            var endpoint = new ConfiguredEndpoint(
                collection: null,
                description,
                EndpointConfiguration.Create(configuration));

            _session = await new DefaultSessionFactory(_telemetry).CreateAsync(
                    configuration,
                    endpoint,
                    updateBeforeConnect: false,
                    sessionName: "UrHmi.Screwdriver",
                    sessionTimeout: SessionTimeoutMs,
                    identity: new UserIdentity(),
                    preferredLocales: [],
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await CloseSessionAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<OpcBrowseNode>> BrowseChildrenAsync(
        string nodeId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);
        var session = RequireSession();
        var references = await session.FetchReferencesAsync(NodeId.Parse(nodeId), cancellationToken)
            .ConfigureAwait(false);

        var children = new List<OpcBrowseNode>();
        foreach (var reference in references)
        {
            if (!reference.IsForward)
            {
                continue;
            }

            if (reference.NodeClass is not NodeClass.Object and not NodeClass.Variable and not NodeClass.Unspecified)
            {
                continue;
            }

            var id = FormatNodeId(reference.NodeId);
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            var browseName = reference.BrowseName.Name ?? string.Empty;
            var displayName = string.IsNullOrWhiteSpace(reference.DisplayName.Text)
                ? browseName
                : reference.DisplayName.Text;
            children.Add(new OpcBrowseNode(id, browseName, displayName));
        }

        return children;
    }

    public async Task<object?> ReadValueAsync(string nodeId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);
        var session = RequireSession();
        var value = await session.ReadValueAsync(NodeId.Parse(nodeId), cancellationToken).ConfigureAwait(false);
        return Unwrap(value);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await DisconnectAsync().ConfigureAwait(false);
        _gate.Dispose();
        (_telemetry as IDisposable)?.Dispose();
    }

    private ISession RequireSession() =>
        _session ?? throw new InvalidOperationException("OPC UA session is not connected.");

    private async Task CloseSessionAsync()
    {
        var session = _session;
        _session = null;
        if (session is null)
        {
            return;
        }

        try
        {
            await session.CloseAsync(1000, true, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Closing a dropped session is best-effort.
        }

        if (session is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    private static string FormatNodeId(ExpandedNodeId id) =>
        id.IsNull ? string.Empty : id.ToString();

#pragma warning disable CS0618
    private static object? Unwrap(object? value)
    {
        switch (value)
        {
            case null:
                return null;
            case DataValue data:
                return Unwrap(data.WrappedValue);
            case Variant variant:
                return variant.IsNull ? null : Unwrap(variant.AsBoxedObject(Variant.BoxingBehavior.Legacy));
            case ExtensionObject extension:
                return extension.IsNull ? null : Unwrap(extension.Body);
            case LocalizedText text:
                return text.Text;
            case QualifiedName name:
                return name.Name;
            case Array array and not byte[] and not char[]:
                var items = new object?[array.Length];
                for (var i = 0; i < array.Length; i++)
                {
                    items[i] = Unwrap(array.GetValue(i));
                }

                return items;
            default:
                return value;
        }
    }
#pragma warning restore CS0618
}
