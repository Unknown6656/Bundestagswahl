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

public sealed class Party(PartyIdentifier identifier, string name, string color)
    : IEquatable<Party>
{
    public static Party CDU { get; }       = new("cdu", "CDU/CSU",         "\e[38;2;117;100;135m");
    public static Party SPD { get; }       = new("spd", "SPD",             "\e[38;2;255;40;40m");
    public static Party FDP { get; }       = new("fdp", "FDP",             "\e[38;2;255;200;0m");
    public static Party AFD { get; }       = new("afd", "AfD",             "\e[38;2;0;158;224m");
    public static Party GRÜNE { get; }     = new("grü", "B.90/Die Grünen", "\e[38;2;60;155;0m");
    public static Party LINKE { get; }     = new("lin", "Die Linke",       "\e[38;2;208;0;67m");
    public static Party PIRATEN { get; }   = new("pir", "Die Piraten",     "\e[38;2;255;135;0m");
    public static Party FW { get; }        = new("fw",  "Freie Wähler",    "\e[38;2;0;70;255m");
    public static Party RECHTE { get; }    = new("rep", "NPD/REP/Rechte",  "\e[38;2;170;122;44m");
    public static Party BSW { get; }       = new("bsw", "BSW",             "\e[38;2;111;0;60m");
    public static Party __OTHER__ { get; } = new("son", "Sonstige",        "\e[38;2;126;176;165m");

    public static Party[] All { get; } = [CDU, SPD, FDP, AFD, GRÜNE, LINKE, BSW, PIRATEN, FW, RECHTE, __OTHER__];
    public static Party[] LeftToRight { get; } = [LINKE, BSW, PIRATEN, SPD, GRÜNE, FDP, FW, CDU, AFD, RECHTE];


    internal PartyIdentifier Identifier { get; } = identifier;

    public string Name { get; } = name;

    public string VT100Color { get; } = color;


    public override int GetHashCode() => Identifier.GetHashCode();

    public override bool Equals(object? obj) => obj is Party p && Equals(p);

    public bool Equals(Party? other) => Identifier.Equals(other?.Identifier);

    public override string ToString() => Name;

    public static Party TryGetParty(string name)
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
            case "sonstige" or "andere":
                return __OTHER__;
            default:
                Debug.Write($"Unknown party '{name}'.");

                return __OTHER__; // TODO

        }
    }
}

public sealed class Coalition
{
    public Party[] CoalitionParties { get; }
    public Party[] OppositionParties { get; }
    public double CoalitionPercentage { get; }
    public double OppositionPercentage { get; }
    private PollResult Result { get; }

    public double this[Party p] => Result[p];


    public Coalition(PollResult result, params Party[] parties)
    {
        Result = result;
        CoalitionParties = parties;
        OppositionParties = new []{ Party.CDU, Party.SPD, Party.FDP, Party.AFD, Party.GRÜNE, Party.LINKE }.Except(parties).ToArray();
        CoalitionPercentage = 0;
        OppositionPercentage = 0;

        foreach (Party party in Result.Results.Keys)
            if (parties.Contains(party))
                CoalitionPercentage += Result[party];
            else
                OppositionPercentage += Result[party];
    }
}

public sealed class PollResult
{
    public DateTime Date { get; }

    public string Pollster { get; }

    public string SourceURI { get; }

    public State? State { get; }

    public bool IsFederal => State is null;

    public Party StrongestParty => Results.OrderByDescending(kvp => kvp.Value).FirstOrDefault().Key ?? Party.__OTHER__;

    internal IReadOnlyDictionary<Party, double> Results { get; }

    public double this[Party p] => Results.ContainsKey(p) ? Results[p] : 0;


    public PollResult(DateTime date, State? state, string pollster, string source_uri, Dictionary<Party, double> values)
    {
        Date = date;
        State = state;
        Pollster = pollster;
        SourceURI = source_uri;

        if (!values.ContainsKey(Party.__OTHER__))
            values[Party.__OTHER__] = 1 - values.Values.Sum();

        double sum = values.Values.Sum();

        if (sum is not 1 or 0)
            values = values.ToDictionary(pair => pair.Key, pair => pair.Value / sum);

        Results = new ReadOnlyDictionary<Party, double>(values);
    }

    public PollResult(DateTime date, State? state, string pollster, string source_uri, Dictionary<string, double> values)
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
        $"{Date:yyyy-MM-dd}, {State?.ToString() ?? "BUND"} {Results.Select(kvp => $"| {kvp.Key.Identifier}: {kvp.Value:P1}").StringConcat()} ({SourceURI})";

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

    internal static PollResult? TryDeserialize(BinaryReader reader)
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

// TODO : combined poll results?

public sealed partial class PollFetcher(FileInfo cachefile)
{
    public const long MAX_CACHE_LIFETIME_SECONDS = 3600 * 24 * 7; // keep cache for a maximum of one week.

    // TODO : do per state

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
    private static readonly Regex _regex_backpath = GenerateRegexBackpath();
    private static readonly Regex _regex_date = GenerateRegexDate();



    public void InvalidateCache()
    {
        if (cachefile.Exists)
            cachefile.Delete();
    }

    public async Task WriteCacheAsync(IEnumerable<PollResult> results)
    {
        await using FileStream fs = new(cachefile.FullName, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read);
        await using BinaryWriter wr = new(fs);

        wr.Write(DateTime.UtcNow.Ticks);

        foreach (PollResult result in results.OrderBy(r => r.Date))
            result.Serialize(wr);

        wr.Flush();

        await fs.FlushAsync();
    }

