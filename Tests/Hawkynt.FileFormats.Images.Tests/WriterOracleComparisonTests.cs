using System;
using System.IO;
using System.Linq;
using FileFormat.Core;
using NUnit.Framework;

namespace Hawkynt.FileFormats.Images.Tests;

/// <summary>
/// Holds the writer-oracle comparison to the scales it says it can account for.
/// </summary>
/// <remarks>
/// These need none of the tools installed, which is the point of them. The scales a decode may come
/// back at are the part of the comparison most easily got wrong in the quiet direction — a rule that
/// stops reconciling a size does not fail anywhere a tool is absent, it simply demotes a row the
/// next time somebody sweeps, and the sweep is opt-in and takes minutes. So the two conventions the
/// comparison undoes are pinned here against a real writer, together with the cases it must still
/// refuse.
/// <para/>
/// <c>AdvancedArtStudio</c> is the witness for the non-square pixel: it codes 160 by 200 and the
/// three tools that read it return 320 by 200, which is a whole multiple in one axis and none in the
/// other.
/// </remarks>
[TestFixture]
[Category("Unit")]
public sealed class WriterOracleComparisonTests {

  private const string _NON_SQUARE_PIXEL_FORMAT = "AdvancedArtStudio";
  private const int _CODED_WIDTH = 160;
  private const int _CODED_HEIGHT = 200;

  /// <summary>
  /// The complaint the comparison makes when it would not reconcile the two sizes, which has to be
  /// told apart from the one it makes about a flat picture — both phrase it as what the file holds.
  /// </summary>
  private const string _SIZE_COMPLAINT = @"it rebuilt \d+x\d+ where the file holds \d+x\d+";

  [Test]
  public void ADecodeAtTwiceTheCodedWidthIsThePictureTheFileHolds() {
    var (entry, path, source, ours) = _WriteProbe();

    try {
      var displayed = _Replicate(ours, 2, 1);
      var (verdict, detail) = WriterOracleComparison.Judge(entry, path, source, displayed);

      Assert.That(verdict, Is.EqualTo(WriterOracleTool.Verdict.Accepted),
        "A chip that draws a 160-wide screen 320 across has not disagreed about the file, and three "
        + $"tools return it that way: {detail}");
    } finally {
      _Discard(path);
    }
  }

  [Test]
  public void ADecodeAtThreeQuartersInBothAxesIsJudgedAtTheStoredSize() {
    var (entry, path, source, ours) = _WriteProbe();

    try {
      var rasterised = _Resample(ours, ours.Width * 3 / 4, ours.Height * 3 / 4);
      var (_, detail) = WriterOracleComparison.Judge(entry, path, source, rasterised);

      Assert.That(detail, Does.Not.Match(_SIZE_COMPLAINT),
        "An interpreter rasterising points at 72 to the inch where this package uses 96 returns "
        + "three pixels for every four in both axes, which is one scale over both and has to be "
        + "undone before the pixels are looked at.");
    } finally {
      _Discard(path);
    }
  }

  [Test]
  public void ADecodeOfADifferentShapeIsStillRefused() {
    var (entry, path, source, ours) = _WriteProbe();

    try {
      // Neither a whole multiple in each axis nor one scale over both: 1.875 across and 1.0 down.
      var stretched = _Resample(ours, 300, ours.Height);
      var (verdict, detail) = WriterOracleComparison.Judge(entry, path, source, stretched);

      Assert.Multiple(() => {
        Assert.That(verdict, Is.EqualTo(WriterOracleTool.Verdict.Rejected));
        Assert.That(detail, Does.Match(_SIZE_COMPLAINT),
          "A stretch that answers to no convention is a change of shape and has to be judged at its "
          + "own size, which rejects it.");
      });
    } finally {
      _Discard(path);
    }
  }

  [Test]
  public void ADecodeAtTwiceTheCodedWidthThatIsAlsoTheWrongPictureIsStillRefused() {
    var (entry, path, source, ours) = _WriteProbe();

    try {
      var displayed = _Replicate(ours, 2, 1);
      var (verdict, detail) = WriterOracleComparison.Judge(entry, path, source, _Flattened(displayed));

      Assert.Multiple(() => {
        Assert.That(verdict, Is.EqualTo(WriterOracleTool.Verdict.Rejected));
        Assert.That(detail, Does.Not.Match(_SIZE_COMPLAINT),
          "It has to be refused on its pixels and not on its size, or the scale rule is doing the "
          + "rejecting and would let a wrong picture of the right size through.");
      });
    } finally {
      _Discard(path);
    }
  }

