using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;
using FileFormat.Gem;

namespace FileFormat.Gem.Tests;

[TestFixture]
public sealed class GemTests {

  /// <summary>Builds a metafile from a header and a list of records, ended as the format ends one.</summary>
  private static byte[] _Metafile(short[] header, params short[][] records) {
    var words = new List<short>(header);
    foreach (var record in records)
      words.AddRange(record);

    words.Add(-1);

    var bytes = new byte[words.Count * 2];
    for (var i = 0; i < words.Count; ++i) {
      bytes[i * 2] = (byte)(words[i] & 0xFF);
      bytes[i * 2 + 1] = (byte)((words[i] >> 8) & 0xFF);
    }

    return bytes;
  }

  /// <summary>
  /// A header stating an extent of 0..1000 in x and 0..500 in y on a page of exactly that shape.
  /// </summary>
  private static short[] _Header(short extentX = 1000, short extentY = 500) => [
    -1, 24, 101, 2,
    0, 0, extentX, extentY,
    1000, 500,
    0, 0, 1000, 500,
    0, 0, 0, 0, 0, 0, 0, 0, 0, 0
  ];

  /// <summary>A filled area over the whole extent: opcode 9, four points, no integers.</summary>
  private static short[] _FilledSquare(short x0, short y0, short x1, short y1)
    => [9, 4, 0, 0, x0, y0, x1, y0, x1, y1, x0, y1];

  private static short[] _SetFillInterior(short style) => [23, 0, 1, 0, style];

  private static short[] _SetFillColour(short pen) => [25, 0, 1, 0, pen];

