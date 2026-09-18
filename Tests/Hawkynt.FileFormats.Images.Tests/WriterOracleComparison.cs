using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;
using Hawkynt.FileFormats.Images;

namespace Hawkynt.FileFormats.Images.Tests;

/// <summary>
/// Decides whether the picture an outside tool rebuilt from one of our files is the picture that
/// file holds.
/// </summary>
/// <remarks>
/// This is the whole worth of the Oracle column, and for a long time it was the weakest part of it.
/// The old rule was that the tool had to return a picture of the requested size holding at least one
/// non-zero byte, which proves the container parsed and nothing about the pixels. The Apple IIgs
/// <c>$C1</c> writer is what that rule cost: every file it produced decoded as solid black at exactly
/// the right size, and the defect was caught only because black happened to land on palette index
/// zero and trip the byte scan by luck. The scan was unsound in both directions for the same reason
/// — <c>recoil2png</c> emits paletted PNGs, where the bytes it was reading are indices, so a correct
/// decode of a flat picture whose index is zero was recorded as a refusal and a wrong all-black
/// decode whose index is not zero was recorded as agreement.
/// <para/>
/// So nothing here looks at a raw byte. Every picture is resolved through its palette to colour
/// first, which is the only way index-versus-index cannot happen, and the comparison is made on
/// colour.
/// <para/>
/// There are three references a decode can be held to and the format decides which apply:
/// <list type="number">
/// <item>the picture that was handed to the writer, which is the strongest and is reachable wherever
/// the format can hold it — a truecolour writer that is lossless reproduces the probe gradient
/// exactly, and dozens here do;</item>
/// <item>this package's own decode of the same file, which is not a circular check because the tool
/// is the independent party: a writer and a reader that share one misunderstanding of the format
/// agree with each other and the outside tool is what they cannot both be right about;</item>
/// <item>what the writer kept of the source, which is what stops the second reference being satisfied
/// by two decoders agreeing that a file holds nothing.</item>
/// </list>
/// A palettised or otherwise lossy format cannot be held to the first — a gradient is not
/// representable in sixteen fixed hardware colours and demanding it would measure the palette rather
/// than the writer — so those are judged on the second and third together, which is the method the
/// Mapletown and ECI work already used by hand.
/// <para/>
/// Geometry comes from the file and not from the request, which is the other thing that was wrong
/// before. A Spectrum screen is 256 by 192 whatever size it was asked for, and holding the tool to
/// the requested size recorded the probe's mistake as the writer's.
/// </remarks>
internal static class WriterOracleComparison {

  /// <summary>
  /// How far, on average and per colour channel, a tool's decode may sit from this package's decode
  /// of the same file and still be the same picture.
  /// </summary>
  /// <remarks>
  /// Two independent decoders of a lossy codec differ by a level or two from the inverse transform
  /// and the chroma upsampler alone, and a format carrying a colour matrix can differ by more where
  /// the two disagree about which matrix that is. The band has to clear that and stay far below the
  /// distance a wrong picture sits at: the probe gradient against a flat decode of any colour is
  /// never nearer than about sixty levels, and against a wrong one is further still, so twenty-four
  /// separates the two populations with room on both sides rather than splitting the difference.
  /// </remarks>
  private const double _AGREEMENT_TOLERANCE = 24.0;

