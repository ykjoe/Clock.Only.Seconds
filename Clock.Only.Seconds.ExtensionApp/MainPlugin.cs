using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System.Text.Json;
using WidBar.SDK;

namespace Clock.Only.Seconds.ExtensionApp;

// Sample widget: a clock on the taskbar, a bigger clock in the flyout and a
// 12/24h toggle in settings. Replace the UI with your own. This code runs in
// its own process, so feel free to pull in any NuGet or native dependency.
public sealed class MainPlugin : WidgetPluginBase, IConfigurableWidgetPlugin
{
    private Settings _settings = new();
    private TextBlock? _previewText;
    private Timer? _timer;

    public override string Id => "com.clock.only.seconds";
    public override string Name => "Clock Only Seconds";
    public override string Description => "A clock that only shows seconds.";
    public override WidgetCategory Category => WidgetCategory.Utility;

    public override int PreviewLogicalWidth => 150;
    public override int FlyoutWidth => 360;
    public override int FlyoutHeight => 680;
    public override WidgetFlyoutBackdrop FlyoutBackdrop => WidgetFlyoutBackdrop.Acrylic;


    private sealed class Settings
    {
        // # Settings Option
        public TimeDisp.TimeDisplayMode DispMode { get; set; } = TimeDisp.TimeDisplayMode.OnlySeconds;
        public bool Use24h { get; set; } = true;

        // # Saving & Restore
        public static Settings FromJson(string? json)
        {
            try
            {
                return string.IsNullOrWhiteSpace(json)
                    ? new Settings()
                    : JsonSerializer.Deserialize<Settings>(json) ?? new Settings();
            }
            catch
            {
                return new Settings();
            }
        }

        public string ToJson() => JsonSerializer.Serialize(this);
    }

    private string TimeText => TimeDisp.DispTimeNow(_settings.DispMode, _settings.Use24h);
    private TaskManager _taskManager = new();

    public override Task InitializeAsync(IWidgetContext context)
    {
        _settings = Settings.FromJson(context.SettingsJson);

        // Initialization
        _taskManager.Load(context);

        base.InitializeAsync(context);

        // Ask WidBar to refresh the taskbar preview once a second.
        _timer = new Timer(_ => Context?.RequestPreviewRefresh(), null,
            TimeSpan.Zero, TimeSpan.FromSeconds(1));

        return Task.CompletedTask;
    }

    // Taskbar preview. Return a compact WinUI element sized for a taskbar slot.
    // Hover, placement and click-to-open are handled on the WidBar side.
    public override UIElement? CreatePreviewContent()
    {
        _previewText = new TextBlock
        {
            Text = TimeText,
            FontSize = 16,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Colors.MediumPurple),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var root = new Grid();
        root.Children.Add(_previewText);
        root.Loaded += (_, _) => _previewText.Text = TimeText;

        // Keep the visible text fresh while the preview is alive.
        var refresh = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        refresh.Tick += (_, _) => _previewText!.Text = TimeText;
        refresh.Start();

        return root;
    }

    // Flyout shown when the user clicks the preview. This is a real window,
    // so anything goes: scrolling, input, Win2D, whatever you need.
    public override UIElement? CreateFlyoutContent()
    {
        // elements
        var clock = new TextBlock
        {
            Text = TimeText,
            FontSize = 36,
            HorizontalAlignment = HorizontalAlignment.Center,
        };

        var taskContainer = new StackPanel {
            Margin = new Thickness(0, 0, 0, 0),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10)
        };
        if (Context != null)
        {
            taskContainer.Children.Add(_taskManager.BuildTaskUIList(_settings.DispMode, Context));
        }

        // timer update
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) =>
        {
            clock.Text = TimeText;
            _taskManager.UpdateTaskUI(_settings.DispMode);
        };
        timer.Start();

        // pannel display
        var panel = new StackPanel
        {
            Spacing = 12,
            Padding = new Thickness(12),
            VerticalAlignment = VerticalAlignment.Top,
        };
        panel.Children.Add(clock);
        panel.Children.Add(taskContainer);

        return panel;
    }

    // Settings UI. The SDK hosts it in a window with Save/Cancel buttons and
    // opens it from the WidBar app or from the gear in the flyout. Call
    // SaveSettings on every change so the draft stays current.
    public UIElement? CreateSettingsContent(IWidgetSettingsContext context)
    {
        // # Define saving json
        var draft = Settings.FromJson(context.SettingsJson);

        // # content in settings interface
        var modeSelector = new ComboBox
        {
            Header = "Display Mode",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Items = { "Only Seconds", "Local General", "UTC General" },
            SelectedIndex = (int)draft.DispMode
        };
        modeSelector.SelectionChanged += (_, _) =>
        {
            draft.DispMode = (TimeDisp.TimeDisplayMode)modeSelector.SelectedIndex;
            context.SaveSettings(draft.ToJson());
        };

        var toggle = new ToggleSwitch
        {
            Header = "Use 24-hour clock",
            IsOn = draft.Use24h,
        };
        toggle.Toggled += (_, _) =>
        {
            draft.Use24h = toggle.IsOn;
            context.SaveSettings(draft.ToJson());
        };

        // # register all the content
        var panel = new StackPanel { Spacing = 16 };
        panel.Children.Add(toggle);
        panel.Children.Add(modeSelector);
        return panel;
    }

    // Called while the user edits settings (and again with the original JSON
    // if they cancel). Apply the draft so the taskbar preview updates live.
    public override void OnSettingsDraftChanged(string settingsJson)
    {
        _settings = Settings.FromJson(settingsJson);
        if (_previewText is not null)
        {
            _previewText.Text = TimeText;
        }
    }

    public override ValueTask DisposeAsync()
    {
        _timer?.Dispose();
        _timer = null;
        return ValueTask.CompletedTask;
    }
}
