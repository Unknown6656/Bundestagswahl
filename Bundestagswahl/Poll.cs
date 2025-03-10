﻿using System.Text.RegularExpressions;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System;

using Unknown6656.Generics.Text;
using Unknown6656.Generics;
using Unknown6656;

namespace Bundestagswahl;


public interface IPoll
{
    public DateOnly Date { get; }

    public string? PollingSource { get; }

    public Party StrongestParty { get; }

    public float this[Party party] { get; }
}

public sealed class Coalition
    : IPoll
{
    private readonly Dictionary<Party, float> _values = [];


    public Party[] CoalitionParties { get; }

    public Party[] OppositionParties { get; }

    public Party? StrongestParty { get; }

    public float CoalitionPercentage { get; }

    public float OppositionPercentage { get; }

    public IPoll UnderlyingPoll { get; }

    public string? PollingSource => UnderlyingPoll.PollingSource;

    public DateOnly Date => UnderlyingPoll.Date;


    public float this[Party party] => _values.TryGetValue(party, out float percentage) ? percentage : 0;


    public Coalition(IPoll poll, params Party[] parties)
    {
        UnderlyingPoll = poll;
        CoalitionParties = [.. parties.Where(p => poll[p] >= .05).OrderByDescending(p => poll[p])];
        OppositionParties = Party.All.Except(parties).ToArrayWhere(p => poll[p] >= .05);
        StrongestParty = CoalitionParties.MaxBy(p => poll[p]);
        CoalitionPercentage = 0;

        float sum = 0;

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

    public override string ToString() => $"{Date:yyyy-MM-dd}, {CoalitionParties.Select(p => p.Identifier).StringJoin(" + ")}: {CoalitionPercentage:P1}";
}

[Flags]
file enum RawPollFlags
    : byte
{
    None = 0,
    IsSynthetic = 1,
    State_NotNull = 2,
    Pollster_NotNull = 4,
    SourceURI_NotNull = 8,
}

public sealed partial class RawPoll
    : IPoll
{
    public static RawPoll Empty { get; } = new(new(1970, 1, 1), null, null, null, true, new Dictionary<Party, float>());


    public DateOnly Date { get; }

    public string RawPollster { get; }

    public PollSource Pollster { get; }

    public string? SourceURI { get; }

    public bool IsSynthetic { get; }

    public State? State { get; }

    public bool IsFederal => State is null;

    string? IPoll.PollingSource => (RawPollster ?? SourceURI)?.Replace(".html", ".htm", StringComparison.OrdinalIgnoreCase)
                                                             ?.TrimEnd(".htm");

    public Party StrongestParty => Results.OrderByDescending(static kvp => kvp.Value).FirstOrDefault().Key ?? Party.__OTHER__;

    internal IReadOnlyDictionary<Party, float> Results { get; }

    public float this[Party p] => Results.ContainsKey(p) ? Results[p] : 0;


    public RawPoll(DateOnly date, State? state, Union<string?, (string, PollSource)> pollster, string? source_uri, bool synthetic, Dictionary<Party, float> values)
    {
        Date = date;
        State = state;
        (RawPollster, Pollster) = pollster.Match(ParsePollster, LINQ.id);
        SourceURI = source_uri;
        IsSynthetic = synthetic;

        if (!values.ContainsKey(Party.__OTHER__))
            values[Party.__OTHER__] = float.Max(0, 1 - values.Values.Sum());

        float sum = values.Values.Sum();

        if (sum is not 1 or 0)
            values = values.ToDictionary(pair => pair.Key, pair => pair.Value / sum);

        Results = new ReadOnlyDictionary<Party, float>(values);
    }

    public RawPoll(DateOnly date, State? state, Union<string?, (string, PollSource)> pollster, string? source_uri, bool synthetic, Dictionary<string, float> values)
        : this(date, state, pollster, source_uri, synthetic, new Func<Dictionary<Party, float>>(() =>
        {
            Dictionary<Party, float> percentages = [];

            foreach (string id in values.Keys)
                if (Party.All.FirstOrDefault(p => p.Identifier == id) is Party p)
                    percentages[p] = values[id];

            return percentages;
        })())
    {
    }

    public override string ToString() =>
        $"{Date:yyyy-MM-dd}, {State?.ToString() ?? "BUND"} {Results.Select(static kvp => $", {kvp.Key.Identifier}={kvp.Value:P1}").StringConcat()} ({SourceURI ?? "unknown"})";

    internal void Serialize(BinaryWriter writer)
    {
        writer.Write((ushort)Date.Year);
        writer.Write((byte)Date.Month);
        writer.Write((byte)Date.Day);

        RawPollFlags flags = RawPollFlags.None
                           | (State is not null ? RawPollFlags.State_NotNull : 0)
                           | (SourceURI is not null ? RawPollFlags.SourceURI_NotNull : 0)
                           | (IsSynthetic ? RawPollFlags.IsSynthetic : 0);

        writer.Write((byte)flags);

        if (State != null)
            writer.Write((byte)State);

        if (SourceURI != null)
            writer.Write(SourceURI);

        writer.Write(RawPollster);
        writer.Write((uint)Pollster);
        writer.Write(Results.Count);

        foreach ((Party party, float result) in Results)
        {
            writer.Write(party.Identifier);
            writer.Write(result);
        }
    }

    internal static RawPoll? TryDeserialize(BinaryReader reader)
    {
        try
        {
            ushort year = reader.ReadUInt16();
            byte month = reader.ReadByte();
            byte day = reader.ReadByte();
            RawPollFlags flags = (RawPollFlags)reader.ReadByte();
            State? state = null;
            string? source_uri = null;

            if (flags.HasFlag(RawPollFlags.State_NotNull))
                state = (State)reader.ReadByte();

            if (flags.HasFlag(RawPollFlags.SourceURI_NotNull))
                source_uri = reader.ReadString();

            string raw_pollster = reader.ReadString();
            PollSource pollster = (PollSource)reader.ReadUInt32();

            bool synth = flags.HasFlag(RawPollFlags.IsSynthetic);
            int count = reader.ReadInt32();

            Dictionary<Party, float> results = new()
            {
                [Party.__OTHER__] = 0f
            };

            for (int i = 0; i < count; ++i)
            {
                string identifier = reader.ReadString();
                float result = reader.ReadSingle();

                if (Party.All.FirstOrDefault(p => p.Identifier == identifier) is Party party)
                    results[party] = result;
                else
                    results[Party.__OTHER__] += result;
            }

            return new(new(year, month, day), state, (raw_pollster, pollster), source_uri, synth, results);
        }
        catch
        {
            return null;
        }
    }

    private static (string raw, PollSource source) ParsePollster(string? pollster)
    {
        pollster ??= "";
        pollster = pollster.Replace('’', '\'');
        pollster = new(pollster.SelectWhere(c => !(char.IsWhiteSpace(c) || c is '*' or '-' or '+' or '&' or '\'' or '"' or '!' or '(' or ')' or '[' or ']'), char.ToLowerInvariant).ToArray());

        if (pollster is "")
            return (pollster, PollSource._OTHERS_);

        pollster = REGEX_TRIM_HTM().Replace(pollster ?? "", "");
        pollster = REGEX_TRIM_SLASH().Replace(pollster ?? "", m => m.Groups[1].Value);

        string raw = pollster;
        PollSource source = pollster.Replace(".", "") switch
        {
            "wahlleiter" or "landeswahlleiter" or "bundeswahlleiter" or
            ['l', 'a', 'n', 'd', 't', 'a', 'g', ..] or
            ['b', 'u', 'n', 'd', 'e', 's', 't', 'a', 'g', ..] or
            ['b', 'u', 'e', 'r', 'g', 'e', 'r', 's', 'c', 'h', 'a', 'f', 't', ..] => PollSource.Offiziell,
            ['i', 'n', 'f', 'r', 'a', 't', 'e', 's', 't', ..] or
            [.., 'i', 'n', 'f', 'r', 'a', 't', 'e', 's', 't'] or "dimap" => PollSource.Infratest_Dimap,
            "forsa" => PollSource.Forsa,
            "insa" => PollSource.Insa,
            "emnid" or "verian" => PollSource.Emnid,
            "allensbach" => PollSource.Allensbach,
            "politbarometer" => PollSource.Politbarometer,
            "fgwtelefonfeld" or "forschgrwahlen" or "forschungsgruppewahlen" => PollSource.FGWahlen,
            "yougov" => PollSource.Yougov,
            "ipos" or "ipsos" => PollSource.Ipsos,
            "gms" => PollSource.GMS,
            "apropro" or "aproxima" => PollSource.Approxima,
            "tuilmenau" or ['u', 'n', 'i', _, ..] or "ifm" or "ifmleipzig" or "mifmmuenchen" or "infas" or "psephos" => PollSource.Universität,
            "gess" or "gessphone" or "gessphonefield" => PollSource.Gess,


            _ => PollSource._OTHERS_,
            
            //"absmarktforschung" => PollSource.,
            //"amrduesseldorf" => PollSource.,
            //"basisresearch" => PollSource.,
            //"conoscope" => PollSource.,
            //"customerresearch42" => PollSource.,
            //"electionde" => PollSource.,
            //"fbczaplicki" => PollSource.,
            //"imfield" => PollSource.,
            //"infogmbh" => PollSource.,
            //"inra" => PollSource.,
            //"iwd" => PollSource.,
            //"konkretmarktforschung" => PollSource.,
            //"mafode" => PollSource.,
            //"marktforschungsservicedukath" => PollSource.,
            //"mentefactum" => PollSource.,
            //"omniquest" => PollSource.,
            //"peinelt" => PollSource.,
            //"pmgpolicymatters" => PollSource.,
            //"polis" or "polissinus" => PollSource.,
            //"pollytix" => PollSource.,
            //"projektcontor" => PollSource.,
            //"saarzoom" or "hippchen" => PollSource.,
            //"tnsforschung" => PollSource.,
            //"trendresearchhamburg" => PollSource.,

            //"usuma" => PollSource.,
        };

        return (raw, source);
    }

    [GeneratedRegex(@"\.html?$", RegexOptions.Compiled)]
    internal static partial Regex REGEX_TRIM_HTM();

    [GeneratedRegex(@"^([^/]+)/.+$", RegexOptions.Compiled)]
    internal static partial Regex REGEX_TRIM_SLASH();
}

public sealed class PollHistory
{
    public static PollHistory Empty { get; } = new([]);


    public int PollCount => Polls.Length;

    public RawPoll[] Polls { get; }

    public DateOnly[] Dates { get; }

    public DateOnly? EarliestDate { get; }

    public DateOnly? LatestDate { get; }

    public MergedPoll this[DateOnly date, params State?[] states] => this[date, states as IEnumerable<State?>];

    public MergedPoll this[DateOnly date, IEnumerable<State?> states] => GetSlice(date, states);

    public MergedPollHistory this[DateOnly? lower, DateOnly? upper, params State?[] states] => this[lower, upper, states as IEnumerable<State?>];

    public MergedPollHistory this[DateOnly? lower, DateOnly? upper, IEnumerable<State?> states] => GetRange(lower, upper, states);


    public PollHistory(IEnumerable<RawPoll> polls)
    {
        Polls = [.. polls.OrderBy(static p => p.Date)];
        Dates = [.. Polls.Select(static p => p.Date).Distinct()];
        EarliestDate = Dates.Length > 0 ? Dates[0] : null;
        LatestDate = Dates.Length > 0 ? Dates[^1] : null;
    }

    public DateOnly? GetPreviousDate(DateOnly date) => Util.ApproximateBinarySearch(Dates, date) is int idx and > 0 ? Dates[idx - 1] : EarliestDate;

    public DateOnly? GetNextDate(DateOnly date) => Util.ApproximateBinarySearch(Dates, date) is int idx && idx < Dates.Length - 1 ? Dates[idx + 1] : LatestDate;

    public MergedPoll GetSlice(DateOnly date, IEnumerable<State?> states) => new(Polls.ToArrayWhere(p => p.Date == date && states.Contains(p.State)));

    public MergedPollHistory GetRange(DateOnly? lower, DateOnly? upper, IEnumerable<State?> states)
    {
        lower ??= EarliestDate;
        upper ??= LatestDate;

        if (lower > upper)
            (lower, upper) = (upper, lower);

        return new(from p in Polls
                   where p.Date >= lower
                      && p.Date <= upper
                      && states.Contains(p.State)
                   group p by p.Date into g
                   select new MergedPoll([.. g]));
    }


    //public MergedPoll Interpolate(DateOnly date, IEnumerable<State?> states)
    //{
    //    DateOnly? prev = GetPreviousDate(date);
    //    DateOnly? next = GetNextDate(date);

    //    if (prev is null && next is null)
    //        return MergedPoll.Empty;
    //    else if (prev is null)
    //        return this[next!.Value, states];
    //    else if (next is null || prev == next)
    //        return this[prev.Value, states];
    //    else
    //    {
    //        DateOnly p = prev.Value;
    //        DateOnly n = next.Value;
    //        MergedPoll prev_poll = GetSlice(p, states);
    //        MergedPoll next_poll = GetSlice(n, states);

    //        float delta = (date - p).TotalDays / (n - p).TotalDays;

    //        Dictionary<Party, float> results = [];

    //        foreach (Party party in Party.All)
    //        {
    //            float prev_val = prev_poll[party];
    //            float next_val = next_poll[party];

    //            results[party] = prev_val + (next_val - prev_val) * delta;
    //        }

    //        return new(date, null, null, null, true, results);
    //    }
    //}



    //public Poll? MostRecent() => Polls.FirstOrDefault();

    //public Poll[] MostRecent(int count) => Polls.Take(count).ToArray();

    //public Poll? MostRecent(State? state) => Polls.Where(p => p.State == state).FirstOrDefault();

    //public Poll[] MostRecent(State? state, int count) => Polls.Where(p => p.State == state).Take(count).ToArray();

    //public Poll[] In(State? state) => Polls.Where(p => p.State == state).ToArray();

    //public Poll[] During(DateOnly earliest, DateOnly latest) => Polls.Where(p => p.Date >= earliest && p.Date <= latest).ToArray();

    //public Poll[] During(DateOnly earliest, DateOnly latest, State state) => Polls.Where(p => p.Date >= earliest && p.Date <= latest && p.State == state).ToArray();

    public string AsCSV()
    {
        Party[] parties = Party.All;
        StringBuilder sb = new();

        sb.AppendLine($"id,date,pollster,source,state,synthetic,{parties.Select(p => p.Identifier).StringJoin(",")}");

        for (int i = 0; i < Polls.Length; i++)
        {
            RawPoll poll = Polls[i];

            sb.AppendLine($"{i},{poll.Date:yyyy-MM-dd},\"{poll.RawPollster}\",\"{poll.SourceURI}\",{poll.State?.ToString() ?? "DE"},{poll.IsSynthetic},{parties.Select(p => poll[p].ToString("P2")).StringJoin(",")}");
        }

        return sb.ToString();
    }

    public string AsCSV(State? state)
    {
        Party[] parties = Party.All;
        StringBuilder sb = new();

        sb.AppendLine($"id,date,pollster,source,state,synthetic,{parties.Select(p => p.Identifier).StringJoin(",")}");

        for (int i = 0; i < Polls.Length; i++)
        {
            RawPoll poll = Polls[i];

            if (poll.State == state || (state is State.BE && poll.State is State.BE_W or State.BE_O))
                sb.AppendLine($"{i},{poll.Date:yyyy-MM-dd},\"{poll.RawPollster}\",\"{poll.SourceURI}\",{poll.State},{poll.IsSynthetic},{parties.Select(p => poll[p].ToString("P2")).StringJoin(",")}");
        }

        return sb.ToString();
    }
}

public sealed class MergedPoll
    : IPoll
{
    private const float EAST_BERLIN_PERCENTAGE = .37516441883f;
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

    public (Party party, float percentage)[] Percentages => [..from party in Party.All
                                                                let perc = this[party]
                                                                where perc > 0
                                                                orderby perc descending
                                                                select (party, perc)];

    public DateOnly EarliestDate { get; }

    public DateOnly LatestDate { get; }

    DateOnly IPoll.Date => LatestDate;

    string? IPoll.PollingSource
    {
        get
        {
            IEnumerable<string> sources = Polls.Select(static (IPoll p) => p.PollingSource)
                                               .Where(s => !string.IsNullOrEmpty(s))
                                               .Distinct(StringComparer.OrdinalIgnoreCase)!;

            return sources.Any() ? sources.StringJoin(";") : null;
        }
    }

    internal IReadOnlyDictionary<Party, float> Results { get; }

    public float this[Party p] => Results.ContainsKey(p) ? Results[p] : 0;


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
            States = polls.SelectWhere(static p => p.State.HasValue, static p => p.State!.Value).Distinct().ToArray();
            EarliestDate = polls.Min(static p => p.Date);
            LatestDate = polls.Max(static p => p.Date);

            Dictionary<Party, float> results = [];
            float total = 0;

            foreach (Party party in Party.All)
            {
                float sum = 0;
                float pop = 0;

                foreach (RawPoll poll in polls)
                {
                    int p = poll.State is State s ? _population_per_state[s] : _population_total;

                    sum += poll[party] * p;
                    pop += p;
                }

                sum = float.Max(sum / pop, 0);
                results[party] = sum;
                total += sum;
            }

            foreach (Party party in Party.All)
                results[party] /= total;

            Results = new ReadOnlyDictionary<Party, float>(results);
            StrongestParty = Results.OrderByDescending(static kvp => kvp.Value).FirstOrDefault().Key ?? Party.__OTHER__;
        }
        else
        {
            DateTime now = DateTime.UtcNow;

            States = [];
            EarliestDate =
            LatestDate = new(now.Year, now.Month, now.Day);
            Results = Party.All.ToDictionary(LINQ.id, static p => 0f);
            StrongestParty = Party.__OTHER__;
        }
    }

    public override string ToString() =>
        $"{EarliestDate:yyyy-MM-dd}-{LatestDate:yyyy-MM-dd}, {States.Select(static s => s.ToString() ?? "BUND").StringJoin(", ")} {Results.Select(static kvp => $", {kvp.Key.Identifier}={kvp.Value:P1}").StringConcat()} ({Polls.Length} polls)";

    public static implicit operator RawPoll[](MergedPoll poll) => poll.Polls;

    public static implicit operator MergedPoll(RawPoll[] polls) => new(polls);
}


