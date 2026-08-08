using FileFormat.Core;

namespace FileFormat.VerticalHiresInterlace.Tests;

[TestFixture]
public sealed class FromRawImageTests {

  /// <summary>
  /// A picture the screen can hold exactly: per 8x8 cell two of the machine's colours and the
  /// average of the two, which is the third shade the interlace buys.
  /// </summary>
  private static RawImage ThreeShadesPerCell(int width, int height) {
    var palette = Commodore64Graphics.CreatePalette();
    var rgb = new byte[width * height * 3];

    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var cell = y / 8 * (width / 8 + 1) + x / 8;
      var first = cell * 7 % 16;
      var second = (cell * 11 + 5) % 16;

      var at = (y * width + x) * 3;
      for (var channel = 0; channel < 3; ++channel) {
        var high = palette[first * 3 + channel];
        var low = palette[second * 3 + channel];

        rgb[at + channel] = ((x + y) & 3) switch {
          0 or 1 => high,
          2 => low,
          _ => (byte)((high + low) / 2),
        };
      }
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = rgb };
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ThreeShadesPerCell_IsExact() {
    var source = ThreeShadesPerCell(VerticalHiresInterlaceFile.Width, VerticalHiresInterlaceFile.Height);

    var bytes = VerticalHiresInterlaceWriter.ToBytes(_Encode<VerticalHiresInterlaceFile>(source));
    var decoded = VerticalHiresInterlaceFile.ToRawImage(VerticalHiresInterlaceReader.FromBytes(bytes));

    Assert.That(decoded.PixelData, Is.EqualTo(source.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_SamplesAPictureOfAnotherSize() {
    var file = _Encode<VerticalHiresInterlaceFile>(ThreeShadesPerCell(101, 67));

    Assert.That(file.Data, Has.Length.EqualTo(VerticalHiresInterlaceFile.UnpackedSize));
  }

  /// <summary>
  /// The third shade only exists where the two fields disagree, so an encoder that wrote the same
  /// bitmap twice would be throwing away the whole of what this format is for.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void FromRawImage_LetsTheTwoFieldsDiffer() {
    var data = _Encode<VerticalHiresInterlaceFile>(
      ThreeShadesPerCell(VerticalHiresInterlaceFile.Width, VerticalHiresInterlaceFile.Height)).Data;

    var first = data[VerticalHiresInterlaceFile.FirstPackedBitmapOffset..VerticalHiresInterlaceFile.SecondPackedBitmapOffset];
    var second = data[VerticalHiresInterlaceFile.SecondPackedBitmapOffset..VerticalHiresInterlaceFile.PackedVideoMatrixOffset];

    Assert.That(second, Is.Not.EqualTo(first));
  }

  private static TFile _Encode<TFile>(RawImage image) where TFile : IImageFromRawImage<TFile>
    => TFile.FromRawImage(image);

}
