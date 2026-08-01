using System;
using System.IO;
using FileFormat.Core;
using FileFormat.PalmPdb;

namespace FileFormat.PalmPdb.Tests;

[TestFixture]
public sealed class RoundTripTests {

  /// <summary>A row of sixteen pixels, four of each grey.</summary>
  private static byte[] FourGreysRow() => [0x1B, 0x1B, 0x1B, 0x1B];

  [Test]
  [Category("Integration")]
  public void RoundTrip_SmallImage() {
    var original = new PalmPdbFile {
      Width = 16,
      Height = 2,
      Name = string.Empty,
      PixelData = [.. FourGreysRow(), .. FourGreysRow()],
    };

    var restored = PalmPdbReader.FromBytes(PalmPdbWriter.ToBytes(original));

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(original.Width));
      Assert.That(restored.Height, Is.EqualTo(original.Height));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_NamePreserved() {
    var original = new PalmPdbFile {
      Width = 16,
      Height = 1,
      Name = "TestImage",
      PixelData = FourGreysRow(),
    };

    var restored = PalmPdbReader.FromBytes(PalmPdbWriter.ToBytes(original));

    Assert.That(restored.Name, Is.EqualTo("TestImage"));
  }

  /// <summary>The Image Viewer stores whole tiles, so a picture is widened to a multiple of sixteen.</summary>
  [Test]
  [Category("Integration")]
  [TestCase(40, 48)]
  [TestCase(16, 16)]
  [TestCase(1, 16)]
  [TestCase(33, 48)]
  public void FromRawImage_WidensToAWholeTile(int width, int expected) {
    var image = new RawImage {
      Width = width,
      Height = 2,
      Format = PixelFormat.Rgb24,
      PixelData = new byte[width * 2 * 3],
    };

    Assert.That(PalmPdbFile.FromRawImage(image).Width, Is.EqualTo(expected));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ToRawImage_FromRawImage() {
    var image = new RawImage {
      Width = 16,
      Height = 1,
      Format = PixelFormat.Rgb24,
      PixelData = [
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, // white
        170, 170, 170, 170, 170, 170, 170, 170, 170, 170, 170, 170,
        85, 85, 85, 85, 85, 85, 85, 85, 85, 85, 85, 85,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,                          // black
      ],
    };

    var back = PalmPdbFile.ToRawImage(PalmPdbFile.FromRawImage(image)).ToRgb24();

    Assert.Multiple(() => {
      Assert.That(back[0], Is.EqualTo(255));
      Assert.That(back[4 * 3], Is.EqualTo(170));
      Assert.That(back[8 * 3], Is.EqualTo(85));
      Assert.That(back[12 * 3], Is.EqualTo(0));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var original = new PalmPdbFile {
      Width = 16,
      Height = 2,
      Name = "OnDisk",
      PixelData = [.. FourGreysRow(), .. FourGreysRow()],
    };

    var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pdb");
    try {
      File.WriteAllBytes(path, PalmPdbWriter.ToBytes(original));
      var restored = PalmPdbReader.FromFile(new FileInfo(path));

      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    } finally {
      if (File.Exists(path))
        File.Delete(path);
    }
  }
}
