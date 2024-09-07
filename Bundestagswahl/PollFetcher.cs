using System.Text.RegularExpressions;
using System.Collections.ObjectModel;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections;
using System.Threading.Tasks;
using System.Globalization;
using System.Diagnostics;
using System.Net.Http;
using System.Linq;
using System.Text;
using System.Web;
using System.IO;
using System;

using HtmlAgilityPack;

using Unknown6656.Generics;
using Unknown6656.Common;

namespace Bundestagswahl;


public unsafe struct PartyIdentifier
    : IEquatable<PartyIdentifier>
    , IEnumerable<char>
{
    internal const int SIZE = 3;

    private fixed char _buffer[SIZE];


    public PartyIdentifier(string ident)
    {
        ident = ident.ToLowerInvariant();

        for (int i = 0; i < SIZE; ++i)
            _buffer[i] = ident.Length > i ? ident[i] : '\0';
    }

    public override string ToString()
    {
        fixed (char* ptr = _buffer)
            return new(ptr, 0, SIZE);
    }

    public override int GetHashCode()
    {
        int hc = HashCode.Combine(_buffer[0]);

        for (int i = 1; i < SIZE; ++i)
            hc = HashCode.Combine(hc, _buffer[i]);

        return hc;
    }

    public override bool Equals(object? obj) => obj is PartyIdentifier i && Equals(i);

    public bool Equals(PartyIdentifier other) => other.GetHashCode() == GetHashCode();

    public IEnumerator<char> GetEnumerator() => ToString().GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public static implicit operator string(PartyIdentifier identifier) => identifier.ToString();

    public static implicit operator PartyIdentifier(string identifier) => new(identifier);
}

public sealed class Party(PartyIdentifier identifier, string name, string color, double lr_axis, double al_axis)
    : IEquatable<Party>
{
    public static Party CDU { get; }       = new("cdu", "CDU/CSU",         "\e[38;2;217;200;235m",  .3, -.3);
  //public static Party CDU { get; }       = new("cdu", "CDU/CSU",         "\e[38;2;117;100;135m",  .3, -.3);
    public static Party SPD { get; }       = new("spd", "SPD",             "\e[38;2;255;40;40m",   -.3, -.4);
    public static Party FDP { get; }       = new("fdp", "FDP",             "\e[38;2;255;200;0m",    .6,  .5);
    public static Party AFD { get; }       = new("afd", "AfD",             "\e[38;2;0;158;224m",    .5, -.8);
    public static Party GRÜNE { get; }     = new("grü", "B.90/Die Grünen", "\e[38;2;60;155;0m",    -.5,  .1);
    public static Party LINKE { get; }     = new("lin", "Die Linke",       "\e[38;2;208;0;67m",    -.6, -.8);
    public static Party PIRATEN { get; }   = new("pir", "Die Piraten",     "\e[38;2;255;135;0m",   -.1,  .8);
    public static Party FW { get; }        = new("fw",  "Freie Wähler",    "\e[38;2;0;70;255m",     .4,  .0);
    public static Party RECHTE { get; }    = new("rep", "NPD/REP/Rechte",  "\e[38;2;170;122;44m",   .9, -.8);
    public static Party BSW { get; }       = new("bsw", "BSW",             "\e[38;2;111;0;60m",    -.6, -.8);
    public static Party __OTHER__ { get; } = new("son", "Sonstige",        "\e[38;2;126;176;165m",  .0,  .0);

    public static Party[] All { get; } = [CDU, SPD, FDP, AFD, GRÜNE, LINKE, BSW, PIRATEN, FW, RECHTE, __OTHER__];
    public static Party[] LeftToRight { get; } = [LINKE, BSW, PIRATEN, SPD, GRÜNE, FDP, FW, CDU, AFD, RECHTE];


    internal PartyIdentifier Identifier { get; } = identifier;

    public string Name { get; } = name;

    public string VT100Color { get; } = color;

    public double EconomicLeftRightAxis { get; } = double.Clamp(lr_axis, -1, 1);

    public double AuthoritarianLibertarianAxis { get; } = double.Clamp(al_axis, -1, 1);


    public override int GetHashCode() => Identifier.GetHashCode();

    public override bool Equals(object? obj) => obj is Party p && Equals(p);

    public bool Equals(Party? other) => Identifier.Equals(other?.Identifier);

    public override string ToString() => Name;

    public static Party? TryGetParty(string name)
    {
        name = new([.. name.ToLowerInvariant()
                           .Replace('ä', 'a')
                           .Replace('ö', 'o')
                           .Replace('ü', 'u')
                           .Replace('ß', 's')
                           .Where(char.IsAsciiLetterOrDigit)]);

        switch (name)
        {
            case "cdu" or "csu" or "cducsu" or "csucdu":
                return CDU;
            case "spd":
                return SPD;
            case "fdp":
                return FDP;
            case "grn" or "grune" or "diegrune" or "diegrunen":
                return GRÜNE;
            case "lin" or "lnk" or "linke" or "dielinke" or "linkepds" or "pds" or "pdsdielinke" or "pdslinke" or "dielinkepds":
                return LINKE;
            case "pir" or "piraten" or "diepiraten":
                return PIRATEN;
            case "fw" or "freienwahler" or "freiewahler" or "diefreienwahler" or "diefreiewahler" or "fwahler" or "diefwahler":
                return FW;
            case "afd" or "alternativefurdeutschland" or "alternativefurd" or "alternativefur" or "alternativefurd":
                return AFD;
            case "rechte" or "dierechte":
            case "npd" or "nationaldemokraten":
            case "rep" or "republikaner" or "dierepublikaner" or "dvu" or "repdvu" or "dvurep":
                return RECHTE;
            case "bsw" or "bswvg" or "bundnissahrawagenknecht":
                return BSW;
            case "sonstig" or "sonstige" or "andere" or "sonst" or "rest":
                return __OTHER__;
            default:
                Debug.Write($"Unknown party '{name}'.");

                return null; // TODO

        }
    }
}

