using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Globalization;
using System.Linq;
using System.Net;
using System;

using HtmlAgilityPack;


namespace Bundestagswahl
{
    using static Party;


    public static class Util
    {
        internal static readonly Dictionary<string, Party> _ids = new Dictionary<string, Party>
        {
            ["cdu"] = CDU,
            ["spd"] = SPD,
            ["fdp"] = FDP,
            ["afd"] = AFD,
            ["gru"] = GRÜNE,
            ["lin"] = LINKE,
            ["fw"] = FW,
            ["pir"] = PIRATEN,
            ["son"] = __OTHER__
        };


        public static async Task<HtmlDocument> GetHTML(string uri)
        {
            HtmlDocument doc = new HtmlDocument();

            using (WebClient wc = new WebClient())
                doc.LoadHtml(await wc.DownloadStringTaskAsync(uri));

            return doc;
        }

        public static async Task<HtmlNodeCollection> FetchTableRows(string uri, string selector = "//table[@class='wilko']/tbody") =>
            (await GetHTML(uri)).DocumentNode.SelectSingleNode(selector)?.ChildNodes;

        public static async Task<PollResult[]> FetchThisWeeksPollResultsAsync()
        {
            HtmlNodeCollection rows = await FetchTableRows("https://www.wahlrecht.de/umfragen/");
            List<PollResult> polls = new List<PollResult>();
            int index = 0;

            foreach (HtmlNode poll in rows.First(node => node.Id == "datum").SelectNodes(".//td/span[@class='li']"))
                if (DateTime.TryParseExact(poll.InnerText, "dd.MM.yyyy", null, DateTimeStyles.None, out DateTime date))
                {
                    ++index;

                    Dictionary<string, double> results = rows.Where(node => node.Id != "datum" && !string.IsNullOrEmpty(node.Id))
                                                             .ToDictionary(node => node.Id, node => double.TryParse(node.ChildNodes
                                                                                                                        .Where(n => n.Name == "td")
                                                                                                                        .Skip(index)
                                                                                                                        .First()
                                                                                                                        .InnerText
                                                                                                                        .Replace("%", "")
                                                                                                                        .Replace(',', '.')
                                                                                                                        .Trim(), out double d) ? d / 100d : double.NaN);

                    polls.Add(new PollResult(date, results));
                }

            return polls.ToArray();
        }

        public static async Task<PollResult[]> FetchThisLegislativePeriodsPollResultsAsync()
        {
            HtmlDocument doc = await GetHTML("https://www.wahlrecht.de/umfragen/insa.htm");
            List<PollResult> polls = new List<PollResult>();

            var toprow = doc.DocumentNode.SelectNodes("//table[@class='wilko']/thead/tr/th");
            var header = toprow.Where(node => node.Attributes.Contains("class") && node.Attributes["class"].Value == "part")
                               .Select(node => node.ChildNodes.First(child => child.Name == "a")
                                                   .Attributes["href"]
                                                   .Value
                                                   .Replace("#fn-", ""))
                               .ToArray();

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
                                                            .ToDictionary(sd => sd.s, sd => sd.d)));
            }

            return polls.ToArray();
        }
    }

    public readonly struct Coalition
    {
        public Party[] CoalitionParties { get; }
        public Party[] OppositionParties { get; }
        public double CoalitionPercentage { get; }
        public double OppositionPercentage { get; }
        private PollResult Result { get; }
        public double this[Party p] => Result[p];


        public Coalition(PollResult result, params Party[] parties)
            : this()
        {
            Result = result.Normalized;
            CoalitionParties = parties;
            OppositionParties = new []{ CDU, AFD, SPD, FDP, GRÜNE, LINKE }.Except(parties).ToArray();
            CoalitionPercentage = 0;
            OppositionPercentage = 0;

            foreach (Party party in Result.Results.Keys)
                if (parties.Contains(party))
                    CoalitionPercentage += Result[party];
                else
                    OppositionPercentage += Result[party];
        }
    }

    public readonly struct PollResult
    {
        public DateTime Date { get; }
        internal IReadOnlyDictionary<Party, double> Results { get; }
        public double this[Party p] => Results.ContainsKey(p) ? Results[p] : 0;
        public PollResult Normalized
        {
            get
            {
                double sum = 1 - this[__OTHER__];

                return new PollResult(Date, Results.ToDictionary(kvp => kvp.Key, kvp => kvp.Value / sum));
            }
        }


        public PollResult(DateTime date, Dictionary<Party, double> values)
            : this()
        {
            Date = date;
            Results = new ReadOnlyDictionary<Party, double>(values);
        }

        public PollResult(DateTime date, Dictionary<string, double> values)
            : this(date, new Func<Dictionary<Party, double>>(() =>
            {
                Dictionary<Party, double> percentages = new Dictionary<Party, double>();

                foreach (string id in values.Keys)
                    if (Util._ids.ContainsKey(id))
                        percentages[Util._ids[id]] = values[id];

                if (!percentages.ContainsKey(__OTHER__))
                    percentages[__OTHER__] = 1 - percentages.Values.Sum();

                return percentages;
            })())
        {
        }

        public override string ToString()
        {
            var coll = Results;

            return string.Join(" | ", Enum.GetValues(typeof(Party)).Cast<Party>().Select(p => $"{p}: {Math.Round(coll[p] * 100d, 1)} %"));
        }
    }

    public enum Party
    {
        CDU,
        AFD,
        SPD,
        FDP,
        FW,
        GRÜNE,
        LINKE,
        PIRATEN,
        __OTHER__
    }
}
