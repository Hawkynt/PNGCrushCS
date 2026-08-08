using System;
using FileFormat.Core;

namespace FileFormat.AtariGraphics10.Tests;

[TestFixture]
public sealed class FromRawImageTests {

  /// <summary>The nine stock registers, which is what a file without registers of its own is drawn with.</summary>
  private static readonly int[] _Registers = [
    0x000000, 0x1C4BA0, 0x6AA232, 0xC85C24, 0xE8E8E8, 0xA82A2A, 0x8040C0, 0x48A0A0, 0xC0C040,
  ];

  private static RawImage _RegisterImage(int width, int height) {
    var rgb = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var color = _Registers[(x + y) % _Registers.Length];
      var offset = (y * width + x) * 3;
      rgb[offset] = (byte)(color >> 16);
      rgb[offset + 1] = (byte)(color >> 8);
      rgb[offset + 2] = (byte)color;
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = rgb };
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_APictureOnTheNineRegisters_IsExact() {
    var source = _RegisterImage(80, 192);

    var bytes = AtariGraphics10Writer.ToBytes(_Encode<AtariGraphics10File>(source));
    var decoded = AtariGraphics10File.ToRawImage(AtariGraphics10Reader.FromBytes(bytes));

    for (var y = 0; y < 192; ++y)
    for (var x = 0; x < 80; ++x)
      Assert.That(decoded.PixelData[y * 80 + x], Is.EqualTo((x + y) % _Registers.Length), $"pixel {x},{y}");
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_SamplesAPictureOfAnotherSize() {
    var file = _Encode<AtariGraphics10File>(_RegisterImage(320, 200));

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(80));
      Assert.That(file.Height, Is.EqualTo(192));
      Assert.That(file.PixelData, Has.Length.EqualTo(AtariGraphics10File.FileSize));
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