public interface IPoll
{
    public DateTime Date { get; }

    public Party StrongestParty { get; }

    public double this[Party party] { get; }
}

public sealed class Coalition
    : IPoll
{
    private Dictionary<Party, double> _values = [];


    public Party[] CoalitionParties { get; }

    public Party[] OppositionParties { get; }

    public Party? StrongestParty { get; }

    public double CoalitionPercentage { get; }

    public double OppositionPercentage { get; }

    public IPoll UnderlyingPoll { get; }

    public DateTime Date => UnderlyingPoll.Date;


    public double this[Party party] => _values.TryGetValue(party, out double percentage) ? percentage : 0;


    public Coalition(IPoll poll, params Party[] parties)
    {
        UnderlyingPoll = poll;
        CoalitionParties = parties.Where(p => poll[p] >= .05)
                                  .OrderByDescending(p => poll[p])
                                  .ToArray();
        OppositionParties = Party.All.Except(parties).ToArrayWhere(p => poll[p] >= .05);
        StrongestParty = CoalitionParties.MaxBy(p => poll[p]);
        CoalitionPercentage = 0;

        double sum = 0;

        foreach (Party party in CoalitionParties)
        {
            sum += _values[party] = poll[party];
            CoalitionPercentage += poll[party];
        }

        foreach (Party party in OppositionParties)
            sum += _values[party] = poll[party];

        foreach (Party party in CoalitionParties.Concat(OppositionParties))
            _values[party] /= sum;

        CoalitionPercentage /= sum;
        OppositionPercentage = 1 - CoalitionPercentage;
    }

    public override bool Equals(object? obj) => obj is Coalition other &&
                                                other.UnderlyingPoll == UnderlyingPoll &&
                                                other._values.Keys.SetEquals(_values.Keys);

    public override int GetHashCode()
    {
        int hc = UnderlyingPoll.GetHashCode();

        foreach (Party party in _values.Keys)
            hc = HashCode.Combine(hc, (string)party.Identifier);

        return hc;
    }
}

