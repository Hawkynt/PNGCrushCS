using System;
using System.Collections.Generic;

namespace FileFormat.Codecs.CineForm;

/// <summary>
/// The single codebook a highpass codeblock's entropy-coded runs and coefficients are read through.
/// </summary>
/// <remarks>
/// SMPTE ST 2073-1:2017, Annex C.1 (Table C.1) and Annex C.2 (Table C.2), transcribed in full: 264
/// entries — a run length and value for every zero run the format codes directly (1, 12, 20, 32, 60,
/// 100, 180 and 320 zeros), a magnitude for every value from 0 to 255 a single non-zero coefficient
/// can carry, and the twenty-six bit band end marker. Table C.1's own note states the shape this
/// reads by: "All non-zero values have a run length of one. The codebook entry for a non-zero value
/// specifies the magnitude of the coded value" — so a run count greater than one is always a run of
/// zeros, and a run count of one is always a single coefficient, zero or not.
/// <para/>
/// Every codeword is a prefix code, so trying candidate lengths shortest first and testing the bits
/// already read against every entry of that length finds the one that matches without ambiguity —
/// which is what <see cref="TryDecodeRun"/> does, mirroring the sequential <c>getrun()</c> function
/// Annex G.9 describes the format around.
/// </remarks>
internal static class CineFormCodebook {

  /// <summary>One codebook entry: a codeword's bits, its length, how many times its value repeats,
  /// and the value itself — zero for a run of zeros, the coefficient magnitude otherwise.</summary>
  private readonly record struct Entry(uint Code, int Length, int RunCount, int Value);

