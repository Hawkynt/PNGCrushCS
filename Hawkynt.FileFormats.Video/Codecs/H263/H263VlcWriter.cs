using System;

namespace FileFormat.Codecs.H263;

/// <summary>The write direction of the variable-length tables transcribed in <see cref="H263VlcTables"/>.</summary>
/// <remarks>
/// No table is transcribed a second time here. The decoder's entries are turned around once, at type
/// initialization, so an encoder-side typo cannot make the two directions disagree about a codeword.
/// The independent check is FFmpeg, not another copy of the same table.
/// </remarks>
internal static class H263VlcWriter {

  private static readonly (int Code, int Length)[] _IntraMacroblockType = _Build(H263VlcTables.IntraMacroblockType);
  private static readonly (int Code, int Length)[] _LuminancePattern = _Build(H263VlcTables.LuminancePattern);
  private static readonly (int Code, int Length)[] _Coefficient = _Build(H263VlcTables.Coefficient);

  /// <summary>MCBPC for an intra macroblock of type INTRA with the stated two-bit chrominance pattern.</summary>
  internal static (int Code, int Length) IntraMacroblockType(int chromaPattern)
    => _IntraMacroblockType[3 * 4 + chromaPattern];

  /// <summary>CBPY for an intra macroblock's four-bit luminance coded-block pattern.</summary>
  internal static (int Code, int Length) LuminancePattern(int pattern) => _LuminancePattern[pattern];

  /// <summary>The escape code of Table 16.</summary>
  internal static (int Code, int Length) CoefficientEscape => _Coefficient[H263VlcTables.CoefficientEscape];

  /// <summary>
  /// Finds the ordinary Table 16 spelling of one run-level tuple, when there is one.
  /// </summary>
  internal static bool TryCoefficient(bool last, int run, int magnitude, out (int Code, int Length) code) {
    for (var index = 0; index < H263VlcTables.CoefficientEscape; ++index)
      if (H263VlcTables.CoefficientIsLast[index] == last
          && H263VlcTables.CoefficientRun[index] == run
          && H263VlcTables.CoefficientLevel[index] == magnitude) {
        code = _Coefficient[index];
        return true;
      }

    code = default;
    return false;
  }

  private static (int Code, int Length)[] _Build(H263VlcTable table) {
    var largest = 0;
    foreach (var (_, value) in table.Entries)
      if (value > largest)
        largest = value;

    var result = new (int Code, int Length)[largest + 1];
    foreach (var (text, value) in table.Entries) {
      if (value < 0)
        continue;

      result[value] = _Parse(text);
    }

    return result;
  }

  private static (int Code, int Length) _Parse(string text) {
    var code = 0;
    var length = 0;

    foreach (var character in text)
      switch (character) {
        case '0':
          code <<= 1;
          ++length;
          break;

        case '1':
          code = (code << 1) | 1;
          ++length;
          break;

        case ' ':
          break;

        default:
          throw new InvalidOperationException($"'{character}' is not a bit in H.263 code '{text}'.");
      }

    return (code, length);
  }
}
