using System;
using FileFormat.Core;
using FileFormat.Mapletown;

namespace FileFormat.MapletownMl1.Tests;

[TestFixture]
public sealed class MapletownMl1FileFromRawImageTests {

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
      rgb[pixel * 3] = MapletownPicture.Channel(block % 4);
      rgb[pixel * 3 + 1] = MapletownPicture.Channel(block / 4 % 4);
      rgb[pixel * 3 + 2] = MapletownPicture.Channel(block / 16 % 8);
    }

    return new() { Width = _WIDTH, Height = _HEIGHT, Format = PixelFormat.Rgb24, PixelData = rgb };
  }

  private static byte[] _Rgb(RawImage image) => PixelConverter.Convert(image, PixelFormat.Rgb24).PixelData;

  private static RawImage _Written(RawImage source)
    => MapletownMl1File.ToRawImage(
      MapletownMl1Reader.FromBytes(MapletownMl1Writer.ToBytes(MapletownMl1File.FromRawImage(source))));

  [Test]
  [Category("Unit")]
  public void EncodeThenDecode_ReturnsAPictureTheFormatHoldsUnchanged() {
    var source = _Handmade();
    var decoded = _Written(source);

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
    var decoded = _Written(_Handmade().SampleTo(101, 77));

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(101));
      Assert.That(decoded.Height, Is.EqualTo(77));
    });
  }

  /// <summary>
  /// A run that reaches the end of a row carries on into the next one, because the reader walks the
  /// picture as a single line of pixels rather than row by row.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void AFlatPictureOfAnOddWidthComesBackWhole() {
    var flat = new RawImage {
      Width = 17,
      Height = 11,
      Format = PixelFormat.Rgb24,
      PixelData = new byte[17 * 11 * 3],
    };

    Array.Fill(flat.PixelData, MapletownPicture.Channel(5));

    Assert.That(_Rgb(_Written(flat)), Is.EqualTo(flat.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void ASinglePixelIsAPicture() {
    var one = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Rgb24,
      PixelData = [255, 0, 255],
    };

    var decoded = _Written(one);

    Assert.Multiple(() => {
      Assert.That((decoded.Width, decoded.Height), Is.EqualTo((1, 1)));
      Assert.That(_Rgb(decoded), Is.EqualTo(new byte[] { 255, 0, 255 }));
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

    var file = MapletownMl1File.FromRawImage(wide);

    Assert.Multiple(() => {
      Assert.That((long)file.Width * file.Height, Is.LessThanOrEqualTo(MapletownMl1File.MaxPixels));
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

    Assert.That(_Rgb(_Written(odd)), Is.EqualTo(new byte[] { 191, 191, 191, 0, 0, 0, 255, 255, 255 }));
  }

  /// <summary>
  /// ML1 is the binary skin of the format, so the file starts on the signature and carries no
  /// announcing line — that is MX1's, and it is the only thing that separates the two.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void TheStreamIsBinaryAndOpensOnTheSignature() {
    var bytes = MapletownMl1Writer.ToBytes(MapletownMl1File.FromRawImage(_Handmade()));
    var signature = (bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3];

    Assert.That(signature, Is.EqualTo(MapletownDecoder.Signature));
  }

  /// <summary>The two limits the stream itself cannot state past are refused rather than truncated.</summary>
  [Test]
  [Category("Unit")]
  public void APictureTheStreamCannotStateIsRefused() {
    var empty = new MapletownMl1File { Width = 0, Height = 4, Pixels = [] };
    var vast = new MapletownMl1File { Width = 4096, Height = 4096, Pixels = [] };
    var beyondACorner = new MapletownMl1File { Width = 70000, Height = 1, Pixels = [] };

    Assert.Multiple(() => {
      Assert.Throws<ArgumentException>(() => MapletownMl1Writer.ToBytes(empty));
      Assert.Throws<ArgumentException>(() => MapletownMl1Writer.ToBytes(vast));
      Assert.Throws<ArgumentException>(() => MapletownMl1Writer.ToBytes(beyondACorner));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_Throws()
    => Assert.Throws<ArgumentNullException>(() => MapletownMl1File.FromRawImage(null!));
}