  private static short[] _SetFillPerimeter(short on) => [104, 0, 1, 0, on];

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => GemReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_WrongMagic_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => GemReader.FromBytes(new byte[64]));

  [Test]
  [Category("Unit")]
  public void FromBytes_WithoutTheTerminatingWord_ThrowsInvalidDataException() {
    var data = _Metafile(_Header(), _FilledSquare(0, 0, 1000, 500));
    Array.Resize(ref data, data.Length - 2);

    Assert.Throws<InvalidDataException>(() => GemReader.FromBytes(data), "a metafile that never ends was never read as one");
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_RecordRunningPastTheEnd_ThrowsInvalidDataException() {
    // A polyline claiming a thousand points in a file that holds four.
    var data = _Metafile(_Header(), [6, 1000, 0, 0, 0, 0, 10, 10]);

    Assert.Throws<InvalidDataException>(() => GemReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ReadsEveryRecordUpToTheTerminator() {
    var file = GemReader.FromBytes(_Metafile(_Header(), _SetFillInterior(1), _SetFillColour(1), _FilledSquare(0, 0, 1000, 500)));

    Assert.That(file.Records, Has.Count.EqualTo(3));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ReadsTheHeaderAsTheFormatLaysItOut() {
    var file = GemReader.FromBytes(_Metafile(_Header(800, 400), _FilledSquare(0, 0, 800, 400)));

    Assert.Multiple(() => {
      Assert.That(file.Version, Is.EqualTo(101));
      Assert.That(file.CoordinateFlag, Is.EqualTo(GemFile.RasterCoordinates));
      Assert.That(file.Extent, Is.EqualTo((0, 0, 800, 400)));
      Assert.That(file.PageSize, Is.EqualTo((1000, 500)));
      Assert.That(file.Window, Is.EqualTo((0, 0, 1000, 500)));
    });
  }

  /// <summary>
  /// The size comes from the header, not from a number somebody picked.
  /// </summary>
  /// <remarks>
  /// The extent is a fraction of the coordinate window, the window covers the page, and the page is
  /// stated in tenths of a millimetre. An extent of 1000 by 500 over a window of the same size on a
  /// page of 100 by 50 millimetres is 100 by 50 millimetres, which at ninety-six pixels to the inch
  /// is 378 by 189. Halving the extent has to halve both.
  /// </remarks>
  [Test]
  [Category("Unit")]
  public void ToRawImage_TakesItsSizeFromTheHeadersOwnPageAndWindow() {
    var whole = GemFile.ToRawImage(GemReader.FromBytes(_Metafile(_Header(), _FilledSquare(0, 0, 1000, 500))));
    var half = GemFile.ToRawImage(GemReader.FromBytes(_Metafile(_Header(500, 250), _FilledSquare(0, 0, 500, 250))));

    Assert.Multiple(() => {
      Assert.That(whole.Width, Is.EqualTo(378), "a hundred millimetres at ninety-six pixels to the inch");
      Assert.That(whole.Height, Is.EqualTo(189));
      Assert.That(half.Width, Is.EqualTo(189), "half the extent is half the picture");
      Assert.That(half.Height, Is.EqualTo(94));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ASolidFillCoversThePictureAndAHollowOneDoesNot() {
    var solid = GemFile.ToRawImage(GemReader.FromBytes(_Metafile(
      _Header(), _SetFillPerimeter(0), _SetFillInterior(GemAttributes.InteriorSolid), _SetFillColour(1), _FilledSquare(0, 0, 1000, 500))));

    var hollow = GemFile.ToRawImage(GemReader.FromBytes(_Metafile(
      _Header(), _SetFillPerimeter(0), _SetFillInterior(GemAttributes.InteriorHollow), _FilledSquare(0, 0, 1000, 500))));

    Assert.Multiple(() => {
      Assert.That(_Darkness(solid), Is.GreaterThan(0.9), "a solid fill over the whole extent covers it");
      Assert.That(_Darkness(hollow), Is.LessThan(0.02), "a hollow one paints nothing");
    });
  }

  /// <summary>
  /// A dropped or mis-scaled path shows up here: the shape has to land where the header puts it.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void ToRawImage_APartialFillLandsWhereTheExtentSaysItShould() {
    // The left half of the extent, filled solid, with no outline.
    var image = GemFile.ToRawImage(GemReader.FromBytes(_Metafile(
      _Header(), _SetFillPerimeter(0), _SetFillInterior(GemAttributes.InteriorSolid), _SetFillColour(1), _FilledSquare(0, 0, 500, 500))));

    Assert.Multiple(() => {
      Assert.That(_At(image, image.Width / 4, image.Height / 2), Is.EqualTo(0), "the left quarter is painted");
      Assert.That(_At(image, image.Width * 3 / 4, image.Height / 2), Is.EqualTo(255), "the right quarter is not");
      Assert.That(_Darkness(image), Is.EqualTo(0.5).Within(0.03), "and half the picture is covered");
    });
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_PatternedFillsCoverLessThanSolidOnes() {
    // Fill style 1 under a pattern interior is the lightest of the dithers.
    var patterned = GemFile.ToRawImage(GemReader.FromBytes(_Metafile(
      _Header(), _SetFillPerimeter(0), _SetFillInterior(GemAttributes.InteriorPattern), [24, 0, 1, 0, 1], _SetFillColour(1), _FilledSquare(0, 0, 1000, 500))));

    var darkness = _Darkness(patterned);
    Assert.Multiple(() => {
      Assert.That(darkness, Is.GreaterThan(0.02), "a pattern paints something");
      Assert.That(darkness, Is.LessThan(0.5), "but nothing like a solid fill");
    });
  }

  /// <summary>
  /// A dash pattern has to close on itself, or the line alternates the wrong way round every
  /// sixteen pixels.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void Dashes_ASolidLineTypeHasNoRunsAndADashedOneClosesOnItself() {
    var dotted = GemAttributes.Dashes(3, 1);
    var total = 0.0;
    foreach (var run in dotted)
      total += run;

    Assert.Multiple(() => {
      Assert.That(GemAttributes.Dashes(1, 1), Is.Empty, "type one is solid");
      Assert.That(dotted, Is.Not.Empty, "type three is not");
      Assert.That(dotted.Length % 2, Is.Zero, "the runs alternate, so there is an even number of them");
      Assert.That(total, Is.EqualTo(16), "and together they are the sixteen bits of the mask");
      Assert.That(GemAttributes.Dashes(3, 2)[0], Is.EqualTo(dotted[0] * 2), "the runs scale with the picture");
    });
  }

  [Test]
  [Category("Unit")]
  public void Palette_PenZeroIsThePaperAndPenOneIsTheInk() {
    Assert.Multiple(() => {
      Assert.That(GemAttributes.Colour(0), Is.EqualTo(Rgba32.White));
      Assert.That(GemAttributes.Colour(1), Is.EqualTo(Rgba32.Black));
      Assert.That(GemAttributes.Colour(99), Is.EqualTo(Rgba32.Black), "a pen the table has no entry for draws in ink");
    });
  }

  private static byte _At(RawImage image, int x, int y) => image.PixelData[(y * image.Width + Math.Min(x, image.Width - 1)) * 4];

  /// <summary>How much of the picture is ink, from nothing at all to every pixel black.</summary>
  private static double _Darkness(RawImage image) {
    var pixels = image.PixelData;
    var total = 0.0;
    for (var i = 0; i < pixels.Length; i += 4)
      total += (255 - pixels[i]) / 255.0;

    return total / (image.Width * image.Height);
  }
}
