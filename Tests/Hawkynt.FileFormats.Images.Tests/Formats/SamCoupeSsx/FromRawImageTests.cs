using FileFormat.Core;
using FileFormat.SamCoupeMode4;

namespace FileFormat.SamCoupeSsx.Tests;

[TestFixture]
public sealed class FromRawImageTests {

  /// <summary>A picture of at most sixteen colours, each one the hardware can make.</summary>
  private static RawImage SixteenHardwareColours(int width, int height) {
    var rgb = new byte[width * height * 3];

    for (var i = 0; i < width * height; ++i) {
      // Colour bytes 1 to 16, which are sixteen distinct ones of the machine's 128.
      var color = SamCoupePalette.ToRgb((byte)(i % 16 + 1));
      rgb[i * 3] = (byte)(color >> 16);
      rgb[i * 3 + 1] = (byte)(color >> 8);
      rgb[i * 3 + 2] = (byte)color;
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = rgb };
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_SixteenColours_IsExact() {
    var source = SixteenHardwareColours(SamCoupeSsxFile.Mode4Width, SamCoupeSsxFile.StoredRows);

    var bytes = SamCoupeSsxWriter.ToBytes(_Encode<SamCoupeSsxFile>(source));
    var decoded = SamCoupeSsxFile.ToRawImage(SamCoupeSsxReader.FromBytes(bytes));

    Assert.That(decoded.EnsureFormat(PixelFormat.Rgb24).PixelData, Is.EqualTo(source.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_SamplesAPictureOfAnotherSize() {
    var file = _Encode<SamCoupeSsxFile>(SixteenHardwareColours(101, 67));

    Assert.That(SamCoupeSsxWriter.ToBytes(file), Has.Length.EqualTo(SamCoupeSsxFile.Mode4Size));
  }

  /// <summary>
  /// Nothing in a dump says which mode it is and the length is the whole of what there is to go on,
  /// so a written one has to be exactly the length of the mode it holds and of no other.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void ToBytes_IsExactlyTheLengthOfOneMode() {
    var bytes = SamCoupeSsxWriter.ToBytes(_Encode<SamCoupeSsxFile>(SixteenHardwareColours(64, 64)));

    Assert.Multiple(() => {
      Assert.That(bytes, Has.Length.EqualTo(SamCoupeSsxFile.Mode4Size));
      Assert.That(bytes, Has.Length.Not.EqualTo(SamCoupeSsxFile.Mode1Size));
      Assert.That(bytes, Has.Length.Not.EqualTo(SamCoupeSsxFile.Mode2Size));
      Assert.That(bytes, Has.Length.Not.EqualTo(SamCoupeSsxFile.Mode3Size));
      Assert.That(bytes, Has.Length.Not.EqualTo(SamCoupeSsxFile.ChunkySize));

      // The mode is what settles the picture's size, so reading it back must give mode 4's.
      var decoded = SamCoupeSsxFile.ToRawImage(SamCoupeSsxReader.FromBytes(bytes));
      Assert.That(decoded.Width, Is.EqualTo(SamCoupeSsxFile.Mode4Width));
      Assert.That(decoded.Height, Is.EqualTo(SamCoupeSsxFile.StoredRows));
    });
  }

  private static TFile _Encode<TFile>(RawImage image) where TFile : IImageFromRawImage<TFile>
    => TFile.FromRawImage(image);

}
