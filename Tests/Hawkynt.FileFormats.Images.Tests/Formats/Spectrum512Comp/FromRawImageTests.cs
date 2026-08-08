using System;
using System.Buffers.Binary;
using FileFormat.Core;

namespace FileFormat.Spectrum512Comp.Tests;

[TestFixture]
public sealed class FromRawImageTests {

  /// <summary>The eight levels this decoder's three-bit channel reaches.</summary>
  private static readonly byte[] _Levels = [0, 36, 72, 109, 145, 182, 218, 255];

  /// <summary>Sixteen colours a line, a different sixteen on every line — what the format is for.</summary>
  private static RawImage _PerLinePalette(int width, int height) {
    var rgb = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var index = x * 16 / width;
      var offset = (y * width + x) * 3;
      rgb[offset] = _Levels[(index + y) & 7];
      rgb[offset + 1] = _Levels[(index * 3 + y) & 7];
      rgb[offset + 2] = _Levels[index >> 1];
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = rgb };
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_SixteenStColoursPerScanline_IsExact() {
    var source = _PerLinePalette(320, 199);

    var bytes = Spectrum512CompWriter.ToBytes(_Encode<Spectrum512CompFile>(source));
    var decoded = Spectrum512CompFile.ToRawImage(Spectrum512CompReader.FromBytes(bytes));

    Assert.That(decoded.PixelData, Is.EqualTo(source.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_SamplesAPictureOfAnotherSize() {
    var file = _Encode<Spectrum512CompFile>(_PerLinePalette(160, 100));
    var decoded = Spectrum512CompFile.ToRawImage(Spectrum512CompReader.FromBytes(Spectrum512CompWriter.ToBytes(file)));

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(320));
      Assert.That(decoded.Height, Is.EqualTo(199));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_FillsAllThreePaletteZonesAlike() {
    // A player that models the ST's mid-line register reloads reads the second and third sets over
    // most of the line; leaving them at nought would show two thirds of the picture black.
    var file = _Encode<Spectrum512CompFile>(_PerLinePalette(320, 199));
    var plain = PackBits.Unpack(file.RawData, Spectrum512CompFile.DecompressedSize);

    for (var entry = 0; entry < 16; ++entry) {
      var first = BinaryPrimitives.ReadInt16BigEndian(plain.AsSpan(32000 + entry * 2));
      Assert.That(BinaryPrimitives.ReadInt16BigEndian(plain.AsSpan(32000 + (entry + 16) * 2)), Is.EqualTo(first));
      Assert.That(BinaryPrimitives.ReadInt16BigEndian(plain.AsSpan(32000 + (entry + 32) * 2)), Is.EqualTo(first));
    }
  }

  /// <summary>
  /// Encodes through the interface rather than the type, so this stops compiling if the declaration
  /// goes away — which is what the registry generator reads to decide the format can be written at
  /// all, and nothing else here would notice its absence.
  /// </summary>
  private static TFile _Encode<TFile>(RawImage image) where TFile : IImageFromRawImage<TFile>
    => TFile.FromRawImage(image);

}
