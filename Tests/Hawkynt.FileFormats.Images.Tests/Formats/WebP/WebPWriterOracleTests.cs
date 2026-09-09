using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;
using Hawkynt.FileFormats.Images.Tests;

namespace FileFormat.WebP.Tests;

/// <summary>
/// Hands pictures this package wrote to the two decoders that speak for WebP outside it, and holds
/// both writers to what those decoders make of them.
/// </summary>
/// <remarks>
/// Every fixture that checked these writers before this one was small enough to fit in a handful of
/// macroblocks, and checked the result by reading it back with this package's own reader. Neither
/// limit was deliberate and together they hid a real defect in the lossless writer: an alphabet
/// whose symbols all came out the same Huffman depth reduced the code-length code to a single
/// symbol, which a decoder resolves without consuming a bit while the writer spent one on it, and
/// every code length after that landed a bit out of place. It takes a few hundred pixels of varied
/// content before an alphabet fills that evenly, so the pictures below are sized and shaded to
/// reach that state rather than to be convenient — three and four macroblocks a side, a picture
/// larger than a thumbnail, single rows and single columns, and the shades that were measured to
/// provoke it.
/// </remarks>
[TestFixture]
public sealed class WebPWriterOracleTests {

  private static readonly ConformanceOracle[] _ORACLES = [ConformanceOracle.DWebp, ConformanceOracle.FFmpeg];

  /// <summary>How a case paints its pixels.</summary>
  public enum Shade {

    /// <summary>Red across, green down, blue on the diagonal — the shape the defect was found with.</summary>
    Diagonal,

    /// <summary>Red across the full range, a coarse green ramp and a raw column index.</summary>
    CoarseRamp,

    /// <summary>Pseudo-random bytes, which fill every literal alphabet.</summary>
    Noise,

    /// <summary>One colour everywhere, which drives the writer down its back-reference path.</summary>
    Flat,
  }

  public static IEnumerable<TestCaseData> LosslessPictures() {
    // Three and four macroblocks a side, where the older fixtures stopped, and past them.
    yield return _Case(48, 48, Shade.CoarseRamp);
    yield return _Case(64, 64, Shade.CoarseRamp);
    yield return _Case(48, 48, Shade.Diagonal);
    yield return _Case(64, 64, Shade.Diagonal);
    yield return _Case(16, 16, Shade.Diagonal);
    yield return _Case(33, 33, Shade.Diagonal);

    // Not a multiple of the macroblock, and lopsided in each direction.
    yield return _Case(48, 16, Shade.Diagonal);
    yield return _Case(16, 48, Shade.Diagonal);
    yield return _Case(47, 71, Shade.CoarseRamp);

    // A picture the size of a real one.
    yield return _Case(320, 200, Shade.Diagonal);
    yield return _Case(320, 200, Shade.Noise);
    yield return _Case(320, 200, Shade.Flat);

    // One pixel wide and one pixel tall, long enough to fill an alphabet.
    yield return _Case(1, 300, Shade.Diagonal);
    yield return _Case(300, 1, Shade.Diagonal);
  }

  private static TestCaseData _Case(int width, int height, Shade shade)
    => new TestCaseData(width, height, shade).SetName($"{{m}}({width}x{height}, {shade})");

  [TestCaseSource(nameof(LosslessPictures))]
  public void WhatTheLosslessWriterProduces_IsDecodedSampleForSampleElsewhere(int width, int height, Shade shade) {
    _RequireAnOracle();

    var source = _Paint(width, height, shade);
    var directory = Directory.CreateTempSubdirectory("webpwriter");
    try {
      var path = Path.Combine(directory.FullName, "written.webp");
      File.WriteAllBytes(path, WebPFile.ToBytes(WebPFile.FromRawImage(source)));

      foreach (var oracle in _ORACLES) {
        var rebuilt = _RebuildWith(oracle, path, width, height, $"the {width}x{height} {shade} picture");
        if (rebuilt == null)
          continue;

        var difference = _FirstDifference(source, rebuilt);
        Assert.That(difference, Is.Null,
          $"{oracle.DisplayName()} decoded the {width}x{height} {shade} picture to different pixels: {difference}");
      }
    } finally {
      try { directory.Delete(recursive: true); } catch { /* best effort */ }
    }
  }

