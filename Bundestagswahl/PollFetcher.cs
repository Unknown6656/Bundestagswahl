// use the following flag if you want to use SQLITE an an in-memory caching layer.
//#define USE_SQLITE_DB

using System.Text.RegularExpressions;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Globalization;
using System.Net.Http;
using System.Linq;
using System.Web;
using System.IO;
using System;

#if USE_SQLITE_DB
using System.Diagnostics.CodeAnalysis;
using System.Data.Common;
using System.Reflection;
using System.Text;

using Microsoft.Data.Sqlite;
#endif

using HtmlAgilityPack;

using Unknown6656.Generics;
using Unknown6656.Common;

namespace Bundestagswahl;


public sealed class PollDatabase
{
#if USE_SQLITE_DB
    private SqliteConnection _sqlite_conn;
#else
    private RawPolls _polls;
#endif
    private readonly FileInfo _dump_file;


    public PollDatabase(FileInfo dumpfile)
    {
        _dump_file = dumpfile;
#if USE_SQLITE_DB
        _sqlite_conn = null!;

        Connect().GetAwaiter().GetResult();
#else
        _polls = RawPolls.Empty;
#endif
    }

#if USE_SQLITE_DB
    private async Task Connect()
    {
        SqliteConnectionStringBuilder builder = new()
        {
            DataSource = ":memory:",
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
        };
        _sqlite_conn = new(builder.ConnectionString);

        await _sqlite_conn.OpenAsync();
        await ExecuteCommand("""
        PRAGMA journal_mode = WAL;
        PRAGMA synchronous = NORMAL;
        """);
        await CreateTables();
    }
#endif

    public async Task<bool> Load()
    {
        if (_dump_file.Exists)
        {
            List<RawPoll> results = [];

            using FileStream fs = new(_dump_file.FullName, FileMode.OpenOrCreate, FileAccess.Read, FileShare.ReadWrite);
            using BinaryReader rd = new(fs);

            while (RawPoll.TryDeserialize(rd) is RawPoll result)
                results.Add(result);
#if USE_SQLITE_DB
            await InsertPolls(results);
#else
            _polls = new(results.OrderBy(p => p.Date));
#endif
            return true;
        }

        return false;
    }

    public async Task Save()
    {
        if (_dump_file.Exists)
            _dump_file.Delete();

        await using FileStream fs = new(_dump_file.FullName, FileMode.Create, FileAccess.Write, FileShare.Read);
        await using BinaryWriter wr = new(fs);

#if USE_SQLITE_DB
        await foreach (RawPoll poll in FetchAllPolls())
#else
        foreach (RawPoll poll in _polls.Polls)
#endif
            poll.Serialize(wr);

        wr.Flush();
    }

#if USE_SQLITE_DB
    private async Task ResetConnection()
    {
        await _sqlite_conn.CloseAsync();
        await _sqlite_conn.DisposeAsync();

        _sqlite_conn = null!;

        if (_dump_file.Exists)
            _dump_file.Delete();

        await Connect();
    }

    [return: NotNullIfNotNull(nameof(str))]
    private static string? normalize(string? str) => str is null ? null : new(str.ToArrayWhere(char.IsAsciiLetterOrDigit, char.ToLower));

    private static object? Convert(object? value, Type target_type, out bool success)
    {
        success = true;

        if (target_type == typeof(void))
            return null;
        else if (target_type.IsGenericType && target_type.GetGenericTypeDefinition() == typeof(Nullable<>))
            return value is DBNull or null ? null : Convert(value, target_type.GetGenericArguments()[0], out success);
        else if (value is DBNull or null)
            return target_type.IsValueType ? Activator.CreateInstance(target_type) : null;
        else if (target_type.IsAssignableFrom(value.GetType()))
            return value;
        else if (target_type == typeof(bool))
            return Convert(value as string ?? value?.ToString() ?? "0", typeof(int), out success) is int and not 0;
        else if (target_type == typeof(Guid))
            return Guid.Parse(value as string ?? value?.ToString());
        else
            try
            {
                object? result = System.Convert.ChangeType(value, target_type);

                if (result is null && target_type.IsValueType)
                    result = Activator.CreateInstance(target_type);

                if (result is null || result.GetType().IsAssignableFrom(target_type))
                    return result;
            }
            catch
            {
            }

        if (value is string str)
        {
            // TODO : ?



            success = false;

            throw new InvalidCastException($"Cannot convert \"{str}\" ({value?.GetType()}) to an instance of {target_type}.");
        }
        else
            return Convert(value.ToString(), target_type, out success);
    }

