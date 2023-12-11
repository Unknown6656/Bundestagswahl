using System.Windows.Media;

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Globalization;
using System.Linq;
using System;

using HtmlAgilityPack;

using Unknown6656.Generics;
using System.Net.Http;

namespace Bundestagswahl;


public record Party(string Identifier, string Name, Brush Brush)
{
    public static Party CDU { get; } = new("cdu", "CDU/CSU", Brushes.Black);
    public static Party SPD { get; } = new("spd", "SPD", Brushes.Red);
    public static Party FDP { get; } = new("fdp", "FDP", Brushes.Gold);
    public static Party AFD { get; } = new("afd", "AfD", Brushes.DodgerBlue);
    public static Party GRÜNE { get; } = new("gru", "B.90/Die Grünen", Brushes.ForestGreen);
    public static Party LINKE { get; } = new("lin", "Die Linke", Brushes.Purple);
    public static Party PIRATEN { get; } = new("pir", "Die Piraten", Brushes.DarkOrange);
    public static Party FW { get; } = new("fw", "Freie Wähler", Brushes.Blue);
    public static Party __OTHER__ { get; } = new("son", "Sonstige", Brushes.Gray);

    public static Party[] All { get; } = [CDU, SPD, FDP, AFD, GRÜNE, LINKE, PIRATEN, FW, __OTHER__];
    public static Party[] LeftToRight { get; } = [LINKE, PIRATEN, SPD, GRÜNE, FDP, FW, CDU, AFD,];


    public override string ToString() => Name;
}

public static class Util
{
    public static async Task<HtmlDocument> GetHTML(string uri)
    {
        HtmlDocument doc = new();
        using HttpClient client = new();

        doc.LoadHtml(await client.GetStringAsync(uri));

        return doc;
    }

    public static async Task<HtmlNodeCollection> FetchTableRows(string uri, string selector = "//table[@class='wilko']/tbody") =>
        (await GetHTML(uri)).DocumentNode.SelectSingleNode(selector)?.ChildNodes;

    public static async Task<PollResult[]> FetchThisWeeksPollResultsAsync()
    {
        HtmlNodeCollection rows = await FetchTableRows("https://www.wahlrecht.de/umfragen/");
        List<PollResult> polls = [];
        int index = 0;

        foreach (HtmlNode poll in rows.First(node => node.Id is "datum").SelectNodes(".//td/span[@class='li']"))
            if (DateTime.TryParseExact(poll.InnerText, "dd.MM.yyyy", null, DateTimeStyles.None, out DateTime date))
            {
                ++index;

                Dictionary<string, double> results = rows.Where(node => node.Id != "datum" && !string.IsNullOrEmpty(node.Id))
                                                         .ToDictionary(node => node.Id, node => double.TryParse(node.ChildNodes
                                                                                                                    .Where(n => n.Name is "td")
                                                                                                                    .Skip(index)
                                                                                                                    .First()
                                                                                                                    .InnerText
                                                                                                                    .Replace("%", "")
                                                                                                                    .Replace(',', '.')
                                                                                                                    .Trim(), out double d) ? d / 100d : double.NaN);

                polls.Add(new PollResult(date, results));
            }

        return [.. polls];
    }

    public static async Task<PollResult[]> FetchThisLegislativePeriodsPollResultsAsync()
    {
        HtmlDocument doc = await GetHTML("https://www.wahlrecht.de/umfragen/insa.htm");
        List<PollResult> polls = [];

        HtmlNodeCollection toprow = doc.DocumentNode.SelectNodes("//table[@class='wilko']/thead/tr/th");
        string[] header = toprow.ToArrayWhere(
            node => node.Attributes.Contains("class") && node.Attributes["class"].Value == "part",
            node => node.ChildNodes.First(child => child.Name == "a")
                        .Attributes["href"]
                        .Value
                        .Replace("#fn-", "")
        );

        foreach (HtmlNode row in doc.DocumentNode.SelectNodes("//table[@class='wilko']/tbody/tr"))
        {
            HtmlNode[] cells = row.ChildNodes.Where(child => child.Name == "td").ToArray();

            if (DateTime.TryParseExact(cells.FirstOrDefault(child => child.Attributes["class"]?.Value == "s")?.InnerText, "dd.MM.yyyy", null, DateTimeStyles.None, out DateTime date))
                if (cells.Length >= toprow.Count)
                    polls.Add(new PollResult(date, cells.Skip(2)
                                                        .Take(header.Length)
                                                        .Select(node => double.TryParse(node.InnerText
                                                                                            .Replace(" %", "")
                                                                                            .Replace(',', '.'), out double value) ? value / 100d : 0)
                                                        .Zip(header, (v, h) => (s: h, d: v))
                                                        .ToDictionary()));
        }

        return [.. polls];
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
        Result = result.Normalized;
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

    internal IReadOnlyDictionary<Party, double> Results { get; }

    public double this[Party p] => Results.ContainsKey(p) ? Results[p] : 0;

    public PollResult Normalized
    {
        get
        {
            double sum = 1 - this[Party.__OTHER__];

            return new PollResult(Date, Results.ToDictionary(kvp => kvp.Key, kvp => kvp.Value / sum));
        }
    }


    public PollResult(DateTime date, Dictionary<Party, double> values)
    {
        Date = date;
        Results = new ReadOnlyDictionary<Party, double>(values);
    }

    public PollResult(DateTime date, Dictionary<string, double> values)
        : this(date, new Func<Dictionary<Party, double>>(() =>
        {
            Dictionary<Party, double> percentages = [];

            foreach (string id in values.Keys)
                if (Party.All.FirstOrDefault(p => p.Identifier == id) is Party p)
                    percentages[p] = values[id];

            if (!percentages.ContainsKey(Party.__OTHER__))
                percentages[Party.__OTHER__] = 1 - percentages.Values.Sum();

            return percentages;
        })())
    {
    }

    public override string ToString() => Party.All.Select(p => $"{p}: {Math.Round(Results[p] * 100d, 1)} %").StringJoin(" | ");
}