public sealed class Poll
    : IPoll
{
    public static Poll Empty { get; } = new(DateTime.UnixEpoch, null, "<none>", "<none>", new Dictionary<Party, double>());


    public DateTime Date { get; }

    public string Pollster { get; }

    public string SourceURI { get; }

    public State? State { get; }

    public bool IsFederal => State is null;

    public Party StrongestParty => Results.OrderByDescending(kvp => kvp.Value).FirstOrDefault().Key ?? Party.__OTHER__;

    internal IReadOnlyDictionary<Party, double> Results { get; }

    public double this[Party p] => Results.ContainsKey(p) ? Results[p] : 0;


    public Poll(DateTime date, State? state, string pollster, string source_uri, Dictionary<Party, double> values)
    {
        Date = date;
        State = state;
        Pollster = pollster;
        SourceURI = source_uri;

        if (!values.ContainsKey(Party.__OTHER__))
            values[Party.__OTHER__] = Math.Max(0, 1 - values.Values.Sum());

        double sum = values.Values.Sum();

        if (sum is not 1 or 0)
            values = values.ToDictionary(pair => pair.Key, pair => pair.Value / sum);

        Results = new ReadOnlyDictionary<Party, double>(values);
    }

    public Poll(DateTime date, State? state, string pollster, string source_uri, Dictionary<string, double> values)
        : this(date, state, pollster, source_uri, new Func<Dictionary<Party, double>>(() =>
        {
            Dictionary<Party, double> percentages = [];

            foreach (string id in values.Keys)
                if (Party.All.FirstOrDefault(p => p.Identifier == id) is Party p)
                    percentages[p] = values[id];

            return percentages;
        })())
    {
    }

    public override string ToString() =>
        $"{Date:yyyy-MM-dd}, {State?.ToString() ?? "BUND"} {Results.Select(kvp => $", {kvp.Key.Identifier}={kvp.Value:P1}").StringConcat()} ({SourceURI})";

    internal void Serialize(BinaryWriter writer)
    {
        writer.Write(Date.Ticks);

        if (State is null)
            writer.Write((byte)0xff);
        else
            writer.Write((byte)State);

        writer.Write(Pollster);
        writer.Write(SourceURI);
        writer.Write(Results.Count);

        foreach ((Party party, double result) in Results)
        {
            char[] identifier = [..party.Identifier];

            writer.Write(identifier, 0, PartyIdentifier.SIZE);
            writer.Write(result);
        }
    }

    internal static Poll? TryDeserialize(BinaryReader reader)
    {
        try
        {
            long ticks = reader.ReadInt64();
            State? state = reader.ReadByte() is byte b and not 0xff ? (State)b : null;
            string pollster = reader.ReadString();
            string source_uri = reader.ReadString();
            int count = reader.ReadInt32();

            Dictionary<Party, double> results = new()
            {
                [Party.__OTHER__] = 0d
            };

            for (int i = 0; i < count; ++i)
            {
                char[] identifier = new char[PartyIdentifier.SIZE];

                reader.Read(identifier, 0, identifier.Length);

                double result = reader.ReadDouble();

                if (Party.All.FirstOrDefault(p => p.Identifier == new string(identifier)) is Party party)
                    results[party] = result;
                else
                    results[Party.__OTHER__] += result;
            }

            return new(new(ticks), state, pollster, source_uri, results);
        }
        catch
        {
            return null;
        }
    }
}