    public PollResult[] ReadCache()
    {
        try
        {
            using FileStream fs = new(cachefile.FullName, FileMode.OpenOrCreate, FileAccess.Read, FileShare.ReadWrite);
            using BinaryReader rd = new(fs);

            DateTime created = new(rd.ReadInt64());
            DateTime now = DateTime.UtcNow;

            if (Math.Abs((now - created).TotalSeconds) < MAX_CACHE_LIFETIME_SECONDS)
            {
                List<PollResult> results = [];

                while (PollResult.TryDeserialize(rd) is PollResult result)
                    results.Add(result);

                return [.. results.OrderBy(r => r.Date)];
            }
        }
        catch (EndOfStreamException)
        {
        }

        return [];
    }

    public async Task<PollResult[]> FetchAsync()
    {
        PollResult[] results = ReadCache();

        if (results.Length == 0)
        {
            results = await FetchAllPollResultsAsync();

            await WriteCacheAsync(results);
        }

        return results;
    }

    public static async Task<PollResult[]> FetchAllPollResultsAsync()
    {
        IDictionary<string, HtmlDocument> documents = await FetchAllPollsAsync();
        ConcurrentBag<PollResult> results = [];

        Parallel.ForEach(documents, kvp => FetchPollResults(kvp.Value, kvp.Key).Do(results.Add));

        return [.. results.OrderBy(r => r.Date)];
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

    private static async Task<IDictionary<string, HtmlDocument>> FetchAllPollsAsync()
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

                    sanitized_uri = _regex_backpath.Replace(sanitized_uri, "");

                    open.Add(sanitized_uri);
                }
        }

        //await Parallel.ForEachAsync(BASE_URL_FEDERAL_POLLING.Concat(BASE_URL_STATES.Values), async (base_uri, _) => await fetch(base_uri));
        await Parallel.ForEachAsync([BASE_URL_STATES[State.BW]], async (base_uri, _) => await fetch(base_uri));

        while (open.Count > 0 && open.First() is string uri)
        {
            open.Remove(uri);

            if (!results.ContainsKey(uri))
                await fetch(uri);
        }

        return results;
    }

    private static IEnumerable<PollResult> FetchPollResults(HtmlDocument document, string source_uri)
    {
        HtmlNodeCollection tables = document.DocumentNode.SelectNodes("//table[@class='wilko']");
        State? state = null;

        foreach ((State s, string uri) in BASE_URL_STATES)
            if (uri == source_uri)
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
            int? date_index = null;

            foreach ((HtmlNode node, int index) in toprow.WithIndex())
                if (node.GetAttributeValue("class", "") == "part")
                    header[index] = Party.TryGetParty(node.InnerText);
                else
                {
                    string header_text = new(node.InnerText.SelectWhere(char.IsAsciiLetter, char.ToLower).ToArray());

                    if (header_text.Contains("institut"))
                        pollster_index = index;
                    else if (header_text.Contains("befragte") || header_text.Contains("teilnehmer"))
                        participant_index = index;
                    else if (header_text.Contains("datum"))
                        date_index = index;
                    else if (header_text.Contains("zeitraum") && date_index is null)
                        date_index = index;
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

                DateTime? date = TryFindDate(cells[date_index ?? 0].InnerText);

                foreach (HtmlNode cell in cells.Skip(1))
                    if ((date ??= TryFindDate(cell.InnerText)) is { })
                        break;

                if (date is null)
                    continue; // TODO : find better solution

                string pollster = pollster_index is null ? source_uri : cells[pollster_index.Value];
                int participants = participant_index is int i && int.TryParse(cells[i], out int p) ? p : 0; // TODO

                yield return new(
                    date.Value,
                    state,
                    pollster,
                    source_uri,
                    header.ToDictionary(kvp => kvp.Value, kvp => double.TryParse(cells[kvp.Key].Replace(" %", "")
                                                                                               .Replace(',', '.'), out double value) ? value / 100d : 0)
                );
            }
        }
    }

    public static string Normalize(string text) => text.ToLowerInvariant()
        .Trim()
        .Replace('•', '.')
                   .Replace('–', '-') // en dash
                   .Replace('—', '-') // em dash
                   .Replace('‒', '-') // fig dash
                   .Replace('―', '-');// hor. bar

    public static DateTime? TryFindDate(string text)
    {

        foreach (Match match in _regex_date.Matches(text))
        {
            string format = match.Value.Contains('-') ? "yyyy-MM-dd" : "dd.MM.yyyy";

            if (DateTime.TryParseExact(match.Value, format, null, DateTimeStyles.None, out DateTime date))
                return date;
        }

        return null;
    }

    public static (DateTime Start, DateTime End)? TryFindDateRange(string text)
    {
        text = 

        foreach (Match match in _regex_date.Matches(text))
        {
            string format = match.Value.Contains('-') ? "yyyy-MM-dd" : "dd.MM.yyyy";

            if (DateTime.TryParseExact(match.Value, format, null, DateTimeStyles.None, out DateTime date))
                return date;
        }

        return null;
    }

    [GeneratedRegex(@"/[^/]*?/\.\.", RegexOptions.Compiled | RegexOptions.ECMAScript)]
    private static partial Regex GenerateRegexBackpath();

    [GeneratedRegex(@"(\d{1,2}\.\d{1,2}\.\d{2}(\d{2})?|\d{4}-\d{2}-\d{2})", RegexOptions.Compiled | RegexOptions.ECMAScript)]
    private static partial Regex GenerateRegexDate();
}
