using System;
using System.IO;
using FileFormat.IffMultiPalette;

namespace FileFormat.IffMultiPalette.Tests;

[TestFixture]
public sealed class IffMultiPaletteReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => IffMultiPaletteReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => IffMultiPaletteReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mpl"));
    Assert.Throws<FileNotFoundException>(() => IffMultiPaletteReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => IffMultiPaletteReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[IffMultiPaletteFile.MinFileSize - 1];
    Assert.Throws<InvalidDataException>(() => IffMultiPaletteReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_MinimalValid_ReturnsDefaultDimensions() {
    var data = _CreateMinimalValidData();

    var result = IffMultiPaletteReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(IffMultiPaletteFile.DefaultWidth));
      Assert.That(result.Height, Is.EqualTo(IffMultiPaletteFile.DefaultHeight));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidData_CopiesRawData() {
    var data = _CreateMinimalValidData();

    var result = IffMultiPaletteReader.FromBytes(data);

    Assert.That(result.RawData, Is.EqualTo(data));
    Assert.That(result.RawData, Is.Not.SameAs(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_WithBmhd_ParsesDimensions() {
    var data = _CreateDataWithBmhd(64, 48);

    var result = IffMultiPaletteReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(64));
      Assert.That(result.Height, Is.EqualTo(48));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidData_ParsesCorrectly() {
    var data = _CreateDataWithBmhd(16, 32);

    using var ms = new MemoryStream(data);
    var result = IffMultiPaletteReader.FromStream(ms);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(16));
      Assert.That(result.Height, Is.EqualTo(32));
      Assert.That(result.RawData, Is.EqualTo(data));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_RoundTrip_PreservesData() {
    var original = new IffMultiPaletteFile {
      Width = 100,
      Height = 80,
      RawData = _CreateDataWithBmhd(100, 80),
    };

    var bytes = IffMultiPaletteWriter.ToBytes(original);
    var restored = IffMultiPaletteReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(100));
      Assert.That(restored.Height, Is.EqualTo(80));
      Assert.That(restored.RawData, Is.EqualTo(original.RawData));
    });
  }

  private static byte[] _CreateMinimalValidData() {
    var data = new byte[IffMultiPaletteFile.MinFileSize];
    for (var i = 0; i < data.Length; ++i)
      data[i] = (byte)(i & 0xFF);
    return data;
  }

  private static byte[] _CreateDataWithBmhd(int width, int height) {
    var bmhdData = new byte[20];
    bmhdData[0] = (byte)(width >> 8);
    bmhdData[1] = (byte)(width & 0xFF);
    bmhdData[2] = (byte)(height >> 8);
    bmhdData[3] = (byte)(height & 0xFF);

    var data = new byte[12 + 4 + 4 + 20 + 10];
    var offset = 0;

    data[offset++] = (byte)'F';
    data[offset++] = (byte)'O';
    data[offset++] = (byte)'R';
    data[offset++] = (byte)'M';
    offset += 4;
    data[offset++] = (byte)'M';
    data[offset++] = (byte)'P';
    data[offset++] = (byte)'A';
    data[offset++] = (byte)'L';

    data[offset++] = 0x42; // 'B'
    data[offset++] = 0x4D; // 'M'
    data[offset++] = 0x48; // 'H'
    data[offset++] = 0x44; // 'D'

    data[offset++] = 0;
    data[offset++] = 0;
    data[offset++] = 0;
    data[offset++] = 20;

    Array.Copy(bmhdData, 0, data, offset, bmhdData.Length);

    return data;
  }
}
