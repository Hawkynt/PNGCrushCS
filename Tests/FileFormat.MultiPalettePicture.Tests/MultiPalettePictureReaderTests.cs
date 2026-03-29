using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.MultiPalettePicture;

namespace FileFormat.MultiPalettePicture.Tests;

[TestFixture]
public sealed class MultiPalettePictureReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => MultiPalettePictureReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => MultiPalettePictureReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mpp"));
    Assert.Throws<FileNotFoundException>(() => MultiPalettePictureReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => MultiPalettePictureReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[100];
    Assert.Throws<InvalidDataException>(() => MultiPalettePictureReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidData_ParsesCorrectly() {
    var data = _BuildMpp();
    var result = MultiPalettePictureReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(320));
      Assert.That(result.Height, Is.EqualTo(200));
      Assert.That(result.PixelData, Has.Length.EqualTo(32000));
      Assert.That(result.Palettes, Has.Length.EqualTo(200));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_EachScanlineHas16EntryPalette() {
    var data = _BuildMpp();
    var result = MultiPalettePictureReader.FromBytes(data);

    for (var y = 0; y < 200; ++y)
      Assert.That(result.Palettes[y], Has.Length.EqualTo(16), $"Scanline {y} palette should have 16 entries");
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_PreservesPaletteValues() {
    var data = _BuildMpp();
    // Set palette of first scanline, entry 0 to known value
    var paletteOffset = MultiPalettePictureFile.BytesPerScanline; // after pixel data of first line
    BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(paletteOffset), 0x0F00);

    var result = MultiPalettePictureReader.FromBytes(data);

    Assert.That(result.Palettes[0][0], Is.EqualTo(0x0F00));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_PreservesPixelData() {
    var data = _BuildMpp();
    data[0] = 0xAB; // first byte of first scanline pixel data

    var result = MultiPalettePictureReader.FromBytes(data);

    Assert.That(result.PixelData[0], Is.EqualTo(0xAB));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidData_ParsesCorrectly() {
    var data = _BuildMpp();
    using var ms = new MemoryStream(data);
    var result = MultiPalettePictureReader.FromStream(ms);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(320));
      Assert.That(result.Height, Is.EqualTo(200));
    });
  }

  private static byte[] _BuildMpp() {
    var data = new byte[MultiPalettePictureFile.ExpectedFileSize];
    for (var y = 0; y < 200; ++y) {
      var recordOffset = y * MultiPalettePictureFile.RecordSize;
      // Fill pixel data with pattern
      for (var i = 0; i < MultiPalettePictureFile.BytesPerScanline; ++i)
        data[recordOffset + i] = (byte)((y + i) & 0xFF);

      // Fill palette with trivial values
      var paletteOffset = recordOffset + MultiPalettePictureFile.BytesPerScanline;
      for (var i = 0; i < 16; ++i)
        BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(paletteOffset + i * 2), (short)(i * 0x111 & 0xFFF));
    }
    return data;
  }
}
