using System;
using FileFormat.Core;

namespace FileFormat.AppleShr.Tests;

[TestFixture]
public sealed class FromRawImageTests {

  /// <summary>
  /// A picture the screen can hold exactly: sixteen colours on any one line, and no more than
  /// sixteen distinct sets of sixteen down the whole picture — one per palette the file holds.
  /// </summary>
  private static RawImage _PerLinePalettes(int width, int height) {
    var rgb = new byte[width * height * 3];

    for (var y = 0; y < height; ++y) {
      var bank = y * 16 / height;
      for (var x = 0; x < width; ++x) {
        var entry = x * 16 / width;
        var offset = (y * width + x) * 3;

        // Channels are multiples of seventeen, which is what four bits a channel comes back as.
        rgb[offset] = (byte)(((bank + entry) & 15) * 17);
        rgb[offset + 1] = (byte)((entry * 3 % 16) * 17);
        rgb[offset + 2] = (byte)((bank * 5 % 16) * 17);
      }
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = rgb };
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_SixteenColoursPerScanline_IsExact() {
    var source = _PerLinePalettes(AppleShrFile.FixedWidth, AppleShrFile.FixedHeight);

    var bytes = AppleShrWriter.ToBytes(_Encode<AppleShrFile>(source));
    var decoded = AppleShrFile.ToRawImage(AppleShrReader.FromBytes(bytes));

    Assert.That(decoded.PixelData, Is.EqualTo(source.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_GivesLinesOfDifferentColoursDifferentPalettes() {
    var file = _Encode<AppleShrFile>(_PerLinePalettes(AppleShrFile.FixedWidth, AppleShrFile.FixedHeight));

    Assert.That(new System.Collections.Generic.HashSet<byte>(file.ScanlineControl), Has.Count.GreaterThan(1));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_SamplesAPictureOfAnotherSize() {
    var file = _Encode<AppleShrFile>(_PerLinePalettes(160, 100));

    Assert.Multiple(() => {
      Assert.That(file.PixelData, Has.Length.EqualTo(AppleShrFile.PixelDataSize));
      Assert.That(file.ScanlineControl, Has.Length.EqualTo(AppleShrFile.ScbSize));
      Assert.That(AppleShrWriter.ToBytes(file), Has.Length.EqualTo(AppleShrFile.ExpectedFileSize));
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
