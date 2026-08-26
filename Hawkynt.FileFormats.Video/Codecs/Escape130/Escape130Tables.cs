namespace FileFormat.Codecs.Escape130;

/// <summary>
/// The fixed tables Escape 130's block codes read three-bit, two-bit and six-bit indices into —
/// transcribed from the format's own "EIDOS ESCAPE Codec 130" technical description (mirrored at
/// <c>multimedia.cx/mirror/dec130-spec.txt</c>, itself a reverse-engineer's account of disassembling
/// Eidos' own <c>dec130.dll</c>) and checked against real files rather than trusted outright — see
/// this codec's own remarks for what that checking found.
/// </summary>
internal static class Escape130Tables {

  /// <summary>The three-bit Y adjustment code, in six-bit Y' units, indexed 0-7 in the order the
  /// specification's own table lists them.</summary>
  internal static readonly int[] YAdjustment = [-4, -3, -2, -1, 1, 2, 3, 4];

  /// <summary>
  /// The three-bit Pb'/Pr' adjustment code, each entry a (Pb, Pr) pair in five-bit units.
  /// </summary>
  /// <remarks>
  /// MultimediaWiki's own Escape 130 page states that the source document's version of this table "had
  /// errors" and prints a different set of three entries (codes 3, 4 and 5) in their place. Measured
  /// against 202,374 absolute Pb'/Pr' block codes and every adjustment code reachable from them, on the
  /// one real file with genuine colour rather than near-flat greyscale found for this decoder — a
  /// Breitling advertisement's own intro, 320x240, 200 pictures, from
  /// <c>samples.ffmpeg.org/game-formats/rpl/joint-strike-fighter/</c> — against ffmpeg's own decoded
  /// <c>yuv420p</c> planes: the table exactly as this source document states it, unmodified, reproduces
  /// every Cb and Cr sample of every picture with no difference at all. Four further real files, 1,097
  /// more pictures between them, corroborate it at the coarser grain their own near-flat chroma allows —
  /// every one of their samples matches too, though none of them exercises more than a handful of this
  /// table's eight entries. The wiki page's own correction was not carried here.
  /// </remarks>
  internal static readonly (int Pb, int Pr)[] ChromaAdjustment = [
    (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1), (0, -1), (1, -1),
  ];

  /// <summary>The two-bit difference selector of a four-brightness block, in six-bit Y' units.</summary>
  internal static readonly int[] BrightnessStrength = [2, 4, 10, 20];

  /// <summary>
  /// The nonlinear Pb'/Pr' to fraction mapping the specification offers as an alternative to the plain
  /// linear one — thirty-two entries, index 16 landing on exactly zero.
  /// </summary>
  /// <remarks>
  /// This is the mapping real files use, not the linear one the same document also describes: fit
  /// against 202,374 absolute block codes and ffmpeg's own decoded <c>yuv420p</c> planes, <c>U = round
  /// (256 * table[Pb'] + 128)</c> and <c>V = round(256 * table[Pr'] + 128)</c> reproduce every sample
  /// exactly, where the linear mapping's own best-fit line reproduces well under half of them.
  /// </remarks>
  internal static readonly double[] ChromaFraction = [
    -0.421875, -0.390625, -0.359375, -0.328125, -0.296875, -0.265625, -0.234375, -0.203125,
    -0.171875, -0.140625, -0.109375, -0.0859375, -0.0625, -0.046875, -0.03125, -0.015625,
    0.0, 0.015625, 0.03125, 0.046875, 0.0625, 0.0859375, 0.109375, 0.140625,
    0.171875, 0.203125, 0.234375, 0.265625, 0.296875, 0.328125, 0.359375, 0.390625,
  ];

  /// <summary>
  /// A four-brightness block's six-bit sign selector, mapped to the four pixels' own +1/0/-1 offset
  /// from the base — top-left, top-right, bottom-left, bottom-right, in that order.
  /// </summary>
  /// <remarks>
  /// The specification's own table stops at code <c>0x35</c> (53); nothing above it is stated, and
  /// nothing above it was ever read from a real file across the four measured here — every entry not
  /// listed here defaults to all zero, and the four-file measurement above never needed one of them to
  /// differ.
  /// </remarks>
  internal static readonly (int Lt, int Rt, int Lb, int Rb)[] BrightnessSign = _BuildSignTable();

  private static (int, int, int, int)[] _BuildSignTable() {
    var table = new (int, int, int, int)[64];

    table[0x01] = (-1, 1, 0, 0);
    table[0x02] = (1, -1, 0, 0);
    table[0x03] = (-1, 0, 1, 0);
    table[0x04] = (-1, 1, 1, 0);
    table[0x05] = (0, -1, 1, 0);
    table[0x06] = (1, -1, 1, 0);
    table[0x07] = (-1, -1, 1, 0);
    table[0x08] = (1, 0, -1, 0);
    table[0x09] = (0, 1, -1, 0);
    table[0x0A] = (1, 1, -1, 0);
    table[0x0B] = (-1, 1, -1, 0);
    table[0x0C] = (1, -1, -1, 0);
    table[0x0D] = (-1, 0, 0, 1);
    table[0x0E] = (-1, 1, 0, 1);
    table[0x0F] = (0, -1, 0, 1);

    table[0x11] = (1, -1, 0, 1);
    table[0x12] = (-1, -1, 0, 1);
    table[0x13] = (-1, 0, 1, 1);
    table[0x14] = (-1, 1, 1, 1);
    table[0x15] = (0, -1, 1, 1);
    table[0x16] = (1, -1, 1, 1);
    table[0x17] = (-1, -1, 1, 1);
    table[0x18] = (0, 0, -1, 1);
    table[0x19] = (1, 0, -1, 1);
    table[0x1A] = (-1, 0, -1, 1);
    table[0x1B] = (0, 1, -1, 1);
    table[0x1C] = (1, 1, -1, 1);
    table[0x1D] = (-1, 1, -1, 1);
    table[0x1E] = (0, -1, -1, 1);
    table[0x1F] = (1, -1, -1, 1);

    table[0x21] = (-1, -1, -1, 1);
    table[0x22] = (1, 0, 0, -1);
    table[0x23] = (0, 1, 0, -1);
    table[0x24] = (1, 1, 0, -1);
    table[0x25] = (-1, 1, 0, -1);
    table[0x26] = (1, -1, 0, -1);
    table[0x27] = (0, 0, 1, -1);
    table[0x28] = (1, 0, 1, -1);
    table[0x29] = (-1, 0, 1, -1);
    table[0x2A] = (0, 1, 1, -1);
    table[0x2B] = (1, 1, 1, -1);
    table[0x2C] = (-1, 1, 1, -1);
    table[0x2D] = (0, -1, 1, -1);
    table[0x2E] = (1, -1, 1, -1);
    table[0x2F] = (-1, -1, 1, -1);

    table[0x31] = (1, 0, -1, -1);
    table[0x32] = (0, 1, -1, -1);
    table[0x33] = (1, 1, -1, -1);
    table[0x34] = (-1, 1, -1, -1);
    table[0x35] = (1, -1, -1, -1);

    return table;
  }
}
