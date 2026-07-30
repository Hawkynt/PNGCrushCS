using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using FileFormat.DjVu;

namespace FileFormat.DjVu.Tests;

[TestFixture]
public sealed class DjVuReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => DjVuReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => DjVuReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".djvu"));
    Assert.Throws<FileNotFoundException>(() => DjVuReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => DjVuReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[10];
    Assert.Throws<InvalidDataException>(() => DjVuReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    var data = new byte[24];
    Encoding.ASCII.GetBytes("XXXX", data.AsSpan(0));
    Encoding.ASCII.GetBytes("FORM", data.AsSpan(4));
    Assert.Throws<InvalidDataException>(() => DjVuReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidFormTag_ThrowsInvalidDataException() {
    var data = new byte[24];
    Encoding.ASCII.GetBytes("AT&T", data.AsSpan(0));
    Encoding.ASCII.GetBytes("LIST", data.AsSpan(4));
    Assert.Throws<InvalidDataException>(() => DjVuReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidFormType_ThrowsInvalidDataException() {
    var data = _BuildMinimalDjVu("XXXX", 4, 4, 300);
    // Overwrite the form type
    Encoding.ASCII.GetBytes("XXXX", data.AsSpan(12));
    Assert.Throws<InvalidDataException>(() => DjVuReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidInfo_ParsesDimensions() {
    var data = _BuildMinimalDjVu("DJVU", 320, 240, 150);
    var result = DjVuReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(320));
    Assert.That(result.Height, Is.EqualTo(240));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidInfo_ParsesDpi() {
    var data = _BuildMinimalDjVu("DJVU", 100, 100, 600);
    var result = DjVuReader.FromBytes(data);

    Assert.That(result.Dpi, Is.EqualTo(600));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidInfo_ParsesGamma() {
    var data = _BuildMinimalDjVu("DJVU", 10, 10, 300, gamma: 33);
    var result = DjVuReader.FromBytes(data);

    Assert.That(result.Gamma, Is.EqualTo(33));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidInfo_ParsesVersion() {
    var data = _BuildMinimalDjVu("DJVU", 10, 10, 300, versionMajor: 1, versionMinor: 2);
    var result = DjVuReader.FromBytes(data);

    Assert.That(result.VersionMajor, Is.EqualTo(1));
    Assert.That(result.VersionMinor, Is.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidInfo_DefaultDpiWhenZero() {
    var data = _BuildMinimalDjVu("DJVU", 10, 10, 0);
    var result = DjVuReader.FromBytes(data);

    Assert.That(result.Dpi, Is.EqualTo(300));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Valid_ParsesCorrectly() {
    var data = _BuildMinimalDjVu("DJVU", 64, 48, 200);
    using var ms = new MemoryStream(data);
    var result = DjVuReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(64));
    Assert.That(result.Height, Is.EqualTo(48));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_MissingInfoChunk_ThrowsInvalidDataException() {
    // Build a file with AT&T + FORM + DJVU but no INFO chunk
    using var ms = new MemoryStream();
    ms.Write(Encoding.ASCII.GetBytes("AT&T"));
    ms.Write(Encoding.ASCII.GetBytes("FORM"));
    var sizeBytes = new byte[4];
    BinaryPrimitives.WriteUInt32BigEndian(sizeBytes, 12); // DJVU(4) + chunk(8)
    ms.Write(sizeBytes);
    ms.Write(Encoding.ASCII.GetBytes("DJVU"));
    // Write a non-INFO chunk
    ms.Write(Encoding.ASCII.GetBytes("BG44"));
    BinaryPrimitives.WriteUInt32BigEndian(sizeBytes, 0);
    ms.Write(sizeBytes);

    Assert.Throws<InvalidDataException>(() => DjVuReader.FromBytes(ms.ToArray()));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_PixelDataBlankWhenNoPM44() {
    var data = _BuildMinimalDjVu("DJVU", 2, 2, 300);
    var result = DjVuReader.FromBytes(data);

    Assert.That(result.PixelData.Length, Is.EqualTo(2 * 2 * 3));
    Assert.That(result.PixelData, Is.All.EqualTo(0));
  }

  /// <summary>Builds a minimal valid DjVu file with just an INFO chunk.</summary>
  private static byte[] _BuildMinimalDjVu(
    string formType,
    int width,
    int height,
    int dpi,
    byte gamma = 22,
    byte versionMajor = 0,
    byte versionMinor = 26,
    byte flags = 0
  ) {
    using var ms = new MemoryStream();

    // AT&T magic
    ms.Write(Encoding.ASCII.GetBytes("AT&T"));

    // FORM tag
    ms.Write(Encoding.ASCII.GetBytes("FORM"));

    // FORM size placeholder
    var formSizeOffset = (int)ms.Position;
    ms.Write(new byte[4]);

    // Form type
    ms.Write(Encoding.ASCII.GetBytes(formType));

    // INFO chunk
    ms.Write(Encoding.ASCII.GetBytes("INFO"));
    var sizeBytes = new byte[4];
    BinaryPrimitives.WriteUInt32BigEndian(sizeBytes, 10);
    ms.Write(sizeBytes);

    // INFO data: width(2 LE), height(2 LE), version minor(1), version major(1), dpi(2 LE), gamma(1), flags(1)
    var info = new byte[10];
    BinaryPrimitives.WriteUInt16LittleEndian(info.AsSpan(0), (ushort)width);
    BinaryPrimitives.WriteUInt16LittleEndian(info.AsSpan(2), (ushort)height);
    info[4] = versionMinor;
    info[5] = versionMajor;
    BinaryPrimitives.WriteUInt16LittleEndian(info.AsSpan(6), (ushort)dpi);
    info[8] = gamma;
    info[9] = flags;
    ms.Write(info);

    // Patch FORM size
    var result = ms.ToArray();
    var formSize = result.Length - 12;
    BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(formSizeOffset), (uint)formSize);

    return result;
  }
}
