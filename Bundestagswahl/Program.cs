using System.Collections.Generic;
using System.Threading.Tasks;
using System.Text;
using System.Linq;
using System;

using Unknown6656.Runtime.Console;
using Unknown6656.Runtime;
using Unknown6656.Generics;
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

    public const string CACHE_FILE = "poll-cache.bin";
    public const ConsoleKey KEY_VIEW_SWITCH = ConsoleKey.Tab;
    public const ConsoleKey KEY_STATE_ENTER = ConsoleKey.Enter;
    public const ConsoleKey KEY_STATE_NEXT = ConsoleKey.RightArrow;
    public const ConsoleKey KEY_STATE_PREV = ConsoleKey.LeftArrow;
    public const ConsoleKey KEY_STATE_UP = ConsoleKey.UpArrow;
    public const ConsoleKey KEY_STATE_DOWN = ConsoleKey.DownArrow;
    public const int TIME_PLOT_HEIGHT = 20;

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
        [Party.CDU, Party.FDP, Party.AFD],
        [Party.CDU, Party.FDP, Party.FW],
        [Party.CDU, Party.FW],
        [Party.CDU, Party.FW, Party.AFD],
        [Party.AFD, Party.BSW],
    ];


    private DateTime _start_date = new(1900, 1, 1);
    private DateTime _end_date = DateTime.UtcNow;
    private readonly Dictionary<State, bool> _selected_states = _state_values.ToDictionary(LINQ.id, s => true);
    private readonly ConsoleState _console_state;
    private StateCursorPosition _state_cursor = StateCursorPosition.Federal;
    private Views _current_view = Views.States;
    private RenderSize _render_size;


    public bool IsActive { get; private set; } = true;

    public PollResult Polls { get; private set; } = PollResult.Empty;

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
        _console_state = ConsoleExtensions.SaveConsoleState();

        ConsoleExtensions.ClearAndResetAll();
        Console.OutputEncoding = Encoding.UTF8;
        Console.InputEncoding = Encoding.UTF8;
        ConsoleExtensions.CursorVisible = false;

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

    public void Render(bool clear)
    {
        if (clear)
            ConsoleExtensions.FullClear();

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
                ConsoleExtensions.FullClear();
                Console.Write("\e[91;1m");
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
            RenderFrame(width, height, clear);
            RenderMap(height);

            (IPoll[] historic, IPoll? display) = FetchPolls();

            RenderHistoricPlot(width, TIME_PLOT_HEIGHT, historic);
            RenderResults(width, height, display);
        }
    }

    private (IPoll[] Historic, IPoll? Display) FetchPolls()
    {
        List<State?> states = _selected_states.SelectWhere(kvp => kvp.Value, kvp => (State?)kvp.Key).ToList();

        if (_state_values.All(s => states.Contains(s)))
            states.Add(null);

        Poll[] filtered = Polls.Polls.Where(p => p.Date >= _start_date
                                              && p.Date <= _end_date
                                              && states.Contains(p.State)).ToArray();
        MergedPoll newest = new([..states.Select(s => filtered.Where(p => p.State == s)
                                                              .FirstOrDefault())
                                         .Where(p => p is { })]);

        return (filtered, newest);
    }

    public async Task FetchPollsAsync() => Polls = await RenderFetchingPrompt(PollFetcher.FetchAsync);

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
        Console.Write($"{(active ? "\e[1;91m" : "\e[96m")} {title} \e[22m");
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

    private void RenderFrame(int width, int height, bool clear)
    {
        RenderBox(0, 0, width, height, clear);

        RenderFrameLine(Map.Width + 3, 0, height, false);
        RenderFrameLine(0, Map.Height + 3, Map.Width + 4, true);
        RenderFrameLine(Map.Width + 3, TIME_PLOT_HEIGHT, width - Map.Width - 3, true);
        RenderFrameLine(Map.Width + 35, 0, TIME_PLOT_HEIGHT + 1, false);
        //RenderFrameLine(Map.Width + 3, 0, Map.Width + 4, false);

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
            Console.CursorTop = y + 1;
            Console.CursorLeft = x;
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.Write(cursor == _state_cursor? "\e[6m°°°°°°\e[25m" : "      ");
        }
    }

    private void RenderHistoricPlot(int width, int height, IPoll[] historic)
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

    private void RenderResults(int width, int height, IPoll? poll)
    {
        int left = Map.Width + 6;
        int top = TIME_PLOT_HEIGHT + 2;

        width -= left;
        height -= top;

        Console.CursorTop = top;
        Console.CursorLeft = left;

        if (poll is { })
        {
            Console.Write($"\e[0mUmfrageergebnis am {poll.Date:yyyy-MM-dd} für: ");
            Console.Write(string.Join(", ", from kvp in _selected_states
                                            where kvp.Value
                                            let color = MapColoring.Default.States[kvp.Key].Color
                                            select $"{color}{kvp.Key}\e[0m"));
        }

        foreach ((Party party, int index) in Party.All.WithIndex())
            RenderPartyResult(left, top + 2 + index, width, poll, party);

        Console.CursorTop += 3;
        Console.CursorLeft = left;
        Console.Write("\e[0mPolitischer Kompass:");

        int y = Console.CursorTop;
        int vertical_space = height + top - y - 5;

        (int coalition_x, _) = RenderCompass(left, y + 2, vertical_space - 4, poll);

        coalition_x += 6;

        Console.CursorTop = y;
        Console.CursorLeft = left + coalition_x;
        Console.Write("\e[0mKoalitionsmöglichkeiten:");

        Coalition[] coalitions = poll is { } ? Coalitions.Select(parties => new Coalition(poll, parties))
                                                         .Where(c => c.CoalitionParties.Length > 1) // filter coalitions where all other parties are < 5%
                                                         .Distinct()
                                                         .OrderByDescending(c => c.CoalitionPercentage)
                                                         .Take(vertical_space)
                                                         .ToArray() : [];

        foreach ((Coalition coalition, int index) in coalitions.WithIndex())
            RenderCoalition(left + coalition_x, y + index + 2, width - coalition_x - 5, poll is { } ? coalition : null);

        for (int i = coalitions.Length; i < vertical_space; ++i)
        {
            Console.CursorTop = y + i + 2;
            Console.CursorLeft = left + coalition_x;
            Console.Write(new string(' ', width - coalition_x - 5));
        }
    }

    private static (int width, int height) RenderCompass(int left, int top, int height, IPoll? poll)
    {
        int width = height * 2;

        while (height % 2 != 1 || ((height - 2) / 2) % 2 != 1)
            ++height;

        while (width % 3 != 1 || ((width - 2) / 3) % 2 != 1)
            ++width;

        RenderBox(left, top, width, height, false, "\e[90m");

        Console.Write("\e[38;2;80;80;80m");

        for (int y = 1; y < height - 1; ++y)
            for (int x = 1; x < width - 1; ++x)
            {
                Console.CursorLeft = left + x;
                Console.CursorTop = top + y;
                Console.Write(y == height / 2 || x == width / 2 ? "\e[90m" : "\e[38;2;80;80;80m");
                Console.Write((x % 3, y % 2) switch
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
            foreach (var party in Party.All)
            {
                double perc = poll[party];

                lr_axis += perc * party.EconomicLeftRightAxis;
                al_axis += perc * party.AuthoritarianLibertarianAxis;

                render_compas_dot(party.EconomicLeftRightAxis, party.AuthoritarianLibertarianAxis, party.VT100Color + (width < 30 ? "·" : "◯"));
            }

        render_compas_dot(lr_axis, al_axis, "\e[97m⬤");

        return (width, height);
    }

    private static void RenderPartyResult(int left, int top, int width, IPoll? poll, Party party)
    {
        width -= 21;

        Console.CursorTop = top;
        Console.CursorLeft = left;
        Console.Write("\e[0m" + party.Identifier.ToString().ToUpper());

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
        Console.Write($"\e[38;2;80;80;80m{new string('·', width)}\e[0m {percentage,6:P1}  {status}");

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
        Console.Write(coalition?.CoalitionParties?.Select(party => party.VT100Color + party.Identifier.ToString().ToUpper() + "\e[0m").StringJoin(", "));
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
        await using ConsoleResizeListener resize = new();

        resize.SizeChanged += (_, _, _, _) =>
        {
            // fix rendering of modal form during resizing.

            try
            {
                renderer.Render(true);
            }
            catch
            {
                renderer.Render(true); // do smth. if it fails the second time
            }
        };
        resize.Start();

        await renderer.FetchPollsAsync();

        while (Console.ReadKey(true) is { Key: not ConsoleKey.Escape } key)
            renderer.HandleInput(key);

        resize.Stop();

        Console.CursorTop = Console.WindowHeight - 1;
        Console.CursorLeft = Console.WindowWidth - 1;
        Console.WriteLine();
        Console.ResetColor();
    }
}
