using System;
using System.IO;
using FileFormat.Msx;

namespace FileFormat.Msx.Tests;

[TestFixture]
public sealed class MsxReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => MsxReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => MsxReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".msx"));
    Assert.Throws<FileNotFoundException>(() => MsxReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => MsxReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[100];
    Assert.Throws<InvalidDataException>(() => MsxReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_WrongSize_ThrowsInvalidDataException() {
    var wrongSize = new byte[20000];
    Assert.Throws<InvalidDataException>(() => MsxReader.FromBytes(wrongSize));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidSc2_ParsesCorrectly() {
    var data = new byte[16384];
    for (var i = 0; i < data.Length; ++i)
      data[i] = (byte)(i % 256);

    var result = MsxReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(256));
    Assert.That(result.Height, Is.EqualTo(192));
    Assert.That(result.Mode, Is.EqualTo(MsxMode.Screen2));
    Assert.That(result.BitsPerPixel, Is.EqualTo(1));
    Assert.That(result.Palette, Is.Null);
    Assert.That(result.HasBloadHeader, Is.False);
    Assert.That(result.PixelData.Length, Is.EqualTo(16384));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidSc5_ParsesCorrectly() {
    var data = new byte[26880];
    for (var i = 0; i < data.Length; ++i)
      data[i] = (byte)(i % 256);

    var result = MsxReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(256));
    Assert.That(result.Height, Is.EqualTo(212));
    Assert.That(result.Mode, Is.EqualTo(MsxMode.Screen5));
    Assert.That(result.BitsPerPixel, Is.EqualTo(4));
    Assert.That(result.Palette, Is.Not.Null);
    Assert.That(result.Palette!.Length, Is.EqualTo(32));
    Assert.That(result.HasBloadHeader, Is.False);
    Assert.That(result.PixelData.Length, Is.EqualTo(26848));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidSc8_ParsesCorrectly() {
    var data = new byte[54272];
    for (var i = 0; i < data.Length; ++i)
      data[i] = (byte)(i % 256);

    var result = MsxReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(256));
    Assert.That(result.Height, Is.EqualTo(212));
    Assert.That(result.Mode, Is.EqualTo(MsxMode.Screen8));
    Assert.That(result.BitsPerPixel, Is.EqualTo(8));
    Assert.That(result.Palette, Is.Null);
    Assert.That(result.HasBloadHeader, Is.False);
    Assert.That(result.PixelData.Length, Is.EqualTo(54272));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_WithBloadHeader_DetectsHeader() {
    var raw = new byte[16384];
    for (var i = 0; i < raw.Length; ++i)
      raw[i] = (byte)(i % 256);

    var data = new byte[7 + 16384];
    data[0] = 0xFE;
    data[1] = 0x00; // start low
    data[2] = 0x00; // start high
    data[3] = 0xFF; // end low
    data[4] = 0x3F; // end high
    data[5] = 0x00; // exec low
    data[6] = 0x00; // exec high
    Array.Copy(raw, 0, data, 7, raw.Length);

    var result = MsxReader.FromBytes(data);

    Assert.That(result.HasBloadHeader, Is.True);
    Assert.That(result.Mode, Is.EqualTo(MsxMode.Screen2));
    Assert.That(result.PixelData, Is.EqualTo(raw));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidSc2_ParsesCorrectly() {
    var data = new byte[16384];
    for (var i = 0; i < data.Length; ++i)
      data[i] = (byte)(i % 256);

    using var stream = new MemoryStream(data);
    var result = MsxReader.FromStream(stream);

    Assert.That(result.Width, Is.EqualTo(256));
    Assert.That(result.Height, Is.EqualTo(192));
    Assert.That(result.Mode, Is.EqualTo(MsxMode.Screen2));
  }
}
