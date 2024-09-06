using System.Collections.Generic;
using System.Threading.Tasks;
using System.Text;
using System.Linq;
using System;

using Unknown6656.Controls.Console;
using Unknown6656.Runtime;
using Unknown6656.Generics;

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

public sealed class Renderer
    : IDisposable
{
    #region ONLY FOR CACHING REASONS

    private static readonly StateCursorPosition[] _state_cursor_values = Enum.GetValues<StateCursorPosition>();
    private static readonly State[] _state_values = Enum.GetValues<State>();
    private static readonly State[] _state_values_lfa = [State.BW, State.BY, State.HE, State.HH];
    private static readonly State[] _state_values_pop_growth = [State.BY, State.BW, State.HE, State.RP, State.NW, State.NI, State.HH, State.SH, State.BE];
    private static readonly State[] _state_values_pop = [State.NW, State.BY, State.BW]; // (NI)
    private static readonly State[] _state_values_north_ger = [State.HB, State.HH, State.MV, State.NI, State.SH];
    private static readonly State[] _state_values_south_ger = [State.BW, State.BY, State.HE, State.RP, State.SL];
    private static readonly State[] _state_values_west_ger = [State.BW, State.BY, State.HB, State.HH, State.HE, State.NI, State.NW, State.RP, State.SL, State.SH];

    #endregion

    private static readonly Dictionary<RenderSize, (int MinWidth, int MinHeight)> _min_sizes = new()
    {
        [RenderSize.Small] = (155, 55),
        [RenderSize.Medium] = (170, 71),
        [RenderSize.Large] = (190, 99),
    };

    public static Party[][] Coalitions { get; } = [
        [Party.CDU, Party.SPD],
        [Party.CDU, Party.SPD, Party.FDP],
        [Party.CDU, Party.SPD, Party.GRÜNE],
        [Party.CDU, Party.FDP, Party.GRÜNE],
        [Party.SPD, Party.LINKE, Party.GRÜNE],
        [Party.SPD, Party.LINKE, Party.BSW],
        [Party.SPD, Party.LINKE, Party.BSW, Party.GRÜNE],
        [Party.CDU, Party.AFD],
        [Party.CDU, Party.FDP, Party.AFD],
        [Party.CDU, Party.FDP, Party.FW],
        [Party.CDU, Party.FW],
        [Party.CDU, Party.FW, Party.AFD],
        [Party.SPD, Party.GRÜNE],
        [Party.CDU, Party.FDP],
        [Party.CDU, Party.GRÜNE],
        [Party.AFD, Party.BSW],
    ];

    private readonly ConsoleState _console_state;
    private readonly Dictionary<State, bool> _selected_states = _state_values.ToDictionary(LINQ.id, s => true);
    private StateCursorPosition _state_cursor = StateCursorPosition.Federal;
    private Views _current_view = Views.States;
    private RenderSize _render_size;

    public const string CACHE_FILE = "poll-cache.bin";
    public const ConsoleKey KEY_VIEW_SWITCH = ConsoleKey.Tab;
    public const ConsoleKey KEY_STATE_ENTER = ConsoleKey.Enter;
    public const ConsoleKey KEY_STATE_NEXT = ConsoleKey.RightArrow;
    public const ConsoleKey KEY_STATE_PREV = ConsoleKey.LeftArrow;
    public const ConsoleKey KEY_STATE_UP = ConsoleKey.UpArrow;
    public const ConsoleKey KEY_STATE_DOWN = ConsoleKey.DownArrow;
    public const int TIME_PLOT_HEIGHT = 20;


    public bool IsActive { get; private set; } = true;

    public PollFetcher PollFetcher { get; }

    public RenderSize CurrentRenderSize
    {
        get => _render_size;
        set
        {
            if (_render_size != value)
            {
                _render_size = value is RenderSize.Small or RenderSize.Medium or RenderSize.Large ? value : RenderSize.Small;

                Render();
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
        _console_state = ConsoleExtensions.SaveConsoleState();

        if (OS.IsWindows)
            Console.CursorVisible = false;

        Console.OutputEncoding = Encoding.UTF8;
        Console.InputEncoding = Encoding.UTF8;
        Console.Clear();
        Console.Write("\e[3J");

        PollFetcher = new(new(CACHE_FILE));
    }

    ~Renderer() => Dispose(false);

    public void Dispose()
    {
        Dispose(true);

        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
        IsActive = false;

        ConsoleExtensions.RestoreConsoleState(_console_state);
    }

    public void Render()
    {
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
                Console.Clear();
                Console.Write("\e[3J\e[91;1m");
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
            RenderFrame(width, height);
            RenderMap(height);
            RenderHistoricPlot(width, TIME_PLOT_HEIGHT);


            PollResult pr = new(DateTime.Now, null, "", "", new Dictionary<Party, double>()
                {
                    [Party.CDU] = .25,
                    [Party.SPD] = .15,
                    [Party.FDP] = .10,
                    [Party.AFD] = .20,
                    [Party.GRÜNE] = .09,
                    [Party.LINKE] = .05,
                    [Party.PIRATEN] = .02,
                    [Party.FW] = .03,
                    [Party.RECHTE] = .01,
                    [Party.BSW] = .10,
                }
            );

            RenderResults(width, height, pr);
        }
    }

    public async Task<T> RenderFetchingPrompt<T>(Func<Task<T>> task)
    {
        int width = Console.WindowWidth;
        int height = Console.WindowHeight;

        Console.Write("\e[0;36m");

        for (int _y = 1; _y < height - 1; ++_y)
        {
            Console.CursorTop = _y;

            for (int _x = _y % 2 + 1; _x < width - 1; _x += 2)
            {
                Console.CursorLeft = _x;
                Console.Write('/');
            }
        }

        string[] prompt = """
                          ┌───────────────────────────────────────────────┐
                          │                                               │
                          │   xxx   Umfrageergebnisse werden geladen...   │
                          │   xxx   Bitte warten.                         │
                          │                                               │
                          └───────────────────────────────────────────────┘
                          """.Split('\n');
        int x = (width - prompt.Max(s => s.Length)) / 2;
        int y = (height - prompt.Length) / 2;

        Console.Write("\e[0;96m");

        for (int i = 0; i < prompt.Length; ++i)
        {
            Console.CursorLeft = x;
            Console.CursorTop = y + i;
            Console.Write(prompt[i]);
        }

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

        T result = await task();
        completed = true;

        await spinner;

        Render();

        return result;
    }

    private void RenderTitle(int x, int y, string title, bool active)
    {
        Console.CursorLeft = x;
        Console.CursorTop = y;
        Console.Write($"{(active ? "\e[1;91m" : "\e[96m")} {title} \e[22m");
    }

    private void RenderFrameLine(int x, int y, int size, bool horizontal)
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

    private void RenderOuterFrame(int width, int height)
    {
        string s = $"\e[0;97m┌{new string('─', width - 2)}┐";

        for (int i = 0; i < height - 2; ++i)
            s += $"\n│{new string(' ', width - 2)}│";

        s += $"\n└{new string('─', width - 2)}┘";

        Console.CursorTop = 0;
        Console.CursorLeft = 0;
        Console.Write(s);
    }

    private void RenderFrame(int width, int height)
    {
        RenderOuterFrame(width, height);

        RenderFrameLine(Map.Width + 3, 0, height, false);
        RenderFrameLine(0, Map.Height + 3, Map.Width + 4, true);
        RenderFrameLine(Map.Width + 3, TIME_PLOT_HEIGHT, width - Map.Width - 3, true);
        RenderFrameLine(Map.Width + 35, 0, TIME_PLOT_HEIGHT + 1, false);
        RenderFrameLine(Map.Width + 3, 0, Map.Width + 4, true);

        RenderTitle(4, 0, "ÜBERSICHTSKARTE DEUTSCHLAND", false);
        RenderTitle(4, Map.Height + 3, "BUNDESLÄNDER", _current_view is Views.States);
        RenderTitle(Map.Width + 8, 0, "ZEITRAHMEN & QUELLE", _current_view is Views.Source);
        RenderTitle(Map.Width + 40, 0, "HISTORISCHER VERLAUF", _current_view is Views.Historic);
        RenderTitle(Map.Width + 8, TIME_PLOT_HEIGHT, "UMFRAGEERGEBNISSE", _current_view is Views.Result);
    }

    private void RenderMap(int height)
    {
        MapColoring coloring = MapColoring.Default;

        Console.ResetColor();
        Console.ForegroundColor = ConsoleColor.White;

        Map.RenderToConsole(new(
            _state_values.ToDictionary(LINQ.id, s => _selected_states[s] ? coloring.States[s] : ("\e[90m", '·'))
        ), 2, 2);

        int sel_width = Map.Width / 8;

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
                _ => state.ToString(),
            };

            Console.ForegroundColor = ConsoleColor.White;
            Console.CursorTop = y;
            Console.CursorLeft = x;

            if (_state_values.Contains(state))
                Console.Write(coloring.States[state].Color);

            if (!_selected_states.TryGetValue(state, out bool active))
                if (cursor is StateCursorPosition.Federal)
                    active = _selected_states.Values.All(LINQ.id);
                else if (cursor is StateCursorPosition.Deselect)
                    active = _selected_states.Values.All(v => !v);
                else if (cursor is StateCursorPosition.West)
                    active = _selected_states.All(kvp => _state_values_west_ger.Contains(kvp.Key) == kvp.Value);
                else if (cursor is StateCursorPosition.East)
                    active = _selected_states.All(kvp => _state_values_west_ger.Contains(kvp.Key) != kvp.Value);
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

            if (active)
                Console.Write("\e[7m");

            Console.Write($"[{txt.PadRight(3),4}]\e[27m");

            if (cursor == _state_cursor)
            {
                Console.CursorTop = y + 1;
                Console.CursorLeft = x;
                Console.ForegroundColor = ConsoleColor.Gray;
                Console.Write("\e[6m°°°°°°\e[25m");
            }
        }
    }

    private void RenderHistoricPlot(int width, int height)
    {
        int left = Map.Width + 6;

        width -= left;

        


        Console.CursorLeft = left;
        Console.CursorTop = 2;
        Console.Write(" START  <[  xxxx-xxx  ]>");

        Console.CursorLeft = left;
        Console.CursorTop = 4;
        Console.Write("  ENDE  <[  xxxx-xxx  ]>");

        Console.CursorLeft = left;
        Console.CursorTop = 6;
        Console.Write("QUELLE  <[  xxxxxxxx  ]>");
    }

    private void RenderResults(int width, int height, PollResult poll)
    {
        int left = Map.Width + 6;
        int top = TIME_PLOT_HEIGHT + 2;

        width -= left;
        height -= top;

        Console.CursorTop = top;
        Console.CursorLeft = left;
        Console.Write($"\e[0mUmfrageergebnis am {poll.Date:yyyy-MM-dd} für: ");
        Console.Write(string.Join(", ", from kvp in _selected_states
                                        where kvp.Value
                                        let color = MapColoring.Default.States[kvp.Key].Color
                                        select $"{color}{kvp.Key}\e[0m"));

        foreach ((Party party, int index) in Party.All.WithIndex())
            RenderPartyResult(left, top + 2 + index, width, poll, party);

        Console.CursorTop += 2;
        Console.CursorLeft = left;
        Console.Write($"\e[0mKoalitionsmöglichkeiten:");

        int y = Console.CursorTop;

        foreach ((Party[] parties, int index) in Coalitions.WithIndex())
            RenderCoalition(left + 1, y + index + 2, width - 50, new(poll, parties));
    }

    private void RenderPartyResult(int left, int top, int width, PollResult poll, Party party)
    {
        width -= 20;

        Console.CursorTop = top;
        Console.CursorLeft = left;
        Console.Write("\e[0m" + party.Identifier.ToString().ToUpper());

        double percentage = poll[party];
        string status = percentage switch
        {
            > .666 => "\e[92m⬤⬤⬤",
            >= .50 => "\e[90m◯\e[92m⬤⬤",
            >= .33 => "\e[90m◯◯\e[92m⬤",
            >= .05 => "\e[90m◯\e[33m⬤\e[90m◯",
            _ => "\e[31m⬤\e[90m◯◯",
        };

        if (party == poll.StrongestParty)
            status += "\e[94m⬤";
        else
            status += "\e[90m◯";

        Console.CursorLeft = left + 5;
        Console.Write($"\e[38;2;80;80;80m{new string('·', width)}\e[0m {percentage,5:P1}  {status}");

        for (double d = 0; d <= 1; d += .125)
        {
            Console.CursorLeft = left + 5 + (int)Math.Round((width - 1) * d);
            Console.Write($"\e[38;2;80;80;80m{(d is 0 or 1 or .5 ? '¦' : ':')}");
        }

        int w = (int)(percentage * width);
        char end = " ⡀⡄⡆⡇⣇⣧⣷"[(int)(8 * (percentage * width - w))];

        Console.CursorTop = top;
        Console.CursorLeft = left + 5;
        Console.Write((party.VT100Color + new string('⣿', w) + end).TrimEnd());
    }

    private void RenderCoalition(int left, int top, int width, Coalition coalition)
    {
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.CursorLeft = left;
        Console.CursorTop = top;
        Console.Write($"{new string('─', width - 1)}┘ {coalition.CoalitionPercentage:P1}  (");
        Console.Write(coalition.CoalitionParties.Select(party => party.VT100Color + party.Identifier.ToString().ToUpper() + "\e[0m").StringJoin(", "));
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write(')');

        Console.CursorLeft = left + width / 2 - 1;
        Console.Write("┴");
        Console.CursorLeft = left;

        foreach (Party party in coalition.CoalitionParties)
        {
            int w = (int)double.Round(coalition[party] * width);

            Console.Write(party.VT100Color + new string('━', w));
        }
    }


    public void HandleInput(ConsoleKeyInfo key)
    {
        switch (key.Key)
        {
            case KEY_VIEW_SWITCH:
                int dir = key.Modifiers.HasFlag(ConsoleModifiers.Shift) ? -1 : 1;
                int count = Enum.GetValues<Views>().Length;

                _current_view = (Views)(((int)_current_view + dir + count) % count);

                break;
            case KEY_STATE_NEXT:
                _state_cursor = _state_cursor_values[(_state_cursor_values.IndexOf(_state_cursor) + 1) % _state_cursor_values.Length];

                break;
            case KEY_STATE_PREV:
                _state_cursor = _state_cursor_values[(_state_cursor_values.IndexOf(_state_cursor) - 1 + _state_cursor_values.Length) % _state_cursor_values.Length];

                break;
            case KEY_STATE_DOWN: // TODO : implement
                break;
            case KEY_STATE_UP: // TODO : implement
                break;
            case KEY_STATE_ENTER:
                if (_state_cursor is StateCursorPosition.Invert)
                    foreach (State state in _state_values)
                        _selected_states[state] ^= true;
                else if (_state_values.Contains((State)_state_cursor))
                    _selected_states[(State)_state_cursor] ^= true;
                else
                {
                    State[] target_states = _state_cursor switch
                    {
                        StateCursorPosition.Federal => _state_values,
                        StateCursorPosition.Deselect => [],
                        StateCursorPosition.West => _state_values_west_ger,
                        StateCursorPosition.East => [.._state_values.Except(_state_values_west_ger)],
                        StateCursorPosition.North => _state_values_north_ger,
                        StateCursorPosition.South => _state_values_south_ger,
                        StateCursorPosition.Population => _state_values_pop,
                        StateCursorPosition.PopulationGrowth => _state_values_pop_growth,
                        StateCursorPosition.Economy => _state_values_lfa,
                    };

                    foreach (State state in _state_values)
                        _selected_states[state] = target_states.Contains(state);
                }

                break;
            default:
                return;
        }

        Render();
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

public static class Program
{
    public static async Task Main()
    {
        using Renderer renderer = new()
        {
            CurrentRenderSize = RenderSize.Large
        };
        using Task resize_watcher = Task.Factory.StartNew(async delegate
        {
            int width = Console.WindowWidth;
            int height = Console.WindowHeight;
            int timeout = 100;

            do
                if ((Console.WindowWidth, Console.WindowHeight) is (int nw, int nh) && (nw, nh) != (width, height))
                {
                    timeout = 100;
                    (width, height) = (nw, nh);

                    try
                    {
                        Console.Clear();
                        Console.Write("\e[3J");
                        renderer.Render();
                    }
                    catch
                    {
                        Console.Clear();
                        Console.Write("\e[3J");
                        renderer.Render(); // do smth. if it fails the second time
                    }
                }
                else
                    await Task.Delay(timeout = Math.Max(500, timeout + 50));
            while (renderer.IsActive);
        });

        var results = await renderer.RenderFetchingPrompt(renderer.PollFetcher.FetchAsync);


        while (Console.ReadKey(true) is { Key: not ConsoleKey.Escape } key)
            renderer.HandleInput(key);

        Console.CursorTop = Console.WindowHeight - 1;
        Console.CursorLeft = Console.WindowWidth - 1;
        Console.WriteLine();
        Console.ResetColor();
    }
}
