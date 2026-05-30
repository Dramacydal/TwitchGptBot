using Terminal.Gui;

namespace TwitchGpt.Tui;

public static class TuiApplication
{
    /// <summary>Horizontal separator line that stretches to fill its container width.</summary>
    private sealed class HRule : View
    {
        private readonly string _label;

        public HRule(string label = "")
        {
            _label = label;
            Height = 1;
            CanFocus = false;
        }

        public override void Redraw(Rect bounds)
        {
            Driver.SetAttribute(GetNormalColor());
            Move(0, 0);
            var prefix = string.IsNullOrEmpty(_label) ? "" : $"─ {_label} ";
            var remaining = Math.Max(0, bounds.Width - prefix.Length);
            Driver.AddStr(prefix + new string('─', remaining));
        }
    }


    private static TextView? _logView;
    private static TextField? _inputField;

    /// <summary>Called when the user submits a command in the input field.</summary>
    public static Func<string, Task>? OnCommand { get; set; }

    /// <summary>Called when the TUI window is closing (Ctrl+Q or OS close).</summary>
    public static Action? OnQuit { get; set; }

    private static void ApplyConsoleColors()
    {
        var d = Application.Driver;

        var baseScheme = new ColorScheme
        {
            Normal   = d.MakeAttribute(Color.Gray,        Color.Black),
            Focus    = d.MakeAttribute(Color.White,       Color.Black),
            HotNormal = d.MakeAttribute(Color.BrightYellow, Color.Black),
            HotFocus  = d.MakeAttribute(Color.BrightYellow, Color.Black),
            Disabled  = d.MakeAttribute(Color.DarkGray,   Color.Black),
        };

        Colors.Base     = baseScheme;
        Colors.TopLevel = baseScheme;
        Colors.Dialog   = baseScheme;
        Colors.Menu     = baseScheme;
        Colors.Error    = new ColorScheme
        {
            Normal    = d.MakeAttribute(Color.BrightRed, Color.Black),
            Focus     = d.MakeAttribute(Color.BrightRed, Color.Black),
            HotNormal = d.MakeAttribute(Color.BrightRed, Color.Black),
            HotFocus  = d.MakeAttribute(Color.BrightRed, Color.Black),
            Disabled  = d.MakeAttribute(Color.Red,       Color.Black),
        };
    }

    public static void Init()
    {
        Application.Init();
        ApplyConsoleColors();

        var win = new Window("TwitchGpt Bot")
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
        };

        _logView = new TextView
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill() - 2, // leave room for separator + input
            ReadOnly = true,
            WordWrap = false,
        };

        var separator = new HRule("Command")
        {
            X = 0,
            Y = Pos.Bottom(_logView),
            Width = Dim.Fill(),
        };

        _inputField = new TextField("")
        {
            X = 0,
            Y = Pos.Bottom(separator),
            Width = Dim.Fill(),
            Height = 1,
        };

        // Handle Enter key in the input field
        _inputField.KeyDown += (View.KeyEventEventArgs args) =>
        {
            if (args.KeyEvent.Key == Key.Enter)
            {
                var cmd = (_inputField.Text?.ToString() ?? "").Trim();
                _inputField.Text = "";
                if (!string.IsNullOrEmpty(cmd))
                    _ = Task.Run(() => OnCommand?.Invoke(cmd));
                args.Handled = true;
            }
        };

        Application.Top.Closing += (ToplevelClosingEventArgs args) => OnQuit?.Invoke();

        win.Add(_logView, separator, _inputField);
        Application.Top.Add(win);
    }

    /// <summary>Thread-safe: appends a line to the log view.</summary>
    public static void AppendLog(string line)
    {
        if (_logView == null) return;

        Application.MainLoop?.Invoke(() =>
        {
            var current = _logView.Text?.ToString() ?? "";
            _logView.Text = current + line + "\n";
            _logView.MoveEnd();
        });
    }

    /// <summary>Starts the TUI event loop. Blocks until the window is closed.</summary>
    public static void Run() => Application.Run();

    public static void Shutdown() => Application.Shutdown();

    /// <summary>Thread-safe: request the TUI to close.</summary>
    public static void RequestStop() => Application.MainLoop?.Invoke(() => Application.RequestStop());
}