  /// <summary>Judges one decode against the file it came from.</summary>
  public static (WriterOracleTool.Verdict Verdict, string Detail) Judge(
    FormatEntry entry,
    string path,
    RawImage source,
    RawImage rebuilt
  ) {
    var ours = _OurOwnDecode(entry, path);

    // The size the file holds, which is the size the writer chose and not the size it was offered.
    var width = ours?.Width ?? source.Width;
    var height = ours?.Height ?? source.Height;
    rebuilt = _AtStoredScale(rebuilt, width, height);
    if (rebuilt.Width != width || rebuilt.Height != height)
      return (WriterOracleTool.Verdict.Rejected,
        $"it rebuilt {rebuilt.Width}x{rebuilt.Height} where the file holds {width}x{height}");

    var theirs = _Colours(rebuilt);
    if (theirs == null)
      return (WriterOracleTool.Verdict.Rejected, "its decode could not be resolved to colour");

    // 1. The source itself, where the geometry lets it be asked. An exact answer here is the
    //    strongest thing any of this can say and needs no second opinion to support it.
    var wanted = rebuilt.Width == source.Width && rebuilt.Height == source.Height ? _Colours(source) : null;
    if (wanted != null && _Identical(theirs, wanted))
      return (WriterOracleTool.Verdict.Accepted, "it reproduced the source exactly");

    if (ours == null)
      return (WriterOracleTool.Verdict.Rejected,
        "this package cannot read back what it wrote, so the tool's decode has nothing to be held to");

    var mine = _Colours(ours);
    if (mine == null)
      return (WriterOracleTool.Verdict.Rejected, "this package's own decode could not be resolved to colour");

    // 3. What the writer kept. Two decoders agreeing that a file holds one flat colour, where the
    //    picture that went in had several and the file has room for more than one pixel, is the two
    //    of them agreeing about nothing — and it is the exact shape of the Apple IIgs defect.
    if (wanted != null && _DistinctColours(wanted) > 1 && width * height > 1 && _DistinctColours(mine) < 2)
      return (WriterOracleTool.Verdict.Rejected,
        "the file holds one flat colour where the picture written to it had several");

    if (_DistinctColours(mine) > 1 && _DistinctColours(theirs) < 2)
      return (WriterOracleTool.Verdict.Rejected,
        "it rebuilt one flat colour where the file holds several");

    // 2. This package's decode of the same bytes. Exact agreement is what a lossless format owes;
    //    where the two put the same boundaries in the same places but paint them different colours
    //    the disagreement is over a palette and not over the picture, and a lossy coder is allowed
    //    the band above.
    if (_Identical(theirs, mine))
      return (WriterOracleTool.Verdict.Accepted, "it agrees with this package's decode exactly");

    if (_SameStructure(theirs, mine))
      return (WriterOracleTool.Verdict.Accepted,
        "it agrees with this package's decode on every boundary, differing only in the colours chosen");

    var distance = _MeanAbsoluteDifference(theirs, mine);
    return distance <= _AGREEMENT_TOLERANCE
      ? (WriterOracleTool.Verdict.Accepted, $"it agrees with this package's decode to within {distance:0.0} levels per channel")
      : (WriterOracleTool.Verdict.Rejected, $"its decode differs from this package's by {distance:0.0} levels per channel");
  }

