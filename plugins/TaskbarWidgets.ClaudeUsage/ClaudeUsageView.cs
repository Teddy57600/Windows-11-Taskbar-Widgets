using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace TaskbarWidgets.ClaudeUsage;

/// <summary>
/// Vue personnalisée : deux barres (5 heures à gauche, 7 jours à droite), le libellé et les deux taux.
/// Reprend les ressources de l'hôte (BarTrackBrush, ValueBrush) pour suivre le thème clair/sombre.
/// DataContext = <see cref="ClaudeUsageWidget"/>.
/// </summary>
internal sealed class ClaudeUsageView : Grid
{
    public ClaudeUsageView()
    {
        Margin = new Thickness(8, 0, 10, 0);
        VerticalAlignment = VerticalAlignment.Center;
        Background = Brushes.Transparent;
        SetBinding(ToolTipProperty, new Binding(nameof(ClaudeUsageWidget.ToolTip)));
        ToolTipService.SetShowDuration(this, 30000);

        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var fiveHour = CreateBar(nameof(ClaudeUsageWidget.FiveHourLevel), nameof(ClaudeUsageWidget.AccentBrush));
        var sevenDay = CreateBar(nameof(ClaudeUsageWidget.SevenDayLevel), nameof(ClaudeUsageWidget.SevenDayBrush));
        sevenDay.Margin = new Thickness(2, 0, 0, 0);
        SetColumn(sevenDay, 1);

        var label = new TextBlock { FontSize = 10.5, FontWeight = FontWeights.SemiBold };
        label.SetBinding(TextBlock.TextProperty, new Binding(nameof(ClaudeUsageWidget.Label)));
        label.SetBinding(TextBlock.ForegroundProperty, new Binding(nameof(ClaudeUsageWidget.AccentBrush)));

        var value = new TextBlock { FontSize = 12.5, FontWeight = FontWeights.Bold, Margin = new Thickness(0, -2, 0, 0) };
        value.SetBinding(TextBlock.TextProperty, new Binding(nameof(ClaudeUsageWidget.Value)));
        value.SetResourceReference(TextBlock.ForegroundProperty, "ValueBrush");

        var text = new StackPanel { Margin = new Thickness(7, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        text.SetBinding(MinWidthProperty, new Binding(nameof(ClaudeUsageWidget.MinValueWidth)));
        text.Children.Add(label);
        text.Children.Add(value);
        SetColumn(text, 2);

        Children.Add(fiveHour);
        Children.Add(sevenDay);
        Children.Add(text);
    }

    private static Grid CreateBar(string levelPath, string brushPath)
    {
        var bar = new Grid { Width = 4, Height = 28, VerticalAlignment = VerticalAlignment.Center };

        var track = new Border { CornerRadius = new CornerRadius(2) };
        track.SetResourceReference(Border.BackgroundProperty, "BarTrackBrush");

        var scale = new ScaleTransform(1, 0);
        var fill = new Border
        {
            CornerRadius = new CornerRadius(2),
            RenderTransformOrigin = new Point(0.5, 1),
            RenderTransform = scale,
        };
        fill.SetBinding(Border.BackgroundProperty, new Binding(brushPath));
        BindingOperations.SetBinding(scale, ScaleTransform.ScaleYProperty,
            new Binding(levelPath) { TargetNullValue = 0.0, FallbackValue = 0.0 });

        bar.Children.Add(track);
        bar.Children.Add(fill);
        return bar;
    }
}
