﻿using System.Collections.Generic;
using System.Collections;
using System.Diagnostics;
using System.Linq;
using System;

namespace Bundestagswahl;


public unsafe struct PartyIdentifier
    : IEquatable<PartyIdentifier>
    , IEnumerable<char>
{
    internal const int SIZE = 3;
    private readonly string _buffer;


    public PartyIdentifier(string ident)
    {
        ident = ident.ToLowerInvariant();
        _buffer = ident[..Math.Min(ident.Length, SIZE)];
    }

    public readonly override string ToString() => _buffer;

    public readonly override int GetHashCode() => _buffer.GetHashCode();

    public readonly override bool Equals(object? obj) => obj is PartyIdentifier i && Equals(i);

    public readonly bool Equals(PartyIdentifier other) => other.GetHashCode() == GetHashCode();

    public readonly IEnumerator<char> GetEnumerator() => _buffer.GetEnumerator();

    readonly IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public static implicit operator string(PartyIdentifier identifier) => identifier._buffer;

    public static implicit operator PartyIdentifier(string identifier) => new(identifier);
}

public sealed class Party(PartyIdentifier identifier, string name, ConsoleColor color, float lr_axis, float al_axis, float pc_axis)
    : IEquatable<Party>
{
    public static Party CDU { get; }       = new("cdu", "CDU/CSU",         new(217, 200, 235),  .3f,  -.3f,  .8f); // (117, 100, 135)
    public static Party SPD { get; }       = new("spd", "SPD",             new(255,  40,  40), -.3f,  -.4f,  .1f);
    public static Party FDP { get; }       = new("fdp", "FDP",             new(255, 200,   0),  .6f,   .5f, -.4f);
    public static Party AFD { get; }       = new("afd", "AfD",             new(  0, 158, 224),  .5f,  -.8f,  .9f);
    public static Party GRÜNE { get; }     = new("grü", "B.90/Die Grünen", new( 60, 155,   0), -.5f,   .1f, -.7f);
    public static Party LINKE { get; }     = new("lin", "Die Linke",       new(255,  10, 150), -.7f, -.65f, -.8f);
    public static Party PIRATEN { get; }   = new("pir", "Die Piraten",     new(255, 135,   0), -.1f,   .8f, -.7f);
    public static Party FW { get; }        = new("fw",  "Freie Wähler",    new(  0,  70, 255),  .4f,   .0f,  .7f);
    public static Party RECHTE { get; }    = new("rep", "NPD/REP/Rechte",  new(170, 122,  44),  .9f,  -.8f,  .8f);
    public static Party BSW { get; }       = new("bsw", "BSW",             new(200,   0,  80), -.5f,  -.8f, -.2f); // (111,   0,  60)
    public static Party __OTHER__ { get; } = new("son", "Sonstige",        new(126, 176, 165),  .0f,   .0f,  .0f);

    public static Party[] All { get; } = [CDU, SPD, FDP, AFD, GRÜNE, LINKE, BSW, PIRATEN, FW, RECHTE, __OTHER__];
    public static Party[] LeftToRight { get; } = [LINKE, BSW, PIRATEN, SPD, GRÜNE, FDP, FW, CDU, AFD, RECHTE];


    internal PartyIdentifier Identifier => identifier;

    public string Name => name;

    public ConsoleColor Color => color;

    public float EconomicLeftRightAxis { get; } = float.Clamp(lr_axis, -1, 1);

    public float AuthoritarianLibertarianAxis { get; } = float.Clamp(al_axis, -1, 1);

    public float ProgressiveConservativeAxis { get; } = float.Clamp(pc_axis, -1, 1);


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
