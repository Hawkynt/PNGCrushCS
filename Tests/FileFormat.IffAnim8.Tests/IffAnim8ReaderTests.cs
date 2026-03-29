using System;
using System.IO;
using FileFormat.IffAnim8;

namespace FileFormat.IffAnim8.Tests;

[TestFixture]
public sealed class IffAnim8ReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => IffAnim8Reader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => IffAnim8Reader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".an8"));
    Assert.Throws<FileNotFoundException>(() => IffAnim8Reader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => IffAnim8Reader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[IffAnim8File.MinFileSize - 1];
    Assert.Throws<InvalidDataException>(() => IffAnim8Reader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_MinimalValid_ReturnsDefaultDimensions() {
    var data = _CreateMinimalValidData();

    var result = IffAnim8Reader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(IffAnim8File.DefaultWidth));
      Assert.That(result.Height, Is.EqualTo(IffAnim8File.DefaultHeight));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidData_CopiesRawData() {
    var data = _CreateMinimalValidData();

    var result = IffAnim8Reader.FromBytes(data);

    Assert.That(result.RawData, Is.EqualTo(data));
    Assert.That(result.RawData, Is.Not.SameAs(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_WithBmhd_ParsesDimensions() {
    var data = _CreateDataWithBmhd(64, 48);

    var result = IffAnim8Reader.FromBytes(data);

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
    var result = IffAnim8Reader.FromStream(ms);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(16));
      Assert.That(result.Height, Is.EqualTo(32));
      Assert.That(result.RawData, Is.EqualTo(data));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_RoundTrip_PreservesData() {
    var original = new IffAnim8File {
      Width = 100,
      Height = 80,
      RawData = _CreateDataWithBmhd(100, 80),
    };

    var bytes = IffAnim8Writer.ToBytes(original);
    var restored = IffAnim8Reader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(100));
      Assert.That(restored.Height, Is.EqualTo(80));
      Assert.That(restored.RawData, Is.EqualTo(original.RawData));
    });
  }

  private static byte[] _CreateMinimalValidData() {
    var data = new byte[IffAnim8File.MinFileSize];
    for (var i = 0; i < data.Length; ++i)
      data[i] = (byte)(i & 0xFF);
    return data;
  }

  private static byte[] _CreateDataWithBmhd(int width, int height) {
    // Build a minimal IFF-like structure with a BMHD chunk
    // BMHD chunk: "BMHD" + 4-byte size (BE) + 20-byte data (width BE, height BE, ...)
    var bmhdData = new byte[20];
    bmhdData[0] = (byte)(width >> 8);
    bmhdData[1] = (byte)(width & 0xFF);
    bmhdData[2] = (byte)(height >> 8);
    bmhdData[3] = (byte)(height & 0xFF);

    // prefix + BMHD tag (4) + size (4) + data (20) + padding
    var data = new byte[12 + 4 + 4 + 20 + 10];
    var offset = 0;

    // Some IFF-like header bytes
    data[offset++] = (byte)'F';
    data[offset++] = (byte)'O';
    data[offset++] = (byte)'R';
    data[offset++] = (byte)'M';
    // size (BE) - fill in later
    offset += 4;
    // form type
    data[offset++] = (byte)'A';
    data[offset++] = (byte)'N';
    data[offset++] = (byte)'8';
    data[offset++] = (byte)' ';

    // BMHD chunk
    data[offset++] = 0x42; // 'B'
    data[offset++] = 0x4D; // 'M'
    data[offset++] = 0x48; // 'H'
    data[offset++] = 0x44; // 'D'

    // chunk size (BE) = 20
    data[offset++] = 0;
    data[offset++] = 0;
    data[offset++] = 0;
    data[offset++] = 20;

    // copy BMHD data
    Array.Copy(bmhdData, 0, data, offset, bmhdData.Length);

    return data;
  }
}
