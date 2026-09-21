using System;
using FileFormat.Core;

namespace FileFormat.Codecs.ProRes;

/// <summary>
/// One of the profiles an encoder may write: the quantisation weights it uses and the data rate it
/// aims at.
/// </summary>
/// <remarks>
/// <b>A ProRes profile is an encoder's policy, not a bitstream mode.</b> RDD 36:2022 describes one
/// decoding process and no profiles at all — every frame carries its own weight matrices and its own
/// per-slice quantisation index, so a decoder never needs to know which of the six names a stream was
/// written under. What the name selects here is therefore entirely on this side: the two weight
/// matrices to put in the frame header, the sampling precision, and the number of bits a macroblock
/// is allowed, which is what the quantisation index of each slice is chosen to meet.
/// <para/>
/// <b>The weight matrices are copied, not derived.</b> They are the matrices Apple's profiles are
/// actually written with, and reading them out of the frame header of a file is the only way to know
/// them — the specification prints none of them, since a decoder is told them by every frame. The
/// five distinct tables here are FFmpeg's <c>prores_quant_matrices</c>, and each was read back byte
/// for byte out of a frame header FFmpeg wrote; see <c>THIRD-PARTY-NOTICE.FFmpeg.txt</c> beside this
/// file. A matrix worked out afresh would be a different profile wearing the same four-character
/// code, which is why <c>ProResProfileMatrixTests</c> asserts them against those values a second
/// time: no decoder can catch a wrong one, since every frame states the matrix it was written with
/// and every decoder believes it.
/// <para/>
/// <b>The data rates are Apple's published ones.</b> The <i>Apple ProRes White Paper</i> states each
/// profile's target at 1920x1080 and 29.97 frames a second; dividing by the 8160 macroblocks of that
/// raster and by that frame rate gives a figure in bits per macroblock, which is the form that
/// carries to any other size and rate unchanged. The 4444 rates are the published values without
/// alpha, because alpha is lossless and therefore depends on the matte rather than on the colour
/// profile's rate target.
/// </remarks>
/// <param name="Tag">The four-character code a container names this profile by.</param>
/// <param name="Name">The profile's name as Apple writes it.</param>
/// <param name="LumaMatrix">The luma quantisation weights, raster order, <c>[v * 8 + u]</c>.</param>
/// <param name="ChromaMatrix">The chroma quantisation weights, raster order.</param>
/// <param name="BitsPerMacroblock">The coded colour size a macroblock is allowed on average.</param>
internal sealed record ProResProfile(
  CodecTag Tag,
  string Name,
  byte[] LumaMatrix,
  byte[] ChromaMatrix,
  int BitsPerMacroblock) {

  /// <summary>ProRes 422 Proxy — 45 Mbit/s at 1920x1080 29.97.</summary>
  internal static readonly ProResProfile Proxy = new(
    CodecTag.FromCharacters("apco"), "422 Proxy",
    [
       4,  7,  9, 11, 13, 14, 15, 63,
       7,  7, 11, 12, 14, 15, 63, 63,
       9, 11, 13, 14, 15, 63, 63, 63,
      11, 11, 13, 14, 63, 63, 63, 63,
      11, 13, 14, 63, 63, 63, 63, 63,
      13, 14, 63, 63, 63, 63, 63, 63,
      13, 63, 63, 63, 63, 63, 63, 63,
      63, 63, 63, 63, 63, 63, 63, 63,
    ],
    [
       4,  7,  9, 11, 13, 14, 63, 63,
       7,  7, 11, 12, 14, 63, 63, 63,
       9, 11, 13, 14, 63, 63, 63, 63,
      11, 11, 13, 14, 63, 63, 63, 63,
      11, 13, 14, 63, 63, 63, 63, 63,
      13, 14, 63, 63, 63, 63, 63, 63,
      13, 63, 63, 63, 63, 63, 63, 63,
      63, 63, 63, 63, 63, 63, 63, 63,
    ],
    184);

  /// <summary>ProRes 422 LT — 102 Mbit/s at 1920x1080 29.97.</summary>
  internal static readonly ProResProfile Lt = new(
    CodecTag.FromCharacters("apcs"), "422 LT",
    [
       4,  5,  6,  7,  9, 11, 13, 15,
       5,  5,  7,  8, 11, 13, 15, 17,
       6,  7,  9, 11, 13, 15, 15, 17,
       7,  7,  9, 11, 13, 15, 17, 19,
       7,  9, 11, 13, 14, 16, 19, 23,
       9, 11, 13, 14, 16, 19, 23, 29,
       9, 11, 13, 15, 17, 21, 28, 35,
      11, 13, 16, 17, 21, 28, 35, 41,
    ],
    [
       4,  5,  6,  7,  9, 11, 13, 15,
       5,  5,  7,  8, 11, 13, 15, 17,
       6,  7,  9, 11, 13, 15, 15, 17,
       7,  7,  9, 11, 13, 15, 17, 19,
       7,  9, 11, 13, 14, 16, 19, 23,
       9, 11, 13, 14, 16, 19, 23, 29,
       9, 11, 13, 15, 17, 21, 28, 35,
      11, 13, 16, 17, 21, 28, 35, 41,
    ],
    417);

  /// <summary>ProRes 422 — 147 Mbit/s at 1920x1080 29.97.</summary>
  internal static readonly ProResProfile Standard = new(
    CodecTag.FromCharacters("apcn"), "422",
    [
      4,  4,  5,  5,  6,  7,  7,  9,
      4,  4,  5,  6,  7,  7,  9,  9,
      5,  5,  6,  7,  7,  9,  9, 10,
      5,  5,  6,  7,  7,  9,  9, 10,
      5,  6,  7,  7,  8,  9, 10, 12,
      6,  7,  7,  8,  9, 10, 12, 15,
      6,  7,  7,  9, 10, 11, 14, 17,
      7,  7,  9, 10, 11, 14, 17, 21,
    ],
    [
      4,  4,  5,  5,  6,  7,  7,  9,
      4,  4,  5,  6,  7,  7,  9,  9,
      5,  5,  6,  7,  7,  9,  9, 10,
      5,  5,  6,  7,  7,  9,  9, 10,
      5,  6,  7,  7,  8,  9, 10, 12,
      6,  7,  7,  8,  9, 10, 12, 15,
      6,  7,  7,  9, 10, 11, 14, 17,
      7,  7,  9, 10, 11, 14, 17, 21,
    ],
    601);

  /// <summary>ProRes 422 HQ — 220 Mbit/s at 1920x1080 29.97.</summary>
  internal static readonly ProResProfile HighQuality = new(
    CodecTag.FromCharacters("apch"), "422 HQ",
    [
      4, 4, 4, 4, 4, 4, 4, 4,
      4, 4, 4, 4, 4, 4, 4, 4,
      4, 4, 4, 4, 4, 4, 4, 4,
      4, 4, 4, 4, 4, 4, 4, 5,
      4, 4, 4, 4, 4, 4, 5, 5,
      4, 4, 4, 4, 4, 5, 5, 6,
      4, 4, 4, 4, 5, 5, 6, 7,
      4, 4, 4, 4, 5, 6, 7, 7,
    ],
    [
      4, 4, 4, 4, 4, 4, 4, 4,
      4, 4, 4, 4, 4, 4, 4, 4,
      4, 4, 4, 4, 4, 4, 4, 4,
      4, 4, 4, 4, 4, 4, 4, 5,
      4, 4, 4, 4, 4, 4, 5, 5,
      4, 4, 4, 4, 4, 5, 5, 6,
      4, 4, 4, 4, 5, 5, 6, 7,
      4, 4, 4, 4, 5, 6, 7, 7,
    ],
    900);

  /// <summary>ProRes 4444 — about 330 Mbit/s at 1920x1080 29.97, excluding alpha.</summary>
  internal static readonly ProResProfile FourFourFour = new(
    CodecTag.FromCharacters("ap4h"), "4444",
    [.. HighQuality.LumaMatrix],
    [.. HighQuality.ChromaMatrix],
    1349);

  /// <summary>ProRes 4444 XQ — about 495 Mbit/s at 1920x1080 29.97, excluding alpha.</summary>
  internal static readonly ProResProfile FourFourFourXq = new(
    CodecTag.FromCharacters("ap4x"), "4444 XQ",
    [
      2, 2, 2, 2, 2, 2, 2, 2,
      2, 2, 2, 2, 2, 2, 2, 2,
      2, 2, 2, 2, 2, 2, 2, 2,
      2, 2, 2, 2, 2, 2, 2, 3,
      2, 2, 2, 2, 2, 2, 3, 3,
      2, 2, 2, 2, 2, 3, 3, 3,
      2, 2, 2, 2, 3, 3, 3, 4,
      2, 2, 2, 2, 3, 3, 4, 4,
    ],
    [.. HighQuality.ChromaMatrix],
    2024);

  /// <summary>The four 4:2:2 profiles, in increasing order of data rate.</summary>
  internal static readonly ProResProfile[] FourTwoTwo = [Proxy, Lt, Standard, HighQuality];

  /// <summary>All six Apple ProRes profiles, in increasing order within their sampling families.</summary>
  internal static readonly ProResProfile[] All = [.. FourTwoTwo, FourFourFour, FourFourFourXq];

  /// <summary>Whether this profile codes colour at 4:4:4 rather than 4:2:2.</summary>
  internal bool IsFourFourFour => this.Tag.EqualsIgnoringCase(FourFourFour.Tag) || this.Tag.EqualsIgnoringCase(FourFourFourXq.Tag);

  /// <summary>The <c>chroma_format</c> value RDD 36 puts in the frame header.</summary>
  internal int ChromaFormat => this.IsFourFourFour ? 3 : 2;

  /// <summary>The colour precision conventionally reconstructed for this profile.</summary>
  internal int BitDepth => this.IsFourFourFour ? 12 : 10;

  /// <summary>The profile a four-character code names, or <c>null</c> where none of them does.</summary>
  internal static ProResProfile? For(CodecTag tag) {
    foreach (var profile in All)
      if (tag.EqualsIgnoringCase(profile.Tag))
        return profile;

    return null;
  }
}
