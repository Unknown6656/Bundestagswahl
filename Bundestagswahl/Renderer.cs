//#define ENABLE_HISTORIC_PLOT_SUBPIXEL_RENDERING

using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using System.Text;
using System.Linq;
using System;

using Unknown6656.Generics.Text;
using Unknown6656.Generics;
using Unknown6656.Runtime;
using Unknown6656.Console;

namespace Bundestagswahl;


public enum RenderSize
{
    Small = 0,
    Medium = 1,
    Large = 2,
}

public enum Views
{
    States,
    Source,
    Historic,
    Options,
}

public enum StateCursorPosition
    : byte
{
    Federal,
    Deselect,
    Invert,
    West,
    East,
    South,
    North,
    Population,
    PopulationGrowth,
    Economy,
    BW = State.BW,
    BY = State.BY,
    BE = State.BE,
    BE_O = State.BE_O,
    BE_W = State.BE_W,
    BB = State.BB,
    HB = State.HB,
    HH = State.HH,
    HE = State.HE,
    MV = State.MV,
    NI = State.NI,
    NW = State.NW,
    RP = State.RP,
    SL = State.SL,
    SN = State.SN,
    ST = State.ST,
    SH = State.SH,
    TH = State.TH,
}

public enum SourceCursorPosition
{
    Button_Last40Years,
    Button_Last20Years,
    Button_Last16Years,
    Button_Last12Years,
    Button_Last8Years,
    Button_Last4Years,
    Button_Last1Year,
    Button_Last6Months,
    Button_Last3Months,
    DateSelector,
}

public enum OptionsCursorPosition
{
    SixelRendering,
    RefreshData,
}

[Flags]
public enum RenderInvalidation
    : int
{
    None            = 0b_00000000_00000000_00000000_00000000,

    FrameBorder     = 0b_00000000_00000000_00000000_00000001,
    FrameText       = 0b_00000000_00000000_00000000_00000010,
    Map             = 0b_00000000_00000000_00000000_00000100,
    StateSelector   = 0b_00000000_00000000_00000000_00001000,
    DataSource      = 0b_00000000_00000000_00000000_00010000,
    HistoricPlot    = 0b_00000000_00000000_00000000_00100000,
    PollResults     = 0b_00000000_00000000_00000000_01000000,
    Compass         = 0b_00000000_00000000_00000000_10000000,
    Coalitions      = 0b_00000000_00000000_00000001_00000000,
    Options         = 0b_00000000_00000000_00000010_00000000,

    All             = ~None,
}

public enum ModalPromptIcon
{
    None = 0,
    Info = 1,
    Exclamation = 2,
    Question = 3,
    Spinner = 4,
}

public class ModalPromptInfo
{
    public string[] ContentLines { get; }
    public ConsoleColor Foreground { get; }
    public ConsoleColor Background { get; }
    public int PromptWidth { get; }
    public int PromptHeight { get; }
    public ModalPromptIcon Icon { get; }
    public (int X, int Y) SpinnerPosition { get; private set; }


    public ModalPromptInfo(string content, ConsoleColor foreground, ConsoleColor background, ModalPromptIcon icon = ModalPromptIcon.None)
    {
        Foreground = foreground;
        Background = background;
        ContentLines = content.Replace("\r\n", "\n").Replace("\r", "").SplitIntoLines();
        SpinnerPosition = (-1, -1);
        Icon = icon;

        string[] icon_lines = icon switch
        {
            ModalPromptIcon.Info => ["╱i╲", "‾‾‾"],
            ModalPromptIcon.Exclamation => ["╱!╲", "‾‾‾"],
            ModalPromptIcon.Question => ["╱?╲", "‾‾‾"],
            _ => [],
        };
        int prompt_width = ContentLines.Max(s => s.Length + 12);

        if (ContentLines.Length == 1)
            ContentLines = [ContentLines[0], ""];

        // TODO : use RenderBox(...) ?
        // TODO : re-implement spinner

        ContentLines = [
            $"┌{new string('─', prompt_width)}┐",
            $"│{new string(' ', prompt_width)}│",
            ..ContentLines.Select((line, i) => $"│   {(i < icon_lines.Length ? icon_lines[i] : "   ")}   {line.PadRight(prompt_width - 12)}   │"),
            $"│{new string(' ', prompt_width)}│",
            $"└{new string('─', prompt_width)}┘",
        ];
        PromptWidth = prompt_width + 2;
        PromptHeight = ContentLines.Length;
    }

    public (int x, int y) Render() => Render(Console.WindowWidth, Console.WindowHeight);

    public (int x, int y) Render(int console_width, int console_height)
    {
        Console.Write(Background.ToVT520(ColorMode.Foreground));

        for (int _y = 1; _y < console_height - 1; ++_y)
            for (int _x = _y % 2 + 1; _x < console_width - 1; _x += 2)
            {
                Console.SetCursorPosition(_x, _y);
                Console.Write('╱');
            }

        int x = (console_width - PromptWidth) / 2;
        int y = (console_height - PromptHeight) / 2;

        SpinnerPosition = (x + 4, y + 2);

        Console.Write(Foreground.ToVT520(ColorMode.Foreground));
        Console.DiscardAllPendingInput();

        for (int i = 0; i < PromptHeight; ++i)
        {
            Console.SetCursorPosition(x, y + i);
            Console.Write(ContentLines[i]);
        }

        return (x, y);
    }
}

