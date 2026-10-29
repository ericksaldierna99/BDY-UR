using CommunityToolkit.Mvvm.ComponentModel;
using UrHmi.Domain;
using UrHmi.Domain.Indicators;

namespace UrHmi.Presentation.Dashboard;

public sealed partial class IndicatorViewModel : ObservableObject
{
    public IndicatorViewModel(
        string title,
        string displayText,
        IndicatorKind kind,
        Freshness freshness,
        string freshnessCaption)
    {
        Title = title;
        DisplayText = displayText;
        Kind = kind;
        Freshness = freshness;
        FreshnessCaption = freshnessCaption;
        IsStale = freshness == Freshness.Stale;
        IsUnavailable = freshness == Freshness.Unavailable;
    }

    public string Title { get; }

    [ObservableProperty]
    private string _displayText;

    [ObservableProperty]
    private IndicatorKind _kind;

    [ObservableProperty]
    private Freshness _freshness;

    [ObservableProperty]
    private string _freshnessCaption;

    [ObservableProperty]
    private bool _isStale;

    [ObservableProperty]
    private bool _isUnavailable;

    public static string CaptionFor(Freshness freshness) => freshness switch
    {
        Freshness.Live => string.Empty,
        Freshness.Stale => "dato no actualizado",
        Freshness.Unavailable => "sin dato",
        _ => "sin dato"
    };

    public static IndicatorViewModel Unavailable(string title) =>
        new(
            title,
            "—",
            IndicatorKind.Normal,
            Freshness.Unavailable,
            CaptionFor(Freshness.Unavailable));

    public static IndicatorViewModel Connection(string title, ChannelHealth health)
    {
        var (text, kind) = health.Link switch
        {
            LinkState.Connected => ("Connected", IndicatorKind.Normal),
            LinkState.Connecting => ("Connecting", IndicatorKind.Warning),
            LinkState.Reconnecting => ("Reconnecting", IndicatorKind.Warning),
            LinkState.Faulted => ("Faulted", IndicatorKind.Fault),
            _ => ("Disconnected", IndicatorKind.CommsLoss)
        };

        return new(title, text, kind, Freshness.Live, string.Empty);
    }

    public static IndicatorViewModel DegradedConnection(string title) =>
        new(title, "Degraded", IndicatorKind.Warning, Freshness.Live, string.Empty);
}
