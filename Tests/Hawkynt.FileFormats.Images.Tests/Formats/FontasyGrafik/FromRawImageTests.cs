using System;
using FileFormat.Core;

namespace FileFormat.FontasyGrafik.Tests;

[TestFixture]
public sealed class FromRawImageTests {

  private static readonly byte[] _Levels = [
    ChannelScaling.Expand3(0), ChannelScaling.Expand3(1), ChannelScaling.Expand3(2), ChannelScaling.Expand3(3),
    ChannelScaling.Expand3(4), ChannelScaling.Expand3(5), ChannelScaling.Expand3(6), ChannelScaling.Expand3(7),
  ];

  private static RawImage _StColourImage(int width, int height) {
    var rgb = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var index = (x * 16 / width + y) & 15;
      var offset = (y * width + x) * 3;
      rgb[offset] = _Levels[index & 7];
      rgb[offset + 1] = _Levels[(index * 5 + 2) & 7];
      rgb[offset + 2] = _Levels[(index >> 3) * 7];
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = rgb };
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_SixteenStColours_IsExact() {
    var source = _StColourImage(320, 200);

    var bytes = FontasyGrafikWriter.ToBytes(_Encode<FontasyGrafikFile>(source));
    var decoded = FontasyGrafikFile.ToRawImage(FontasyGrafikReader.FromBytes(bytes));

    Assert.That(decoded.Format, Is.EqualTo(PixelFormat.Indexed8));
    for (var i = 0; i < 320 * 200; ++i) {
      var entry = decoded.PixelData[i] * 3;
      Assert.That(decoded.Palette![entry], Is.EqualTo(source.PixelData[i * 3]), $"pixel {i} red");
      Assert.That(decoded.Palette[entry + 1], Is.EqualTo(source.PixelData[i * 3 + 1]), $"pixel {i} green");
      Assert.That(decoded.Palette[entry + 2], Is.EqualTo(source.PixelData[i * 3 + 2]), $"pixel {i} blue");
    }
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_SamplesAPictureOfAnotherSize() {
    var file = _Encode<FontasyGrafikFile>(_StColourImage(640, 400));

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(320));
      Assert.That(file.Height, Is.EqualTo(200));
      Assert.That(file.PixelData, Has.Length.EqualTo(FontasyGrafikFile.PlanarDataSize));
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