  [Test]
  public void ADecodeAtFiveTimesTheCodedWidthIsBeyondWhatAnyConventionExplains() {
    var (entry, path, source, ours) = _WriteProbe();

    try {
      var enlarged = _Replicate(ours, 5, 1);
      var (verdict, detail) = WriterOracleComparison.Judge(entry, path, source, enlarged);

      Assert.Multiple(() => {
        Assert.That(verdict, Is.EqualTo(WriterOracleTool.Verdict.Rejected));
        Assert.That(detail, Does.Match(_SIZE_COMPLAINT),
          "Four is the largest multiple any of these machines draws; past that it is a poster.");
      });
    } finally {
      _Discard(path);
    }
  }

  // ============================================================================================
  // Scaffolding
  // ============================================================================================

  /// <summary>Writes the probe through a real writer and reads back what the file holds.</summary>
  private static (FormatEntry Entry, string Path, RawImage Source, RawImage Ours) _WriteProbe() {
    var entry = FormatRegistry.AllFormats.FirstOrDefault(e => e.Name == _NON_SQUARE_PIXEL_FORMAT);
    if (entry == null)
      Assert.Ignore($"{_NON_SQUARE_PIXEL_FORMAT} is not in the registry on this build.");

    var mode = entry!.VideoModes?.FirstOrDefault();
    var source = OracleProbePicture.Sample(_CODED_WIDTH, _CODED_HEIGHT, mode);
    var path = Path.Combine(Path.GetTempPath(), $"oraclescale-{Guid.NewGuid():N}{entry.PrimaryExtension}");

    if (!FormatRegistry.Write(source, entry.Format, new FileInfo(path))) {
      _Discard(path);
      Assert.Ignore($"{entry.Name} declined the probe, so there is no file to judge a decode of.");
    }

    var ours = entry.LoadRawImage(new(path));
    Assert.That(ours.Width, Is.EqualTo(_CODED_WIDTH), "the coded width this fixture is built on");
    Assert.That(ours.Height, Is.EqualTo(_CODED_HEIGHT), "the coded height this fixture is built on");

    return (entry, path, source, ours);
  }

  /// <summary>The picture with each pixel drawn <paramref name="across"/> by <paramref name="down"/>.</summary>
  private static RawImage _Replicate(RawImage picture, int across, int down) {
    var rgb = picture.ToRgb24();
    var width = picture.Width * across;
    var height = picture.Height * down;
    var data = new byte[width * height * 3];

    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var from = (y / down * picture.Width + x / across) * 3;
      var to = (y * width + x) * 3;
      data[to] = rgb[from];
      data[to + 1] = rgb[from + 1];
      data[to + 2] = rgb[from + 2];
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = data };
  }

  /// <summary>The picture at an arbitrary size, nearest sample.</summary>
  private static RawImage _Resample(RawImage picture, int width, int height) {
    var rgb = picture.ToRgb24();
    var data = new byte[width * height * 3];

    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var sx = Math.Min(picture.Width - 1, x * picture.Width / width);
      var sy = Math.Min(picture.Height - 1, y * picture.Height / height);
      var from = (sy * picture.Width + sx) * 3;
      var to = (y * width + x) * 3;
      data[to] = rgb[from];
      data[to + 1] = rgb[from + 1];
      data[to + 2] = rgb[from + 2];
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = data };
  }

  /// <summary>The same picture painted one flat colour — the Apple IIgs shape of wrong.</summary>
  private static RawImage _Flattened(RawImage picture) => new() {
    Width = picture.Width,
    Height = picture.Height,
    Format = PixelFormat.Rgb24,
    PixelData = new byte[picture.Width * picture.Height * 3],
  };

  private static void _Discard(string path) {
    try {
      File.Delete(path);
    } catch (Exception) {
      // best effort
    }
  }
}