  private static readonly Entry[] _Entries = [
    new(0x00000000, 1, 1, 0), new(0x00000002, 2, 1, 1), new(0x00000007, 3, 1, 2),
    new(0x00000019, 5, 1, 3), new(0x00000030, 6, 1, 4), new(0x00000036, 6, 1, 5),
    new(0x0000006F, 7, 1, 8), new(0x00000063, 7, 1, 6), new(0x00000069, 7, 12, 0),
    new(0x0000006B, 7, 1, 7), new(0x000000D1, 8, 20, 0), new(0x000000D4, 8, 1, 9),
    new(0x000000DC, 8, 1, 10), new(0x00000189, 9, 1, 11), new(0x0000018A, 9, 32, 0),
    new(0x000001A0, 9, 1, 12), new(0x000001AB, 9, 1, 13), new(0x00000377, 10, 1, 18),
    new(0x00000310, 10, 1, 14), new(0x00000316, 10, 1, 15), new(0x00000343, 10, 60, 0),
    new(0x00000354, 10, 1, 16), new(0x00000375, 10, 1, 17), new(0x00000623, 11, 1, 19),
    new(0x00000684, 11, 1, 20), new(0x00000685, 11, 100, 0), new(0x000006AB, 11, 1, 21),
    new(0x000006EC, 11, 1, 22), new(0x00000DDB, 12, 1, 29), new(0x00000C5C, 12, 1, 24),
    new(0x00000C5E, 12, 1, 25), new(0x00000C44, 12, 1, 23), new(0x00000D55, 12, 1, 26),
    new(0x00000DD1, 12, 1, 27), new(0x00000DD3, 12, 1, 28), new(0x00001BB5, 13, 1, 35),
    new(0x0000188B, 13, 1, 30), new(0x000018BB, 13, 1, 31), new(0x000018BF, 13, 180, 0),
    new(0x00001AA8, 13, 1, 32), new(0x00001BA0, 13, 1, 33), new(0x00001BA5, 13, 320, 0),
    new(0x00001BA4, 13, 1, 34), new(0x03114BA2, 26, 1, 116), new(0x00003115, 14, 1, 36), new(0x00003175, 14, 1, 37),
    new(0x0000317D, 14, 1, 38), new(0x00003553, 14, 1, 39), new(0x00003768, 14, 1, 40),
    new(0x00006E87, 15, 1, 46), new(0x00006ED3, 15, 1, 47), new(0x000062E8, 15, 1, 42),
    new(0x000062F8, 15, 1, 43), new(0x00006228, 15, 1, 41), new(0x00006AA4, 15, 1, 44),
    new(0x00006E85, 15, 1, 45), new(0x0000C453, 16, 1, 48), new(0x0000C5D3, 16, 1, 49),
    new(0x0000C5F3, 16, 1, 50), new(0x0000DDA4, 16, 1, 53), new(0x0000DD08, 16, 1, 51),
    new(0x0000DD0C, 16, 1, 52), new(0x0001BB4B, 17, 1, 61), new(0x0001BB4A, 17, 1, 60),
    new(0x00018BA5, 17, 1, 55), new(0x00018BE5, 17, 1, 56), new(0x0001AA95, 17, 1, 57),
    new(0x0001AA97, 17, 1, 58), new(0x000188A4, 17, 1, 54), new(0x0001BA13, 17, 1, 59),
    new(0x00031748, 18, 1, 62), new(0x000317C8, 18, 1, 63), new(0x00035528, 18, 1, 64),
    new(0x0003552C, 18, 1, 65), new(0x00037424, 18, 1, 66), new(0x00037434, 18, 1, 67),
    new(0x00037436, 18, 1, 68), new(0x00062294, 19, 1, 69), new(0x00062E92, 19, 1, 70),
    new(0x00062F92, 19, 1, 71), new(0x0006AA52, 19, 1, 72), new(0x0006AA5A, 19, 1, 73),
    new(0x0006E86A, 19, 1, 75), new(0x0006E86E, 19, 1, 76), new(0x0006E84A, 19, 1, 74),
    new(0x000C452A, 20, 1, 77), new(0x000C5D27, 20, 1, 78), new(0x000C5F26, 20, 1, 79),
    new(0x000D54A6, 20, 1, 80), new(0x000D54B6, 20, 1, 81), new(0x000DD096, 20, 1, 82),
    new(0x000DD0D6, 20, 1, 83), new(0x000DD0DE, 20, 1, 84), new(0x00188A56, 21, 1, 85),
    new(0x0018BA4D, 21, 1, 86), new(0x0018BE4E, 21, 1, 87), new(0x0018BE4F, 21, 1, 88),
    new(0x001AA96E, 21, 1, 89), new(0x001BA12E, 21, 1, 90), new(0x001BA12F, 21, 1, 91),
    new(0x001BA1AF, 21, 1, 92), new(0x001BA1BF, 21, 1, 93), new(0x0037435D, 22, 1, 99),
    new(0x0037437D, 22, 1, 100), new(0x00317498, 22, 1, 94), new(0x0035529C, 22, 1, 95),
    new(0x0035529D, 22, 1, 96), new(0x003552DE, 22, 1, 97), new(0x003552DF, 22, 1, 98),
    new(0x0062E933, 23, 1, 102), new(0x0062295D, 23, 1, 101), new(0x006AA53D, 23, 1, 103),
    new(0x006AA53F, 23, 1, 105), new(0x006AA53E, 23, 1, 104), new(0x006E86B9, 23, 1, 106),
    new(0x006E86F8, 23, 1, 107), new(0x00D54A79, 24, 1, 111), new(0x00C5D265, 24, 1, 109),
    new(0x00C452B8, 24, 1, 108), new(0x00DD0D71, 24, 1, 113), new(0x00D54A78, 24, 1, 110),
    new(0x00DD0D70, 24, 1, 112), new(0x00DD0DF2, 24, 1, 114), new(0x00DD0DF3, 24, 1, 115),
    new(0x0188A5F6, 25, 1, 225), new(0x0188A5F5, 25, 1, 189), new(0x0188A5F4, 25, 1, 188),
    new(0x0188A5F3, 25, 1, 203), new(0x0188A5F2, 25, 1, 202), new(0x0188A5F1, 25, 1, 197),
    new(0x0188A5F0, 25, 1, 207), new(0x0188A5EF, 25, 1, 169), new(0x0188A5EE, 25, 1, 223),
    new(0x0188A5ED, 25, 1, 159), new(0x0188A5AA, 25, 1, 235), new(0x0188A5E3, 25, 1, 152),
    new(0x0188A5DF, 25, 1, 192), new(0x0188A589, 25, 1, 179), new(0x0188A5DD, 25, 1, 201),
    new(0x0188A578, 25, 1, 172), new(0x0188A5E0, 25, 1, 149), new(0x0188A588, 25, 1, 178),
    new(0x0188A5D6, 25, 1, 120), new(0x0188A5DB, 25, 1, 219), new(0x0188A5E1, 25, 1, 150),
    new(0x0188A587, 25, 1, 127), new(0x0188A57D, 25, 1, 162), new(0x0188A59A, 25, 1, 211),
    new(0x0188A5A3, 25, 1, 213), new(0x0188A5C4, 25, 1, 125), new(0x0188A5E8, 25, 1, 165),
    new(0x0188A5EC, 25, 1, 158), new(0x0188A5A2, 25, 1, 212), new(0x0188A586, 25, 1, 247),
    new(0x0188A57C, 25, 1, 227), new(0x0188A573, 25, 1, 238), new(0x0188A58E, 25, 1, 198),
    new(0x0188A59C, 25, 1, 163), new(0x0188A5B3, 25, 1, 236), new(0x0188A5C8, 25, 1, 228),
    new(0x0188A5B2, 25, 1, 234), new(0x0188A5FB, 25, 1, 183), new(0x0188A5B1, 25, 1, 117),
    new(0x0188A5A1, 25, 1, 217), new(0x0188A5B0, 25, 1, 215), new(0x0188A5EB, 25, 1, 168),
    new(0x0188A5AF, 25, 1, 124), new(0x0188A5A8, 25, 1, 122), new(0x0188A5AE, 25, 1, 123),
    new(0x0188A584, 25, 1, 128), new(0x0188A5AD, 25, 1, 254), new(0x0188A5D2, 25, 1, 249),
    new(0x0188A5AC, 25, 1, 253), new(0x0188A599, 25, 1, 187), new(0x0188A5AB, 25, 1, 148),
    new(0x0188A598, 25, 1, 186), new(0x0188A5DA, 25, 1, 218), new(0x0188A583, 25, 1, 136),
    new(0x0188A5E4, 25, 1, 146), new(0x018BA4C9, 25, 1, 181), new(0x0188A5E5, 25, 1, 147),
    new(0x0188A5D0, 25, 1, 255), new(0x0188A5D9, 25, 1, 224), new(0x0188A594, 25, 1, 230),
    new(0x0188A5B5, 25, 1, 143), new(0x0188A582, 25, 1, 135), new(0x0188A5BC, 25, 1, 184),
    new(0x0188A5CB, 25, 1, 233), new(0x0188A5BD, 25, 1, 185), new(0x0188A5D8, 25, 1, 222),
    new(0x0188A5E9, 25, 1, 166), new(0x0188A5E7, 25, 1, 145), new(0x0188A5CC, 25, 1, 132),
    new(0x0188A581, 25, 1, 134), new(0x0188A585, 25, 1, 129), new(0x0188A5EA, 25, 1, 167),
    new(0x0188A5D3, 25, 1, 250), new(0x0188A5A9, 25, 1, 248), new(0x0188A5E2, 25, 1, 151),
    new(0x0188A5A6, 25, 1, 209), new(0x0188A595, 25, 1, 119), new(0x0188A580, 25, 1, 243),
    new(0x0188A596, 25, 1, 193), new(0x0188A5A0, 25, 1, 216), new(0x0188A5B8, 25, 1, 176),
    new(0x0188A59D, 25, 1, 164), new(0x0188A590, 25, 1, 245), new(0x0188A5C3, 25, 1, 140),
    new(0x0188A5C9, 25, 1, 229), new(0x0188A57F, 25, 1, 157), new(0x0188A5A4, 25, 1, 206),
    new(0x0188A5C0, 25, 1, 239), new(0x0188A5E6, 25, 1, 144), new(0x0188A5DE, 25, 1, 191),
    new(0x0188A5A5, 25, 1, 208), new(0x0188A5D4, 25, 1, 251), new(0x0188A5CE, 25, 1, 137),
    new(0x0188A57E, 25, 1, 156), new(0x0188A5BF, 25, 1, 241), new(0x0188A5C2, 25, 1, 139),
    new(0x0188A572, 25, 1, 237), new(0x0188A592, 25, 1, 242), new(0x0188A59B, 25, 1, 190),
    new(0x0188A5CD, 25, 1, 133), new(0x0188A5BE, 25, 1, 240), new(0x0188A5C7, 25, 1, 131),
    new(0x0188A5CA, 25, 1, 232), new(0x0188A5D5, 25, 1, 252), new(0x0188A57B, 25, 1, 171),
    new(0x0188A58D, 25, 1, 205), new(0x0188A58C, 25, 1, 204), new(0x0188A58B, 25, 1, 118),
    new(0x0188A58A, 25, 1, 214), new(0x018BA4C8, 25, 1, 180), new(0x0188A5C5, 25, 1, 126),
    new(0x0188A5FA, 25, 1, 182), new(0x0188A5BB, 25, 1, 175), new(0x0188A5C1, 25, 1, 141),
    new(0x0188A5CF, 25, 1, 138), new(0x0188A5B9, 25, 1, 177), new(0x0188A5B6, 25, 1, 153),
    new(0x0188A597, 25, 1, 194), new(0x0188A5FE, 25, 1, 160), new(0x0188A5D7, 25, 1, 121),
    new(0x0188A5BA, 25, 1, 174), new(0x0188A591, 25, 1, 246), new(0x0188A5C6, 25, 1, 130),
    new(0x0188A5DC, 25, 1, 200), new(0x0188A57A, 25, 1, 170), new(0x0188A59F, 25, 1, 221),
    new(0x0188A5F9, 25, 1, 196), new(0x0188A5B4, 25, 1, 142), new(0x0188A5A7, 25, 1, 210),
    new(0x0188A58F, 25, 1, 199), new(0x0188A5FD, 25, 1, 155), new(0x0188A5B7, 25, 1, 154),
    new(0x0188A593, 25, 1, 244), new(0x0188A59E, 25, 1, 220), new(0x0188A5F8, 25, 1, 195),
    new(0x0188A5FF, 25, 1, 161), new(0x0188A5FC, 25, 1, 231), new(0x0188A579, 25, 1, 173),
    new(0x0188A5F7, 25, 1, 226),

    // Table C.2: the band end marker, the only codeword whose "value" (256) is not a coefficient
    // magnitude or a run of zeros — it terminates the codeblock instead.
    new(0x03114BA3, 26, 0, BandEndMarkerValue),
  ];

