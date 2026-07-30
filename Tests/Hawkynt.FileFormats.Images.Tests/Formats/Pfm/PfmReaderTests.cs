using System;
using System.IO;
using System.Text;
using FileFormat.Pfm;

namespace FileFormat.Pfm.Tests;

[TestFixture]
public sealed class PfmReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PfmReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PfmReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pfm"));
    Assert.Throws<FileNotFoundException>(() => PfmReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PfmReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[3];
    Assert.Throws<InvalidDataException>(() => PfmReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    var bad = Encoding.ASCII.GetBytes("XX\n2 2\n-1.0\n");
    var data = new byte[bad.Length + 2 * 2 * 3 * 4];
    Array.Copy(bad, data, bad.Length);
    Assert.Throws<InvalidDataException>(() => PfmReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidRgb_ParsesCorrectly() {
    var pfm = _BuildMinimalRgbPfm(2, 2);
    var result = PfmReader.FromBytes(pfm);

    Assert.That(result.Width, Is.EqualTo(2));
    Assert.That(result.Height, Is.EqualTo(2));
    Assert.That(result.ColorMode, Is.EqualTo(PfmColorMode.Rgb));
    Assert.That(result.IsLittleEndian, Is.True);
    Assert.That(result.Scale, Is.EqualTo(1.0f));
    Assert.That(result.PixelData, Has.Length.EqualTo(2 * 2 * 3));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidGrayscale_ParsesCorrectly() {
    var pfm = _BuildMinimalGrayscalePfm(3, 2);
    var result = PfmReader.FromBytes(pfm);

    Assert.That(result.Width, Is.EqualTo(3));
    Assert.That(result.Height, Is.EqualTo(2));
    Assert.That(result.ColorMode, Is.EqualTo(PfmColorMode.Grayscale));
    Assert.That(result.IsLittleEndian, Is.True);
    Assert.That(result.PixelData, Has.Length.EqualTo(3 * 2));
  }

  private static byte[] _BuildMinimalRgbPfm(int width, int height) {
    var header = Encoding.ASCII.GetBytes($"PF\n{width} {height}\n-1.0\n");
    var floatCount = width * height * 3;
    var data = new byte[header.Length + floatCount * 4];
    Array.Copy(header, data, header.Length);

    // Fill with recognizable float values (bottom-to-top row order in file)
    for (var row = height - 1; row >= 0; --row)
      for (var x = 0; x < width * 3; ++x) {
        var value = (row * width * 3 + x) * 0.1f;
        var bytes = BitConverter.GetBytes(value);
        var fileRow = height - 1 - row;
        var offset = header.Length + (fileRow * width * 3 + x) * 4;
        Array.Copy(bytes, 0, data, offset, 4);
      }

    return data;
  }

  private static byte[] _BuildMinimalGrayscalePfm(int width, int height) {
    var header = Encoding.ASCII.GetBytes($"Pf\n{width} {height}\n-1.0\n");
    var floatCount = width * height;
    var data = new byte[header.Length + floatCount * 4];
    Array.Copy(header, data, header.Length);

    for (var row = height - 1; row >= 0; --row)
      for (var x = 0; x < width; ++x) {
        var value = (row * width + x) * 0.5f;
        var bytes = BitConverter.GetBytes(value);
        var fileRow = height - 1 - row;
        var offset = header.Length + (fileRow * width + x) * 4;
        Array.Copy(bytes, 0, data, offset, 4);
      }

    return data;
  }
}
