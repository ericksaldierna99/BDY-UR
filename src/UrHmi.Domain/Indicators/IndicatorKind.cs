namespace UrHmi.Domain.Indicators;

/// <summary>
/// Visual condition class. One indicator uses one kind; these must not share a generic lamp.
/// </summary>
public enum IndicatorKind
{
    Normal,
    Warning,
    Fault,
    Safety,
    CommsLoss
}
