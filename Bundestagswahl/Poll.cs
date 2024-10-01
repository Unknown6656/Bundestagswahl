using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.IO;
using System;

using Unknown6656.Generics;

namespace Bundestagswahl;


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

public sealed class RawPoll
    : IPoll
{
    public static RawPoll Empty { get; } = new(DateTime.UnixEpoch, null, "<none>", "<none>", new Dictionary<Party, double>());


    public DateTime Date { get; }

    public string Pollster { get; }

    public string SourceURI { get; }

    public State? State { get; }

    public bool IsFederal => State is null;

    public Party StrongestParty => Results.OrderByDescending(kvp => kvp.Value).FirstOrDefault().Key ?? Party.__OTHER__;

    internal IReadOnlyDictionary<Party, double> Results { get; }

    public double this[Party p] => Results.ContainsKey(p) ? Results[p] : 0;


    public RawPoll(DateTime date, State? state, string pollster, string source_uri, Dictionary<Party, double> values)
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

    public RawPoll(DateTime date, State? state, string pollster, string source_uri, Dictionary<string, double> values)
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
            char[] identifier = [.. party.Identifier];

            writer.Write(identifier, 0, PartyIdentifier.SIZE);
            writer.Write(result);
        }
    }

    internal static RawPoll? TryDeserialize(BinaryReader reader)
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
    private const double EAST_BERLIN_PERCENTAGE = .37516441883;
    private static readonly Dictionary<State, int> _population_per_state = new()
    {
        [State.BW] = 11_280_000,
        [State.BY] = 13_369_000,
        [State.BE] = 3_755_000,
        [State.BB] = 2_573_000,
        [State.HB] = 685_000,
        [State.HH] = 1_892_000,
        [State.HE] = 6_391_000,
        [State.MV] = 1_628_000,
        [State.NI] = 8_140_000,
        [State.NW] = 18_139_000,
        [State.RP] = 4_159_000,
        [State.SL] = 993_000,
        [State.SN] = 4_086_000,
        [State.ST] = 2_187_000,
        [State.SH] = 2_953_000,
        [State.TH] = 2_127_000,
    };
    private static readonly int _population_total = _population_per_state.Values.Sum();

    public RawPoll[] Polls { get; }

    public State[] States { get; }

    public Party StrongestParty { get; }

    public (Party party, double percentage)[] Percentages => [..from party in Party.All
                                                                let perc = this[party]
                                                                where perc > 0
                                                                orderby perc descending
                                                                select (party, perc)];

    public DateTime EarliestDate { get; }

    public DateTime LatestDate { get; }

    DateTime IPoll.Date => LatestDate;

    internal IReadOnlyDictionary<Party, double> Results { get; }

    public double this[Party p] => Results.ContainsKey(p) ? Results[p] : 0;


    static MergedPoll()
    {
        int be = _population_per_state[State.BE];

        _population_per_state[State.BE_O] = (int)(be * EAST_BERLIN_PERCENTAGE);
        _population_per_state[State.BE_W] = be - _population_per_state[State.BE_O];
    }

    public MergedPoll(params RawPoll[] polls)
    {
        Polls = polls;

        if (polls.Length > 0)
        {
            States = polls.SelectWhere(p => p.State.HasValue, p => p.State!.Value).Distinct().ToArray();
            EarliestDate = polls.Min(p => p.Date);
            LatestDate = polls.Max(p => p.Date);

            Dictionary<Party, double> results = [];
            double total = 0;

            foreach (Party party in Party.All)
            {
                double sum = 0;
                double pop = 0;

                foreach (RawPoll poll in polls)
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
            EarliestDate =
            LatestDate = DateTime.UtcNow;
            Results = Party.All.ToDictionary(LINQ.id, p => 0d);
            StrongestParty = Party.__OTHER__;
        }
    }

    public override string ToString() =>
        $"{EarliestDate:yyyy-MM-dd}-{LatestDate:yyyy-MM-dd}, {States.Select(s => s.ToString() ?? "BUND").StringJoin(", ")} {Results.Select(kvp => $", {kvp.Key.Identifier}={kvp.Value:P1}").StringConcat()} ({Polls.Length} polls)";

    public static implicit operator RawPoll[](MergedPoll poll) => poll.Polls;

    public static implicit operator MergedPoll(RawPoll[] polls) => new(polls);
}

