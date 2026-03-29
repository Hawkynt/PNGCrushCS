using System;
using FileFormat.ColoRix;

namespace FileFormat.ColoRix.Tests;

[TestFixture]
public sealed class ColoRixWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => ColoRixWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_MagicBytes_AreRIX3() {
    var file = new ColoRixFile {
      Width = 2,
      Height = 2,
      Palette = new byte[ColoRixFile.PaletteSize],
      PixelData = new byte[4],
      StorageType = ColoRixCompression.None,
    };

    var bytes = ColoRixWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo((byte)'R'));
    Assert.That(bytes[1], Is.EqualTo((byte)'I'));
    Assert.That(bytes[2], Is.EqualTo((byte)'X'));
    Assert.That(bytes[3], Is.EqualTo((byte)'3'));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Dimensions_StoredAsMinusOne() {
    var file = new ColoRixFile {
      Width = 320,
      Height = 200,
      Palette = new byte[ColoRixFile.PaletteSize],
      PixelData = new byte[320 * 200],
      StorageType = ColoRixCompression.None,
    };

    var bytes = ColoRixWriter.ToBytes(file);

    var storedWidth = (ushort)(bytes[4] | (bytes[5] << 8));
    var storedHeight = (ushort)(bytes[6] | (bytes[7] << 8));

    Assert.That(storedWidth, Is.EqualTo(319));
    Assert.That(storedHeight, Is.EqualTo(199));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PaletteType_Is0xAF() {
    var file = new ColoRixFile {
      Width = 2,
      Height = 2,
      Palette = new byte[ColoRixFile.PaletteSize],
      PixelData = new byte[4],
      StorageType = ColoRixCompression.None,
    };

    var bytes = ColoRixWriter.ToBytes(file);

    Assert.That(bytes[8], Is.EqualTo(ColoRixFile.VgaPaletteType));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_StorageTypeByte_MatchesCompression() {
    var fileNone = new ColoRixFile {
      Width = 2,
      Height = 2,
      Palette = new byte[ColoRixFile.PaletteSize],
      PixelData = new byte[4],
      StorageType = ColoRixCompression.None,
    };

    var fileRle = new ColoRixFile {
      Width = 2,
      Height = 2,
      Palette = new byte[ColoRixFile.PaletteSize],
      PixelData = new byte[4],
      StorageType = ColoRixCompression.Rle,
    };

    var bytesNone = ColoRixWriter.ToBytes(fileNone);
    var bytesRle = ColoRixWriter.ToBytes(fileRle);

    Assert.That(bytesNone[9], Is.EqualTo(0));
    Assert.That(bytesRle[9], Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PaletteData_IsPresent() {
    var palette = new byte[ColoRixFile.PaletteSize];
    palette[0] = 63;
    palette[1] = 32;
    palette[2] = 16;

    var file = new ColoRixFile {
      Width = 2,
      Height = 2,
      Palette = palette,
      PixelData = new byte[4],
      StorageType = ColoRixCompression.None,
    };

    var bytes = ColoRixWriter.ToBytes(file);

    Assert.That(bytes[ColoRixFile.HeaderSize], Is.EqualTo(63));
    Assert.That(bytes[ColoRixFile.HeaderSize + 1], Is.EqualTo(32));
    Assert.That(bytes[ColoRixFile.HeaderSize + 2], Is.EqualTo(16));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_UncompressedSize_IsCorrect() {
    const int width = 4;
    const int height = 3;
    var file = new ColoRixFile {
      Width = width,
      Height = height,
      Palette = new byte[ColoRixFile.PaletteSize],
      PixelData = new byte[width * height],
      StorageType = ColoRixCompression.None,
    };

    var bytes = ColoRixWriter.ToBytes(file);

    var expectedSize = ColoRixFile.HeaderSize + ColoRixFile.PaletteSize + width * height;
    Assert.That(bytes.Length, Is.EqualTo(expectedSize));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PixelData_IsPreserved() {
    var pixelData = new byte[] { 10, 20, 30, 40 };
    var file = new ColoRixFile {
      Width = 2,
      Height = 2,
      Palette = new byte[ColoRixFile.PaletteSize],
      PixelData = pixelData,
      StorageType = ColoRixCompression.None,
    };

    var bytes = ColoRixWriter.ToBytes(file);

    var pixelOffset = ColoRixFile.HeaderSize + ColoRixFile.PaletteSize;
    Assert.That(bytes[pixelOffset], Is.EqualTo(10));
    Assert.That(bytes[pixelOffset + 1], Is.EqualTo(20));
    Assert.That(bytes[pixelOffset + 2], Is.EqualTo(30));
    Assert.That(bytes[pixelOffset + 3], Is.EqualTo(40));
  }
}
