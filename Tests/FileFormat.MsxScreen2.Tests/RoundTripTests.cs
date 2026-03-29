using System;
using System.IO;
using FileFormat.MsxScreen2;

namespace FileFormat.MsxScreen2.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_RawData_PreservesAllSections() {
    var patternGen = new byte[MsxScreen2File.PatternGeneratorSize];
    var colorTable = new byte[MsxScreen2File.ColorTableSize];
    var nameTable = new byte[MsxScreen2File.PatternNameTableSize];
    for (var i = 0; i < patternGen.Length; ++i)
      patternGen[i] = (byte)(i % 256);
    for (var i = 0; i < colorTable.Length; ++i)
      colorTable[i] = (byte)(i * 3 % 256);
    for (var i = 0; i < nameTable.Length; ++i)
      nameTable[i] = (byte)(i * 7 % 256);

    var original = new MsxScreen2File {
      PatternGenerator = patternGen,
      ColorTable = colorTable,
      PatternNameTable = nameTable,
      HasBsaveHeader = false
    };

    var bytes = MsxScreen2Writer.ToBytes(original);
    var restored = MsxScreen2Reader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(256));
    Assert.That(restored.Height, Is.EqualTo(192));
    Assert.That(restored.HasBsaveHeader, Is.False);
    Assert.That(restored.PatternGenerator, Is.EqualTo(patternGen));
    Assert.That(restored.ColorTable, Is.EqualTo(colorTable));
    Assert.That(restored.PatternNameTable, Is.EqualTo(nameTable));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_WithBsaveHeader_PreservesHeader() {
    var patternGen = new byte[MsxScreen2File.PatternGeneratorSize];
    var colorTable = new byte[MsxScreen2File.ColorTableSize];
    var nameTable = new byte[MsxScreen2File.PatternNameTableSize];
    for (var i = 0; i < patternGen.Length; ++i)
      patternGen[i] = (byte)(i * 13 % 256);

    var original = new MsxScreen2File {
      PatternGenerator = patternGen,
      ColorTable = colorTable,
      PatternNameTable = nameTable,
      HasBsaveHeader = true
    };

    var bytes = MsxScreen2Writer.ToBytes(original);
    var restored = MsxScreen2Reader.FromBytes(bytes);

    Assert.That(restored.HasBsaveHeader, Is.True);
    Assert.That(restored.PatternGenerator, Is.EqualTo(patternGen));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeros_PreservesData() {
    var original = new MsxScreen2File {
      PatternGenerator = new byte[MsxScreen2File.PatternGeneratorSize],
      ColorTable = new byte[MsxScreen2File.ColorTableSize],
      PatternNameTable = new byte[MsxScreen2File.PatternNameTableSize],
      HasBsaveHeader = false
    };

    var bytes = MsxScreen2Writer.ToBytes(original);
    var restored = MsxScreen2Reader.FromBytes(bytes);

    Assert.That(restored.PatternGenerator, Is.All.EqualTo(0));
    Assert.That(restored.ColorTable, Is.All.EqualTo(0));
    Assert.That(restored.PatternNameTable, Is.All.EqualTo(0));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile_PreservesData() {
    var patternGen = new byte[MsxScreen2File.PatternGeneratorSize];
    var colorTable = new byte[MsxScreen2File.ColorTableSize];
    var nameTable = new byte[MsxScreen2File.PatternNameTableSize];
    for (var i = 0; i < patternGen.Length; ++i)
      patternGen[i] = (byte)(i * 11 % 256);

    var original = new MsxScreen2File {
      PatternGenerator = patternGen,
      ColorTable = colorTable,
      PatternNameTable = nameTable,
      HasBsaveHeader = false
    };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".sc2");
    try {
      var bytes = MsxScreen2Writer.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);
      var restored = MsxScreen2Reader.FromFile(new FileInfo(tempPath));

      Assert.That(restored.Width, Is.EqualTo(256));
      Assert.That(restored.Height, Is.EqualTo(192));
      Assert.That(restored.PatternGenerator, Is.EqualTo(patternGen));
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ToRawImage_ProducesCorrectDimensions() {
    var patternGen = new byte[MsxScreen2File.PatternGeneratorSize];
    var colorTable = new byte[MsxScreen2File.ColorTableSize];
    var nameTable = new byte[MsxScreen2File.PatternNameTableSize];

    var file = new MsxScreen2File {
      PatternGenerator = patternGen,
      ColorTable = colorTable,
      PatternNameTable = nameTable,
      HasBsaveHeader = false
    };

    var rawImage = MsxScreen2File.ToRawImage(file);

    Assert.That(rawImage.Width, Is.EqualTo(256));
    Assert.That(rawImage.Height, Is.EqualTo(192));
    Assert.That(rawImage.Format, Is.EqualTo(FileFormat.Core.PixelFormat.Indexed8));
    Assert.That(rawImage.PixelData.Length, Is.EqualTo(256 * 192));
    Assert.That(rawImage.PaletteCount, Is.EqualTo(16));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ToRawImage_PixelValuesInRange() {
    var patternGen = new byte[MsxScreen2File.PatternGeneratorSize];
    var colorTable = new byte[MsxScreen2File.ColorTableSize];
    var nameTable = new byte[MsxScreen2File.PatternNameTableSize];
    for (var i = 0; i < colorTable.Length; ++i)
      colorTable[i] = 0x12; // fg=1, bg=2

    var file = new MsxScreen2File {
      PatternGenerator = patternGen,
      ColorTable = colorTable,
      PatternNameTable = nameTable,
      HasBsaveHeader = false
    };

    var rawImage = MsxScreen2File.ToRawImage(file);

    for (var i = 0; i < rawImage.PixelData.Length; ++i)
      Assert.That(rawImage.PixelData[i], Is.LessThan(16));
  }
}
