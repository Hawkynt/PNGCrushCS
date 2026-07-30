using System;
using System.IO;
using FileFormat.Gaf;

namespace FileFormat.Gaf.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_SingleFrame_PreservesPixelData() {
    var pixelData = new byte[8 * 8];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 7 % 256);

    var original = new GafFile {
      Width = 8,
      Height = 8,
      Name = "test_unit",
      TransparencyIndex = 9,
      PixelData = pixelData,
    };

    var bytes = GafWriter.ToBytes(original);
    var restored = GafReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.TransparencyIndex, Is.EqualTo(original.TransparencyIndex));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_NamePreserved() {
    var original = new GafFile {
      Width = 2,
      Height = 2,
      Name = "arm_commander",
      PixelData = new byte[4],
    };

    var bytes = GafWriter.ToBytes(original);
    var restored = GafReader.FromBytes(bytes);

    Assert.That(restored.Name, Is.EqualTo("arm_commander"));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile_PreservesData() {
    var pixelData = new byte[4 * 4];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 11 % 256);

    var original = new GafFile {
      Width = 4,
      Height = 4,
      Name = "file_test",
      TransparencyIndex = 9,
      PixelData = pixelData,
    };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".gaf");
    try {
      var bytes = GafWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = GafReader.FromFile(new FileInfo(tempPath));

      Assert.That(restored.Width, Is.EqualTo(original.Width));
      Assert.That(restored.Height, Is.EqualTo(original.Height));
      Assert.That(restored.Name, Is.EqualTo(original.Name));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeros_Preserved() {
    var original = new GafFile {
      Width = 4,
      Height = 4,
      Name = "blank",
      TransparencyIndex = 0,
      PixelData = new byte[16],
    };

    var bytes = GafWriter.ToBytes(original);
    var restored = GafReader.FromBytes(bytes);

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    Assert.That(restored.TransparencyIndex, Is.EqualTo(0));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_LongName_Truncated() {
    var longName = new string('X', 50);

    var original = new GafFile {
      Width = 2,
      Height = 2,
      Name = longName,
      PixelData = new byte[4],
    };

    var bytes = GafWriter.ToBytes(original);
    var restored = GafReader.FromBytes(bytes);

    Assert.That(restored.Name.Length, Is.LessThanOrEqualTo(31));
    Assert.That(restored.Name, Is.EqualTo(longName[..31]));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_CustomTransparencyIndex() {
    var original = new GafFile {
      Width = 2,
      Height = 2,
      Name = "custom_ti",
      TransparencyIndex = 255,
      PixelData = [1, 2, 3, 4],
    };

    var bytes = GafWriter.ToBytes(original);
    var restored = GafReader.FromBytes(bytes);

    Assert.That(restored.TransparencyIndex, Is.EqualTo(255));
  }
}
