using System;
using System.IO;
using FileFormat.MacPaint;

namespace FileFormat.MacPaint.Tests;

[TestFixture]
public sealed class MacPaintReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => MacPaintReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => MacPaintReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mac"));
    Assert.Throws<FileNotFoundException>(() => MacPaintReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => MacPaintReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[10];
    Assert.Throws<InvalidDataException>(() => MacPaintReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidParsesCorrectly() {
    var bytes = _BuildValidMacPaintFile(version: 2);
    var result = MacPaintReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(576));
      Assert.That(result.Height, Is.EqualTo(720));
      Assert.That(result.Version, Is.EqualTo(2));
      Assert.That(result.BrushPatterns, Is.Not.Null);
      Assert.That(result.BrushPatterns!.Length, Is.EqualTo(304));
      Assert.That(result.PixelData.Length, Is.EqualTo(51840));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_WithMacBinaryHeader_SkipsPrefix() {
    var rawBytes = _BuildValidMacPaintFile(version: 0);

    // Prepend a 128-byte MacBinary header
    var macBinary = new byte[128 + rawBytes.Length];
    macBinary[0] = 0;  // old version
    macBinary[1] = 8;  // filename length (1..63)
    macBinary[74] = 0; // zero fill
    macBinary[82] = 0; // zero fill
    Array.Copy(rawBytes, 0, macBinary, 128, rawBytes.Length);

    var result = MacPaintReader.FromBytes(macBinary);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(576));
      Assert.That(result.Height, Is.EqualTo(720));
      Assert.That(result.Version, Is.EqualTo(0));
    });
  }

  internal static byte[] _BuildValidMacPaintFile(int version) {
    // Build a minimal valid MacPaint file: 512-byte header + PackBits-compressed pixel data
    var pixelData = new byte[51840]; // all zeros (white image)
    var compressed = PackBitsCompressor.Compress(pixelData);

    var result = new byte[512 + compressed.Length];
    // Write version as big-endian int32
    result[0] = (byte)(version >> 24);
    result[1] = (byte)(version >> 16);
    result[2] = (byte)(version >> 8);
    result[3] = (byte)version;
    // Patterns (304 bytes at offset 4) and padding (204 bytes at offset 308) are already zero
    Array.Copy(compressed, 0, result, 512, compressed.Length);

    return result;
  }
}