public sealed class MergedPoll
    : IPoll
{
    private static readonly Dictionary<State, int> _population_per_state = new()
    {
        [State.BW] = 11_280_000,
        [State.BY] = 13_369_000,
        [State.BE] =  3_755_000,
        [State.BB] =  2_573_000,
        [State.HB] =    685_000,
        [State.HH] =  1_892_000,
        [State.HE] =  6_391_000,
        [State.MV] =  1_628_000,
        [State.NI] =  8_140_000,
        [State.NW] = 18_139_000,
        [State.RP] =  4_159_000,
        [State.SL] =    993_000,
        [State.SN] =  4_086_000,
        [State.ST] =  2_187_000,
        [State.SH] =  2_953_000,
        [State.TH] =  2_127_000,
    };
    private static readonly int _population_total = _population_per_state.Values.Sum();

    public Poll[] Polls { get; }

    public State[] States { get; }

    public Party StrongestParty { get; }

    public DateTime EarliestPoll { get; }

    public DateTime LatestPoll { get; }

    DateTime IPoll.Date => LatestPoll;

    internal IReadOnlyDictionary<Party, double> Results { get; }

    public double this[Party p] => Results.ContainsKey(p) ? Results[p] : 0;


    public MergedPoll(params Poll[] polls)
    {
        Polls = polls;

        if (polls.Length > 0)
        {
            States = polls.SelectWhere(p => p.State.HasValue, p => p.State!.Value).Distinct().ToArray();
            EarliestPoll = polls.Min(p => p.Date);
            LatestPoll = polls.Max(p => p.Date);

            Dictionary<Party, double> results = [];
            double total = 0;

            foreach (Party party in Party.All)
            {
                double sum = 0;
                double pop = 0;

                foreach (Poll poll in polls)
                {
                    int p = poll.State is State s ? _population_per_state[s] : _population_total;

                    sum += poll[party] * p;
                    pop += p;
                }

                sum = Math.Max(sum / pop, 0);
                results[party] = sum;
                total += sum;
            }

            foreach (Party party in Party.All)
                results[party] /= total;

            Results = new ReadOnlyDictionary<Party, double>(results);
            StrongestParty = Results.OrderByDescending(kvp => kvp.Value).FirstOrDefault().Key ?? Party.__OTHER__;
        }
        else
        {
            States = [];
            EarliestPoll =
            LatestPoll = DateTime.UtcNow;
            Results = Party.All.ToDictionary(LINQ.id, p => 0d);
            StrongestParty = Party.__OTHER__;
        }
    }

    public override string ToString() =>
        $"{EarliestPoll:yyyy-MM-dd}-{LatestPoll:yyyy-MM-dd}, {States.Select(s => s.ToString() ?? "BUND").StringJoin(", ")} {Results.Select(kvp => $", {kvp.Key.Identifier}={kvp.Value:P1}").StringConcat()} ({Polls.Length} polls)";

    public static implicit operator Poll[](MergedPoll poll) => poll.Polls;

    public static implicit operator MergedPoll(Poll[] polls) => new(polls);
}

public sealed class PollResult(IEnumerable<Poll> polls)
{
    public static PollResult Empty { get; } = new([]);

    public int PollCount => Polls.Length;

    public Poll[] Polls { get; } = [.. polls.OrderByDescending(p => p.Date)];


    public Poll? MostRecent() => Polls.FirstOrDefault();

    public Poll[] MostRecent(int count) => Polls.Take(count).ToArray();

    public Poll? MostRecent(State? state) => Polls.Where(p => p.State == state).FirstOrDefault();

    public Poll[] MostRecent(State? state, int count) => Polls.Where(p => p.State == state).Take(count).ToArray();

    public Poll[] In(State? state) => Polls.Where(p => p.State == state).ToArray();

    public Poll[] During(DateTime earliest, DateTime latest) => Polls.Where(p => p.Date >= earliest && p.Date <= latest).ToArray();

    public Poll[] During(DateTime earliest, DateTime latest, State state) => Polls.Where(p => p.Date >= earliest && p.Date <= latest && p.State == state).ToArray();

    public string AsCSV()
    {
        Party[] parties = Party.All;
        StringBuilder sb = new();

        sb.AppendLine($"id,date,pollster,source,state,{parties.Select(p => p.Identifier).StringJoin(",")}");

        for (int i = 0; i < Polls.Length; i++)
        {
            Poll poll = Polls[i];

            sb.AppendLine($"{i},{poll.Date:yyyy-MM-dd},\"{poll.Pollster}\",\"{poll.SourceURI}\",{poll.State?.ToString() ?? "DE"},{parties.Select(p => poll[p].ToString("P1")).StringJoin(",")}");
        }

        return sb.ToString();
    }

