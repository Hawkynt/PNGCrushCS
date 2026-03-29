using System;
using System.IO;
using FileFormat.Core;
using FileFormat.HiresBitmap;

namespace FileFormat.HiresBitmap.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void AllZeros_FieldsPreserved() {
    var original = new HiresBitmapFile {
      LoadAddress = 0,
      BitmapData = new byte[HiresBitmapFile.BitmapDataSize],
      ScreenData = new byte[HiresBitmapFile.ScreenDataSize],
      TrailingData = [],
    };

    var bytes = HiresBitmapWriter.ToBytes(original);
    var restored = HiresBitmapReader.FromBytes(bytes);

    Assert.That(restored.LoadAddress, Is.EqualTo(original.LoadAddress));
    Assert.That(restored.BitmapData, Is.EqualTo(original.BitmapData));
    Assert.That(restored.ScreenData, Is.EqualTo(original.ScreenData));
    Assert.That(restored.TrailingData, Is.Empty);
  }

  [Test]
  [Category("Integration")]
  public void AllFields_Preserved() {
    var bitmapData = new byte[HiresBitmapFile.BitmapDataSize];
    var screenData = new byte[HiresBitmapFile.ScreenDataSize];
    for (var i = 0; i < bitmapData.Length; ++i)
      bitmapData[i] = (byte)(i * 7 % 256);
    for (var i = 0; i < screenData.Length; ++i)
      screenData[i] = (byte)(i * 13 % 256);

    var original = new HiresBitmapFile {
      LoadAddress = 0x2000,
      BitmapData = bitmapData,
      ScreenData = screenData,
    };

    var bytes = HiresBitmapWriter.ToBytes(original);
    var restored = HiresBitmapReader.FromBytes(bytes);

    Assert.That(restored.LoadAddress, Is.EqualTo(0x2000));
    Assert.That(restored.BitmapData, Is.EqualTo(bitmapData));
    Assert.That(restored.ScreenData, Is.EqualTo(screenData));
  }

  [Test]
  [Category("Integration")]
  public void TrailingData_Preserved() {
    var trailing = new byte[] { 0xCA, 0xFE, 0xBA, 0xBE };
    var original = new HiresBitmapFile {
      BitmapData = new byte[HiresBitmapFile.BitmapDataSize],
      ScreenData = new byte[HiresBitmapFile.ScreenDataSize],
      TrailingData = trailing,
    };

    var bytes = HiresBitmapWriter.ToBytes(original);
    var restored = HiresBitmapReader.FromBytes(bytes);

    Assert.That(restored.TrailingData, Is.EqualTo(trailing));
  }

  [Test]
  [Category("Integration")]
  public void ViaFile_RoundTrip() {
    var bitmapData = new byte[HiresBitmapFile.BitmapDataSize];
    bitmapData[0] = 0xFF;
    bitmapData[4000] = 0xAB;

    var original = new HiresBitmapFile {
      LoadAddress = 0x6000,
      BitmapData = bitmapData,
      ScreenData = new byte[HiresBitmapFile.ScreenDataSize],
    };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".hbm");
    try {
      var bytes = HiresBitmapWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = HiresBitmapReader.FromFile(new FileInfo(tempPath));

      Assert.That(restored.LoadAddress, Is.EqualTo(original.LoadAddress));
      Assert.That(restored.BitmapData, Is.EqualTo(original.BitmapData));
      Assert.That(restored.ScreenData, Is.EqualTo(original.ScreenData));
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }

  [Test]
  [Category("Integration")]
  public void CustomLoadAddress_Preserved() {
    var original = new HiresBitmapFile {
      LoadAddress = 0xABCD,
      BitmapData = new byte[HiresBitmapFile.BitmapDataSize],
      ScreenData = new byte[HiresBitmapFile.ScreenDataSize],
    };

    var bytes = HiresBitmapWriter.ToBytes(original);
    var restored = HiresBitmapReader.FromBytes(bytes);

    Assert.That(restored.LoadAddress, Is.EqualTo(0xABCD));
  }

  [Test]
  [Category("Integration")]
  public void MaxValues_AllBytesFF() {
    var bitmapData = new byte[HiresBitmapFile.BitmapDataSize];
    var screenData = new byte[HiresBitmapFile.ScreenDataSize];
    Array.Fill(bitmapData, (byte)0xFF);
    Array.Fill(screenData, (byte)0xFF);

    var original = new HiresBitmapFile {
      LoadAddress = 0xFFFF,
      BitmapData = bitmapData,
      ScreenData = screenData,
    };

    var bytes = HiresBitmapWriter.ToBytes(original);
    var restored = HiresBitmapReader.FromBytes(bytes);

    Assert.That(restored.LoadAddress, Is.EqualTo(0xFFFF));
    Assert.That(restored.BitmapData, Is.EqualTo(bitmapData));
    Assert.That(restored.ScreenData, Is.EqualTo(screenData));
  }

  [Test]
  [Category("Integration")]
  public void ToRawImage_AllZeroBitmap_ProducesBackgroundColor() {
    var screenData = new byte[HiresBitmapFile.ScreenDataSize];
    screenData[0] = 0x10; // upper nibble=1 (white), lower nibble=0 (black)

    var file = new HiresBitmapFile {
      BitmapData = new byte[HiresBitmapFile.BitmapDataSize],
      ScreenData = screenData,
    };

    var raw = HiresBitmapFile.ToRawImage(file);

    // All bitmap bits are 0, so all pixels use lower nibble color (index 0 = black for cell 0)
    // Cell (0,0) covers pixels (0..7, 0..7), screenData[0] lower nibble = 0 = black = 0x000000
    Assert.That(raw.PixelData[0], Is.EqualTo(0x00));
    Assert.That(raw.PixelData[1], Is.EqualTo(0x00));
    Assert.That(raw.PixelData[2], Is.EqualTo(0x00));
  }

  [Test]
  [Category("Integration")]
  public void ToRawImage_Dimensions_Match() {
    var file = new HiresBitmapFile {
      BitmapData = new byte[HiresBitmapFile.BitmapDataSize],
      ScreenData = new byte[HiresBitmapFile.ScreenDataSize],
    };

    var raw = HiresBitmapFile.ToRawImage(file);

    Assert.That(raw.Width, Is.EqualTo(HiresBitmapFile.ImageWidth));
    Assert.That(raw.Height, Is.EqualTo(HiresBitmapFile.ImageHeight));
  }
}
