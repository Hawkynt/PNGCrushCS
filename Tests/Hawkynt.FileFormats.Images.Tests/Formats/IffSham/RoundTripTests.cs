using System;
using System.IO;
using FileFormat.Core;
using FileFormat.IffSham;

namespace FileFormat.IffSham.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RawImage_RoundTrip_Rgb444BaseColoursAreExact() {
    var source = _CreateFourColourImage();

    var encoded = IffShamFile.FromRawImage(source);
    var bytes = IffShamWriter.ToBytes(encoded);
    var parsed = IffShamReader.FromBytes(bytes);
    var decoded = IffShamFile.ToRawImage(parsed);

    Assert.Multiple(() => {
      Assert.That(parsed.PixelData, Is.EqualTo(encoded.PixelData));
      Assert.That(parsed.ScanlinePalettes, Is.EqualTo(encoded.ScanlinePalettes));
      Assert.That(decoded.Width, Is.EqualTo(320));
      Assert.That(decoded.Height, Is.EqualTo(200));
      Assert.That(decoded.Format, Is.EqualTo(PixelFormat.Rgb24));
      Assert.That(decoded.PixelData, Is.EqualTo(source.PixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void FromRawImage_ResamplesToHistoricalScreen() {
    var source = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Rgb24,
      PixelData = [0x44, 0x88, 0xCC],
    };

    var file = IffShamFile.FromRawImage(source);
    var decoded = IffShamFile.ToRawImage(file);

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(320));
      Assert.That(file.Height, Is.EqualTo(200));
      Assert.That(file.PixelData, Has.Length.EqualTo(320 * 200));
      Assert.That(file.ScanlinePalettes, Has.Length.EqualTo(200 * 16 * 3));
      Assert.That(decoded.PixelData.AsSpan(0, 3).SequenceEqual(new byte[] { 0x44, 0x88, 0xCC }), Is.True);
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var original = IffShamFile.FromRawImage(_CreateFourColourImage());
    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".sham");
    try {
      File.WriteAllBytes(tempPath, IffShamWriter.ToBytes(original));

      var restored = IffShamReader.FromFile(new FileInfo(tempPath));

      Assert.Multiple(() => {
        Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
        Assert.That(restored.ScanlinePalettes, Is.EqualTo(original.ScanlinePalettes));
      });
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }

  private static RawImage _CreateFourColourImage() {
    byte[][] colours = [
      [0x00, 0x00, 0x00],
      [0xFF, 0x00, 0x00],
      [0x00, 0xFF, 0x00],
      [0x00, 0x00, 0xFF],
    ];
    var pixels = new byte[320 * 200 * 3];

    for (var y = 0; y < 200; ++y)
      for (var x = 0; x < 320; ++x) {
        var colour = colours[(x / 40 + y / 25) & 3];
        var at = (y * 320 + x) * 3;
        pixels[at] = colour[0];
        pixels[at + 1] = colour[1];
        pixels[at + 2] = colour[2];
      }

    return new() {
      Width = 320,
      Height = 200,
      Format = PixelFormat.Rgb24,
      PixelData = pixels,
    };
  }
}