  /// <summary>
  /// The tool's picture back at the scale the file stores, where the tool gave it at a scale this
  /// package can account for.
  /// </summary>
  /// <remarks>
  /// Two tools may put a different number of pixels on the same picture and both be right, and there
  /// are two separate reasons for it here. Neither is a disagreement about what the file holds, so
  /// both are undone before the pixels are judged.
  /// <list type="number">
  /// <item><b>A pixel that is not square.</b> A C64 multicolour picture is 160 pixels across and was
  /// always shown on 320, because the chip doubles them horizontally and leaves the 200 lines alone;
  /// the same is true of the Atari, the Amstrad and the Spectrum's wider modes. RECOIL, XnView and
  /// IrfanView all hand such a picture back at its display size and this package stores it at the
  /// size it is actually coded, so the two differ in one axis only. Holding the three of them to the
  /// stored size called twenty-six C64 formats wrong on a question none of the four disagree
  /// about.</item>
  /// <item><b>A different idea of how big a point is.</b> A PostScript interpreter rasterises points
  /// at 72 to the inch where this package uses 96, so it returns three pixels for every four of ours
  /// in both axes at once — a scale that is not a whole number and not an upscale either.</item>
  /// </list>
  /// So a whole multiple up to four in each axis is undone, and so is a single fractional scale that
  /// serves both axes alike. What is not undone is an arbitrary stretch in one axis and something
  /// unrelated in the other: that is a change of shape, and it falls through to be judged at its own
  /// size, which rejects it.
  /// <para/>
  /// Nothing is conceded by any of this. The comparison that follows is the same one, and a decode
  /// that is rescaled and also wrong still fails it; all this removes is the scale, which is a
  /// convention about how the picture is shown rather than a claim about what it holds.
  /// </remarks>
  private static RawImage _AtStoredScale(RawImage rebuilt, int width, int height) {
    if (width <= 0 || height <= 0 || rebuilt.Width <= 0 || rebuilt.Height <= 0)
      return rebuilt;

    var scaleX = (double)rebuilt.Width / width;
    var scaleY = (double)rebuilt.Height / height;
    if (Math.Abs(scaleX - 1.0) < 1e-9 && Math.Abs(scaleY - 1.0) < 1e-9)
      return rebuilt;

    if (!_IsOneScaleOverBothAxes(scaleX, scaleY) && !_IsWholeMultiplePerAxis(rebuilt, width, height))
      return rebuilt;

    byte[] source;
    try {
      source = rebuilt.ToRgb24();
    } catch (Exception) {
      return rebuilt;
    }

    if (source.Length < (long)rebuilt.Width * rebuilt.Height * 3)
      return rebuilt;

    var reduced = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var sy = Math.Min(rebuilt.Height - 1, (int)(y * scaleY));
      var sx = Math.Min(rebuilt.Width - 1, (int)(x * scaleX));
      var from = (sy * rebuilt.Width + sx) * 3;
      var to = (y * width + x) * 3;
      reduced[to] = source[from];
      reduced[to + 1] = source[from + 1];
      reduced[to + 2] = source[from + 2];
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = reduced };
  }

  /// <summary>One scale, whole or not, serving both axes alike.</summary>
  /// <remarks>
  /// The two axes are allowed to differ by a fiftieth because a renderer working in points arrives
  /// at whole pixels by rounding, and 4:3 of an odd number does not land on one. The range stops the
  /// predicate from explaining away a thumbnail or a poster, neither of which is a convention about
  /// how the picture is shown.
  /// </remarks>
  private static bool _IsOneScaleOverBothAxes(double scaleX, double scaleY)
    => Math.Abs(scaleX - scaleY) <= 0.02 * Math.Max(scaleX, scaleY) && scaleX is >= 0.2 and <= 4.0;

  /// <summary>
  /// A whole multiple in each axis, the two not having to agree — which is how a non-square pixel
  /// reaches a square-pixel picture.
  /// </summary>
  private static bool _IsWholeMultiplePerAxis(RawImage rebuilt, int width, int height)
    => rebuilt.Width % width == 0 && rebuilt.Height % height == 0
       && rebuilt.Width / width <= 4 && rebuilt.Height / height <= 4;

  /// <summary>What this package reads back out of the file it just wrote.</summary>
  /// <remarks>
  /// Through the file rather than through the bytes, because a few formats keep their palette in a
  /// second file beside the first and reading the main one alone would answer with the wrong colours
  /// — which is precisely the mistake this whole comparison exists to avoid making.
  /// </remarks>
  private static RawImage? _OurOwnDecode(FormatEntry entry, string path) {
    try {
      return entry.LoadRawImage(new(path));
    } catch (Exception) {
      // A reader that throws on our own writer's output is a defect, and a real one, but it is not
      // the outside tool's opinion and it is not what this fixture reports.
      return null;
    }
  }

  /// <summary>The picture as colour, with any palette already resolved.</summary>
  private static byte[]? _Colours(RawImage picture) {
    try {
      var rgb = picture.ToRgb24();

      return rgb.Length < (long)picture.Width * picture.Height * 3 ? null : rgb;
    } catch (Exception) {
      return null;
    }
  }

  private static bool _Identical(byte[] left, byte[] right) {
    if (left.Length != right.Length)
      return false;

    for (var i = 0; i < left.Length; ++i)
      if (left[i] != right[i])
        return false;

    return true;
  }

  private static int _DistinctColours(byte[] rgb) {
    var seen = new HashSet<int>();
    for (var i = 0; i + 2 < rgb.Length; i += 3) {
      seen.Add((rgb[i] << 16) | (rgb[i + 1] << 8) | rgb[i + 2]);
      if (seen.Count > 4096)
        break;
    }

    return seen.Count;
  }

  /// <summary>
  /// Whether two pictures agree about which pixels are the same colour as which, whatever colours
  /// they each chose.
  /// </summary>
  /// <remarks>
  /// The retro formats need this and nothing weaker would do. A hardware palette is a table of
  /// colours somebody measured off a television, and no two decoders measured the same television:
  /// RECOIL's idea of C64 brown and this package's differ by more than any sample-distance band
  /// worth having, while both decoders put every boundary in the picture in exactly the same place.
  /// Labelling each colour by where it first appears in raster order throws the colours away and
  /// keeps the boundaries, so agreement here says the two read the same picture out of the same
  /// bytes and disagree only about what the hardware looked like.
  /// <para/>
  /// It is not a weaker test than comparing samples — it is a different one, and in the direction
  /// that matters it is stronger: a decode that loses the picture loses its boundaries too, and no
  /// choice of palette puts them back.
  /// </remarks>
  private static bool _SameStructure(byte[] left, byte[] right) {
    if (left.Length != right.Length)
      return false;

    var leftLabels = new Dictionary<int, int>();
    var rightLabels = new Dictionary<int, int>();
    for (var i = 0; i + 2 < left.Length; i += 3) {
      var leftColour = (left[i] << 16) | (left[i + 1] << 8) | left[i + 2];
      var rightColour = (right[i] << 16) | (right[i + 1] << 8) | right[i + 2];

      var leftIsNew = leftLabels.TryAdd(leftColour, leftLabels.Count);
      var rightIsNew = rightLabels.TryAdd(rightColour, rightLabels.Count);
      if (leftIsNew != rightIsNew || leftLabels[leftColour] != rightLabels[rightColour])
        return false;
    }

    return true;
  }

  private static double _MeanAbsoluteDifference(byte[] left, byte[] right) {
    if (left.Length != right.Length || left.Length == 0)
      return double.MaxValue;

    var total = 0L;
    for (var i = 0; i < left.Length; ++i)
      total += Math.Abs(left[i] - right[i]);

    return (double)total / left.Length;
  }
}