public sealed class Renderer
    : IDisposable
{
    #region ONLY FOR CACHING REASONS

    private static readonly StateCursorPosition[] _state_cursor_values = Enum.GetValues<StateCursorPosition>();
    internal static readonly State[] _state_values = Enum.GetValues<State>();
    private static readonly State[] _state_values_lfa = [State.BW, State.BY, State.HE, State.HH];
    private static readonly State[] _state_values_pop_growth = [State.BY, State.BW, State.HE, State.RP, State.NW, State.NI, State.HH, State.SH, State.BE];
    private static readonly State[] _state_values_pop = [State.NW, State.BY, State.BW, State.NI];
    private static readonly State[] _state_values_north_ger = [State.HB, State.HH, State.MV, State.NI, State.SH];
    private static readonly State[] _state_values_south_ger = [State.BW, State.BY, State.HE, State.RP, State.SL];
    private static readonly State[] _state_values_west_ger = [State.BW, State.BY, State.HB, State.HH, State.HE, State.NI, State.NW, State.RP, State.SL, State.SH, State.BE_W];
    private static readonly State[] _state_values_east_ger = [State.BB, State.MV, State.SN, State.ST, State.TH, State.BE_O];

    #endregion

    private static readonly Dictionary<RenderSize, (int MinWidth, int MinHeight)> _min_sizes = new()
    {
        [RenderSize.Small] = (155, 58),
        [RenderSize.Medium] = (170, 71),
        [RenderSize.Large] = (190, 99),
    };
    private static readonly ConsoleColor _dark = new(.27);
    private static readonly object _render_mutex = new();
    private static readonly bool _sixel_supported = LINQ.TryDo(() => Console.CurrentTerminalEmulatorInfo.HasSixelSupport, false);

    public const ConsoleKey KEY_VIEW_SWITCH = ConsoleKey.Tab;
    public const ConsoleKey KEY_ENTER = ConsoleKey.Enter;
    public const ConsoleKey KEY_RIGHT = ConsoleKey.RightArrow;
    public const ConsoleKey KEY_LEFT = ConsoleKey.LeftArrow;
    public const ConsoleKey KEY_UP = ConsoleKey.UpArrow;
    public const ConsoleKey KEY_DOWN = ConsoleKey.DownArrow;
    public const ConsoleKey KEY_PAGE_UP = ConsoleKey.PageUp;
    public const ConsoleKey KEY_PAGE_DOWN = ConsoleKey.PageDown;
    public const ConsoleKey KEY_HOME = ConsoleKey.Home;
    public const ConsoleKey KEY_END = ConsoleKey.End;
    public const ConsoleKey KEY_EXIT = ConsoleKey.Escape;

    public static Party[][] Coalitions { get; } = [
        [Party.CDU, Party.SPD],
        [Party.CDU, Party.FDP],
        [Party.SPD, Party.GRÜNE],
        [Party.CDU, Party.GRÜNE],
        [Party.CDU, Party.SPD, Party.FDP],
        [Party.CDU, Party.SPD, Party.GRÜNE],
        [Party.CDU, Party.FDP, Party.GRÜNE],
        [Party.SPD, Party.LINKE, Party.GRÜNE],
        [Party.SPD, Party.LINKE, Party.BSW],
        [Party.SPD, Party.LINKE, Party.BSW, Party.GRÜNE],
        [Party.SPD, Party.BSW, Party.GRÜNE],
        [Party.SPD, Party.PIRATEN, Party.GRÜNE],
        [Party.SPD, Party.PIRATEN, Party.LINKE, Party.GRÜNE],
        [Party.FDP, Party.GRÜNE, Party.PIRATEN],
        [Party.GRÜNE, Party.PIRATEN],
        [Party.LINKE, Party.PIRATEN],
        [Party.BSW, Party.LINKE, Party.PIRATEN],
        [Party.BSW, Party.PIRATEN],
        [Party.FDP, Party.FW, Party.PIRATEN],
        [Party.FDP, Party.FW],
        [Party.CDU, Party.AFD],
        [Party.CDU, Party.FDP, Party.FW, Party.AFD],
        [Party.CDU, Party.FDP, Party.AFD],
        [Party.CDU, Party.FDP, Party.FW],
        [Party.CDU, Party.FW],
        [Party.CDU, Party.FW, Party.AFD],
        [Party.AFD, Party.BSW],
        [Party.AFD, Party.RECHTE],
        [Party.FW, Party.AFD, Party.RECHTE],
    ];


    private (DateOnly Start, DateOnly End, DateOnly Cursor)? _selected_range;
    private readonly Dictionary<State, bool> _selected_states = _state_values.ToDictionary(LINQ.id, static s => false);
    private readonly ConsoleState _console_state;

    private StateCursorPosition _state_cursor = StateCursorPosition.Federal;
    private SourceCursorPosition _source_cursor = SourceCursorPosition.Button_Last40Years;
    private SourceCursorPosition? _current_source = SourceCursorPosition.Button_Last40Years;
    private OptionsCursorPosition _option_cursor = OptionsCursorPosition.SixelRendering;
    private ModalPromptInfo? _modal_prompt = null;
    private Views _current_view = Views.States;
    private RenderInvalidation _invalidate;
    private RenderSize _render_size;
    private bool _sixel_enabled;
    private bool _too_small;


    public bool IsActive { get; private set; } = true;

    public PollHistory PollHistory { get; private set; }

    public PollFetcher PollFetcher { get; }

    public RenderSize CurrentRenderSize
    {
        get => _render_size;
        set
        {
            if (_render_size != value)
            {
                _render_size = value is RenderSize.Small or RenderSize.Medium or RenderSize.Large ? value : RenderSize.Small;

                Render(true);
            }
        }
    }

    public Map Map => _render_size switch
    {
        RenderSize.Large => Map.LargeMap,
        RenderSize.Medium => Map.MediumMap,
        _ => Map.SmallMap,
    };


    public Renderer(IPollDatabase poll_db)
    {
        _console_state = Console.CurrentConsoleState;

        InvalidateAll();

        Console.HardResetAndFullClear();
        Console.OutputEncoding = Encoding.UTF8;
        Console.InputEncoding = Encoding.UTF8;
        Console.CursorVisible = false;

        PollHistory = PollHistory.Empty;
        PollFetcher = new(poll_db);
    }

    ~Renderer() => Dispose(false);

    public void InvalidateAll() => Invalidate(RenderInvalidation.All);

    public void Invalidate(RenderInvalidation invalidation) => _invalidate |= invalidation;

    public void Dispose()
    {
        Dispose(true);

        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
        IsActive = false;

        Console.CurrentConsoleState = _console_state;
    }

    public void Render(bool clear)
    {
        lock (_render_mutex)
        {
            if (clear)
            {
                InvalidateAll();

                Console.FullClear();
            }

            Console.ResetGraphicRenditions();
            Console.CursorVisible = false;

            (int min_width, int min_height) = _min_sizes[_render_size];
            int width = Console.WindowWidth;
            int height = Console.WindowHeight;

            if (OS.IsWindows)
#pragma warning disable CA1416 // Validate platform compatibility
                Console.SetBufferSize(
                    Math.Max(Console.BufferWidth, width + 2),
                    Math.Max(Console.BufferHeight, height + 4)
                );
#pragma warning restore CA1416

            _too_small = false;

            if (width < min_width || height < min_height)
                if (_render_size is RenderSize.Small)
                {
                    _too_small = true;

                    InvalidateAll();

                    Console.CurrentGraphicRendition = new()
                    {
                        BackgroundColor = ConsoleColor.DarkRed, // ConsoleColor.Default,
                        ForegroundColor = ConsoleColor.White, // ConsoleColor.Red,
                        Intensity = TextIntensityMode.Bold,
                    };
                    Console.FullClear();
                    Console.Write($"""
                     ┌─────────────────────────────────────────┐
                     │       ⚠️ ⚠️ FENSTER ZU KLEIN ⚠️ ⚠️      │
                     ├─────────────────────────────────────────┤
                     │ Bitte ändern Sie die Größe des Fensters │
                     │ auf mindestens {min_width,3} x {min_height,2}. Die aktuelle   │
                     | Fenstergröße beträgt {width,3} x {height,2}.          │
                     │ Alternativ können Sie die Schriftgröße  │
                     │ verkleinern oder den Zoomfaktor ändern. │
                     └─────────────────────────────────────────┘
                    """);
                }
                else
                    --CurrentRenderSize;
            else if (_render_size < RenderSize.Large && width >= _min_sizes[_render_size + 1].MinWidth && height >= _min_sizes[_render_size + 1].MinHeight)
                ++CurrentRenderSize;
            else
            {
                int timeplot_height = (int)double.Clamp(height * height * .006, 20, height * .6);

                RenderFrame(width, height, timeplot_height, clear);
                RenderMap();
                RenderSourceSelection(Map.Width + 6, 30, timeplot_height);

                if ((_invalidate | RenderInvalidation.PollResults
                                 | RenderInvalidation.Compass
                                 | RenderInvalidation.Coalitions
                                 | RenderInvalidation.HistoricPlot) != 0)
                {
                    MergedPollHistory historic = FetchPolls();
                    MergedPoll? display = historic.Polls.FirstOrDefault(p => p.EarliestDate <= _selected_range?.Cursor && p.LatestDate >= _selected_range?.Cursor);

                    RenderHistoricPlot(width, timeplot_height, historic);
                    RenderResults(width, height, timeplot_height, display);
                }

                RenderOptions(Map.Height + 17, Map.Width + 2);

                Console.ResetGraphicRenditions();

                _invalidate = RenderInvalidation.None;
            }

            _modal_prompt?.Render();

            Console.DiscardAllPendingInput();
        }
    }

    public async Task Run()
    {
        await FetchAllPollsAsync();

        while (Console.ReadKey(true) is { Key: not KEY_EXIT } key)
            await HandleInput(key);
    }

    private async Task FetchAllPollsAsync()
    {
        PollHistory = await RenderFetchingPrompt(PollFetcher.FetchAsync);

        foreach (State state in _selected_states.Keys)
            _selected_states[state] = true;

        AdjustTimeRanges();
        Render(true);
    }

    private void AdjustTimeRanges()
    {
        if (PollHistory is { EarliestDate: DateOnly min, LatestDate: DateOnly max })
        {
            (DateOnly start, DateOnly end, DateOnly cursor) = _selected_range ?? (min, max, max);

            if (min > max)
                (min, max) = (max, min);

            if (start > end)
                (start, end) = (end, start);

            if (start < min)
                start = min;

            if (end > max)
                end = max;

            cursor = cursor < start ? start : cursor > end ? end : cursor;
            _selected_range = (start, end, cursor);
        }
        else
            _selected_range = null;
    }

    private MergedPollHistory FetchPolls()
    {
        List<State?> states = _selected_states.SelectWhere(kvp => kvp.Value, kvp => (State?)kvp.Key).ToList();

        if (_state_values.All(s => states.Contains(s)))
            states = [null];
        else if (states.Contains(State.BE))
            states.AddRange([State.BE_W, State.BE_O]);

        return PollHistory[_selected_range?.Start, _selected_range?.End, states]; ;
    }

    public async Task<PollHistory> RenderFetchingPrompt(Func<Task<PollHistory>> task)
    {
        PollHistory result = await RenderModalPromptUntil(
            "Umfrageergebnisse werden geladen...\nBitte warten.",
            ConsoleColor.Blue,
            ConsoleColor.DarkBlue,
            ModalPromptIcon.Spinner,
            task()
        );

        await RenderModalPromptUntilKeyPress(
            $"{result.PollCount} Umfrageergebnisse wurden erfolgreich geladen.\nZum Starten bitte eine beliebige Taste drücken.",
            ConsoleColor.Green,
            ConsoleColor.DarkGreen,
            ModalPromptIcon.Info
        );

        return result;
    }

    private (int x, int y) RenderModalPrompt(string content, ConsoleColor foreground, ConsoleColor background, ModalPromptIcon icon)
    {
        Render(false);

        return (_modal_prompt = new(content, foreground, background, icon)).Render();
    }

    private async Task<T> RenderModalPromptUntil<T>(string content, ConsoleColor foreground, ConsoleColor background, ModalPromptIcon icon, Task<T>? task)
    {
        (int x, int y) = RenderModalPrompt(content, foreground, background, icon);
        bool completed = false;
        Task spinner = _modal_prompt?.Icon is ModalPromptIcon.Spinner ? Task.Factory.StartNew(async delegate
        {
            // ⡎⠉⢱
            // ⢇⣀⡸
            const string TL = "⡀⡄⡆⡎⠎⠊⠈⠀⠀⠀⠀⠀⠀⠀⠀⠀⠀⠀⠀⠀";
            const string TM = "⠀⠀⠀⠀⠁⠉⠉⠉⠈⠀⠀⠀⠀⠀⠀⠀⠀⠀⠀⠀";
            const string TR = "⠀⠀⠀⠀⠀⠀⠁⠑⠱⢱⢰⢠⢀⠀⠀⠀⠀⠀⠀⠀";
            const string BR = "⠀⠀⠀⠀⠀⠀⠀⠀⠀⠀⠈⠘⠸⡸⡰⡠⡀⠀⠀⠀";
            const string BM = "⠀⠀⠀⠀⠀⠀⠀⠀⠀⠀⠀⠀⠀⠀⢀⣀⣀⣀⡀⠀";
            const string BL = "⠇⠃⠁⠀⠀⠀⠀⠀⠀⠀⠀⠀⠀⠀⠀⠀⢀⢄⢆⢇";
            int step = 0;

            while (!completed)
            {
                step = (step + 1) % TL.Length;
                (int spinner_x, int spinner_y) = _modal_prompt.SpinnerPosition;

                Console.ForegroundColor = foreground;
                Console.SetCursorPosition(spinner_x, spinner_y);
                Console.Write(TL[step]);
                Console.Write(TM[step]);
                Console.Write(TR[step]);
                Console.SetCursorPosition(spinner_x, spinner_y + 1);
                Console.Write(BL[step]);
                Console.Write(BM[step]);
                Console.Write(BR[step]);

                await Task.Delay(50);
            }
        }) : Task.CompletedTask;
        T result = default!;

        if (task is { })
        {
            result = await task;
            completed = true;
        }

        await spinner;

        return result;
    }

    private async Task<ConsoleKeyInfo> RenderModalPromptUntilKeyPress(string content, ConsoleColor foreground, ConsoleColor background, ModalPromptIcon icon)
    {
        await RenderModalPromptUntil<__empty>(content, foreground, background, icon, null);

        Console.DiscardAllPendingInput();

        ConsoleKeyInfo key = Console.ReadKey(true);

        CloseModalPrompt();

        return key;
    }

    private void CloseModalPrompt()
    {
        if (Interlocked.Exchange(ref _modal_prompt, null) is { })
            Render(true);
    }

    private static void RenderTitle(int x, int y, string title, bool active)
    {
        Console.SetCursorPosition(x, y);
        Console.BackgroundColor = ConsoleColor.Default;
        Console.Write(' ');
        Console.WriteFormatted(title, new()
        {
            ForegroundColor = active ? ConsoleColor.Red : ConsoleColor.Cyan,
            Underlined = active ? TextUnderlinedMode.Single : TextUnderlinedMode.NotUnderlined,
            Intensity = active ? TextIntensityMode.Bold : TextIntensityMode.Regular,
        });
        Console.Write(' ');
    }

    private static void RenderFrameLine(int x, int y, int size, bool horizontal)
    {
        (char start, char mid, char end) = horizontal ? ('├', '─', '┤') : ('┬', '│', '┴');
        string line = start + new string(mid, size - 2) + end;

        if (horizontal)
        {
            Console.SetCursorPosition(x, y);
            Console.Write(line);
        }
        else
            for (int i = 0; i < size; ++i)
            {
                Console.SetCursorPosition(x, y + i);
                Console.Write(line[i]);
            }
    }

    private static void RenderBox(int x, int y, int width, int height, bool clear, ConsoleColor line_color)
    {
        Console.SetCursorPosition(x, y);
        Console.CurrentGraphicRendition = ConsoleGraphicRendition.Default with
        {
            ForegroundColor = line_color
        };
        Console.Write($"┌{new string('─', width - 2)}┐");

        for (int i = 1; i < height - 1; ++i)
        {
            Console.SetCursorPosition(x, y + i);

            if (clear)
                Console.Write($"│{new string(' ', width - 2)}│");
            else
            {
                Console.Write('│');
                Console.CursorLeft = x + width - 1;
                Console.Write('│');
            }
        }

        Console.SetCursorPosition(x, y + height - 1);
        Console.Write($"└{new string('─', width - 2)}┘");
    }

    private static void RenderButton(int x, int y, int? width, string text, ConsoleColor color, bool active, bool? hover)
    {
        text = text.Trim();
        width ??= text.Length + 4;
        width -= 2;

        if (text.Length > width)
            text = text[..width.Value];

        int padding = (width.Value - text.Length) / 2;

        text = $"[{text.PadLeft(text.Length + padding).PadRight(width.Value)}]";
        width += 2;

        Console.SetCursorPosition(x, y);
        Console.WriteFormatted(text, new()
        {
            ForegroundColor = color,
            AreColorsInverted = active,
        });
        Console.ResetGraphicRenditions();

        RenderHoverUnderline(x, y + 1, width.Value, hover);
    }

    private static void RenderDateSelector(int x, int y, string description, int width, DateOnly? date, bool? hover)
    {
        width -= 15;
        description = description.PadLeft(width) + "  ";

        Console.SetCursorPosition(x, y);
        Console.ResetGraphicRenditions();
        Console.Write(description);
        Console.InvertedColors = hover ?? false;
        Console.Write($"[ {date?.ToString("yyyy-MM-dd") ?? "xxxx-xx-xx"} ]");
        Console.InvertedColors = false;

        RenderHoverUnderline(x + description.Length, y + 1, 14, hover);
    }

    private static void RenderHoverUnderline(int x, int y, int width, bool? hover = true, char underline_char = '^' /* '°' */)
    {
        if (hover is null)
            return;

        Console.SetCursorPosition(x, y);

        if (hover is false)
            Console.Write(new string(' ', width));
        else
            Console.WriteFormatted(new string(underline_char, width), new()
            {
                Blink = TextBlinkMode.Slow,
                ForegroundColor = ConsoleColor.Gray,
            });

        Console.ResetGraphicRenditions();
    }

    private void RenderFrame(int width, int height, int timeplot_height, bool clear)
    {
        if (_invalidate.HasFlag(RenderInvalidation.FrameBorder))
        {
            RenderBox(0, 0, width, height, clear, ConsoleColor.Gray);

            RenderFrameLine(Map.Width + 3, 0, height, false);
            RenderFrameLine(0, Map.Height + 3, Map.Width + 4, true);
            RenderFrameLine(Map.Width + 3, timeplot_height, width - Map.Width - 3, true);
            RenderFrameLine(Map.Width + 32, 0, timeplot_height + 1, false);
            RenderFrameLine(0, Map.Height + 15, Map.Width + 4, true);
        }

        if (_invalidate.HasFlag(RenderInvalidation.FrameText))
        {
            RenderTitle(3, 0, "ÜBERSICHTSKARTE DEUTSCHLAND", false);
            RenderTitle(3, Map.Height + 3, "BUNDESLÄNDER", _current_view is Views.States);
            RenderTitle(Map.Width + 6, 0, "ZEITRAHMEN & QUELLE", _current_view is Views.Source);
            RenderTitle(Map.Width + 35, 0, "HISTORISCHER VERLAUF", _current_view is Views.Historic);
            RenderTitle(Map.Width + 6, timeplot_height, "UMFRAGEERGEBNISSE", false);
            RenderTitle(3, Map.Height + 15, "OPTIONEN", _current_view is Views.Options);
        }
    }

    private void RenderMap()
    {
        MapColoring coloring = MapColoring.Default;
        HashSet<State> selected_states = [..from kvp in _selected_states
                                            where kvp.Value
                                            select kvp.Key];

        if (selected_states.Contains(State.BE_W) && selected_states.Contains(State.BE_O))
            selected_states.Add(State.BE);
        else
            selected_states.Remove(State.BE);

        //else if (selected_states.Contains(State.BE))
        //{
        //    selected_states.Add(State.BE_W);
        //    selected_states.Add(State.BE_O);
        //}

        Console.CurrentGraphicRendition = ConsoleGraphicRendition.Default with
        {
            ForegroundColor = ConsoleColor.White
        };

        if (_invalidate.HasFlag(RenderInvalidation.Map))
                Map.RenderToConsole(new(
                    _state_values.ToDictionary(LINQ.id, s => selected_states.Contains(s) ? (coloring.States[s].Color, 'X') : (ConsoleColor.DarkGray, '·'))
                ), 2, 2);

        int sel_width = Map.Width / 8;

        if (_invalidate.HasFlag(RenderInvalidation.StateSelector))
            foreach ((StateCursorPosition cursor, int index) in _state_cursor_values.WithIndex())
            {
                int x = 3 + (index % sel_width) * 8;
                int y = Map.Height + 5 + (index / sel_width) * 2;
                State state = (State)cursor;

                string txt = cursor switch
                {
                    StateCursorPosition.Federal => "BUND",
                    StateCursorPosition.Deselect => "KEIN",
                    StateCursorPosition.Invert => "INV.",
                    StateCursorPosition.West => "WEST",
                    StateCursorPosition.East => "OST",
                    StateCursorPosition.South => "SÜD",
                    StateCursorPosition.North => "NORD",
                    StateCursorPosition.Population => "BEV.",
                    StateCursorPosition.PopulationGrowth => "BEV+",
                    StateCursorPosition.Economy => "LFA+",
                    StateCursorPosition.BE_O => "O-BE",
                    StateCursorPosition.BE_W => "W-BE",
                    _ => state.ToString(),
                };

                if (!_selected_states.TryGetValue(state, out bool active))
                    if (cursor is StateCursorPosition.Federal)
                        active = _selected_states.Values.All(LINQ.id);
                    else if (cursor is StateCursorPosition.Deselect)
                        active = _selected_states.Values.All(v => !v);
                    else if (cursor is StateCursorPosition.West)
                        active = _selected_states.All(kvp => _state_values_west_ger.Contains(kvp.Key) == kvp.Value);
                    else if (cursor is StateCursorPosition.East)
                        active = _selected_states.All(kvp => _state_values_east_ger.Contains(kvp.Key) == kvp.Value);
                    else if (cursor is StateCursorPosition.South)
                        active = _selected_states.All(kvp => _state_values_south_ger.Contains(kvp.Key) == kvp.Value);
                    else if (cursor is StateCursorPosition.North)
                        active = _selected_states.All(kvp => _state_values_north_ger.Contains(kvp.Key) == kvp.Value);
                    else if (cursor is StateCursorPosition.Population)
                        active = _selected_states.All(kvp => _state_values_pop.Contains(kvp.Key) == kvp.Value);
                    else if (cursor is StateCursorPosition.PopulationGrowth)
                        active = _selected_states.All(kvp => _state_values_pop_growth.Contains(kvp.Key) == kvp.Value);
                    else if (cursor is StateCursorPosition.Economy)
                        active = _selected_states.All(kvp => _state_values_lfa.Contains(kvp.Key) == kvp.Value);
                    else
                    {
                        // TODO
                    }

                bool hovered = cursor == _state_cursor && _current_view == Views.States;

                RenderButton(x, y, 6, txt, _selected_states.ContainsKey(state) ? coloring.States[state].Color : ConsoleColor.White, active, hovered);
            }
    }

    private void RenderHistoricPlot(int width, int height, MergedPollHistory historic)
    {
        if (!_invalidate.HasFlag(RenderInvalidation.HistoricPlot))
            return;


        int left = Map.Width + 33;
        double max_perc = 1;
        DateOnly end_date = DateTime.UtcNow.ToDateOnly();
        DateOnly start_date = end_date;

        if (historic.PollCount > 0)
        {
            max_perc = historic.Polls.Max(p => p[p.StrongestParty]);
            start_date = historic.OldestPoll!.LatestDate;
            end_date = historic.MostRecentPoll!.LatestDate;
        }

        width -= left;

        if (_sixel_enabled)
        {
            RenderHistoricPlotSixel(width, height, historic, max_perc, start_date, end_date);

            return;
        }

        long start_ticks = start_date.ToDateTime().Ticks;
        long end_ticks = end_date.ToDateTime().Ticks;

        DateOnly get_date(double d)
        {
            d = double.IsFinite(d) ? double.Clamp(d, 0, 1) : 1;

            long t = (long)(d * (end_ticks - start_ticks)) + start_ticks;

            return new DateTime(t).ToDateOnly();
        }
        double get_xpos(DateOnly date) => (date.ToDateTime().Ticks - start_ticks) / (double)(end_ticks - start_ticks);

        int graph_height = height - 5;
        int graph_width = width - 15;

        for (int y = 0; y <= graph_height; ++y)
        {
            Console.SetCursorPosition(left + 2, 2 + y);

            if (y < graph_height)
            {
                Console.ForegroundColor = ConsoleColor.Default;
                Console.Write($"{(graph_height - y) * max_perc / graph_height,6:P1} ");
            }
            else
                Console.CursorLeft += 7;

            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write(y == 0 ? '┬' : y == graph_height ? '└' : '┼');

            if (y != graph_height)
                Console.ForegroundColor = _dark;

            Console.Write(new string(y == graph_height ? '─' : '·', graph_width + 1));
        }

        for (int index = 0, columns = graph_width / 9; index <= columns; ++index)
        {
            double d = index * 9d / graph_width;

            Console.SetCursorPosition(left + index * 9 + 10, graph_height + 2);
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write('┼');
            Console.SetCursorPosition(left + index * 9 + 7, graph_height + 3);
            Console.ForegroundColor = ConsoleColor.Default;
            Console.Write(get_date(d).ToString("yyyy-MM"));
        }

        Dictionary<Party, double> prev = Party.All.ToDictionary(LINQ.id, _ => 0d);
        int? selected_x = _selected_range?.Cursor is DateOnly sel ? (int)Math.Round(get_xpos(sel) * graph_width) : null;

        for (int x = 0; x <= graph_width; ++x)
        {
            DateOnly date = get_date((double)x / graph_width);
            MergedPoll? poll = historic.GetMostRecentAt(date);

            if (x == selected_x)
            {
                Console.ForegroundColor = poll?.StrongestParty.Color ?? ConsoleColor.White;

                for (int y = 0; y < graph_height; ++y)
                {
                    Console.SetCursorPosition(left + x + 10, y + 2);
                    Console.Write('│');
                }
            }

            if (poll is { })
                foreach ((Party party, double percentage) in (poll.Percentages as IEnumerable<(Party, double)>).Reverse())
                {
                    int y = (int)(graph_height * (1 - percentage / max_perc));
                    double ydiff = graph_height * (1 - percentage / max_perc) - y;

                    Console.SetCursorPosition(left + 10 + x, 2 + y);
                    Console.ForegroundColor = party.Color;

                    if (x == selected_x)
                        Console.Write("⬤"); // *⬤◯
                    else
                    {
                        int braille_right = (int)(ydiff * 4);

                        // TODO : fix subpixel rendering!!!!

#if ENABLE_HISTORIC_PLOT_SUBPIXEL_RENDERING
                        int braille_left = Math.Min(Math.Max(braille_right - percentage.CompareTo(prev[party]), 0), 4);
#else
                        int braille_left = braille_right;
#endif
                        char braille = (char)(0x2800
                                            | (braille_right switch
                                            {
                                                0 => 0b_0000_1000,
                                                1 => 0b_0001_0000,
                                                2 => 0b_0010_0000,
                                                3 => 0b_1000_0000,
                                                _ => 0b_0000_0000,
                                            })
                                            | (braille_left switch
                                            {
                                                0 => 0b_0000_0001,
                                                1 => 0b_0000_0010,
                                                2 => 0b_0000_0100,
                                                3 => 0b_0100_0000,
                                                _ => 0b_0000_0000,
                                            }));

                        Console.Write(braille);
                    }

                    prev[party] = percentage;
                }
        }
    }

    private void RenderHistoricPlotSixel(int width, int height, MergedPollHistory historic, double max_perc, DateOnly start_date, DateOnly end_date)
    {
        (_, _, int sixel_width, int sixel_height) = SixelImage.GetSixelSize(width - 15, height - 5);








    }

    private void RenderSourceSelection(int left, int width, int height)
    {
        if (!_invalidate.HasFlag(RenderInvalidation.DataSource))
            return;

        Console.ResetGraphicRenditions();
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteBlock($"""
        {PollHistory.Polls.Length,5} Umfragen zwischen
        {PollHistory.EarliestDate:yyyy-MM-dd} und {PollHistory.LatestDate:yyyy-MM-dd}
        verfügbar.
        """, left, 2);

        if (PollHistory.Polls.Length > 0)
        {
            int index = 0;

            foreach ((SourceCursorPosition cursor, string text) in new[]
            {
                (SourceCursorPosition.Button_Last40Years, "40y"),
                (SourceCursorPosition.Button_Last20Years, "20y"),
                (SourceCursorPosition.Button_Last16Years, "16y"),
                (SourceCursorPosition.Button_Last12Years, "12y"),
                (SourceCursorPosition.Button_Last8Years, "8 y"),
                (SourceCursorPosition.Button_Last4Years, "4 y"),
                (SourceCursorPosition.Button_Last1Year, "1 y"),
                (SourceCursorPosition.Button_Last6Months, "6 m"),
                (SourceCursorPosition.Button_Last3Months, "3 m"),
            })
            {
                RenderButton(
                    left + (index % 3) * 9,
                    6 + (index / 3) * 2,
                    7,
                    text,
                    ConsoleColor.White,
                    cursor == _current_source,
                    cursor == _source_cursor && _current_view is Views.Source
                );

                ++index;
            }


            bool active = _source_cursor is SourceCursorPosition.DateSelector && _current_view is Views.Source;

            RenderDateSelector(left, 12, "DATUM", 23, _selected_range?.Cursor, active);
        }
        else
        {
            // TODO : ?
        }


        // TODO : source


    }

    private void RenderResults(int width, int height, int timeplot_height, IPoll? poll)
    {
        int left = Map.Width + 6;
        int top = timeplot_height + 2;

        width -= left;
        height -= top;

        if (_invalidate.HasFlag(RenderInvalidation.PollResults))
        {
            Console.SetCursorPosition(left, top);
            Console.ResetGraphicRenditions();

            if (poll is { })
            {
                Console.Write($"Umfrageergebnis am {ConsoleColor.Yellow.ToVT520(ColorMode.Foreground)}{poll.Date:yyyy-MM-dd}{ConsoleColor.Default.ToVT520(ColorMode.Foreground)} für: ");
                Console.Write(string.Join(", ", from kvp in _selected_states
                                                where kvp.Value
                                                let color = MapColoring.Default.States[kvp.Key].Color
                                                select $"{color.ToVT520(ColorMode.Foreground)}{kvp.Key}{ConsoleColor.Default.ToVT520(ColorMode.Foreground)}"));

                if (Console.WindowWidth - 2 - Console.CursorLeft is int cw and > 0)
                    Console.Write(new string(' ', cw));

                Console.SetCursorPosition(left + 20, top + 1);
                Console.Write($"Umfragequelle: ");

                if (poll.PollingSource is string source)
                {
                    if (source.Length + Console.CursorLeft > Console.WindowWidth - 8)
                        source = source[..(Console.WindowWidth - 8 - Console.CursorLeft)] + "...";

                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.Write(source);
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.Write("(unknown)");
                }

                Console.ForegroundColor = ConsoleColor.Default;
                Console.Write(new string(' ', Console.WindowWidth - 2 - Console.CursorLeft));
            }

            foreach ((Party party, int index) in Party.All.WithIndex())
                RenderPartyResult(left, top + 3 + index, width, poll, party);
        }

        top += 5 + Party.All.Length;

        if (_invalidate.HasFlag(RenderInvalidation.Compass))
        {
            Console.SetCursorPosition(left, top);
            Console.ForegroundColor = ConsoleColor.Default;
            Console.Write("Politischer Kompass:");
        }

        int vertical_space = height + timeplot_height - top;
        (int coalition_x, _) = RenderCompass(left, top + 2, vertical_space - 4, poll);

        coalition_x += 6;

        if (_invalidate.HasFlag(RenderInvalidation.Coalitions))
        {
            Console.SetCursorPosition(left + coalition_x, top);
            Console.ForegroundColor = ConsoleColor.Default;
            Console.Write("Sitzverteilung:");

            int seat_width = width - coalition_x - 35;

            seat_width = (seat_width / 2 - 1) * 2 + 3;

            if (poll is { })
            {
                (Party party, double perc)[] seats = [.. from p in Party.LeftToRight
                                                         let perc = poll[p]
                                                         where perc > .05
                                                         select (p, perc)];
                double total = seats.Sum(p => p.perc);

                for (int i = 0; i < seats.Length; ++i)
                    seats[i].perc *= seat_width / total;

                Console.SetCursorPosition(left + coalition_x, top + 2);

                foreach ((Party party, double perc) in seats)
                {
                    Console.ForegroundColor = party.Color;
                    Console.Write(new string('⣿', (int)double.Round(perc)));
                }

                int offs = Console.CursorLeft - left - coalition_x - seat_width;

                if (offs < 0)
                    Console.Write('⣿');
                else if (offs > 0)
                {
                    --Console.CursorLeft;
                    Console.Write(' ');
                }

                Console.Write("  ");
            }
            else
            {
                Console.SetCursorPosition(left + coalition_x, top + 2);
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write($"¦{new string('·', seat_width - 2)}¦");
            }

            Console.SetCursorPosition(left + coalition_x, top + 3);
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write($"└{new string('─', seat_width / 2 - 1)}┴{new string('─', seat_width / 2 - 1)}┘");
        }

        top += 5;
        vertical_space -= 5;

        if (_invalidate.HasFlag(RenderInvalidation.Coalitions))
        {
            Console.SetCursorPosition(left + coalition_x, top);
            Console.ForegroundColor = ConsoleColor.Default;
            Console.Write("Koalitionsmöglichkeiten:");

            Coalition[] coalitions = poll is { } ? Coalitions.Select(parties => new Coalition(poll, parties))
                                                             .Where(c => c.CoalitionPercentage >  .20 & c.CoalitionParties.Length >= 1) // filter coalitions where all other parties are < 5%
                                                             .OrderByDescending(c => c.CoalitionPercentage)
                                                             .Distinct()
                                                             .Take(vertical_space)
                                                             .ToArray() : [];

            foreach ((Coalition coalition, int index) in coalitions.WithIndex())
                RenderCoalition(left + coalition_x, top + index + 2, width - coalition_x - 5, poll is { } ? coalition : null);

            for (int i = coalitions.Length; i < vertical_space - 2; ++i)
            {
                Console.SetCursorPosition(left + coalition_x, top + i + 2);
                Console.Write(new string(' ', width - coalition_x - 5));
            }
        }
    }

    private (int width, int height) RenderCompass(int left, int top, int height, IPoll? poll)
    {
        while (height % 2 != 1 || ((height - 2) / 2) % 2 != 1)
            ++height;

        int width = (height - 3) / 2 * 4 + 5;

        if (_invalidate.HasFlag(RenderInvalidation.Compass))
        {
            RenderBox(left, top, width, height, false, ConsoleColor.DarkGray);

            Console.ForegroundColor = ConsoleColor.Gray;

            for (int y = 1; y < height - 1; ++y)
                for (int x = 1; x < width - 1; ++x)
                {
                    Console.SetCursorPosition(left + x, top + y);
                    Console.ForegroundColor = y == height / 2 || x == width / 2 ? ConsoleColor.DarkGray : _dark;
                    Console.Write((x % 4, y % 2) switch
                    {
                        (0, 0) => '+', // ┼
                        (_, 0) => '-', // ┄
                        (0, 1) => '¦', // ┆
                        _ => ' ',
                    });
                }

            void render_compas_dot(double lr, double al, ConsoleColor color, string dot)
            {
                int x = (int)Math.Round((double.Clamp(lr, -1, 1) + 1) * .5 * (width - 3));
                int y = (int)Math.Round((double.Clamp(al, -1, 1) + 1) * .5 * (height - 3));

                Console.SetCursorPosition(left + x + 1, top + y + 1);
                Console.ForegroundColor = color;
                Console.Write(dot);
            }

            double lr_axis = 0;
            double al_axis = 0;

            if (poll is { })
                foreach (Party party in Party.All)
                {
                    double perc = poll[party];

                    lr_axis += perc * party.EconomicLeftRightAxis;
                    al_axis += perc * party.AuthoritarianLibertarianAxis;

                    render_compas_dot(party.EconomicLeftRightAxis, party.AuthoritarianLibertarianAxis, party.Color, width < 30 ? "*" : "◯");
                }

            render_compas_dot(lr_axis, al_axis, ConsoleColor.White, "⬤");
        }

        return (width, height);
    }

    private static void RenderPartyResult(int left, int top, int width, IPoll? poll, Party party)
    {
        width -= 21;

        Console.ResetGraphicRenditions();
        Console.SetCursorPosition(left, top);
        Console.Write("\e[m" + party.Identifier.ToString().ToUpper());

        double percentage = poll?[party] ?? 0;
        string status = percentage switch
        {
            > .666 => "\e[92m⬤⬤⬤",
            >= .50 => "\e[90m◯\e[92m⬤⬤",
            >= .33 => "\e[90m◯◯\e[92m⬤",
            >= .05 => "\e[90m◯\e[33m⬤\e[90m◯",
            _ => "\e[31m⬤\e[90m◯◯",
        };

        if (party == poll?.StrongestParty)
            status += "\e[94m⬤";
        else
            status += "\e[90m◯";

        Console.CursorLeft = left + 5;
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write(new string('·', width));
        Console.ForegroundColor = ConsoleColor.Default;
        Console.Write($" {percentage,6:P1}  {status}");

        Console.CursorLeft = left + 5 + (int)Math.Round((width - 1) * .05); // 5%-Hürde
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write('¦');

        for (double d = 0; d <= 1; d += .125)
        {
            Console.CursorLeft = left + 5 + (int)Math.Round((width - 1) * d);
            Console.Write(d is 0 or 1 or .5 ? '¦' : ':');
        }

        int w = (int)(percentage * width);
        char end = " ⡀⡄⡆⡇⣇⣧⣷"[(int)(8 * (percentage * width - w))];

        Console.SetCursorPosition(left + 5, top);
        Console.ForegroundColor = party.Color;
        Console.Write((new string('⣿', w) + end).TrimEnd());
    }

    private static void RenderCoalition(int left, int top, int width, Coalition? coalition)
    {
        Console.SetCursorPosition(left, top);
        Console.Write(new string(' ', width));

        width -= 30;

        Console.CursorLeft = left;
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write($"└{new string('─', width - 2)}┘");

        if (coalition?.CoalitionPercentage is double perc and > 0)
        {
            // TODO : seamless coloring of percentages. maybe add traffic-light-style indicator?
            Console.ForegroundColor = perc switch
            {
                >= .5 => ConsoleColor.Green,
                >= .33 => ConsoleColor.Yellow,
                >= .25 => ConsoleColor.DarkYellow,
                _ => ConsoleColor.Red,
            };
            Console.Write($" {perc,6:P1} ");
        }
        else
            Console.Write("        ");

        Console.ForegroundColor = ConsoleColor.Gray;
        Console.Write('(');
        Console.Write(coalition?.CoalitionParties?.Select(static party => $"{party.Color.ToVT520(ColorMode.Foreground)}{party.Identifier.ToString().ToUpper()}\e[m").StringJoin(", "));
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.Write(')');
        Console.ForegroundColor = ConsoleColor.DarkGray;

        foreach (double d in new[] { .33, .5, .66 })
        {
            Console.CursorLeft = left + (int)(width * d) - 1;
            Console.Write('┴');
        }

        Console.CursorLeft = left;

        if (coalition is { })
            foreach (Party party in coalition.CoalitionParties)
            {
                int w = (int)double.Round(coalition[party] * width);

                Console.ForegroundColor = party.Color;
                Console.Write(new string('━', w));
            }
    }

    private void RenderOptions(int top, int width)
    {
        if (!_invalidate.HasFlag(RenderInvalidation.Options))
            return;

        int left = 3;

        Console.SetCursorPosition(3, top);
        Console.ResetGraphicRenditions();
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write("Sixel Rendering Unterstützung: ");

        if (_sixel_supported)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write("JA");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Write("NEIN");
        }

        Console.SetCursorPosition(3, top + 1);
        Console.ResetGraphicRenditions();
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write("       GDI32/GDI+ installiert: ");

        if (LibGDIPlusInstaller.IsGDIInstalled)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write("JA");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Write("NEIN");
        }

        Console.SetCursorPosition(3, top + 2);
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write("              Sixel Rendering: ");

        RenderButton(left + 31, top + 2, 9, _sixel_enabled ? "AN" : "AUS", ConsoleColor.Default, _sixel_enabled, _current_view is Views.Options && _option_cursor is OptionsCursorPosition.SixelRendering);
        RenderButton(left, top + 4, 40, "DATENGRUNDLAGE/UMFRAGEN AKTUALISIEREN", ConsoleColor.Default, false, _current_view is Views.Options && _option_cursor is OptionsCursorPosition.RefreshData);
    }

    private static RenderInvalidation GetRenderInvalidation(Views view) => view switch
    {
        Views.States => RenderInvalidation.StateSelector
                      | RenderInvalidation.Map,
        Views.Source => RenderInvalidation.DataSource,
        Views.Historic => RenderInvalidation.HistoricPlot
                        | RenderInvalidation.PollResults
                        | RenderInvalidation.Compass
                        | RenderInvalidation.Coalitions,
        Views.Options => RenderInvalidation.Options,
        _ => RenderInvalidation.None,
    };

    public async Task HandleInput(ConsoleKeyInfo key)
    {
        if (_too_small)
            return;

        async Task<RenderInvalidation> process()
        {
            switch ((key.Key, _current_view))
            {
                case (KEY_VIEW_SWITCH, _):
                    int dir = key.Modifiers.HasFlag(ConsoleModifiers.Shift) ? -1 : 1;
                    int count = Enum.GetValues<Views>().Length;
                    RenderInvalidation invalidation = RenderInvalidation.FrameText;

                    invalidation |= GetRenderInvalidation(_current_view);
                    _current_view = (Views)(((int)_current_view + dir + count) % count);
                    invalidation |= GetRenderInvalidation(_current_view);

                    return invalidation;

                #region STATE SELECTION

                case (KEY_RIGHT, Views.States):
                    _state_cursor = _state_cursor_values[(_state_cursor_values.IndexOf(_state_cursor) + 1) % _state_cursor_values.Length];

                    return RenderInvalidation.StateSelector;
                case (KEY_LEFT, Views.States):
                    _state_cursor = _state_cursor_values[(_state_cursor_values.IndexOf(_state_cursor) - 1 + _state_cursor_values.Length) % _state_cursor_values.Length];

                    return RenderInvalidation.StateSelector;
                case (KEY_DOWN, Views.States): // TODO : implement
                    return RenderInvalidation.StateSelector;
                case (KEY_UP, Views.States): // TODO : implement
                    return RenderInvalidation.StateSelector;
                case (KEY_ENTER, Views.States):
                    if (_state_cursor is StateCursorPosition.Invert)
                        foreach (State state in _state_values)
                            _selected_states[state] ^= true;
                    else if (_state_values.Contains((State)_state_cursor))
                    {
                        _selected_states[(State)_state_cursor] ^= true;

                        if (_state_cursor is StateCursorPosition.BE)
                            _selected_states[State.BE_W] =
                            _selected_states[State.BE_O] = _selected_states[State.BE];
                    }
                    else
                    {
                        State[] target_states = _state_cursor switch
                        {
                            StateCursorPosition.Federal => _state_values,
                            StateCursorPosition.Deselect => [],
                            StateCursorPosition.West => _state_values_west_ger,
                            StateCursorPosition.East => _state_values_east_ger,
                            StateCursorPosition.North => _state_values_north_ger,
                            StateCursorPosition.South => _state_values_south_ger,
                            StateCursorPosition.Population => _state_values_pop,
                            StateCursorPosition.PopulationGrowth => _state_values_pop_growth,
                            StateCursorPosition.Economy => _state_values_lfa,
                        };

                        foreach (State state in _state_values)
                            _selected_states[state] = target_states.Contains(state);
                    }

                    return GetRenderInvalidation(Views.Historic)
                         | GetRenderInvalidation(Views.States);

                #endregion
                #region SOURCE / DATE SELECTION

                case (KEY_LEFT or KEY_RIGHT, Views.Source):
                    {
                        int offs = key.Key == KEY_LEFT ? -1 : 1;

                        _source_cursor = _source_cursor < SourceCursorPosition.DateSelector ? _source_cursor + offs : _source_cursor;

                        return GetRenderInvalidation(Views.Source);
                    }
                case (KEY_ENTER, Views.Source) when _source_cursor <= SourceCursorPosition.Button_Last3Months:
                    {
                        bool changed = _source_cursor != _current_source;

                        _current_source = _source_cursor;

                        if (PollHistory.LatestDate is { } end)
                        {
                            (int years, int months) = _current_source switch
                            {
                                SourceCursorPosition.Button_Last40Years => (40, 0),
                                SourceCursorPosition.Button_Last20Years => (20, 0),
                                SourceCursorPosition.Button_Last16Years => (16, 0),
                                SourceCursorPosition.Button_Last12Years => (12, 0),
                                SourceCursorPosition.Button_Last8Years => (8, 0),
                                SourceCursorPosition.Button_Last4Years => (4, 0),
                                SourceCursorPosition.Button_Last1Year => (1, 0),
                                SourceCursorPosition.Button_Last6Months => (0, 6),
                                SourceCursorPosition.Button_Last3Months => (0, 3),
                                _ => (0, 0),
                            };

                            _selected_range = (
                                end.AddYears(-years).AddMonths(-months),
                                end,
                                _selected_range?.Cursor ?? end
                            );
                        }

                        AdjustTimeRanges();

                        // TODO : invalidate only if dates have changed
                        return changed ? GetRenderInvalidation(Views.Historic)
                                       | GetRenderInvalidation(Views.Source)
                                       : RenderInvalidation.None;
                    }

                #endregion
                #region HISTORIC PLOT

                case (KEY_RIGHT, Views.Historic):
                case (KEY_LEFT, Views.Historic):
                    {
                        if (_selected_range is (DateOnly min, DateOnly max, DateOnly cursor))
                        {
                            _selected_range = (
                                min,
                                max,
                                (key.Key == KEY_LEFT ? PollHistory.GetPreviousDate(cursor) : PollHistory.GetNextDate(cursor)) ?? cursor
                            );

                            AdjustTimeRanges();

                            if (cursor != _selected_range.Value.Cursor)
                                return GetRenderInvalidation(Views.Historic)
                                     | GetRenderInvalidation(Views.Source);
                        }

                        return RenderInvalidation.None;
                    }
                case (KEY_HOME or KEY_END, Views.Historic):
                    {
                        bool changed = false;

                        if (_selected_range is (DateOnly start, DateOnly end, DateOnly prev))
                        {
                            _selected_range = (start, end, key.Key is KEY_HOME ? start : end);
                            changed = prev != _selected_range?.Cursor;
                        }

                        return changed ? GetRenderInvalidation(Views.Historic)
                                       : RenderInvalidation.None;
                    }

                #endregion
                #region OPTIONS

                case (KEY_UP, Views.Options):
                case (KEY_DOWN, Views.Options):
                    {
                        int offs = key.Key == KEY_UP ? -1 : 1;
                        int max = Enum.GetValues<OptionsCursorPosition>().Select(v => (int)v).Max() + 1;

                        _option_cursor = (OptionsCursorPosition)(((int)_option_cursor + max + offs) % max);

                        return GetRenderInvalidation(Views.Options);
                    }
                case (KEY_ENTER, Views.Options):
                    switch (_option_cursor)
                    {
                        case OptionsCursorPosition.SixelRendering:
                            _sixel_enabled ^= true;

                            if (_sixel_enabled && !LibGDIPlusInstaller.IsGDIInstalled)
                            {
                                await RenderModalPromptUntilKeyPress(
                                    """
                                    Sixel Rendering ist zwar eine rein text-basierte Bildgebungsmethode, jedoch wird für dieses Programm im Hintergrund
                                    zum internen Rendering die Grafikbibliothek 'libgdiplus.so' benötigt. Diese scheint jedoch auf Ihrem System nicht
                                    installiert zu sein. Bitte führen Sie zur Installation der Grafikbibliothek die folgenden Befehle im Terminal aus:

                                    Linux:
                                        $ sudo apt update
                                        $ sudo apt install libx11-dev libc6-dev
                                        $ sudo apt install libgdiplus

                                    MacOS
                                        $ /bin/bash -c "$(curl -fsSL https://raw.githubusercontent.com/Homebrew/install/HEAD/install.sh)"
                                        $ brew install mono-libgdiplus

                                    Drücken Sie eine beliebige Taste, um diesen Warnhinweis wieder zu schließen.
                                    Bitte beachten Sie, dass das Sixel Rendering hierbei nicht aktiviert wird.
                                    """,
                                    ConsoleColor.Red,
                                    ConsoleColor.DarkRed,
                                    ModalPromptIcon.Exclamation
                                );

                                _sixel_enabled = false;
                            }

                            return GetRenderInvalidation(Views.Options)
                                 | RenderInvalidation.HistoricPlot;
                        case OptionsCursorPosition.RefreshData:
                            _option_cursor = OptionsCursorPosition.SixelRendering;
                            _current_view = Views.States;

                            await PollFetcher.PollDatabase.Clear();

                            PollFetcher.PollDatabase.Save();

                            await FetchAllPollsAsync();

                            goto default;
                        default:
                            return RenderInvalidation.None;
                    }

                #endregion

                default:
                    return RenderInvalidation.None;
            }
        }

        Invalidate(await process());
        Render(false);
    }
}

// TODO : dark/light mode switch
// TODO : UTF-8/ASCII mode switch
