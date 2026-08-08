using System;
using System.Buffers.Binary;
using FileFormat.Core;

namespace FileFormat.PaintShopBrowser.Tests;

[TestFixture]
public sealed class FromRawImageTests {

  private static RawImage _Ramp(int width, int height) {
    var data = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var at = (y * width + x) * 3;
        data[at] = (byte)(100 + x % 128);
        data[at + 1] = (byte)(110 + y % 128);
        data[at + 2] = 128;
      }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = data };
  }

  private static PaintShopBrowserFile _RoundTrip(RawImage image)
    => PaintShopBrowserReader.FromBytes(PaintShopBrowserWriter.ToBytes(PaintShopBrowserFile.FromRawImage(image)));

  [Test]
  [Category("Integration")]
  public void RoundTrip_Ramp_ComesBackAtItsSizeAndVeryNearlyItsColours() {
    var source = _Ramp(37, 11);
    var decoded = PaintShopBrowserFile.ToRawImage(_RoundTrip(source));
    var rgb = PixelConverter.Convert(decoded, PixelFormat.Rgb24);

    long error = 0;
    for (var i = 0; i < source.PixelData.Length; ++i)
      error += Math.Abs(rgb.PixelData[i] - source.PixelData[i]);

    Assert.Multiple(() => {
      Assert.That((decoded.Width, decoded.Height), Is.EqualTo((37, 11)));
      Assert.That((double)error / source.PixelData.Length, Is.LessThan(4.0));
    });
  }

  [Test]
  [Category("Integration")]
  public void FromRawImage_KeepsWhateverSizeItIsGiven() {
    var wide = PaintShopBrowserFile.ToRawImage(_RoundTrip(_Ramp(200, 3)));
    var tall = PaintShopBrowserFile.ToRawImage(_RoundTrip(_Ramp(3, 200)));

    Assert.Multiple(() => {
      Assert.That((wide.Width, wide.Height), Is.EqualTo((200, 3)));
      Assert.That((tall.Width, tall.Height), Is.EqualTo((3, 200)));
    });
  }

  [Test]
  [Category("Integration")]
  public void FromRawImage_AcceptsAFormatOtherThanItsOwn() {
    var grey = new RawImage { Width = 37, Height = 11, Format = PixelFormat.Gray8, PixelData = new byte[37 * 11] };

    Assert.That(PaintShopBrowserFile.ToRawImage(_RoundTrip(grey)).Width, Is.EqualTo(37));
  }

  /// <summary>
  /// The two version numbers are the only things in the file written most significant byte first,
  /// and everything else — the count, the name lengths, the thumbnail lengths — the other way round.
  /// A writer that used one order throughout would put the version at 512 and the reader would refuse
  /// it by name.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void ToBytes_TheVersionIsTheOneFieldWrittenTheOtherWayRound() {
    var bytes = PaintShopBrowserWriter.ToBytes(PaintShopBrowserFile.FromRawImage(_Ramp(37, 11)));

    Assert.Multiple(() => {
      Assert.That(BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(15)), Is.EqualTo(2), "major");
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(19)), Is.EqualTo(1), "and the count the usual way round");
      Assert.That(bytes, Has.Length.GreaterThan(PaintShopBrowserFile.HeaderLength));
    });
  }

  /// <summary>
  /// Records follow one another with nothing between them, so the four set bytes before a
  /// thumbnail's length are the only thing that says the walk is still in step.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void ToBytes_PutsTheSentinelBeforeEachThumbnail() {
    var bytes = PaintShopBrowserWriter.ToBytes(PaintShopBrowserFile.FromRawImage(_Ramp(37, 11)));

    var at = PaintShopBrowserFile.HeaderLength;
    var nameLength = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at));
    at += 4 + nameLength + 8 + 6 * 4 + 8;

    Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at)), Is.EqualTo(PaintShopBrowserFile.ThumbnailSentinel));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_RefusesToWriteTheVersionItCannotRead() {
    var file = PaintShopBrowserFile.FromRawImage(_Ramp(9, 5));
    var older = new PaintShopBrowserFile { Version = (1, 0), Directory = file.Directory, Thumbnails = file.Thumbnails };

    var failure = Assert.Throws<ArgumentException>(() => PaintShopBrowserWriter.ToBytes(older));
    Assert.That(failure!.Message, Does.Contain("palette"));
  }
}
