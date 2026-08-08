using System;
using FileFormat.Core;

namespace FileFormat.MapletownMx1.Tests;

[TestFixture]
public sealed class MapletownMx1FileFromRawImageTests {

  private const int _WIDTH = 53;
  private const int _HEIGHT = 29;

  /// <summary>
  /// A picture the format holds exactly: every channel already on one of the nine levels a colour is
  /// written in, and 128 colours in all, which is what a palette holds.
  /// </summary>
  /// <remarks>
  /// In blocks of five pixels, so that the run lengths the format is made of have something to say —
  /// a picture whose every pixel differs from its neighbour would pass this test while proving
  /// nothing about the part of the encoder that does the work.
  /// </remarks>
  private static RawImage _Handmade() {
    var rgb = new byte[_WIDTH * _HEIGHT * 3];
    for (var pixel = 0; pixel < _WIDTH * _HEIGHT; ++pixel) {
      var block = pixel / 5;
      rgb[pixel * 3] = MapletownMx1Writer.Channel(block % 4);
      rgb[pixel * 3 + 1] = MapletownMx1Writer.Channel(block / 4 % 4);
      rgb[pixel * 3 + 2] = MapletownMx1Writer.Channel(block / 16 % 8);
    }

    return new() { Width = _WIDTH, Height = _HEIGHT, Format = PixelFormat.Rgb24, PixelData = rgb };
  }

  private static byte[] _Rgb(RawImage image) => PixelConverter.Convert(image, PixelFormat.Rgb24).PixelData;

  [Test]
  [Category("Unit")]
  public void EncodeThenDecode_ReturnsAPictureTheFormatHoldsUnchanged() {
    var source = _Handmade();
    var bytes = MapletownMx1Writer.ToBytes(MapletownMx1File.FromRawImage(source));
    var decoded = MapletownMx1File.ToRawImage(MapletownMx1Reader.FromBytes(bytes));

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(_WIDTH));
      Assert.That(decoded.Height, Is.EqualTo(_HEIGHT));
      Assert.That(_Rgb(decoded), Is.EqualTo(_Rgb(source)));
    });
  }

  [Test]
  [Category("Unit")]
  public void ADifferentlySizedPictureKeepsItsOwnSizeRatherThanBeingRefused() {
    // The format states its own corners, so there is no size to scale to and none to refuse.
    var decoded = MapletownMx1File.ToRawImage(
      MapletownMx1Reader.FromBytes(
        MapletownMx1Writer.ToBytes(MapletownMx1File.FromRawImage(_Handmade().SampleTo(101, 77)))));

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(101));
      Assert.That(decoded.Height, Is.EqualTo(77));
    });
  }

  [Test]
  [Category("Unit")]
  public void APictureWithMorePixelsThanALengthCanCountIsSampledDown() {
    var wide = new RawImage {
      Width = 1600,
      Height = 1600,
      Format = PixelFormat.Rgb24,
      PixelData = new byte[1600 * 1600 * 3],
    };

    var file = MapletownMx1File.FromRawImage(wide);

    Assert.Multiple(() => {
      Assert.That((long)file.Width * file.Height, Is.LessThanOrEqualTo(MapletownMx1File.MaxPixels));
      Assert.That(file.Width, Is.GreaterThan(1400));
    });
  }

  [Test]
  [Category("Unit")]
  public void AColourOffTheNineLevelsIsSnappedToThem() {
    // The quirk: a colour is a number in base nine with a digit a channel, so the levels are 255/8
    // apart and nothing between them exists. 200 is nearer the seventh level than the eighth.
    var odd = new RawImage {
      Width = 3,
      Height = 1,
      Format = PixelFormat.Rgb24,
      PixelData = [200, 200, 200, 0, 0, 0, 255, 255, 255],
    };

    var decoded = _Rgb(MapletownMx1File.ToRawImage(
      MapletownMx1Reader.FromBytes(MapletownMx1Writer.ToBytes(MapletownMx1File.FromRawImage(odd)))));

    Assert.That(decoded, Is.EqualTo(new byte[] { 191, 191, 191, 0, 0, 0, 255, 255, 255 }));
  }

  [Test]
  [Category("Unit")]
  public void TheStreamIsPrintableAndAnnouncesItself() {
    // Written to survive a bulletin board: nothing outside the alphabet reaches the file, and the
    // reader finds the picture by the line that announces it rather than by counting bytes.
    var bytes = MapletownMx1Writer.ToBytes(MapletownMx1File.FromRawImage(_Handmade()));
    var decode = FileFormat.Mapletown.MapletownStream.CreateDecodeTable();
    var header = System.Text.Encoding.ASCII.GetString(bytes, 0, Array.IndexOf(bytes, (byte)'\n'));

    var offending = 0;
    for (var at = header.Length + 1; at < bytes.Length - 1; ++at)
      if (decode[bytes[at]] >= 128)
        ++offending;

    Assert.Multiple(() => {
      Assert.That(header, Does.StartWith("@@@ ").And.EndWith($"{_HEIGHT} lines) @@@"));
      Assert.That(offending, Is.Zero);
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_Throws()
    => Assert.Throws<ArgumentNullException>(() => MapletownMx1File.FromRawImage(null!));
}
