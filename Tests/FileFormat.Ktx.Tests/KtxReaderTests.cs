using System;
using System.IO;
using FileFormat.Ktx;

namespace FileFormat.Ktx.Tests;

[TestFixture]
public sealed class KtxReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => KtxReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => KtxReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".ktx"));
    Assert.Throws<FileNotFoundException>(() => KtxReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => KtxReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[4];
    Assert.Throws<InvalidDataException>(() => KtxReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidIdentifier_ThrowsInvalidDataException() {
    var bad = new byte[80];
    bad[0] = 0xFF;
    Assert.Throws<InvalidDataException>(() => KtxReader.FromBytes(bad));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidKtx1_ParsesCorrectly() {
    var data = _BuildMinimalKtx1(4, 4, new byte[64]);
    var result = KtxReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(4));
    Assert.That(result.Height, Is.EqualTo(4));
    Assert.That(result.Version, Is.EqualTo(KtxVersion.Ktx1));
    Assert.That(result.MipLevels, Has.Count.EqualTo(1));
    Assert.That(result.MipLevels[0].Data, Has.Length.EqualTo(64));
  }

  private static byte[] _BuildMinimalKtx1(int width, int height, byte[] pixelData) {
    using var ms = new MemoryStream();
    using var bw = new BinaryWriter(ms);

    // Identifier (12 bytes)
    bw.Write(KtxHeader.Identifier);
    // Endianness
    bw.Write(KtxHeader.EndiannessLE);
    // GlType
    bw.Write(0x1401); // GL_UNSIGNED_BYTE
    // GlTypeSize
    bw.Write(1);
    // GlFormat
    bw.Write(0x1908); // GL_RGBA
    // GlInternalFormat
    bw.Write(0x8058); // GL_RGBA8
    // GlBaseInternalFormat
    bw.Write(0x1908); // GL_RGBA
    // PixelWidth
    bw.Write(width);
    // PixelHeight
    bw.Write(height);
    // PixelDepth
    bw.Write(0);
    // NumberOfArrayElements
    bw.Write(0);
    // NumberOfFaces
    bw.Write(1);
    // NumberOfMipmapLevels
    bw.Write(1);
    // BytesOfKeyValueData
    bw.Write(0);

    // Mip level 0
    bw.Write(pixelData.Length);
    bw.Write(pixelData);

    return ms.ToArray();
  }
}
