using System.Collections.Generic;
using System.Threading.Tasks;
using System.Text;
using System.Linq;
using System;

using Unknown6656.Generics;
using Unknown6656.Runtime;
using Unknown6656.Console;
using Unknown6656.Common;

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
    Result,
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
    //0b_00000000_00000000_00000010_00000000,
}

public sealed class Renderer
    : IDisposable
{
    #region ONLY FOR CACHING REASONS

    private static readonly StateCursorPosition[] _state_cursor_values = Enum.GetValues<StateCursorPosition>();
    private static readonly State[] _state_values = Enum.GetValues<State>();
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
        [RenderSize.Small] = (155, 55),
        [RenderSize.Medium] = (170, 71),
        [RenderSize.Large] = (190, 99),
    };

    public const string CACHE_FILE = "poll-cache.bin";
    public const ConsoleKey KEY_VIEW_SWITCH = ConsoleKey.Tab;
    public const ConsoleKey KEY_STATE_ENTER = ConsoleKey.Enter;
    public const ConsoleKey KEY_RIGHT = ConsoleKey.RightArrow;
    public const ConsoleKey KEY_LEFT = ConsoleKey.LeftArrow;
    public const ConsoleKey KEY_UP = ConsoleKey.UpArrow;
    public const ConsoleKey KEY_DOWN = ConsoleKey.DownArrow;

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
        [Party.SPD, Party.PIRATEN, Party.GRÜNE],
        [Party.SPD, Party.PIRATEN, Party.LINKE, Party.GRÜNE],
        [Party.FDP, Party.GRÜNE, Party.PIRATEN],
        [Party.GRÜNE, Party.PIRATEN],
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


    private DateTime? _start_date;
    private DateTime? _end_date;
    private DateTime? _selected_start_date;
    private DateTime? _selected_end_date;
    private readonly Dictionary<State, bool> _selected_states = _state_values.ToDictionary(LINQ.id, s => true);
    private readonly ConsoleState _console_state;

    private StateCursorPosition _state_cursor = StateCursorPosition.Federal;
    private SourceCursorPosition _source_cursor = SourceCursorPosition.StartSelector;
    private Views _current_view = Views.States;
    private RenderInvalidation _invalidate;
    private RenderSize _render_size;


    public bool IsActive { get; private set; } = true;

    public PollResult? Polls { get; private set; } = null;

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
        RenderSize.Small => Map.SmallMap,
        RenderSize.Medium => Map.MediumMap,
        RenderSize.Large => Map.LargeMap,
    };


    public Renderer()
    {
        _console_state = Console.CurrentConsoleState;

        InvalidateAll();

        Console.HardResetAndFullClear();
        Console.OutputEncoding = Encoding.UTF8;
        Console.InputEncoding = Encoding.UTF8;
        Console.CursorVisible = false;

        PollFetcher = new(new(CACHE_FILE));
    }

    ~Renderer() => Dispose(false);

    public void InvalidateAll() => Invalidate(~RenderInvalidation.None);

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
        if (clear)
        {
            InvalidateAll();
            Console.FullClear();
        }

        (int min_width, int min_height) = _min_sizes[_render_size];
        int width = Console.WindowWidth;
        int height = Console.WindowHeight;

        Console.ForegroundColor = ConsoleColor.White;
        Console.BackgroundColor = ConsoleColor.Black;

        if (OS.IsWindows)
        {
            Console.BufferHeight = Math.Min(Console.BufferHeight, height + 4);
            Console.BufferWidth = Math.Min(Console.BufferWidth, width + 2);
        }

        if (width < min_width || height < min_height)
            if (_render_size is RenderSize.Small)
            {
                InvalidateAll();
                Console.FullClear();
                Console.CurrentGraphicRendition = new()
                {
                    ForegroundColor = ConsoleColor.Red,
                    Intensity = TextIntensityMode.Bold,
                };
                Console.WriteLine($"""
                 ┌─────────────────────────────────────────────┐
                 │    ⚠️ ⚠️ CONSOLE WINDOW TOO SMALL ⚠️ ⚠️     │
                 ├─────────────────────────────────────────────┤
                 │ Please resize this window to a minimum size │
                 │ of {min_width,3} x {min_height,2}. Current window size: {width,3} x {height,2}  │
                 │ You may alternatively reduce the font size. │
                 └─────────────────────────────────────────────┘
                """);
            }
            else
                --CurrentRenderSize;
        else if (_render_size < RenderSize.Large && width >= _min_sizes[_render_size + 1].MinWidth && height >= _min_sizes[_render_size + 1].MinHeight)
            ++CurrentRenderSize;
        else
        {
            int timeplot_height = (int)double.Clamp(height * height * .006, 20, height * .4);
            (PollHistory historic, MergedPoll? display) = FetchPolls();

            RenderFrame(width, height, timeplot_height, clear);
            RenderMap();
            RenderSourceSelection(Map.Width + 6, 30, timeplot_height);
            RenderHistoricPlot(width, timeplot_height, historic);
            RenderResults(width, height, timeplot_height, display);

            Console.Write("\e[m");

            _invalidate = RenderInvalidation.None;
        }
    }

    private (PollHistory Historic, MergedPoll? Display) FetchPolls()
    {
        List<State?> states = _selected_states.SelectWhere(kvp => kvp.Value, kvp => (State?)kvp.Key).ToList();

        if (_state_values.All(s => states.Contains(s)))
            states.Add(null);
        else if (states.Contains(State.BE))
            states.AddRange([State.BE_W, State.BE_O]);

        if (Polls?.Polls is { Length: > 0 } polls)
        {
            PollHistory history = new(from p in polls
                                      where p.Date >= _start_date
                                         && p.Date <= _end_date
                                         && states.Contains(p.State)
                                      group p by p.Date into g
                                      select new MergedPoll([.. g]));


            MergedPoll? display = history.Polls.LastOrDefault(); // TODO <--- make this based on the current date selection


            return (history, display);
        }
        else
            return (PollHistory.Empty, null);
    }

    public async Task FetchPollsAsync()
    {
        Polls = await RenderFetchingPrompt(PollFetcher.FetchAsync);

        if (Polls.Polls is { Length:> 0 } polls)
        {
            _start_date = polls[^1].Date;
            _end_date = polls[0].Date;

            _selected_start_date ??= _start_date;
            _selected_end_date ??= _end_date;
            // TODO : adjust start and end date 
        }
        else
            _start_date = _end_date = _selected_start_date = _selected_end_date = null;
    }

    public async Task<PollResult> RenderFetchingPrompt(Func<Task<PollResult>> task)
    {
        (int x, int y) = RenderModalPrompt("Umfrageergebnisse werden geladen...\nBitte warten.", "\e[0;96m", "\e[0;36m");

        Console.Write("\e[96m");

        bool completed = false;
        using Task spinner = Task.Factory.StartNew(async delegate
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

                Console.CursorLeft = x + 4;
                Console.CursorTop = y + 2;
                Console.Write(TL[step]);
                Console.Write(TM[step]);
                Console.Write(TR[step]);
                Console.CursorLeft = x + 4;
                Console.CursorTop = y + 3;
                Console.Write(BL[step]);
                Console.Write(BM[step]);
                Console.Write(BR[step]);

                await Task.Delay(50);
            }
        });

        PollResult result = await task();
        completed = true;

        await spinner;

        RenderModalPrompt($"{result.PollCount} Umfrageergebnisse wurden erfolgreich geladen.\nZum Starten bitte eine beliebige Taste drücken.", "\e[0;92m", "\e[0;32m");

        Console.ReadKey(true);
        Render(true);

        return result;
    }

    private (int x, int y) RenderModalPrompt(string content, string foreground, string background)
    {
        int width = Console.WindowWidth;
        int height = Console.WindowHeight;

        Console.Write(background);

        for (int _y = 1; _y < height - 1; ++_y)
        {
            Console.CursorTop = _y;

            for (int _x = _y % 2 + 1; _x < width - 1; _x += 2)
            {
                Console.CursorLeft = _x;
                Console.Write('/');
            }
        }

        string[] prompt = content.SplitIntoLines();
        int prompt_width = prompt.Max(s => s.Length + 12);

        if (prompt.Length == 1)
            prompt = [prompt[0], ""];

        prompt = [
            $"┌{new string('─', prompt_width)}┐",
            $"│{new string(' ', prompt_width)}│",
            ..prompt.Select((line, i) => $"│   {i switch { 0 => "╱i╲", 1 => "‾‾‾", _ => "   " }}   {line.PadRight(prompt_width - 12)}   │"),
            $"│{new string(' ', prompt_width)}│",
            $"└{new string('─', prompt_width)}┘",
        ];

        int x = (width - prompt_width - 2) / 2;
        int y = (height - prompt.Length) / 2;

        for (int i = 0; i < prompt.Length; ++i)
        {
            Console.CursorLeft = x;
            Console.CursorTop = y + i;
            Console.Write(prompt[i]);
        }

        return (x, y);
    }

    private static void RenderTitle(int x, int y, string title, bool active)
    {
        Console.CursorLeft = x;
        Console.CursorTop = y;
        Console.Write($" {(active ? "\e[1;4;91m" : "\e[96m")}{title}\e[22;24m ");
    }

    private static void RenderFrameLine(int x, int y, int size, bool horizontal)
    {
        (char start, char mid, char end) = horizontal ? ('├', '─', '┤') : ('┬', '│', '┴');
        string line = start + new string(mid, size - 2) + end;

        if (horizontal)
        {
            Console.CursorLeft = x;
            Console.CursorTop = y;
            Console.Write(line);
        }
        else
            for (int i = 0; i < size; ++i)
            {
                Console.CursorLeft = x;
                Console.CursorTop = y + i;
                Console.Write(line[i]);
            }
    }

    private static void RenderBox(int x, int y, int width, int height, bool clear, string color = "\e[0;97m")
    {
        Console.CursorTop = y;
        Console.CursorLeft = x;
        Console.Write($"{color}┌{new string('─', width - 2)}┐");

        for (int i = 1; i < height - 1; ++i)
        {
            Console.CursorTop = y + i;
            Console.CursorLeft = x;

            if (clear)
                Console.Write($"│{new string(' ', width - 2)}│");
            else
            {
                Console.Write('│');
                Console.CursorLeft = x + width - 1;
                Console.Write('│');
            }
        }

        Console.CursorTop = y + height - 1;
        Console.CursorLeft = x;
        Console.Write($"└{new string('─', width - 2)}┘");
    }

    private static void RenderButton(int x, int y, int? width, string text, string color, bool active, bool? hover)
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
        Console.Write($"{color}{(active ? "\e[7m" : "")}{text}\e[27m");

        RenderHoverUnderline(x, y + 1, width.Value, hover);
    }

    private static void RenderDateSelector(int x, int y, string description, int width, DateTime? date, DateTime? min, DateTime? max, bool? hover)
    {
        width -= 15;
        description = description.PadLeft(width);

        Console.SetCursorPosition(x, y);
        Console.Write($"\e[m{description}  ");

        if (hover ?? false)
            Console.Write("\e[7m");

        Console.Write(hover is true && date is { } && min is { } && date > min ? '<' : '[');
        Console.Write($" {date?.ToString("yyyy-MM-dd") ?? "xxxx-xx-xx"} ");
        Console.Write(hover is true && date is { } && max is { } && date < max ? '>' : ']');
        Console.Write("\e[27m");

        RenderHoverUnderline(x + description.Length + 2, y + 1, 14, hover);
    }

    private static void RenderHoverUnderline(int x, int y, int width, bool? hover = true)
    {
        if (hover is bool h)
        {
            Console.SetCursorPosition(x, y);
            Console.Write(h ? $"\e[5;90m{new(/*'°'*/'^', width)}\e[25m" : new(' ', width));
        }
    }

    private void RenderFrame(int width, int height, int timeplot_height, bool clear)
    {
        if (_invalidate.HasFlag(RenderInvalidation.FrameBorder))
        {
            RenderBox(0, 0, width, height, clear);

            RenderFrameLine(Map.Width + 3, 0, height, false);
            RenderFrameLine(0, Map.Height + 3, Map.Width + 4, true);
            RenderFrameLine(Map.Width + 3, timeplot_height, width - Map.Width - 3, true);
            RenderFrameLine(Map.Width + 32, 0, timeplot_height + 1, false);
            //RenderFrameLine(Map.Width + 3, 0, Map.Width + 4, false);
        }

        if (_invalidate.HasFlag(RenderInvalidation.FrameText))
        {
            RenderTitle(3, 0, "ÜBERSICHTSKARTE DEUTSCHLAND", false);
            RenderTitle(3, Map.Height + 3, "BUNDESLÄNDER", _current_view is Views.States);
            RenderTitle(Map.Width + 6, 0, "ZEITRAHMEN & QUELLE", _current_view is Views.Source);
            RenderTitle(Map.Width + 35, 0, "HISTORISCHER VERLAUF", _current_view is Views.Historic);
            RenderTitle(Map.Width + 6, timeplot_height, "UMFRAGEERGEBNISSE", _current_view is Views.Result);
        }
    }

    private void RenderMap()
    {
        MapColoring coloring = MapColoring.Default;

        Console.ResetColor();
        Console.ForegroundColor = ConsoleColor.White;

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

        if (_invalidate.HasFlag(RenderInvalidation.Map))
                Map.RenderToConsole(new(
                    _state_values.ToDictionary(LINQ.id, s => selected_states.Contains(s) ? (coloring.States[s].Color, 'X') : ("\e[90m", '·'))
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

                RenderButton(x, y, 6, txt, _selected_states.ContainsKey(state) ? coloring.States[state].Color : "\e[97m", active, hovered);
            }
    }

    private void RenderHistoricPlot(int width, int height, PollHistory historic)
    {
        if (!_invalidate.HasFlag(RenderInvalidation.HistoricPlot))
            return;

        double max_perc = 1;
        DateTime end_date = DateTime.UtcNow;
        DateTime start_date = end_date;
        int left = Map.Width + 33;

        width -= left;

        if (historic.PollCount > 0)
        {
            max_perc = historic.Polls.Max(p => p[p.StrongestParty]);
            start_date = historic.OldestPoll!.LatestDate;
            end_date = historic.MostRecentPoll!.LatestDate;
        }

        DateTime get_date(double d)
        {
            d = double.IsFinite(d) ? double.Clamp(d, 0, 1) : 1;

            long t = (long)(d * (end_date.Ticks - start_date.Ticks)) + start_date.Ticks;

            return new(t);
        }

        int graph_height = height - 5;
        int graph_width = width - 15;

        for (int y = 0; y <= graph_height; ++y)
        {
            Console.CursorLeft = left + 2;
            Console.CursorTop = 2 + y;

            if (y < graph_height)
                Console.Write($"\e[m{(graph_height - y) * max_perc / graph_height,6:P1} ");
            else
                Console.CursorLeft += 7;

            Console.Write($"\e[38;2;80;80;80m{(y == 0 ? '┬' : y == graph_height ? '├' : '┼')}{new string(y == graph_height ? '─' : '·', graph_width)}");
        }

        for (int index = 0, columns = graph_width / 9; index <= columns; ++index)
        {
            double d = index * 9d / graph_width;

            Console.CursorTop = graph_height + 2;
            Console.CursorLeft = left + index * 9 + 9;
            Console.Write("\e[38;2;80;80;80m" + (index == 0 ? '├' : '┼'));
            Console.CursorTop = graph_height + 3;
            Console.CursorLeft = left + index * 9 + 6;
            Console.Write($"\e[m{get_date(d):yyyy-MM}");
        }

        Dictionary<Party, double> prev = Party.All.ToDictionary(LINQ.id, _ => 0d);
        DateTime dateselector = get_date(.75); // <--- TODO : change this

        for (int x = 0; x <= graph_width; ++x)
        {
            DateTime date = get_date((double)x / graph_width);
            MergedPoll? poll = historic.GetMostRecentAt(date);
            bool current = dateselector < date;

            if (current)
            {
                dateselector = DateTime.MaxValue;

                Console.Write(poll?.StrongestParty.VT100Color ?? "\e[97m");

                for (int y = 0; y < graph_height; ++y)
                {
                    Console.CursorTop = 2 + y;
                    Console.CursorLeft = left + 9 + x;
                    Console.Write('│');
                }
            }

            if (poll is { })
                foreach ((Party party, double percentage) in poll.Percentages.Reverse())
                {
                    int y = (int)(graph_height * (1 - percentage));
                    double ydiff = graph_height * (1 - percentage) - y;

                    Console.CursorTop = 2 + y;
                    Console.CursorLeft = left + 9 + x;
                    Console.Write(party.VT100Color);

                    if (current)
                        Console.Write("⬤"); // *⬤◯
                    else
                    {
                        int braille_right = (int)(ydiff * 4);
                        int braille_left = Math.Min(Math.Max(braille_right - percentage.CompareTo(prev[party]), 0), 4);
                        char braille = (char)(0x2800
                                            | (braille_right switch
                                            {
                                                0 => 0b_1000_0000,
                                                1 => 0b_0010_0000,
                                                2 => 0b_0001_0000,
                                                3 => 0b_0000_1000,
                                                _ => 0b_0000_0000,
                                            })
                                            | (braille_left switch
                                            {
                                                0 => 0b_0100_0000,
                                                1 => 0b_0000_0100,
                                                2 => 0b_0000_0010,
                                                3 => 0b_0000_0001,
                                                _ => 0b_0000_0000,
                                            }));

                        Console.Write(braille);
                    }

                    prev[party] = percentage;
                }
        }
    }

    private void RenderSourceSelection(int left, int width, int height)
    {
        if (!_invalidate.HasFlag(RenderInvalidation.DataSource))
            return;

        Console.ResetGraphicRenditions();
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteBlock($"""
        {Polls?.Polls?.Length ?? 0} Umfragen zwischen   
        {_start_date:yyyy-MM-dd} und {_end_date:yyyy-MM-dd}
        verfügbar.
        """, left, 2);

        if (Polls?.Polls is { Length: > 0 } polls)
        {
            bool active = _source_cursor is SourceCursorPosition.StartSelector && _current_view is Views.Source;

            RenderDateSelector(left, 6, "START", 23, _selected_start_date, _start_date, _end_date, active);

            active = _source_cursor is SourceCursorPosition.EndSelector && _current_view is Views.Source;

            RenderDateSelector(left, 8, "ENDE", 23, _selected_end_date, _start_date, _end_date, active);

            active = _source_cursor is SourceCursorPosition.DateSelector && _current_view is Views.Source;

            RenderDateSelector(left, 10, "DATUM", 23, DateTime.UnixEpoch, _start_date, _end_date, active);
        }
        else
        {
        }

        RenderButton(left, 13, null, "DATEN AKTUALISIEREN", "\e[m", false, false);

        Console.Write();
    }

    private void RenderResults(int width, int height, int timeplot_height, IPoll? poll)
    {
        int left = Map.Width + 6;
        int top = timeplot_height + 2;

        width -= left;
        height -= top;

        if (_invalidate.HasFlag(RenderInvalidation.PollResults))
        {
            Console.CursorTop = top;
            Console.CursorLeft = left;

            if (poll is { })
            {
                Console.Write($"\e[mUmfrageergebnis am {poll.Date:yyyy-MM-dd} für: ");
                Console.Write(string.Join(", ", from kvp in _selected_states
                                                where kvp.Value
                                                let color = MapColoring.Default.States[kvp.Key].Color
                                                select $"{color}{kvp.Key}\e[m"));

                if (Console.WindowWidth - 2 - Console.CursorLeft is int cw and > 0)
                    Console.Write(new string(' ', cw));
            }

            foreach ((Party party, int index) in Party.All.WithIndex())
                RenderPartyResult(left, top + 2 + index, width, poll, party);
        }

        top += 4 + Party.All.Length;

        if (_invalidate.HasFlag(RenderInvalidation.Compass))
        {
            Console.CursorTop = top;
            Console.CursorLeft = left;
            Console.Write("\e[mPolitischer Kompass:");
        }

        int vertical_space = height + timeplot_height - top;
        (int coalition_x, _) = RenderCompass(left, top + 2, vertical_space - 4, poll);

        if (_invalidate.HasFlag(RenderInvalidation.Coalitions))
        {
            coalition_x += 6;

            Console.CursorTop = top;
            Console.CursorLeft = left + coalition_x;
            Console.Write("\e[mKoalitionsmöglichkeiten:");

            Coalition[] coalitions = poll is { } ? Coalitions.Select(parties => new Coalition(poll, parties))
                                                             .Where(c => c.CoalitionParties.Length >= 1) // filter coalitions where all other parties are < 5%
                                                             .Distinct()
                                                             .OrderByDescending(c => c.CoalitionPercentage)
                                                             .Take(vertical_space)
                                                             .ToArray() : [];

            foreach ((Coalition coalition, int index) in coalitions.WithIndex())
                RenderCoalition(left + coalition_x, top + index + 2, width - coalition_x - 5, poll is { } ? coalition : null);

            for (int i = coalitions.Length; i < vertical_space - 2; ++i)
            {
                Console.CursorTop = top + i + 2;
                Console.CursorLeft = left + coalition_x;
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
            RenderBox(left, top, width, height, false, "\e[90m");

            Console.Write("\e[38;2;80;80;80m");

            for (int y = 1; y < height - 1; ++y)
                for (int x = 1; x < width - 1; ++x)
                {
                    Console.CursorLeft = left + x;
                    Console.CursorTop = top + y;
                    Console.Write(y == height / 2 || x == width / 2 ? "\e[90m" : "\e[38;2;80;80;80m");
                    Console.Write((x % 4, y % 2) switch
                    {
                        (0, 0) => '+',
                        (_, 0) => '-',
                        (0, 1) => '¦',
                        _ => ' ',
                    });
                }

            void render_compas_dot(double lr, double al, string dot)
            {
                int x = (int)Math.Round((double.Clamp(lr, -1, 1) + 1) * .5 * (width - 3));
                int y = (int)Math.Round((double.Clamp(al, -1, 1) + 1) * .5 * (height - 3));

                Console.CursorLeft = left + x + 1;
                Console.CursorTop = top + y + 1;
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

                    render_compas_dot(party.EconomicLeftRightAxis, party.AuthoritarianLibertarianAxis, party.VT100Color + (width < 30 ? "*" : "◯"));
                }

            render_compas_dot(lr_axis, al_axis, "\e[97m⬤");
        }

        return (width, height);
    }

    private static void RenderPartyResult(int left, int top, int width, IPoll? poll, Party party)
    {
        width -= 21;

        Console.CursorTop = top;
        Console.CursorLeft = left;
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
        Console.Write($"\e[38;2;80;80;80m{new string('·', width)}\e[m {percentage,6:P1}  {status}");

        for (double d = 0; d <= 1; d += .125)
        {
            Console.CursorLeft = left + 5 + (int)Math.Round((width - 1) * d);
            Console.Write($"\e[38;2;80;80;80m{(d is 0 or 1 or .5 ? '¦' : ':')}");
        }

        Console.CursorLeft = left + 5 + (int)Math.Round((width - 1) * .05); // 5%-Hürde
        Console.Write($"\e[38;2;80;80;80m¦");

        int w = (int)(percentage * width);
        char end = " ⡀⡄⡆⡇⣇⣧⣷"[(int)(8 * (percentage * width - w))];

        Console.CursorTop = top;
        Console.CursorLeft = left + 5;
        Console.Write((party.VT100Color + new string('⣿', w) + end).TrimEnd());
    }

    private static void RenderCoalition(int left, int top, int width, Coalition? coalition)
    {
        Console.CursorLeft = left;
        Console.CursorTop = top;
        Console.Write(new string(' ', width));

        width -= 30;

        Console.CursorLeft = left;
        Console.Write($"\e[38;2;80;80;80m└{new string('─', width - 2)}┘");

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
        Console.Write(coalition?.CoalitionParties?.Select(party => party.VT100Color + party.Identifier.ToString().ToUpper() + "\e[m").StringJoin(", "));
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.Write(')');

        Console.CursorLeft = left + width / 2 - 1;
        Console.Write("\e[38;2;80;80;80m┴");
        Console.CursorLeft = left;

        if (coalition is { })
            foreach (Party party in coalition.CoalitionParties)
            {
                int w = (int)double.Round(coalition[party] * width);

                Console.Write(party.VT100Color + new string('━', w));
            }
    }

    private static RenderInvalidation GetRenderInvalidateion(Views view) => view switch
    {
        Views.States => RenderInvalidation.StateSelector
                      | RenderInvalidation.Map,
        Views.Source => RenderInvalidation.DataSource,
        Views.Historic => RenderInvalidation.HistoricPlot,
        Views.Result => RenderInvalidation.PollResults
                      | RenderInvalidation.Compass
                      | RenderInvalidation.Coalitions,
        _ => RenderInvalidation.None,
    };

    public void HandleInput(ConsoleKeyInfo key)
    {
        RenderInvalidation process()
        {
            switch (key.Key)
            {
                case KEY_VIEW_SWITCH:
                    int dir = key.Modifiers.HasFlag(ConsoleModifiers.Shift) ? -1 : 1;
                    int count = Enum.GetValues<Views>().Length;
                    RenderInvalidation invalidation = RenderInvalidation.FrameText;

                    invalidation |= GetRenderInvalidateion(_current_view);
                    _current_view = (Views)(((int)_current_view + dir + count) % count);
                    invalidation |= GetRenderInvalidateion(_current_view);

                    return invalidation;
                case KEY_RIGHT when _current_view is Views.States:
                    _state_cursor = _state_cursor_values[(_state_cursor_values.IndexOf(_state_cursor) + 1) % _state_cursor_values.Length];

                    return RenderInvalidation.StateSelector;
                case KEY_LEFT when _current_view is Views.States:
                    _state_cursor = _state_cursor_values[(_state_cursor_values.IndexOf(_state_cursor) - 1 + _state_cursor_values.Length) % _state_cursor_values.Length];

                    return RenderInvalidation.StateSelector;
                case KEY_DOWN when _current_view is Views.States: // TODO : implement
                    return RenderInvalidation.StateSelector;
                case KEY_UP when _current_view is Views.States: // TODO : implement
                    return RenderInvalidation.StateSelector;
                case KEY_DOWN when _current_view is Views.Source:
                    _source_cursor = _source_cursor switch
                    {
                        SourceCursorPosition.StartSelector => SourceCursorPosition.EndSelector,
                        _ => SourceCursorPosition.DateSelector,
                    };

                    return RenderInvalidation.DataSource;
                case KEY_UP when _current_view is Views.Source:
                    _source_cursor = _source_cursor switch
                    {
                        SourceCursorPosition.DateSelector => SourceCursorPosition.EndSelector,
                        _ => SourceCursorPosition.StartSelector,
                    };

                    return RenderInvalidation.DataSource;
                case KEY_STATE_ENTER when _current_view is Views.States:
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

                    return RenderInvalidation.StateSelector
                         | RenderInvalidation.Map
                         | RenderInvalidation.PollResults
                         | RenderInvalidation.HistoricPlot
                         | RenderInvalidation.Coalitions
                         | RenderInvalidation.Compass
                         | RenderInvalidation.DataSource;
                default:
                    return RenderInvalidation.None;
            }
        }

        Invalidate(process());
        Render(false);
    }
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
    StartSelector,
    EndSelector,
    DateSelector,
}

// TODO : dark/light mode switch
// TODO : UTF-8/ASCII mode switch
