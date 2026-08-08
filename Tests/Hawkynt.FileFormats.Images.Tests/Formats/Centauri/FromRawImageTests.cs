using System;
using FileFormat.Core;

namespace FileFormat.Centauri.Tests;

[TestFixture]
public sealed class FromRawImageTests {

  /// <summary>
  /// A picture the multicolour screen can hold exactly: black everywhere pattern 00 falls, and no
  /// more than three further colours in any four-by-eight cell.
  /// </summary>
  internal static RawImage MulticolorImage(int width, int height) {
    var rgb = new byte[width * height * 3];

    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var cell = y / 8 * (width / 4) + x / 4;
      var pattern = (x + y) & 3;
      var index = pattern == 0 ? 0 : 1 + (cell * 5 + pattern) % 15;
      var color = Commodore64Graphics.HexColors[index];
      var offset = (y * width + x) * 3;
      rgb[offset] = (byte)(color >> 16);
      rgb[offset + 1] = (byte)(color >> 8);
      rgb[offset + 2] = (byte)color;
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = rgb };
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_APictureTheScreenCanHold_IsExact() {
    var source = MulticolorImage(CentauriFile.FixedWidth, CentauriFile.FixedHeight);

    var bytes = CentauriWriter.ToBytes(_Encode<CentauriFile>(source));
    var decoded = CentauriFile.ToRawImage(CentauriReader.FromBytes(bytes));

    Assert.That(decoded.PixelData, Is.EqualTo(source.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_SamplesAPictureOfAnotherSize() {
    var file = _Encode<CentauriFile>(MulticolorImage(320, 400));

    Assert.Multiple(() => {
      Assert.That(file.BitmapData, Has.Length.EqualTo(CentauriFile.BitmapDataSize));
      Assert.That(file.VideoMatrix, Has.Length.EqualTo(CentauriFile.VideoMatrixSize));
      Assert.That(file.ColorRam, Has.Length.EqualTo(CentauriFile.ColorRamSize));
      Assert.That(CentauriWriter.ToBytes(file), Has.Length.EqualTo(CentauriFile.ExpectedFileSize));
    });
  }

  /// <summary>
  /// Encodes through the interface rather than the type, so this stops compiling if the declaration
  /// goes away — which is what the registry generator reads to decide the format can be written at
  /// all, and nothing else here would notice its absence.
  /// </summary>
  private static TFile _Encode<TFile>(RawImage image) where TFile : IImageFromRawImage<TFile>
    => TFile.FromRawImage(image);

}
