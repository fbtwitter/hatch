using Hatch.Converters;
using Hatch.Helpers;
using Hatch.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI.ViewManagement;

namespace Hatch.Controls;

public sealed partial class PriorityCheckBox : CheckBox
{
    private static readonly AccessibilitySettings Accessibility = new();
    private static readonly UISettings SystemColors = new();
    private readonly SolidColorBrush _outline = new();
    private readonly SolidColorBrush _hoverOutline = new();
    private readonly SolidColorBrush _pressedOutline = new();

    public static readonly DependencyProperty PriorityProperty = DependencyProperty.Register(
        nameof(Priority), typeof(TaskPriority), typeof(PriorityCheckBox),
        new PropertyMetadata(TaskPriority.None, OnMetadataChanged));

    public static readonly DependencyProperty TaskTitleProperty = DependencyProperty.Register(
        nameof(TaskTitle), typeof(string), typeof(PriorityCheckBox),
        new PropertyMetadata(string.Empty, OnMetadataChanged));

    public TaskPriority Priority
    {
        get => (TaskPriority)GetValue(PriorityProperty);
        set => SetValue(PriorityProperty, value);
    }

    public string TaskTitle
    {
        get => (string)GetValue(TaskTitleProperty);
        set => SetValue(TaskTitleProperty, value);
    }

    public PriorityCheckBox()
    {
        DefaultStyleKey = typeof(CheckBox);
        // Mutable local brushes update native template states without replacing
        // its checkmark, focus visuals, disabled treatment, or automation peer.
        Resources["CheckBoxCheckBackgroundStrokeUnchecked"] = _outline;
        Resources["CheckBoxCheckBackgroundStrokeUncheckedPointerOver"] = _hoverOutline;
        Resources["CheckBoxCheckBackgroundStrokeUncheckedPressed"] = _pressedOutline;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        ActualThemeChanged += (_, _) => UpdateOutline();
        Checked += (_, _) => UpdateDescription();
        Unchecked += (_, _) => UpdateDescription();
    }

    private static void OnMetadataChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var checkBox = (PriorityCheckBox)sender;
        checkBox.UpdateOutline();
        checkBox.UpdateDescription();
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        SystemColors.ColorValuesChanged += OnSystemColorsChanged;
        UpdateOutline();
        UpdateDescription();
    }

    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        SystemColors.ColorValuesChanged -= OnSystemColorsChanged;
    }

    private void OnSystemColorsChanged(UISettings sender, object args)
        => DispatcherQueue.TryEnqueue(UpdateOutline);

    private void UpdateOutline()
    {
        string? priorityBrush = Accessibility.HighContrast ? null : Priority switch
        {
            TaskPriority.Low => "AccentTextFillColorPrimaryBrush",
            TaskPriority.Medium => "SystemFillColorCautionBrush",
            TaskPriority.High => "SystemFillColorCriticalBrush",
            _ => null
        };
        UpdateBrush(_outline, priorityBrush ?? "CheckBoxCheckBackgroundStrokeUnchecked");
        UpdateBrush(_hoverOutline, priorityBrush ?? "CheckBoxCheckBackgroundStrokeUncheckedPointerOver");
        UpdateBrush(_pressedOutline, priorityBrush ?? "CheckBoxCheckBackgroundStrokeUncheckedPressed");
        if (priorityBrush != null)
        {
            // Preserve WinUI's softer pressed stroke while keeping the priority hue.
            byte normalAlpha = ThemeResourceHelper.GetColor("ControlStrongStrokeColorDefault").A;
            byte pressedAlpha = ThemeResourceHelper.GetColor("ControlStrongStrokeColorDisabled").A;
            _pressedOutline.Opacity *= Math.Min(1, (double)pressedAlpha / Math.Max(1, (int)normalAlpha));
        }
    }

    private static void UpdateBrush(SolidColorBrush target, string key)
    {
        // WinUI's brushes can use StaticResource colours, which can retain
        // the dictionary's creation theme. Resolve their theme colour directly.
        if (key is "SystemFillColorCautionBrush" or "SystemFillColorCriticalBrush")
        {
            target.Color = ThemeResourceHelper.GetColor(key[..^5]);
            target.Opacity = 1;
            return;
        }
        if (!Accessibility.HighContrast && key.StartsWith("CheckBoxCheckBackgroundStrokeUnchecked", StringComparison.Ordinal))
        {
            target.Color = ThemeResourceHelper.GetColor(key.EndsWith("Pressed", StringComparison.Ordinal)
                ? "ControlStrongStrokeColorDisabled" : "ControlStrongStrokeColorDefault");
            target.Opacity = 1;
            return;
        }
        if (ThemeResourceHelper.GetBrush(key) is SolidColorBrush source)
        {
            target.Color = source.Color;
            target.Opacity = source.Opacity;
        }
    }

    private void UpdateDescription()
    {
        var priority = PriorityToLabelConverter.GetLabel(Priority);
        AutomationProperties.SetName(this, Strings.Task_CompletionName(TaskTitle, priority));
        ToolTipService.SetToolTip(this, Strings.Task_CompletionTooltip(
            IsChecked == true ? Strings.Task_Tooltip_MarkIncomplete : Strings.Task_Tooltip_MarkComplete,
            priority));
    }
}