    public string AsCSV(State? state)
    {
        Party[] parties = Party.All;
        StringBuilder sb = new();

        sb.AppendLine($"id,date,pollster,source,{parties.Select(p => p.Identifier).StringJoin(",")}");

        for (int i = 0; i < Polls.Length; i++)
        {
            Poll poll = Polls[i];

            if (poll.State == state)
                sb.AppendLine($"{i},{poll.Date:yyyy-MM-dd},\"{poll.Pollster}\",\"{poll.SourceURI}\",{parties.Select(p => poll[p].ToString("P1")).StringJoin(",")}");
        }

        return sb.ToString();
    }
}

public sealed partial class PollFetcher(FileInfo cachefile)
{
    public const long MAX_CACHE_LIFETIME_SECONDS = 3600 * 24 * 7; // keep cache for a maximum of one week.

    private const string BASE_URL_FEDERAL = "https://www.wahlrecht.de/umfragen/";
    private static readonly Dictionary<State, string> BASE_URL_STATES = new()
    {
        [State.BW] = $"{BASE_URL_FEDERAL}landtage/baden-wuerttemberg.htm",
        [State.BY] = $"{BASE_URL_FEDERAL}landtage/bayern.htm",
        [State.BE] = $"{BASE_URL_FEDERAL}landtage/berlin.htm",
        [State.BB] = $"{BASE_URL_FEDERAL}landtage/brandenburg.htm",
        [State.HB] = $"{BASE_URL_FEDERAL}landtage/bremen.htm",
        [State.HH] = $"{BASE_URL_FEDERAL}landtage/hamburg.htm",
        [State.HE] = $"{BASE_URL_FEDERAL}landtage/hessen.htm",
        [State.MV] = $"{BASE_URL_FEDERAL}landtage/mecklenburg-vorpommern.htm",
        [State.NI] = $"{BASE_URL_FEDERAL}landtage/niedersachsen.htm",
        [State.NW] = $"{BASE_URL_FEDERAL}landtage/nrw.htm",
        [State.RP] = $"{BASE_URL_FEDERAL}landtage/rheinland-pfalz.htm",
        [State.SL] = $"{BASE_URL_FEDERAL}landtage/saarland.htm",
        [State.SN] = $"{BASE_URL_FEDERAL}landtage/sachsen.htm",
        [State.ST] = $"{BASE_URL_FEDERAL}landtage/sachsen-anhalt.htm",
        [State.SH] = $"{BASE_URL_FEDERAL}landtage/schleswig-holstein.htm",
        [State.TH] = $"{BASE_URL_FEDERAL}landtage/thueringen.htm",
    };
    private static readonly string[] BASE_URL_FEDERAL_POLLING = [
        $"{BASE_URL_FEDERAL}allensbach.htm",
        $"{BASE_URL_FEDERAL}emnid.htm",
        $"{BASE_URL_FEDERAL}forsa.htm",
        $"{BASE_URL_FEDERAL}politbarometer.htm",
        $"{BASE_URL_FEDERAL}gms.htm",
        $"{BASE_URL_FEDERAL}dimap.htm",
        $"{BASE_URL_FEDERAL}insa.htm",
        $"{BASE_URL_FEDERAL}yougov.htm",
        $"{BASE_URL_FEDERAL}ipsos.htm",
    ];


    public void InvalidateCache()
    {
        if (cachefile.Exists)
            cachefile.Delete();
    }

    public async Task WriteCacheAsync(PollResult results)
    {
        await using FileStream fs = new(cachefile.FullName, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read);
        await using BinaryWriter wr = new(fs);

        wr.Write(DateTime.UtcNow.Ticks);

        foreach (Poll result in results.Polls.OrderBy(r => r.Date))
            result.Serialize(wr);

        wr.Flush();

        await fs.FlushAsync();
    }

    public PollResult ReadCache()
    {
        try
        {
            using FileStream fs = new(cachefile.FullName, FileMode.OpenOrCreate, FileAccess.Read, FileShare.ReadWrite);
            using BinaryReader rd = new(fs);

            DateTime created = new(rd.ReadInt64());
            DateTime now = DateTime.UtcNow;

            if (Math.Abs((now - created).TotalSeconds) < MAX_CACHE_LIFETIME_SECONDS)
            {
                List<Poll> results = [];

                while (Poll.TryDeserialize(rd) is Poll result)
                    results.Add(result);

                return new(results.OrderBy(r => r.Date));
            }
        }
        catch (EndOfStreamException)
        {
        }

        return PollResult.Empty;
    }

