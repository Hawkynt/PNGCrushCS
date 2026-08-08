using System;
using FileFormat.Core;

namespace FileFormat.SamarHiresMap.Tests;

[TestFixture]
public sealed class FromRawImageTests {

  /// <summary>
  /// A picture the two fields can hold exactly: one colour register per line, held by both fields
  /// through all their zones, and every pixel one of the three shades that register can reach —
  /// its hue alone, its hue and luminance together, or the average of the two.
  /// </summary>
  private static RawImage ThreeShadesPerLine() {
    const int width = SamarHiresMapFile.Width;
    const int height = SamarHiresMapFile.Height;
    var palette = Atari8BitGraphics.Palette;
    var rgb = new byte[width * height * 3];

    for (var y = 0; y < height; ++y) {
      // An even register: the lowest bit survives neither of the masks a pixel is read through.
      var register = (y * 22 + 34) % 128 * 2;
      var lit = _Color(palette, register & 240);
      var unlit = _Color(palette, register & 254);
      var blend = (lit & unlit) + (((lit ^ unlit) >> 1) & 0x7F7F7F);

      for (var x = 0; x < width; ++x) {
        var color = ((x + y) % 3) switch { 0 => lit, 1 => unlit, _ => blend };
        var at = (y * width + x) * 3;
        rgb[at] = (byte)(color >> 16);
        rgb[at + 1] = (byte)(color >> 8);
        rgb[at + 2] = (byte)color;
      }
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = rgb };
  }

  private static int _Color(ReadOnlySpan<byte> palette, int value)
    => (palette[value * 3] << 16) | (palette[value * 3 + 1] << 8) | palette[value * 3 + 2];

  [Test]
  [Category("Integration")]
  public void RoundTrip_OneRegisterPerLine_IsExact() {
    var source = ThreeShadesPerLine();

    var bytes = SamarHiresMapWriter.ToBytes(_Encode<SamarHiresMapFile>(source));
    var decoded = SamarHiresMapFile.ToRawImage(SamarHiresMapReader.FromBytes(bytes));

    Assert.That(decoded.PixelData, Is.EqualTo(source.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_SamplesAPictureOfAnotherSize() {
    var rgb = new byte[101 * 37 * 3];
    for (var i = 0; i < rgb.Length; ++i)
      rgb[i] = (byte)(i * 23);

    var file = _Encode<SamarHiresMapFile>(
      new() { Width = 101, Height = 37, Format = PixelFormat.Rgb24, PixelData = rgb });

    Assert.That(SamarHiresMapWriter.ToBytes(file), Has.Length.EqualTo(SamarHiresMapFile.FileSize));
  }

  /// <summary>
  /// The two fields change registers at different points along a line, which is what gives it
  /// twelve colour zones rather than six — so an encoder that gave both fields the same map
  /// everywhere would be storing a picture with half the zones the format has.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void FromRawImage_LetsTheTwoFieldsHoldDifferentRegisters() {
    var rgb = new byte[SamarHiresMapFile.Width * SamarHiresMapFile.Height * 3];
    for (var y = 0; y < SamarHiresMapFile.Height; ++y)
    for (var x = 0; x < SamarHiresMapFile.Width; ++x) {
      // A ramp across the line, so no one register can serve a whole zone of either field.
      var at = (y * SamarHiresMapFile.Width + x) * 3;
      rgb[at] = (byte)(x * 255 / SamarHiresMapFile.Width);
      rgb[at + 1] = (byte)(y * 255 / SamarHiresMapFile.Height);
      rgb[at + 2] = (byte)(255 - x * 255 / SamarHiresMapFile.Width);
    }

    var data = _Encode<SamarHiresMapFile>(
      new() {
        Width = SamarHiresMapFile.Width,
        Height = SamarHiresMapFile.Height,
        Format = PixelFormat.Rgb24,
        PixelData = rgb,
      }).Data;

    var first = data[SamarHiresMapFile.FirstColorOffset..(SamarHiresMapFile.FirstColorOffset + 64)];
    var second = data[SamarHiresMapFile.SecondColorOffset..(SamarHiresMapFile.SecondColorOffset + 64)];

    Assert.That(second, Is.Not.EqualTo(first));
  }

  private static TFile _Encode<TFile>(RawImage image) where TFile : IImageFromRawImage<TFile>
    => TFile.FromRawImage(image);

}
