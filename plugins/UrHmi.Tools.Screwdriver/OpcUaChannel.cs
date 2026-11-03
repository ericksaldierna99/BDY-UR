using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Client.ComplexTypes;
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
    private ComplexTypeSystem? _complexTypes;
    private Subscription? _subscription;
    private Action<object?>? _onResultReady;
    private Action<object?>? _onDataChange;
    private readonly List<NodeId> _resultEventTypes = [];
    private int _disposed;

    public bool IsConnected => _session?.Connected == true;

    public async Task ConnectAsync(string endpointUrl, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpointUrl);
        var discoveryUrl = OpcEndpointUrls.Canonical(endpointUrl);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await CloseSessionAsync().ConfigureAwait(false);

            var application = new ApplicationInstance(_telemetry)
            {
                ApplicationName = OpcEndpointUrls.ApplicationName,
                ApplicationType = ApplicationType.Client
            };

#pragma warning disable CS0618
            var configuration = await application
                .Build(OpcEndpointUrls.ApplicationUri, OpcEndpointUrls.ProductUri)
                .AsClient()
                .AddSecurityConfiguration(
                    $"CN={OpcEndpointUrls.ApplicationName}",
                    PkiRoot,
                    PkiRoot,
                    Path.Combine(PkiRoot, "rejected"))
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
                    discoveryUrl,
                    useSecurity: false,
                    DiscoverTimeoutMs,
                    _telemetry,
                    cancellationToken)
                .ConfigureAwait(false)
                ?? throw new InvalidOperationException($"No OPC UA endpoint was found at {discoveryUrl}.");
            var endpoint = new ConfiguredEndpoint(
                collection: null,
                description,
                EndpointConfiguration.Create(configuration));

            _session = await new DefaultSessionFactory(_telemetry).CreateAsync(
                    configuration,
                    endpoint,
                    updateBeforeConnect: false,
                    sessionName: OpcEndpointUrls.ApplicationName,
                    sessionTimeout: SessionTimeoutMs,
                    identity: new UserIdentity(),
                    preferredLocales: [],
                    cancellationToken)
                .ConfigureAwait(false);

            await LoadComplexTypesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (UriFormatException ex)
        {
            throw new InvalidOperationException(
                $"OPC UA {discoveryUrl} is not a valid URI ({ex.Message}). Use {OpcEndpointUrls.Scheme}://host:port, for example opc.tcp://10.42.0.1:4840.",
                ex);
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
        var references = await session.FetchReferencesAsync(ParseNodeId(nodeId), cancellationToken)
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
        var value = await session.ReadValueAsync(ParseNodeId(nodeId), cancellationToken).ConfigureAwait(false);
        return OpcValues.Unwrap(value);
    }

    public async Task SubscribeResultReadyAsync(
        string sourceNodeId,
        Action<object?> onResult,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceNodeId);
        ArgumentNullException.ThrowIfNull(onResult);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var session = RequireSession();
            _onResultReady = onResult;
            var source = ParseNodeId(sourceNodeId);
            _resultEventTypes.Clear();
            _resultEventTypes.AddRange(await DiscoverResultEventTypesAsync(session, source, cancellationToken)
                .ConfigureAwait(false));

            var filter = BuildResultReadyFilter(session);
            var item = new MonitoredItem(_telemetry, new MonitoredItemOptions
            {
                DisplayName = "ResultManagement ResultReady",
                StartNodeId = source,
                NodeClass = NodeClass.Object,
                AttributeId = Attributes.EventNotifier,
                MonitoringMode = MonitoringMode.Reporting,
                SamplingInterval = 0,
                QueueSize = 100,
                DiscardOldest = true,
                Filter = filter
            });
            item.Notification += OnResultReadyNotification;
            await EnsureSubscriptionItemAsync(item, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SubscribeDataChangeAsync(
        string nodeId,
        Action<object?> onValue,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);
        ArgumentNullException.ThrowIfNull(onValue);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _onDataChange = onValue;
            var item = new MonitoredItem(_telemetry, new MonitoredItemOptions
            {
                DisplayName = "LatestResult",
                StartNodeId = ParseNodeId(nodeId),
                NodeClass = NodeClass.Variable,
                AttributeId = Attributes.Value,
                MonitoringMode = MonitoringMode.Reporting,
                SamplingInterval = 200,
                QueueSize = 10,
                DiscardOldest = true
            });
            item.Notification += OnDataChangeNotification;
            await EnsureSubscriptionItemAsync(item, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
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

    private NodeId ParseNodeId(string nodeId) => OpcNodeIds.Resolve(nodeId, RequireSession().NamespaceUris);

    private async Task LoadComplexTypesAsync(CancellationToken cancellationToken)
    {
        if (_session is null)
        {
            return;
        }

        try
        {
            _complexTypes = ComplexTypeSystemClientExtensions.Create(_session, _telemetry);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            await _complexTypes.LoadAsync(
                    onlyEnumTypes: false,
                    throwOnError: false,
                    timeout.Token)
                .ConfigureAwait(false);
        }
        catch (Exception)
        {
            // JoiningResultDataType may still arrive as binary; the client surfaces that as LastError.
        }
    }

    private async Task EnsureSubscriptionItemAsync(MonitoredItem item, CancellationToken cancellationToken)
    {
        var session = RequireSession();
        if (_subscription is null)
        {
            _subscription = new Subscription(_telemetry, new SubscriptionOptions
            {
                DisplayName = "Spin ResultReady",
                PublishingInterval = 200,
                KeepAliveCount = 10,
                LifetimeCount = 50,
                MaxNotificationsPerPublish = 100,
                PublishingEnabled = true,
                TimestampsToReturn = TimestampsToReturn.Both
            });
            session.AddSubscription(_subscription);
            _subscription.AddItem(item);
            await _subscription.CreateAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        _subscription.AddItem(item);
        await _subscription.ApplyChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private EventFilter BuildResultReadyFilter(ISession session)
    {
        var filter = new EventFilter();
        filter.AddSelectClause(ObjectTypeIds.BaseEventType, new QualifiedName(BrowseNames.EventType));
        filter.AddSelectClause(ObjectTypeIds.BaseEventType, new QualifiedName(BrowseNames.Time));

        var resultNames = ResultBrowseNames(session);
        var typeIds = _resultEventTypes.Count > 0
            ? _resultEventTypes
            : [ObjectTypeIds.BaseEventType];
        foreach (var typeId in typeIds)
        {
            foreach (var name in resultNames)
            {
                filter.AddSelectClause(typeId, name);
            }
        }

        return filter;
    }

    private async Task<List<NodeId>> DiscoverResultEventTypesAsync(
        ISession session,
        NodeId resultManagement,
        CancellationToken cancellationToken)
    {
        var found = new List<NodeId>();
        TryResolve(session, SpinBridgeNodes.ResultReadyEventTypeAbsolute, found);

        try
        {
            var references = await session.FetchReferencesAsync(resultManagement, cancellationToken)
                .ConfigureAwait(false);
            foreach (var reference in references)
            {
                if (!reference.IsForward)
                {
                    continue;
                }

                var name = reference.BrowseName.Name;
                if (!IsResultReadyEventTypeName(name))
                {
                    continue;
                }

                var id = ExpandedNodeId.ToNodeId(reference.NodeId, session.NamespaceUris, false);
                if (!id.IsNull)
                {
                    AddUnique(found, id);
                }
            }
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
        }

        return found;
    }

    private static void TryResolve(ISession session, string text, List<NodeId> found)
    {
        try
        {
            AddUnique(found, OpcNodeIds.Resolve(text, session.NamespaceUris));
        }
        catch (Exception)
        {
        }
    }

    private static void AddUnique(List<NodeId> found, NodeId id)
    {
        if (found.Any(existing => existing == id))
        {
            return;
        }

        found.Add(id);
    }

    private static bool IsResultReadyEventTypeName(string? name) =>
        string.Equals(name, SpinBridgeNodes.ResultReadyEventTypeName, StringComparison.OrdinalIgnoreCase)
        || string.Equals(
            name,
            SpinBridgeNodes.JoiningSystemResultReadyEventTypeName,
            StringComparison.OrdinalIgnoreCase);

    private static QualifiedName[] ResultBrowseNames(ISession session)
    {
        var names = new List<QualifiedName> { new(SpinBridgeNodes.ResultFieldName) };
        AddNamespacedName(names, session, SpinBridgeNodes.MachineryResultNamespaceUri, SpinBridgeNodes.ResultFieldName);
        AddNamespacedName(names, session, SpinBridgeNodes.IjtBaseNamespaceUri, SpinBridgeNodes.ResultFieldName);
        AddNamespacedName(names, session, SpinBridgeNodes.NamespaceUri, SpinBridgeNodes.ResultFieldName);
        return [.. names];
    }

    private static void AddNamespacedName(
        List<QualifiedName> names,
        ISession session,
        string namespaceUri,
        string name)
    {
        var index = session.NamespaceUris.GetIndex(namespaceUri);
        if (index < 0)
        {
            return;
        }

        var qualified = new QualifiedName(name, (ushort)index);
        if (names.Any(existing => existing == qualified))
        {
            return;
        }

        names.Add(qualified);
    }

    private void OnResultReadyNotification(MonitoredItem item, MonitoredItemNotificationEventArgs args)
    {
        try
        {
            var payload = ExtractResultField(item, args.NotificationValue);
            _onResultReady?.Invoke(payload);
        }
        catch (Exception)
        {
        }
    }

    private void OnDataChangeNotification(MonitoredItem item, MonitoredItemNotificationEventArgs args)
    {
        try
        {
            _onDataChange?.Invoke(OpcValues.Unwrap(args.NotificationValue));
        }
        catch (Exception)
        {
        }
    }

    private object? ExtractResultField(MonitoredItem item, IEncodeable? notification)
    {
        if (notification is not EventFieldList fields)
        {
            return OpcValues.Unwrap(notification);
        }

        foreach (var typeId in _resultEventTypes.Append(ObjectTypeIds.BaseEventType))
        {
            foreach (var name in ResultBrowseNames(RequireSession()))
            {
                var value = item.GetFieldValue(fields, typeId, name);
                var unwrapped = OpcValues.Unwrap(value);
                if (unwrapped is not null)
                {
                    return unwrapped;
                }
            }

            var byString = OpcValues.Unwrap(
                item.GetFieldValue(fields, typeId, SpinBridgeNodes.ResultFieldName, Attributes.Value));
            if (byString is not null)
            {
                return byString;
            }
        }

        if (fields.EventFields is { Count: > 0 })
        {
            var items = new object?[fields.EventFields.Count];
            for (var i = 0; i < fields.EventFields.Count; i++)
            {
                items[i] = OpcValues.Unwrap(fields.EventFields[i]);
            }

            return items;
        }

        return null;
    }

    private async Task CloseSessionAsync()
    {
        _onResultReady = null;
        _onDataChange = null;
        _resultEventTypes.Clear();

        var subscription = _subscription;
        _subscription = null;
        if (subscription is not null)
        {
            try
            {
                await subscription.DeleteAsync(true, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception)
            {
            }

            if (_session is not null)
            {
                try
                {
                    await _session.RemoveSubscriptionAsync(subscription, CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch (Exception)
                {
                }
            }

            subscription.Dispose();
        }

        var session = _session;
        _session = null;
        var types = _complexTypes;
        _complexTypes = null;
        if (session is null)
        {
            types?.Dispose();
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

        types?.Dispose();
    }

    private string FormatNodeId(ExpandedNodeId id) =>
        OpcNodeIds.Format(id, RequireSession().NamespaceUris);
}