    public async Task<PollResult> FetchAsync()
    {
        PollResult results = ReadCache();

        if (results.PollCount == 0)
        {
            results = await FetchAllPollsAsync();

            await WriteCacheAsync(results);
        }

        return results;
    }

    public static async Task<PollResult> FetchAllPollsAsync()
    {
        IDictionary<string, HtmlDocument> documents = await FetchHTMLDocuments();
        ConcurrentBag<Poll> results = [];

        Parallel.ForEach(documents, kvp => ParseHTMLDocument(kvp.Value, kvp.Key).Do(results.Add));

        return new(results.OrderBy(r => r.Date));
    }

    private static async Task<HtmlDocument> GetHTMLAsync(string uri)
    {
        HtmlDocument doc = new();
        using HttpClient client = new();

        doc.LoadHtml(await client.GetStringAsync(uri));

        return doc;
    }

    private static string[] GetMorePollingLinks(HtmlDocument document, string selector = "//p[@class='navi'][1]/a[@href]") =>
        document.DocumentNode.SelectNodes(selector) is { } nodes ? [.. nodes.Select(node => node.GetAttributeValue("href", ""))] : [];

    private static async Task<IDictionary<string, HtmlDocument>> FetchHTMLDocuments()
    {
        ConcurrentDictionary<string, HtmlDocument> results = [];
        ConcurrentHashSet<string> open = [];

        async Task fetch(string uri)
        {
            HtmlDocument html = results[uri] = await GetHTMLAsync(uri);

            foreach (string link in GetMorePollingLinks(html))
                if (link.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    open.Add(link);
                else
                {
                    string sanitized_uri = uri[..(uri.TrimEnd('/').LastIndexOf('/') + 1)] + link;

                    sanitized_uri = GenerateRegexBackpath().Replace(sanitized_uri, "");

                    open.Add(sanitized_uri);
                }
        }

        await Parallel.ForEachAsync(BASE_URL_FEDERAL_POLLING.Concat(BASE_URL_STATES.Values), async (base_uri, _) => await fetch(base_uri));

        while (open.Count > 0 && open.First() is string uri)
        {
            open.Remove(uri);

            if (!results.ContainsKey(uri))
                await fetch(uri);
        }

        return results;
    }

    private static string NormalizeURI(string uri) => Normalize(uri).Replace(BASE_URL_FEDERAL, "").TrimEnd(".htm");

    private static IEnumerable<Poll> ParseHTMLDocument(HtmlDocument document, string source_uri)
    {
        HtmlNodeCollection tables = document.DocumentNode.SelectNodes("//table[@class='wilko']");
        State? state = null;

        source_uri = NormalizeURI(source_uri);

        foreach ((State s, string uri) in BASE_URL_STATES)
            if (source_uri.Contains(NormalizeURI(uri)))
            {
                state = s;

                break;
            }

        foreach (HtmlNode table in tables)
        {
            HtmlNodeCollection toprow = table.SelectNodes("thead/tr/th");
            Dictionary<int, Party> header = [];
            int? participant_index = null;
            int? pollster_index = null;

            foreach ((HtmlNode node, int index) in toprow.WithIndex())
                if (!node.GetAttributeValue("class", "").Contains("dat", StringComparison.OrdinalIgnoreCase) && Party.TryGetParty(node.InnerText) is Party party)
                    header[index] = party;
                else
                {
                    string header_text = new(node.InnerText.SelectWhere(char.IsAsciiLetter, char.ToLower).ToArray());

                    if (header_text.Contains("institut"))
                        pollster_index = index;
                    else if (header_text.Contains("befragte") || header_text.Contains("teilnehmer"))
                        participant_index = index;
                }

            foreach (HtmlNode row in table.SelectNodes("tbody/tr"))
            {
                List<HtmlNode> cells = new(toprow.Count);

                foreach (HtmlNode cell in row.ChildNodes)
                    if (cell.Name == "td")
                    {
                        cells.Add(cell);

                        if (int.TryParse(cell.GetAttributeValue("colspan", "0"), out int colspan) && colspan > 0)
                            cells.AddRange(Enumerable.Repeat(cell, colspan - 1));
                    }

                string normalized_joined = Normalize(cells.Select(cell => cell.InnerText).StringJoin(" "));
                DateTime? date = TryFindDate(normalized_joined);

                date ??= TryFindDateRange(normalized_joined)?.End;

                if (date is null || header.Keys.Max() >= cells.Count)
                    continue; // TODO : find better solution

                string pollster = pollster_index is null || pollster_index >= cells.Count ? source_uri : Normalize(cells[pollster_index.Value].InnerText);
                int? participants = participant_index.HasValue && participant_index < cells.Count && int.TryParse(cells[participant_index.Value].InnerText, out int i) ? i : null;
                Dictionary<Party, double> votes = Party.All.ToDictionary(LINQ.id, _ => 0d);

                foreach ((int index, Party party) in header)
                    foreach (string text in cells[index].ChildNodes
                                                        .SplitBy(node => node.Name == "br")
                                                        .Select(chunk => Normalize(chunk.Select(node => node.InnerText)
                                                                                        .StringJoin(" ")
                                                                                        .Replace("%", "")
                                                                                        .Replace(',', '.'))))
                        if (double.TryParse(text, out double percentage))
                            votes[party] += percentage * .01;
                        else if (text.Split([' ', '\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries) is [string ident, string perc])
                            if (Party.TryGetParty(ident) is { } p && double.TryParse(perc, out percentage))
                                votes[p] += percentage * .01;

                if (1 - votes.Values.Sum() is double diff and > 0)
                    votes[Party.__OTHER__] += diff;

                foreach (Party party in votes.Keys.ToArray())
                    if (votes[party] <= 0)
                        votes.Remove(party);

                yield return new(date.Value, state, pollster, source_uri, votes);
            }
        }
    }

    public static string Normalize(string text) => HttpUtility.HtmlDecode(text)
                                                              .Trim()
                                                              .ToLowerInvariant()
                                                              .RemoveDiacritics()
                                                              .Replace('•', '.')
                                                              .Replace('–', '-') // en dash
                                                              .Replace('—', '-') // em dash
                                                              .Replace('‒', '-') // fig dash
                                                              .Replace('―', '-');// hor. bar

    public static DateTime? TryFindDate(string text)
    {
        foreach (Match match in GenerateRegexDate().Matches(text))
        {
            string format = match.Value.Contains('-') ? "yyyy-MM-dd" : "dd.MM.yyyy";

            if (DateTime.TryParseExact(match.Value, format, null, DateTimeStyles.None, out DateTime date))
                return date;
        }

        return null;
    }

    public static (DateTime Start, DateTime End)? TryFindDateRange(string text)
    {
        foreach (Match match in GenerateRegexDateRange().Matches(text))
        {
            string start = match.Groups["start"].Value;
            string end = match.Groups["end"].Value;

            if (start.CountOccurences(".") == 2)
                start = "01." + start;

            if (end.CountOccurences(".") == 2)
                end = "01." + end;

            if (DateTime.TryParse(start, out DateTime s) && DateTime.TryParse(end, out DateTime e))
                return (s, e);
        }

        return null;
    }


    [GeneratedRegex(@"/[^/]*?/\.\.", RegexOptions.Compiled | RegexOptions.ECMAScript)]
    private static partial Regex GenerateRegexBackpath();

    [GeneratedRegex(@"(\d{1,2}\.\d{1,2}\.\d{2}(\d{2})?|\d{4}-\d{2}-\d{2})", RegexOptions.Compiled | RegexOptions.ECMAScript)]
    private static partial Regex GenerateRegexDate();

    [GeneratedRegex(@"(?<start>(\d{1,2}\.)?\d{1,2}\.\d{2}(\d{2})?)\s*-+\s*(?<end>(\d{1,2}\.)?\d{1,2}\.\d{2}(\d{2})?)", RegexOptions.Compiled | RegexOptions.ECMAScript)]
    private static partial Regex GenerateRegexDateRange();
}