// TODO : check if we still need this class
public sealed class MergedPollHistory
{
    public static MergedPollHistory Empty { get; } = new([]);


    public int PollCount => Polls.Length;

    public MergedPoll[] Polls { get; }

    public MergedPoll? MostRecentPoll => Polls.FirstOrDefault();

    public MergedPoll? OldestPoll => Polls.LastOrDefault();

    public DateOnly[] PollingDates { get; }


    public MergedPollHistory(IEnumerable<MergedPoll> polls)
    {
        Polls = [.. polls.OrderByDescending(static p => p.LatestDate)];
        PollingDates = [.. Polls.Select(static p => p.LatestDate).Distinct()];
    }

    public MergedPoll? GetMostRecentAt(DateOnly? date)
    {
        if (date is DateOnly d)
        {
            MergedPoll? poll = Polls.SkipWhile(p => p.LatestDate > d).FirstOrDefault();

            if (poll == null && OldestPoll?.EarliestDate <= d.AddDays(1))
                return OldestPoll;
            else
                return poll;
        }
        else
            return null;
    }

    public string AsCSV()
    {
        Party[] parties = Party.All;
        StringBuilder sb = new();

        sb.AppendLine($"id,start,end,pollsters,sources,states,synthetic,{parties.Select(p => p.Identifier).StringJoin(",")}");

        for (int i = 0; i < Polls.Length; i++)
        {
            MergedPoll merged = Polls[i];
            string pollsters = merged.Polls.Select(p => p.RawPollster).Distinct().StringJoin(";");
            string sources = merged.Polls.Select(p => p.SourceURI).Distinct().StringJoin(";");
            string states = merged.Polls.Select(p => p.State?.ToString() ?? "DE").Distinct().StringJoin(";");
            float synthetic = (float)merged.Polls.Count(p => p.IsSynthetic) / merged.Polls.Length;

            sb.AppendLine($"{i},{merged.EarliestDate:yyyy-MM-dd},{merged.LatestDate:yyyy-MM-dd},\"{pollsters}\",\"{sources}\",{states},{synthetic:P2},{parties.Select(p => merged[p].ToString("P2")).StringJoin(",")}");
        }

        return sb.ToString();
    }
}
