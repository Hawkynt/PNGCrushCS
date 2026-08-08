using System;
using FileFormat.Core;

namespace FileFormat.Msx.Tests;

[TestFixture]
public sealed class FromRawImageTests {

  /// <summary>The eight levels a V9938 channel reaches, three bits widened by repetition.</summary>
  private static readonly byte[] _Levels = [
    MsxGraphics.Expand3(0), MsxGraphics.Expand3(1), MsxGraphics.Expand3(2), MsxGraphics.Expand3(3),
    MsxGraphics.Expand3(4), MsxGraphics.Expand3(5), MsxGraphics.Expand3(6), MsxGraphics.Expand3(7),
  ];

  /// <summary>
  /// A picture Screen 5 can hold: sixteen of the machine's colours, and one flat colour over the two
  /// rows and a quarter the file has no room for.
  /// </summary>
  private static RawImage _Screen5Image(int width, int height) {
    var rgb = new byte[width * height * 3];
    var stored = MsxFile.Screen5DataSize * 2;

    for (var i = 0; i < width * height; ++i) {
      var index = i < stored ? (i % 16 + i / width) & 15 : 0;
      rgb[i * 3] = _Levels[index & 7];
      rgb[i * 3 + 1] = _Levels[(index * 3 + 1) & 7];
      rgb[i * 3 + 2] = _Levels[index >> 1 & 7];
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = rgb };
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_SixteenMsxColours_IsExact() {
    var source = _Screen5Image(MsxFile.Screen5Width, MsxFile.Screen5Height);

    var bytes = MsxWriter.ToBytes(_Encode<MsxFile>(source));
    var decoded = MsxFile.ToRawImage(MsxReader.FromBytes(bytes));

    Assert.That(decoded.Format, Is.EqualTo(PixelFormat.Indexed8));
    for (var i = 0; i < MsxFile.Screen5Width * MsxFile.Screen5Height; ++i) {
      var entry = decoded.PixelData[i] * 3;
      Assert.That(decoded.Palette![entry], Is.EqualTo(source.PixelData[i * 3]), $"pixel {i} red");
      Assert.That(decoded.Palette[entry + 1], Is.EqualTo(source.PixelData[i * 3 + 1]), $"pixel {i} green");
      Assert.That(decoded.Palette[entry + 2], Is.EqualTo(source.PixelData[i * 3 + 2]), $"pixel {i} blue");
    }
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_SamplesAPictureOfAnotherSize() {
    var file = _Encode<MsxFile>(_Screen5Image(320, 200));
    var decoded = MsxFile.ToRawImage(MsxReader.FromBytes(MsxWriter.ToBytes(file)));

    Assert.Multiple(() => {
      Assert.That(file.Mode, Is.EqualTo(MsxMode.Screen5));
      Assert.That(decoded.Width, Is.EqualTo(MsxFile.Screen5Width));
      Assert.That(decoded.Height, Is.EqualTo(MsxFile.Screen5Height));
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
