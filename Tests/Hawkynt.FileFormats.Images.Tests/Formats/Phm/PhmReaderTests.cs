using System;
using System.IO;
using System.Text;
using FileFormat.Phm;

namespace FileFormat.Phm.Tests;

[TestFixture]
public sealed class PhmReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => PhmReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => PhmReader.FromFile(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".phm"));
    Assert.Throws<FileNotFoundException>(() => PhmReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => PhmReader.FromStream(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[3];
    Assert.Throws<InvalidDataException>(() => PhmReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    var bad = Encoding.ASCII.GetBytes("XX\n2 2\n-1.0\n");
    var data = new byte[bad.Length + 2 * 2 * 3 * 2];
    Array.Copy(bad, data, bad.Length);
    Assert.Throws<InvalidDataException>(() => PhmReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidRgb_ParsesCorrectly() {
    var phm = _BuildMinimalRgbPhm(2, 2);
    var result = PhmReader.FromBytes(phm);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(2));
      Assert.That(result.Height, Is.EqualTo(2));
      Assert.That(result.ColorMode, Is.EqualTo(PhmColorMode.Rgb));
      Assert.That(result.IsLittleEndian, Is.True);
      Assert.That(result.Scale, Is.EqualTo(1.0f));
      Assert.That(result.PixelData, Has.Length.EqualTo(2 * 2 * 3));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidGrayscale_ParsesCorrectly() {
    var phm = _BuildMinimalGrayscalePhm(3, 2);
    var result = PhmReader.FromBytes(phm);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(3));
      Assert.That(result.Height, Is.EqualTo(2));
      Assert.That(result.ColorMode, Is.EqualTo(PhmColorMode.Grayscale));
      Assert.That(result.IsLittleEndian, Is.True);
      Assert.That(result.PixelData, Has.Length.EqualTo(3 * 2));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_DataTooSmall_ThrowsInvalidDataException() {
    var header = Encoding.ASCII.GetBytes("PH\n100 100\n-1.0\n");
    Assert.Throws<InvalidDataException>(() => PhmReader.FromBytes(header));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ViaStream_Equivalent() {
    var data = _BuildMinimalGrayscalePhm(2, 2);
    var fromBytes = PhmReader.FromBytes(data);

    using var ms = new MemoryStream(data);
    var fromStream = PhmReader.FromStream(ms);

    Assert.Multiple(() => {
      Assert.That(fromStream.Width, Is.EqualTo(fromBytes.Width));
      Assert.That(fromStream.Height, Is.EqualTo(fromBytes.Height));
      Assert.That(fromStream.ColorMode, Is.EqualTo(fromBytes.ColorMode));
    });
  }

  private static byte[] _BuildMinimalRgbPhm(int width, int height) {
    var header = Encoding.ASCII.GetBytes($"PH\n{width} {height}\n-1.0\n");
    var halfCount = width * height * 3;
    var data = new byte[header.Length + halfCount * 2];
    Array.Copy(header, data, header.Length);
    for (var i = 0; i < halfCount; ++i) {
      var h = (Half)(i * 0.01f);
      var bytes = BitConverter.GetBytes(h);
      var offset = header.Length + i * 2;
      data[offset] = bytes[0];
      data[offset + 1] = bytes[1];
    }

    return data;
  }

  private static byte[] _BuildMinimalGrayscalePhm(int width, int height) {
    var header = Encoding.ASCII.GetBytes($"Ph\n{width} {height}\n-1.0\n");
    var halfCount = width * height;
    var data = new byte[header.Length + halfCount * 2];
    Array.Copy(header, data, header.Length);
    for (var i = 0; i < halfCount; ++i) {
      var h = (Half)(i * 0.1f);
      var bytes = BitConverter.GetBytes(h);
      var offset = header.Length + i * 2;
      data[offset] = bytes[0];
      data[offset + 1] = bytes[1];
    }

    return data;
  }
}
