using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Emf;

namespace FileFormat.Emf.Tests;

[TestFixture]
public sealed class EmfReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => EmfReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => EmfReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".emf"));
    Assert.Throws<FileNotFoundException>(() => EmfReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => EmfReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[40];
    Assert.Throws<InvalidDataException>(() => EmfReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidSignature_ThrowsInvalidDataException() {
    var data = _BuildMinimalEmf(2, 1);
    // Corrupt the " EMF" signature at offset 40
    data[40] = 0xFF;
    data[41] = 0xFF;
    data[42] = 0xFF;
    data[43] = 0xFF;
    Assert.Throws<InvalidDataException>(() => EmfReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_NoStretchDiBits_ThrowsInvalidDataException() {
    // Build a minimal EMF with only header + EOF, no StretchDIBits
    var headerSize = 88;
    var eofSize = 20;
    var totalSize = headerSize + eofSize;
    var data = new byte[totalSize];
    var span = data.AsSpan();

    // EMR_HEADER
    BinaryPrimitives.WriteUInt32LittleEndian(span, 1); // type
    BinaryPrimitives.WriteUInt32LittleEndian(span[4..], (uint)headerSize);
    BinaryPrimitives.WriteUInt32LittleEndian(span[40..], 0x464D4520); // " EMF"
    BinaryPrimitives.WriteUInt32LittleEndian(span[44..], 0x00010000); // version
    BinaryPrimitives.WriteUInt32LittleEndian(span[48..], (uint)totalSize);
    BinaryPrimitives.WriteUInt32LittleEndian(span[52..], 2); // 2 records

    // EMR_EOF
    BinaryPrimitives.WriteUInt32LittleEndian(span[headerSize..], 14); // type
    BinaryPrimitives.WriteUInt32LittleEndian(span[(headerSize + 4)..], (uint)eofSize);
    BinaryPrimitives.WriteUInt32LittleEndian(span[(headerSize + 16)..], (uint)eofSize);

    Assert.Throws<InvalidDataException>(() => EmfReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidWithEmbeddedDib_ParsesCorrectly() {
    var data = _BuildMinimalEmf(2, 1);
    var result = EmfReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(2));
    Assert.That(result.Height, Is.EqualTo(1));
    Assert.That(result.PixelData.Length, Is.EqualTo(2 * 1 * 3));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidDib_PixelDataPreserved() {
    // Build a 1x1 EMF with known pixel color (R=0xAA, G=0xBB, B=0xCC)
    var data = _BuildMinimalEmfWithColor(1, 1, 0xAA, 0xBB, 0xCC);
    var result = EmfReader.FromBytes(data);

    Assert.That(result.PixelData[0], Is.EqualTo(0xAA)); // R
    Assert.That(result.PixelData[1], Is.EqualTo(0xBB)); // G
    Assert.That(result.PixelData[2], Is.EqualTo(0xCC)); // B
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Valid_ParsesCorrectly() {
    var data = _BuildMinimalEmf(2, 2);
    using var ms = new MemoryStream(data);
    var result = EmfReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(2));
    Assert.That(result.Height, Is.EqualTo(2));
  }

  /// <summary>Builds a minimal valid EMF with an EMR_STRETCHDIBITS record containing an all-zero RGB24 DIB.</summary>
  private static byte[] _BuildMinimalEmf(int width, int height) => EmfWriter.Assemble(new byte[width * height * 3], width, height);

  /// <summary>Builds a minimal valid EMF with a 1x1 pixel of the specified color.</summary>
  private static byte[] _BuildMinimalEmfWithColor(int width, int height, byte r, byte g, byte b) {
    var pixels = new byte[width * height * 3];
    for (var i = 0; i < pixels.Length; i += 3) {
      pixels[i] = r;
      pixels[i + 1] = g;
      pixels[i + 2] = b;
    }

    return EmfWriter.Assemble(pixels, width, height);
  }
}