// TODO : check whether we need this class or if we can merge its functionality into "PollHistory"
public sealed class RawPolls(IEnumerable<RawPoll> polls)
{
    public static RawPolls Empty { get; } = new([]);

    public int PollCount => Polls.Length;

    public RawPoll[] Polls { get; } = [.. polls.OrderByDescending(p => p.Date)];


    public string AsCSV()
    {
        Party[] parties = Party.All;
        StringBuilder sb = new();

        sb.AppendLine($"id,date,pollster,source,state,{parties.Select(p => p.Identifier).StringJoin(",")}");

        for (int i = 0; i < Polls.Length; i++)
        {
            RawPoll poll = Polls[i];

            sb.AppendLine($"{i},{poll.Date:yyyy-MM-dd},\"{poll.Pollster}\",\"{poll.SourceURI}\",{poll.State?.ToString() ?? "DE"},{parties.Select(p => poll[p].ToString("P1")).StringJoin(",")}");
        }

        return sb.ToString();
    }

    public string AsCSV(State? state)
    {
        Party[] parties = Party.All;
        StringBuilder sb = new();

        sb.AppendLine($"id,date,pollster,source,state,{parties.Select(p => p.Identifier).StringJoin(",")}");

        for (int i = 0; i < Polls.Length; i++)
        {
            RawPoll poll = Polls[i];

            if (poll.State == state || (state is State.BE && poll.State is State.BE_W or State.BE_O))
                sb.AppendLine($"{i},{poll.Date:yyyy-MM-dd},\"{poll.Pollster}\",\"{poll.SourceURI}\",{poll.State},{parties.Select(p => poll[p].ToString("P1")).StringJoin(",")}");
        }

        return sb.ToString();
    }
}

public sealed class MergedPollHistory(IEnumerable<MergedPoll> polls)
{
    public static MergedPollHistory Empty { get; } = new([]);


    public int PollCount => Polls.Length;

    public MergedPoll[] Polls { get; } = [.. polls.OrderByDescending(p => p.LatestDate)];

    public MergedPoll? MostRecentPoll => Polls.FirstOrDefault();

    public MergedPoll? OldestPoll => Polls.LastOrDefault();

    public DateTime[] PollingDates { get; } = [.. polls.Select(p => p.LatestDate).Distinct().OrderByDescending(LINQ.id)];


    //public Poll? MostRecent() => Polls.FirstOrDefault();

    //public Poll[] MostRecent(int count) => Polls.Take(count).ToArray();

    //public Poll? MostRecent(State? state) => Polls.Where(p => p.State == state).FirstOrDefault();

    //public Poll[] MostRecent(State? state, int count) => Polls.Where(p => p.State == state).Take(count).ToArray();

    //public Poll[] In(State? state) => Polls.Where(p => p.State == state).ToArray();

    //public Poll[] During(DateTime earliest, DateTime latest) => Polls.Where(p => p.Date >= earliest && p.Date <= latest).ToArray();

    //public Poll[] During(DateTime earliest, DateTime latest, State state) => Polls.Where(p => p.Date >= earliest && p.Date <= latest && p.State == state).ToArray();


    public MergedPoll? GetMostRecentAt(DateTime date) => Polls.SkipWhile(p => p.LatestDate > date).FirstOrDefault();

    public string AsCSV()
    {
        Party[] parties = Party.All;
        StringBuilder sb = new();

        sb.AppendLine($"id,start,end,pollsters,sources,states,{parties.Select(p => p.Identifier).StringJoin(",")}");

        for (int i = 0; i < Polls.Length; i++)
        {
            MergedPoll merged = Polls[i];
            string pollsters = merged.Polls.Select(p => p.Pollster).Distinct().StringJoin(";");
            string sources = merged.Polls.Select(p => p.SourceURI).Distinct().StringJoin(";");
            string states = merged.Polls.Select(p => p.State?.ToString() ?? "DE").Distinct().StringJoin(";");

            sb.AppendLine($"{i},{merged.EarliestDate:yyyy-MM-dd},{merged.LatestDate:yyyy-MM-dd},\"{pollsters}\",\"{sources}\",{states},{parties.Select(p => merged[p].ToString("P1")).StringJoin(",")}");
        }

        return sb.ToString();
    }
}
