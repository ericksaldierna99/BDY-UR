using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using UrHmi.Domain;
using UrHmi.Domain.Indicators;

namespace UrHmi.Ui.Controls;

public partial class StatusIndicator : UserControl
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title),
        typeof(string),
        typeof(StatusIndicator),
        new PropertyMetadata(string.Empty, OnVisualStateChanged));

    public static readonly DependencyProperty ValueTextProperty = DependencyProperty.Register(
        nameof(ValueText),
        typeof(string),
        typeof(StatusIndicator),
        new PropertyMetadata("—", OnVisualStateChanged));

    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind),
        typeof(IndicatorKind),
        typeof(StatusIndicator),
        new PropertyMetadata(IndicatorKind.Normal, OnVisualStateChanged));

    public static readonly DependencyProperty FreshnessProperty = DependencyProperty.Register(
        nameof(Freshness),
        typeof(Freshness),
        typeof(StatusIndicator),
        new PropertyMetadata(Freshness.Unavailable, OnVisualStateChanged));

    public static readonly DependencyProperty FreshnessCaptionProperty = DependencyProperty.Register(
        nameof(FreshnessCaption),
        typeof(string),
        typeof(StatusIndicator),
        new PropertyMetadata(string.Empty, OnVisualStateChanged));

    public static readonly DependencyProperty CommandProperty = DependencyProperty.Register(
        nameof(Command),
        typeof(ICommand),
        typeof(StatusIndicator),
        new PropertyMetadata(null, OnVisualStateChanged));

    public StatusIndicator()
    {
        InitializeComponent();
        Loaded += (_, _) => ApplyVisualState();
        MouseLeftButtonUp += OnMouseLeftButtonUp;
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string ValueText
    {
        get => (string)GetValue(ValueTextProperty);
        set => SetValue(ValueTextProperty, value);
    }

    public IndicatorKind Kind
    {
        get => (IndicatorKind)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public Freshness Freshness
    {
        get => (Freshness)GetValue(FreshnessProperty);
        set => SetValue(FreshnessProperty, value);
    }

    public string FreshnessCaption
    {
        get => (string)GetValue(FreshnessCaptionProperty);
        set => SetValue(FreshnessCaptionProperty, value);
    }

    public ICommand? Command
    {
        get => (ICommand?)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    private static void OnVisualStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is StatusIndicator indicator)
        {
            indicator.ApplyVisualState();
        }
    }

    private void ApplyVisualState()
    {
        if (TitleText is null || ValueTextBlock is null || FreshnessText is null || Chrome is null || KindMark is null)
        {
            return;
        }

        TitleText.Text = Title;
        ValueTextBlock.Text = string.IsNullOrWhiteSpace(ValueText) ? "—" : ValueText;
        FreshnessText.Text = FreshnessCaption;
        Cursor = Command is null ? Cursors.Arrow : Cursors.Hand;

        var kindBrush = KindBrush(Kind);
        KindMark.Fill = kindBrush;
        Chrome.BorderBrush = kindBrush;

        if (Freshness == Freshness.Unavailable)
        {
            ValueTextBlock.Opacity = 0.45;
            Chrome.BorderBrush = (Brush)FindResource("Brush.Muted");
            KindMark.Fill = (Brush)FindResource("Brush.Muted");
        }
        else if (Freshness == Freshness.Stale)
        {
            ValueTextBlock.Opacity = 0.7;
        }
        else
        {
            ValueTextBlock.Opacity = 1.0;
        }
    }

    private Brush KindBrush(IndicatorKind kind) => kind switch
    {
        IndicatorKind.Normal => (Brush)FindResource("Brush.Normal"),
        IndicatorKind.Warning => (Brush)FindResource("Brush.Warning"),
        IndicatorKind.Fault => (Brush)FindResource("Brush.Fault"),
        IndicatorKind.Safety => (Brush)FindResource("Brush.Safety"),
        IndicatorKind.CommsLoss => (Brush)FindResource("Brush.CommsLoss"),
        _ => (Brush)FindResource("Brush.Muted")
    };

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (Command is null)
        {
            return;
        }

        if (Command.CanExecute(null))
        {
            Command.Execute(null);
            e.Handled = true;
        }
    }
}