  /// <summary>
  /// The lossy writer at each partition count VP8 permits, on pictures large enough for the
  /// partitions to hold more than one macroblock row each.
  /// </summary>
  /// <remarks>
  /// Spreading the coefficient tokens over several arithmetic partitions changes how they are
  /// packed, never which tokens they are, so all four counts have to decode to the same picture —
  /// and to a picture an outside decoder agrees with, which is the half a round trip through this
  /// package's own reader cannot answer.
  /// </remarks>
  [TestCase(48, 48)]
  [TestCase(64, 64)]
  [TestCase(127, 65)]
  [TestCase(320, 200)]
  public void EveryTokenPartitionCount_DecodesElsewhereToTheSamePicture(int width, int height) {
    _RequireAnOracle();

    var source = _Paint(width, height, Shade.Diagonal);
    var directory = Directory.CreateTempSubdirectory("webppartitions");
    try {
      foreach (var oracle in _ORACLES) {
        RawImage? first = null;
        foreach (var partitions in new[] { 1, 2, 4, 8 }) {
          var path = Path.Combine(directory.FullName, $"written{partitions}.webp");
          var options = new WebPLossyEncodingOptions { Quality = 80, TokenPartitions = partitions };
          File.WriteAllBytes(path, WebPFile.ToBytes(WebPLossyEncoder.Encode(source, options)));

          var rebuilt = _RebuildWith(oracle, path, width, height, $"the {width}x{height} picture at {partitions} token partitions");
          if (rebuilt == null)
            break;

          if (first == null) {
            first = rebuilt;
            continue;
          }

          Assert.That(_FirstDifference(first, rebuilt), Is.Null,
            $"{oracle.DisplayName()} decoded the {partitions}-partition stream to a different picture "
            + "than the 1-partition stream of the same source");
        }
      }
    } finally {
      try { directory.Delete(recursive: true); } catch { /* best effort */ }
    }
  }

  private static void _RequireAnOracle() {
    foreach (var oracle in _ORACLES)
      if (WriterOracleTool.IsAvailable(oracle))
        return;

    Assert.Inconclusive("Neither dwebp nor ffmpeg is on this machine, so neither has been asked.");
  }

  /// <summary>Asks one tool to rebuild the file, or null where that tool is not on this machine.</summary>
  private static RawImage? _RebuildWith(ConformanceOracle oracle, string path, int width, int height, string what) {
    if (!WriterOracleTool.IsAvailable(oracle))
      return null;

    var rebuilt = WriterOracleTool.Rebuild(oracle, path);
    Assert.That(rebuilt, Is.Not.Null, $"{oracle.DisplayName()} would not decode {what} this writer produced.");

    Assert.Multiple(() => {
      Assert.That(rebuilt!.Width, Is.EqualTo(width), $"{oracle.DisplayName()} rebuilt {what} at the wrong width.");
      Assert.That(rebuilt.Height, Is.EqualTo(height), $"{oracle.DisplayName()} rebuilt {what} at the wrong height.");
    });

    return rebuilt;
  }

  /// <summary>The first sample the two pictures disagree on, or null when they agree throughout.</summary>
  private static string? _FirstDifference(RawImage expected, RawImage actual) {
    var wanted = expected.EnsureAnyFormat(PixelFormat.Rgb24);
    var got = actual.EnsureAnyFormat(PixelFormat.Rgb24);

    for (var y = 0; y < wanted.Height; ++y)
    for (var x = 0; x < wanted.Width; ++x) {
      var o = (y * wanted.Width + x) * 3;
      if (wanted.PixelData[o] == got.PixelData[o]
          && wanted.PixelData[o + 1] == got.PixelData[o + 1]
          && wanted.PixelData[o + 2] == got.PixelData[o + 2])
        continue;

      return $"at ({x},{y}) expected "
        + $"{wanted.PixelData[o]},{wanted.PixelData[o + 1]},{wanted.PixelData[o + 2]} but read "
        + $"{got.PixelData[o]},{got.PixelData[o + 1]},{got.PixelData[o + 2]}";
    }

    return null;
  }

  private static RawImage _Paint(int width, int height, Shade shade) {
    var data = new byte[width * height * 3];
    var random = new Random(20250909);
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var o = (y * width + x) * 3;
      switch (shade) {
        case Shade.Diagonal:
          data[o] = (byte)(x * 255 / Math.Max(1, width - 1));
          data[o + 1] = (byte)(y * 255 / Math.Max(1, height - 1));
          data[o + 2] = (byte)(x + y);
          break;
        case Shade.CoarseRamp:
          data[o] = (byte)(x * 255 / Math.Max(1, width - 1));
          data[o + 1] = (byte)(y * 4);
          data[o + 2] = (byte)x;
          break;
        case Shade.Noise:
          data[o] = (byte)random.Next(256);
          data[o + 1] = (byte)random.Next(256);
          data[o + 2] = (byte)random.Next(256);
          break;
        default:
          data[o] = 200;
          data[o + 1] = 30;
          data[o + 2] = 90;
          break;
      }
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = data };
  }
}