    private async IAsyncEnumerable<T?> ExecuteCommand<T>(string sql)
    {
        await using SqliteCommand cmd = new(sql, _sqlite_conn);
        await using SqliteDataReader reader = await cmd.ExecuteReaderAsync();
        DbColumn[] schema = [.. await reader.GetColumnSchemaAsync()];
        (ConstructorInfo Ctor, (string Name, Type TargetType)[] Params, Dictionary<string, PropertyInfo> Props)? type_info = null;

        if (typeof(T).GetConstructors().OrderByDescending(c => c.GetParameters().Length).FirstOrDefault() is ConstructorInfo ctor)
        {
            (string Name, Type TargetType)[] param_mapping = ctor.GetParameters().ToArray(param => (normalize(param.Name) ?? "", param.ParameterType));
            Dictionary<string, PropertyInfo> prop_mapping = [];

            foreach (PropertyInfo prop in typeof(T).GetProperties())
                if (prop.CanWrite)
                    prop_mapping[normalize(prop.Name)] = prop;

            type_info = (ctor, param_mapping, prop_mapping);
        }

        while (await reader.ReadAsync())
        {
            object? parsed = null;
            bool success = false;

            if (schema.Length == 1)
                parsed = Convert(reader[0], typeof(T), out success);
            else if (type_info is { } info)
            {
                Dictionary<string, object> fields = schema.ToDictionary(col => normalize(col.ColumnName), col => reader[col.ColumnName]);
                object?[] args = new object?[info.Params.Length];

                success = true;

                for (int i = 0; i < args.Length && success; ++i)
                    args[i] = fields.TryGetValue(info.Params[i].Name, out object? value) ? Convert(value, info.Params[i].TargetType, out success) : null;

                try
                {
                    parsed = info.Ctor.Invoke(args);

                    foreach ((string name, object? value) in fields)
                        if (info.Props.TryGetValue(name, out PropertyInfo? prop) && success)
                            try
                            {
                                prop.SetValue(parsed, Convert(value, prop.PropertyType, out success));
                            }
                            catch
                            {
                                success = true; // TODO
                            }
                }
                catch
                {
                    success = false;
                }
            }

            if (success)
                yield return (T?)parsed;
            else
                yield break;
        }
    }

