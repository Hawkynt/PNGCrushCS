using System;
using System.IO;
using FileFormat.ArtDirector;

namespace FileFormat.ArtDirector.Tests;

[TestFixture]
public sealed class ArtDirectorReaderTests {

  private const int _EXPECTED_SIZE = 32128;

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => ArtDirectorReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => ArtDirectorReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".art"));
    Assert.Throws<FileNotFoundException>(() => ArtDirectorReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => ArtDirectorReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[100];
    Assert.Throws<InvalidDataException>(() => ArtDirectorReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooLarge_ThrowsInvalidDataException() {
    var tooLarge = new byte[_EXPECTED_SIZE + 1];
    Assert.Throws<InvalidDataException>(() => ArtDirectorReader.FromBytes(tooLarge));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ExactSize_Parses() {
    var data = new byte[_EXPECTED_SIZE];
    // Set palette entry 0 to 0x0777 (white in ST)
    // resolution=0 (low res)
    data[0] = 0x00; data[1] = 0x00;
    // Set palette entry 0 at offset 2
    data[2] = 0x07;
    data[3] = 0x77;

    var result = ArtDirectorReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(320));
    Assert.That(result.Height, Is.EqualTo(200));
    Assert.That(result.Palette.Length, Is.EqualTo(16));
    Assert.That(result.Palette[0], Is.EqualTo(0x0777));
    Assert.That(result.PixelData.Length, Is.EqualTo(32000));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Valid() {
    var data = new byte[_EXPECTED_SIZE];
    data[0] = 0x00; data[1] = 0x00; // resolution=0
    data[2] = 0x03;
    data[3] = 0x45;

    using var ms = new MemoryStream(data);
    var result = ArtDirectorReader.FromStream(ms);

    Assert.That(result.Palette[0], Is.EqualTo(0x0345));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_CopiesPixelData() {
    var data = new byte[_EXPECTED_SIZE];
    data[128] = 0x42; // First byte of pixel data

    var result = ArtDirectorReader.FromBytes(data);
    data[128] = 0x00;

    Assert.That(result.PixelData[0], Is.EqualTo(0x42));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ProducesIndexed8() {
    var file = new ArtDirectorFile {
      Palette = new short[16],
      PixelData = new byte[32000]
    };

    var raw = ArtDirectorFile.ToRawImage(file);

    Assert.That(raw.Width, Is.EqualTo(320));
    Assert.That(raw.Height, Is.EqualTo(200));
    Assert.That(raw.Format, Is.EqualTo(FileFormat.Core.PixelFormat.Indexed8));
    Assert.That(raw.PaletteCount, Is.EqualTo(16));
  }
  [Test]
  [Category("Integration")]
  public void RoundTrip_PaletteAndDataPreserved() {
    var data = new byte[_EXPECTED_SIZE];
    // Set a palette entry
    data[0] = 0x00; data[3] = 0x00; // resolution=0
    data[2] = 0x07;
    data[3] = 0x00;
    // Set some pixel data
    data[128] = 0xAA;
    data[_EXPECTED_SIZE - 1] = 0xBB;

    var original = ArtDirectorReader.FromBytes(data);
    var bytes = ArtDirectorWriter.ToBytes(original);
    var restored = ArtDirectorReader.FromBytes(bytes);

    Assert.That(restored.Palette[0], Is.EqualTo(original.Palette[0]));
    Assert.That(restored.PixelData[0], Is.EqualTo(original.PixelData[0]));
    Assert.That(restored.PixelData[^1], Is.EqualTo(original.PixelData[^1]));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var data = new byte[_EXPECTED_SIZE];
    data[0] = 0x00; data[1] = 0x00; // resolution=0
    for (var i = 2; i < 34; ++i)
      data[i] = (byte)(i * 3 % 256);
    for (var i = 128; i < _EXPECTED_SIZE; ++i)
      data[i] = (byte)(i * 7 % 256);

    var original = ArtDirectorReader.FromBytes(data);
    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".art");
    try {
      File.WriteAllBytes(tempPath, ArtDirectorWriter.ToBytes(original));
      var restored = ArtDirectorReader.FromFile(new FileInfo(tempPath));

      Assert.That(restored.Palette, Is.EqualTo(original.Palette));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }

  [Test]
  [Category("Unit")]
  public void WriterToBytes_ProducesExpectedSize() {
    var file = new ArtDirectorFile {
      Palette = new short[16],
      PixelData = new byte[32000]
    };

    var bytes = ArtDirectorWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(_EXPECTED_SIZE));
  }

  [Test]
  [Category("Unit")]
  public void DataType_DefaultPixelData_IsEmpty() {
    var file = new ArtDirectorFile();
    Assert.That(file.PixelData, Is.Not.Null);
    Assert.That(file.PixelData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void DataType_DefaultPalette_Has16Entries() {
    var file = new ArtDirectorFile();
    Assert.That(file.Palette.Length, Is.EqualTo(16));
  }

  [Test]
  [Category("Unit")]
  public void DataType_FixedDimensions() {
    var file = new ArtDirectorFile();
    Assert.That(file.Width, Is.EqualTo(320));
    Assert.That(file.Height, Is.EqualTo(200));
  }

  [Test]
  [Category("Unit")]
  public void DataType_ExpectedFileSize_Is32128() {
    Assert.That(ArtDirectorFile.ExpectedFileSize, Is.EqualTo(32128));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidResolution_ThrowsInvalidDataException() {
    var data = new byte[_EXPECTED_SIZE];
    data[0] = 0x00; data[1] = 0x03;
    Assert.Throws<InvalidDataException>(() => ArtDirectorReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_MedRes_Parses() {
    var data = new byte[_EXPECTED_SIZE];
    data[0] = 0x00; data[1] = 0x01;

    var result = ArtDirectorReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(640));
    Assert.That(result.Height, Is.EqualTo(200));
    Assert.That(result.Resolution, Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_HighRes_Parses() {
    var data = new byte[_EXPECTED_SIZE];
    data[0] = 0x00; data[1] = 0x02;

    var result = ArtDirectorReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(640));
    Assert.That(result.Height, Is.EqualTo(400));
    Assert.That(result.Resolution, Is.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void WriterToBytes_ResolutionInHeader() {
    var file = new ArtDirectorFile {
      Resolution = 1,
      Palette = new short[16],
      PixelData = new byte[32000]
    };

    var bytes = ArtDirectorWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo(0x00));
    Assert.That(bytes[1], Is.EqualTo(0x01));
  }

  [Test]
  [Category("Unit")]
  public void DataType_HeaderSize_Is128() {
    Assert.That(ArtDirectorFile.HeaderSize, Is.EqualTo(128));
  }
}
