using System;
using System.IO;
using FileFormat.MultiPalettePicture;

namespace FileFormat.MultiPalettePicture.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_PreservesPixelData() {
    var original = _BuildFile();
    for (var i = 0; i < original.PixelData.Length; ++i)
      original.PixelData[i] = (byte)(i * 7 % 256);

    var bytes = MultiPalettePictureWriter.ToBytes(original);
    var restored = MultiPalettePictureReader.FromBytes(bytes);

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_PreservesPalettes() {
    var original = _BuildFile();
    for (var y = 0; y < 200; ++y)
      for (var i = 0; i < 16; ++i)
        original.Palettes[y][i] = (short)((y * 16 + i) & 0xFFF);

    var bytes = MultiPalettePictureWriter.ToBytes(original);
    var restored = MultiPalettePictureReader.FromBytes(bytes);

    for (var y = 0; y < 200; ++y)
      Assert.That(restored.Palettes[y], Is.EqualTo(original.Palettes[y]), $"Palette for scanline {y}");
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeros_PreservesData() {
    var original = _BuildFile();

    var bytes = MultiPalettePictureWriter.ToBytes(original);
    var restored = MultiPalettePictureReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(restored.PixelData, Is.All.EqualTo((byte)0));
      for (var y = 0; y < 200; ++y)
        Assert.That(restored.Palettes[y], Is.All.EqualTo((short)0));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile_PreservesData() {
    var original = _BuildFile();
    original.Palettes[0][0] = 0x0FFF;
    original.PixelData[0] = 0xFF;

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mpp");
    try {
      var bytes = MultiPalettePictureWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);
      var restored = MultiPalettePictureReader.FromFile(new FileInfo(tempPath));

      Assert.Multiple(() => {
        Assert.That(restored.Palettes[0][0], Is.EqualTo(0x0FFF));
        Assert.That(restored.PixelData[0], Is.EqualTo(0xFF));
      });
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_DimensionsPreserved() {
    var original = _BuildFile();

    var bytes = MultiPalettePictureWriter.ToBytes(original);
    var restored = MultiPalettePictureReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(320));
      Assert.That(restored.Height, Is.EqualTo(200));
    });
  }

  private static MultiPalettePictureFile _BuildFile() {
    var palettes = new short[200][];
    for (var y = 0; y < 200; ++y)
      palettes[y] = new short[16];

    return new MultiPalettePictureFile {
      PixelData = new byte[32000],
      Palettes = palettes
    };
  }
}
