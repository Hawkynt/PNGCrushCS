using System;
using System.IO;
using FileFormat.BigTiff;

namespace FileFormat.BigTiff.Tests;

[TestFixture]
public sealed class BigTiffReaderTests {

  [Test]
  [Category("Unit")]
  public void FromFile_NullFile_Throws()
    => Assert.Throws<ArgumentNullException>(() => BigTiffReader.FromFile(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_MissingFile_Throws()
    => Assert.Throws<FileNotFoundException>(() => BigTiffReader.FromFile(new FileInfo("nonexistent.btf")));

  [Test]
  [Category("Unit")]
  public void FromBytes_NullData_Throws()
    => Assert.Throws<ArgumentNullException>(() => BigTiffReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_Throws()
    => Assert.Throws<InvalidDataException>(() => BigTiffReader.FromBytes(new byte[8]));

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidByteOrder_Throws()
    => Assert.Throws<InvalidDataException>(() => BigTiffReader.FromBytes(new byte[] {
      0xAA, 0xBB, 0x2B, 0x00, 0x08, 0x00, 0x00, 0x00,
      0x10, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    }));

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidVersion_Throws()
    => Assert.Throws<InvalidDataException>(() => BigTiffReader.FromBytes(new byte[] {
      0x49, 0x49, 0x2A, 0x00, 0x08, 0x00, 0x00, 0x00,
      0x10, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    }));

  [Test]
  [Category("Unit")]
  public void FromStream_NullStream_Throws()
    => Assert.Throws<ArgumentNullException>(() => BigTiffReader.FromStream(null!));

  [Test]
  [Category("Integration")]
  public void FromStream_ValidData_Parses() {
    var data = BigTiffWriter.ToBytes(new BigTiffFile {
      Width = 4, Height = 2, SamplesPerPixel = 1, BitsPerSample = 8,
      PhotometricInterpretation = BigTiffFile.PhotometricMinIsBlack,
      PixelData = new byte[8],
    });
    using var ms = new MemoryStream(data);
    var file = BigTiffReader.FromStream(ms);
    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(4));
      Assert.That(file.Height, Is.EqualTo(2));
      Assert.That(file.SamplesPerPixel, Is.EqualTo(1));
    });
  }

  [Test]
  [Category("Integration")]
  public void FromBytes_Gray8_ParsesPixelData() {
    var pixels = new byte[] { 10, 20, 30, 40 };
    var data = BigTiffWriter.ToBytes(new BigTiffFile {
      Width = 2, Height = 2, SamplesPerPixel = 1, BitsPerSample = 8,
      PhotometricInterpretation = BigTiffFile.PhotometricMinIsBlack,
      PixelData = pixels,
    });
    var file = BigTiffReader.FromBytes(data);
    Assert.That(file.PixelData, Is.EqualTo(pixels));
  }

  [Test]
  [Category("Integration")]
  public void FromBytes_Rgb24_ParsesDimensions() {
    var data = BigTiffWriter.ToBytes(new BigTiffFile {
      Width = 3, Height = 2, SamplesPerPixel = 3, BitsPerSample = 8,
      PhotometricInterpretation = BigTiffFile.PhotometricRgb,
      PixelData = new byte[18],
    });
    var file = BigTiffReader.FromBytes(data);
    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(3));
      Assert.That(file.Height, Is.EqualTo(2));
      Assert.That(file.SamplesPerPixel, Is.EqualTo(3));
      Assert.That(file.PhotometricInterpretation, Is.EqualTo(BigTiffFile.PhotometricRgb));
    });
  }

  [Test]
  [Category("Integration")]
  public void FromBytes_Rgb24_ParsesPixelData() {
    var pixels = new byte[12];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i * 17 % 256);
    var data = BigTiffWriter.ToBytes(new BigTiffFile {
      Width = 2, Height = 2, SamplesPerPixel = 3, BitsPerSample = 8,
      PhotometricInterpretation = BigTiffFile.PhotometricRgb,
      PixelData = pixels,
    });
    var file = BigTiffReader.FromBytes(data);
    Assert.That(file.PixelData, Is.EqualTo(pixels));
  }

  [Test]
  [Category("Integration")]
  public void FromBytes_WrittenAsLE_IsBigEndianIsFalse() {
    var data = BigTiffWriter.ToBytes(new BigTiffFile {
      Width = 2, Height = 2, SamplesPerPixel = 1, BitsPerSample = 8,
      PhotometricInterpretation = BigTiffFile.PhotometricMinIsBlack,
      PixelData = new byte[4],
    });
    var file = BigTiffReader.FromBytes(data);
    Assert.That(file.IsBigEndian, Is.False);
  }

  [Test]
  [Category("Integration")]
  public void FromBytes_CopiesPixelData() {
    var pixels = new byte[] { 55, 66, 77, 88 };
    var data = BigTiffWriter.ToBytes(new BigTiffFile {
      Width = 2, Height = 2, SamplesPerPixel = 1, BitsPerSample = 8,
      PhotometricInterpretation = BigTiffFile.PhotometricMinIsBlack,
      PixelData = pixels,
    });
    var file = BigTiffReader.FromBytes(data);
    data[data.Length - 1] = 0;
    Assert.That(file.PixelData[3], Is.EqualTo(88));
  }
}
