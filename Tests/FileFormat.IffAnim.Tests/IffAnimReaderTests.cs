using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using FileFormat.Core;
using FileFormat.IffAnim;
using FileFormat.Ilbm;

namespace FileFormat.IffAnim.Tests;

[TestFixture]
public sealed class IffAnimReaderTests {

  private static byte[] _CreateAnimBytes(int width, int height, byte[] rgb24Pixels) {
    var rawImage = new RawImage {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgb24,
      PixelData = rgb24Pixels,
    };
    var ilbmFile = IlbmFile.FromRawImage(rawImage);
    var ilbmBytes = IlbmWriter.ToBytes(ilbmFile);

    var formDataSize = 4 + ilbmBytes.Length;
    var result = new byte[8 + formDataSize];
    Encoding.ASCII.GetBytes("FORM").CopyTo(result, 0);
    BinaryPrimitives.WriteInt32BigEndian(result.AsSpan(4), formDataSize);
    Encoding.ASCII.GetBytes("ANIM").CopyTo(result, 8);
    Array.Copy(ilbmBytes, 0, result, 12, ilbmBytes.Length);
    return result;
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => IffAnimReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => IffAnimReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".anim"));
    Assert.Throws<FileNotFoundException>(() => IffAnimReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => IffAnimReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[8];
    Assert.Throws<InvalidDataException>(() => IffAnimReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidFormTag_ThrowsInvalidDataException() {
    var data = new byte[12];
    Encoding.ASCII.GetBytes("JUNK").CopyTo(data, 0);
    BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(4), 4);
    Encoding.ASCII.GetBytes("ANIM").CopyTo(data, 8);
    Assert.Throws<InvalidDataException>(() => IffAnimReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidFormType_ThrowsInvalidDataException() {
    var data = new byte[12];
    Encoding.ASCII.GetBytes("FORM").CopyTo(data, 0);
    BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(4), 4);
    Encoding.ASCII.GetBytes("ILBM").CopyTo(data, 8);
    Assert.Throws<InvalidDataException>(() => IffAnimReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_NoIlbmFrame_ThrowsInvalidDataException() {
    // Valid FORM ANIM header but no embedded FORM ILBM
    var data = new byte[12];
    Encoding.ASCII.GetBytes("FORM").CopyTo(data, 0);
    BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(4), 4);
    Encoding.ASCII.GetBytes("ANIM").CopyTo(data, 8);
    Assert.Throws<InvalidDataException>(() => IffAnimReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidIlbmFrame_ParsesDimensions() {
    // 2x2 image with 4 pixels of RGB data
    var pixels = new byte[2 * 2 * 3];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i * 21 % 256);

    var data = _CreateAnimBytes(2, 2, pixels);
    var result = IffAnimReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(2));
    Assert.That(result.Height, Is.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidIlbmFrame_PixelDataNotEmpty() {
    var pixels = new byte[2 * 2 * 3];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i * 21 % 256);

    var data = _CreateAnimBytes(2, 2, pixels);
    var result = IffAnimReader.FromBytes(data);

    Assert.That(result.PixelData, Is.Not.Empty);
    Assert.That(result.PixelData.Length, Is.EqualTo(2 * 2 * 3));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Valid_ParsesCorrectly() {
    var pixels = new byte[2 * 1 * 3];
    pixels[0] = 0xAA;
    pixels[1] = 0xBB;
    pixels[2] = 0xCC;
    pixels[3] = 0x11;
    pixels[4] = 0x22;
    pixels[5] = 0x33;

    var data = _CreateAnimBytes(2, 1, pixels);
    using var stream = new MemoryStream(data);
    var result = IffAnimReader.FromStream(stream);

    Assert.That(result.Width, Is.EqualTo(2));
    Assert.That(result.Height, Is.EqualTo(1));
  }
}
