using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.JpegXl;

namespace FileFormat.JpegXl.Tests;

[TestFixture]
public sealed class JpegXlReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => JpegXlReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => JpegXlReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".jxl"));
    Assert.Throws<FileNotFoundException>(() => JpegXlReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => JpegXlReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[2];
    Assert.Throws<InvalidDataException>(() => JpegXlReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    var bad = new byte[20];
    bad[0] = 0x00;
    bad[1] = 0x00;
    Assert.Throws<InvalidDataException>(() => JpegXlReader.FromBytes(bad));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidContainer_ParsesDimensions() {
    var data = TestHelper.BuildMinimalJxlContainer(8, 16, 3);
    var result = JpegXlReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(8));
      Assert.That(result.Height, Is.EqualTo(16));
      Assert.That(result.ComponentCount, Is.EqualTo(3));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidContainer_ParsesBrand() {
    var data = TestHelper.BuildMinimalJxlContainer(4, 4, 3);
    var result = JpegXlReader.FromBytes(data);
    Assert.That(result.Brand, Is.EqualTo("jxl "));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_BareCodestream_ParsesDimensions() {
    var data = TestHelper.BuildBareCodestream(10, 20, 3);
    var result = JpegXlReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(10));
      Assert.That(result.Height, Is.EqualTo(20));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidContainer_ParsesCorrectly() {
    var data = TestHelper.BuildMinimalJxlContainer(4, 8, 1);
    using var ms = new MemoryStream(data);
    var result = JpegXlReader.FromStream(ms);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(4));
      Assert.That(result.Height, Is.EqualTo(8));
      Assert.That(result.ComponentCount, Is.EqualTo(1));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_GrayscaleComponent_ParsesCorrectly() {
    var data = TestHelper.BuildMinimalJxlContainer(2, 2, 1);
    var result = JpegXlReader.FromBytes(data);
    Assert.That(result.ComponentCount, Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_PixelDataPreserved() {
    var pixels = new byte[] { 10, 20, 30, 40, 50, 60 };
    var data = TestHelper.BuildMinimalJxlContainer(2, 1, 3, pixels);
    var result = JpegXlReader.FromBytes(data);
    Assert.That(result.PixelData, Is.EqualTo(pixels));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_NoJxlcBox_ThrowsInvalidDataException() {
    // Build a valid ftyp box with "jxl " brand but no jxlc/jxlp box after it
    var ftypPayload = new byte[12]; // brand + minor_version + compatible brand
    ftypPayload[0] = (byte)'j'; ftypPayload[1] = (byte)'x'; ftypPayload[2] = (byte)'l'; ftypPayload[3] = (byte)' ';
    ftypPayload[8] = (byte)'j'; ftypPayload[9] = (byte)'x'; ftypPayload[10] = (byte)'l'; ftypPayload[11] = (byte)' ';
    var ftypBoxSize = 8 + ftypPayload.Length;

    var data = new byte[ftypBoxSize];
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(0, 4), (uint)ftypBoxSize);
    data[4] = (byte)'f'; data[5] = (byte)'t'; data[6] = (byte)'y'; data[7] = (byte)'p';
    Array.Copy(ftypPayload, 0, data, 8, ftypPayload.Length);

    Assert.Throws<InvalidDataException>(() => JpegXlReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidBrand_ThrowsInvalidDataException() {
    // Build an ftyp box with a wrong brand
    var data = new byte[20];
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(0, 4), 20u);
    data[4] = (byte)'f'; data[5] = (byte)'t'; data[6] = (byte)'y'; data[7] = (byte)'p';
    data[8] = (byte)'m'; data[9] = (byte)'p'; data[10] = (byte)'4'; data[11] = (byte)' ';

    Assert.Throws<InvalidDataException>(() => JpegXlReader.FromBytes(data));
  }
}