    private async Task ExecuteCommand(string sql)
    {
        await using SqliteCommand cmd = new(sql, _sqlite_conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private Task CreateTables() => ExecuteCommand($"""
        CREATE TABLE IF NOT EXISTS [PollInfo] (
            [ID]        INTEGER NOT NULL,
            [Date]      TEXT    NOT NULL,
            [Pollster]  TEXT    NULL,
            [Source]    TEXT    NULL,
            [State]     INTEGER NULL,

            PRIMARY KEY ([ID])
        );

        CREATE TABLE IF NOT EXISTS [PollResults] (
            [PollID]        INTEGER NOT NULL,
            [Party]         TEXT    NOT NULL,
            [Percentage]    REAL    NOT NULL,

            PRIMARY KEY ([PollID], [Party]),
            FOREIGN KEY ([PollID]) REFERENCES [PollInfo]([ID])
        );

        CREATE TABLE IF NOT EXISTS [FetcherInfo] (
            [__zero__]      INTEGER NOT NULL,
            [DateFetched]   TEXT    NOT NULL,

            -- TODO : more static data ?

            PRIMARY KEY ([__zero__])
        );
    """);

    public async Task<DateTime?> GetLastUpdated()
    {
        await foreach (DateTime dt in ExecuteCommand<DateTime>("SELECT [DateFetched] FROM [FetcherInfo] WHERE [__zero__] = 0"))
            return dt;

        return null;
    }
    
    public async Task InsertPolls(IEnumerable<RawPoll> polls)
    {
        StringBuilder sb = new("BEGIN;");
        int id = 0;

        foreach (RawPoll poll in polls)
        {
            string state = poll.State is State s ? ((int)s).ToString() : "NULL";

            sb.AppendLine($"""

            INSERT INTO [PollInfo] ([ID], [Date], [Pollster], [Source], [State])
            VALUES ({(++id).ToString(CultureInfo.InvariantCulture)}, '{poll.Date:yyyy-MM-dd}', '{poll.Pollster}', '{poll.SourceURI}', {state});

            INSERT OR IGNORE INTO [PollResults] ([PollID], [Party], [Percentage])
            VALUES {poll.Results.Select(kvp => $"({id.ToString(CultureInfo.InvariantCulture)}, '{kvp.Key.Identifier,3}', {kvp.Value})").StringJoin(",\n       ")};

            INSERT OR IGNORE INTO [FetcherInfo] ([__zero__], [DateFetched]) VALUES (0, '');
            UPDATE [FetcherInfo] SET [DateFetched] = '{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}' WHERE [__zero__] = 0;
            """);
        }

        sb.AppendLine("COMMIT;");

        await ExecuteCommand(sb.ToString());
    }
    
    public async IAsyncEnumerable<RawPoll> FetchAllPolls()
    {
        await foreach (_poll_info? info in ExecuteCommand<_poll_info>("SELECT [ID], [Date], [Pollster], [Source], [State] FROM [PollInfo]"))
            if (info is { })
            {
                Dictionary<Party, double> results = [];

                await foreach (_poll_result? result in ExecuteCommand<_poll_result>($"SELECT [PollID], [Party], [Percentage] FROM [PollResults] WHERE [PollID] = {info.ID.ToString(CultureInfo.InvariantCulture)}"))
                    if (result is { } && Party.TryGetParty(result.Party) is Party party)
                        results[party] = result.Percentage;

                yield return new(info.Date, info.State is int s ? (State)s : null, info.Pollster ?? "(none)", info.Source ?? "(none)", results);
            }
    }
#else
    public Task InsertPolls(IEnumerable<RawPoll> polls)
    {
        _polls = new(polls.Concat(_polls.Polls).OrderBy(p => p.Date));

        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<RawPoll> FetchAllPolls()
    {
        foreach (RawPoll poll in _polls.Polls)
            yield return poll;

        await Task.CompletedTask;
    }
#endif


    private record _poll_info(int ID, DateTime Date, string? Pollster, string? Source, int? State);
    private record _poll_result(int PollID, string Party, double Percentage);
}

public sealed partial class PollFetcher(PollDatabase database)
{
    public const long MAX_CACHE_LIFETIME_SECONDS = 3600 * 24 * 7; // keep cache for a maximum of one week.
    public static DateTime MIN_DATE { set; get; } = new(1990, 01, 01);

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


    public PollDatabase PollDatabase => database;


    private async Task WriteCacheAsync(RawPolls results)
    {
        await database.InsertPolls(results.Polls);
        await database.Save();

        throw null;
    }

    private async Task<RawPolls> ReadCache()
    {
        if (await database.Load())
        {
            List<RawPoll> polls = [];

            await foreach (RawPoll poll in database.FetchAllPolls())
                polls.Add(poll);

            return new(polls);
        }
        else
            return RawPolls.Empty;
    }

    public async Task<RawPolls> FetchAsync()
    {
        RawPolls results = await ReadCache();

        if (results.PollCount == 0)
        {
            results = await FetchAllPollsAsync();

            await WriteCacheAsync(results);
        }

        return results;
    }

    public static async Task<RawPolls> FetchAllPollsAsync()
    {
        IDictionary<string, HtmlDocument> documents = await FetchHTMLDocuments();
        ConcurrentBag<RawPoll> results = [];

        Parallel.ForEach(documents, kvp => ParseHTMLDocument(kvp.Value, kvp.Key).Do(results.Add));

        return new RawPolls(results);
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

    private static IEnumerable<RawPoll> ParseHTMLDocument(HtmlDocument document, string source_uri)
    {
        HtmlNodeCollection tables = document.DocumentNode.SelectNodes("//table[@class='wilko']");
        State? state = null;

        source_uri = NormalizeURI(source_uri);

        foreach ((State s, string uri) in BASE_URL_STATES)
            if (NormalizeURI(uri) is { } base_uri && (base_uri.Contains(source_uri) || source_uri.Contains(base_uri)))
            {
                state = s;

                break;
            }

        if (source_uri.Contains("berlin", StringComparison.OrdinalIgnoreCase))
            if (source_uri.Contains("ost", StringComparison.OrdinalIgnoreCase) || source_uri.Contains("east", StringComparison.OrdinalIgnoreCase))
                state = State.BE_O;
            else if (source_uri.Contains("west", StringComparison.OrdinalIgnoreCase))
                state = State.BE_W;
            else
                state = State.BE;

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

            foreach (HtmlNode row in table.SelectNodes("*/tr")?.Where(row => row.ParentNode.Name is "table" or "tbody") ?? [])
            {
                List<HtmlNode> cells = new(toprow.Count);

                foreach (HtmlNode cell in row.ChildNodes)
                    if (cell.Name == "td")
                    {
                        cells.Add(cell);

                        if (int.TryParse(cell.GetAttributeValue("colspan", "0"), out int colspan) && colspan > 0)
                            cells.AddRange(Enumerable.Repeat(cell, colspan - 1));
                    }

                string normalized_joined = NormalizeText(cells.Select(cell => cell.InnerText).StringJoin(" "));
                DateTime? date = TryFindDate(normalized_joined);

                date ??= TryFindDateRange(normalized_joined)?.End;

                if (date is null || header.Keys.Max() >= cells.Count)
                    continue; // TODO : find better solution

                string pollster = pollster_index is null || pollster_index >= cells.Count ? source_uri : NormalizeText(cells[pollster_index.Value].InnerText);
                int? participants = participant_index.HasValue && participant_index < cells.Count && int.TryParse(cells[participant_index.Value].InnerText, out int i) ? i : null;
                Dictionary<Party, double> votes = Party.All.ToDictionary(LINQ.id, _ => 0d);

                foreach ((int index, Party party) in header)
                    foreach (string text in cells[index].ChildNodes
                                                        .SplitBy(node => node.Name == "br")
                                                        .Select(chunk => NormalizeText(chunk.Select(node => node.InnerText)
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

    private static string NormalizeURI(string uri)
    {
        uri = NormalizeText(uri)
             .Replace(BASE_URL_FEDERAL, "")
             .Replace(".html", ".htm");

        if (!uri.EndsWith(".htm"))
            uri += ".htm";

        return uri;
    }

    public static string NormalizeText(string text) => HttpUtility.HtmlDecode(text)
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