  /// <summary>The value <see cref="TryDecodeRun"/> reports for the band end marker codeword.</summary>
  internal const int BandEndMarkerValue = 256;

  /// <summary>The shortest and longest codewords in the table, so a decoder knows how far to try.</summary>
  internal const int MinimumCodewordLength = 1;
  internal const int MaximumCodewordLength = 26;

  private static readonly Dictionary<(int Length, uint Code), (int RunCount, int Value)> _Lookup = _Build();

  private static Dictionary<(int, uint), (int, int)> _Build() {
    var table = new Dictionary<(int, uint), (int, int)>(_Entries.Length);
    foreach (var entry in _Entries)
      table[(entry.Length, entry.Code)] = (entry.RunCount, entry.Value);

    return table;
  }

  /// <summary>
  /// Reads one codeword — a run of zeros, a single coefficient, or the band end marker — advancing
  /// the reader past it.
  /// </summary>
  /// <remarks>
  /// The codebook is prefix-free, so trying lengths from the shortest up and testing the bits already
  /// peeked against every entry of that length finds the one codeword that matches, unambiguously,
  /// without ever backtracking.
  /// </remarks>
  /// <param name="reader">The reader positioned at the first bit of a codeword.</param>
  /// <param name="runCount">How many times <paramref name="value"/> repeats — the length of a zero
  /// run, or one for a single coefficient or the band end marker.</param>
  /// <param name="value">The coefficient magnitude, zero for a run of zeros, or
  /// <see cref="BandEndMarkerValue"/>.</param>
  /// <returns><see langword="false"/> when no codeword of any length matched, which the caller
  /// reports naming the bit position rather than guessing at a codeword.</returns>
  internal static bool TryDecodeRun(CineFormBitReader reader, out int runCount, out int value) {
    for (var length = MinimumCodewordLength; length <= MaximumCodewordLength; ++length) {
      var code = reader.Peek(length);
      if (!_Lookup.TryGetValue((length, code), out var entry))
        continue;

      reader.Advance(length);
      if (entry.Item2 == BandEndMarkerValue) {
        runCount = 0;
        value = BandEndMarkerValue;
        return true;
      }

      runCount = entry.Item1;
      value = entry.Item2;
      return true;
    }

    runCount = 0;
    value = 0;
    return false;
  }
}
