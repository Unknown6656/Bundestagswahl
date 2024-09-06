using System.Collections.Generic;
using System.Text;
using System.Linq;
using System;

using Unknown6656.Controls.Console;
using Unknown6656.Runtime;
using Unknown6656.Generics;
using System.Threading.Tasks;

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




    public const ConsoleKey KEY_STATE_ENTER = ConsoleKey.Enter;
    public const ConsoleKey KEY_STATE_NEXT = ConsoleKey.RightArrow;
    public const ConsoleKey KEY_STATE_PREV = ConsoleKey.LeftArrow;
    public const ConsoleKey KEY_STATE_UP = ConsoleKey.UpArrow;
    public const ConsoleKey KEY_STATE_DOWN = ConsoleKey.DownArrow;
    public const int TIME_PLOT_HEIGHT = 20;


    public bool IsActive { get; private set; } = true;

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
                Console.WriteLine("\e[91;1m");
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


            PollResult pr = new(
                DateTime.Now,
                "lol",
                new Dictionary<Party, double>()
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

    private void RenderFrame(int width, int height)
    {
        string s = $"┌{new string('─', width - 2)}┐";

        for (int i = 0; i < height - 2; ++i)
            s += $"\n│{new string(' ', width - 2)}│";

        s += $"\n└{ new string('─', width - 2)}┘";

        Console.CursorTop = 0;
        Console.CursorLeft = 0;
        Console.ResetColor();
        Console.ForegroundColor = ConsoleColor.White;
        Console.Write(s);

        foreach ((char c, int y) in $"┬{new string('│', height - 2)}┴".WithIndex())
        {
            Console.CursorTop = y;
            Console.CursorLeft = Map.Width + 3;
            Console.Write(c);
        }

        Console.CursorLeft = Map.Width + 3;
        Console.CursorTop = TIME_PLOT_HEIGHT;
        Console.Write($"├{new string('─', width - Map.Width - 5)}┤");

        foreach ((char c, int y) in $"┬{new string('│', TIME_PLOT_HEIGHT - 1)}┴".WithIndex())
        {
            Console.CursorTop = y;
            Console.CursorLeft = Map.Width + 35;
            Console.Write(c);
        }

        Console.CursorLeft = 0;
        Console.CursorTop = Map.Height + 3;
        Console.Write($"├{new string('─', Map.Width + 2)}┤");

        Console.CursorLeft = 4;
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.Write(" BUNDESLÄNDER ");

        Console.CursorTop = 0;
        Console.CursorLeft = 4;
        Console.Write(" ÜBERSICHTSKARTE DEUTSCHLAND ");

        Console.CursorLeft = Map.Width + 8;
        Console.Write(" ZEITRAHMEN & QUELLE ");

        Console.CursorLeft = Map.Width + 40;
        Console.Write(" HISTORISCHER VERLAUF ");

        Console.CursorTop = TIME_PLOT_HEIGHT;
        Console.CursorLeft = Map.Width + 8;
        Console.Write(" UMFRAGEERGEBNISSE ");
    }

    private void RenderMap(int height)
    {
        MapColoring coloring = MapColoring.Default;

        Console.ResetColor();
        Console.ForegroundColor = ConsoleColor.White;

        Map.RenderToConsole(new(
            _state_values.ToDictionary(LINQ.id, s => _selected_states[s] ? coloring.States[s] : ("\e[90m", '·'))
        ), 2, 2);

        Console.ForegroundColor = ConsoleColor.White;
        Console.CursorTop = Map.Height + 5;
        Console.CursorLeft = 3;

        if (_selected_states.Values.All(LINQ.id))
            Console.Write("\e[7m");

        Console.Write($"[BUND]\e[27m  ");

        if (_selected_states.Values.All(v => !v))
            Console.Write("\e[7m");

        Console.Write($"[KEIN]\e[27m  [INV.]  ");

        foreach ((string region, IEnumerable<State> states) in new[]
        {
            ("WEST", _state_values_west_ger),
            ("OST.", _state_values.Except(_state_values_west_ger)),
            ("SÜD.", _state_values_south_ger),
        })
        {
            if (_selected_states.All(kvp => states.Contains(kvp.Key) == kvp.Value))
                Console.Write("\e[7m");

            Console.Write($"[{region}]\e[27m  ");
        }

        foreach ((State state, int index) in Enum.GetValues<State>().WithIndex())
        {
            Console.CursorTop = Map.Height + 8 + (index / 6) * 3;
            Console.CursorLeft = 3 + (index % 6) * 8;

            bool selected = _selected_states[state];

            if (selected)
                Console.Write("\e[7m");

            Console.Write($"{coloring.States[state].Color}[ {state} ]\e[27m");
        }

        if (_state_cursor <= StateCursorPosition.South)
        {
            Console.CursorTop = Map.Height + 6;
            Console.CursorLeft = 3 + (int)_state_cursor * 8;
        }
        else
        {
            int index = _state_values.IndexOf((State)_state_cursor);

            Console.CursorTop = Map.Height + 9 + (index / 6) * 3;
            Console.CursorLeft = 3 + (index % 6) * 8;
        }

        Console.ForegroundColor = ConsoleColor.Gray;
        Console.Write("\e[6m°°°°°°\e[25m");
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
                if (_state_cursor is StateCursorPosition.Federal)
                    foreach (State state in _state_values)
                        _selected_states[state] = true;
                else if (_state_cursor is StateCursorPosition.Deselect)
                    foreach (State state in _state_values)
                        _selected_states[state] = false;
                else if (_state_cursor is StateCursorPosition.Invert)
                    foreach (State state in _state_values)
                        _selected_states[state] ^= true;
                else if (_state_cursor is StateCursorPosition.West)
                    foreach (State state in _state_values)
                        _selected_states[state] = _state_values_west_ger.Contains(state);
                else if (_state_cursor is StateCursorPosition.East)
                    foreach (State state in _state_values)
                        _selected_states[state] = !_state_values_west_ger.Contains(state);
                else if (_state_cursor is StateCursorPosition.South)
                    foreach (State state in _state_values)
                        _selected_states[state] = _state_values_south_ger.Contains(state);
                else
                    _selected_states[(State)_state_cursor] ^= true;

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
    public static void Main()
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
                        renderer.Render();
                    }
                    catch
                    {
                        renderer.Render(); // do smth. if it fails the second time
                    }
                }
                else
                    await Task.Delay(timeout = Math.Max(500, timeout + 50));
            while (renderer.IsActive);
        });

        while (Console.ReadKey(true) is { Key: not ConsoleKey.Escape } key)
            renderer.HandleInput(key);

        Console.CursorTop = Console.WindowHeight - 1;
        Console.CursorLeft = Console.WindowWidth - 1;
        Console.WriteLine();
        Console.ResetColor();
    }
}


// TODO :
//  - 5% hürde
//  - sperrminorität (1/3)
//  - einf. mehrheit
//  - abs. mehrheit
//  - 2/3 mehrheit
