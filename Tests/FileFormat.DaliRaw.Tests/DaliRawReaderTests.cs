using System;
using System.IO;
using FileFormat.DaliRaw;

namespace FileFormat.DaliRaw.Tests;

[TestFixture]
public sealed class DaliRawReaderTests {

  private const int _EXPECTED_SIZE = 32034;

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => DaliRawReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => DaliRawReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".sd0"));
    Assert.Throws<FileNotFoundException>(() => DaliRawReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => DaliRawReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[100];
    Assert.Throws<InvalidDataException>(() => DaliRawReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooLarge_ThrowsInvalidDataException() {
    var tooLarge = new byte[_EXPECTED_SIZE + 1];
    Assert.Throws<InvalidDataException>(() => DaliRawReader.FromBytes(tooLarge));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ExactSize_Parses() {
    var data = new byte[_EXPECTED_SIZE];
    // Set palette entry 0 to 0x0777 (white in ST)
    data[0] = 0x07;
    data[1] = 0x77;

    var result = DaliRawReader.FromBytes(data);

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
    data[0] = 0x03;
    data[1] = 0x45;

    using var ms = new MemoryStream(data);
    var result = DaliRawReader.FromStream(ms);

    Assert.That(result.Palette[0], Is.EqualTo(0x0345));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_CopiesPixelData() {
    var data = new byte[_EXPECTED_SIZE];
    data[34] = 0x42; // First byte of pixel data (after 32 palette + 2 padding)

    var result = DaliRawReader.FromBytes(data);
    data[34] = 0x00;

    Assert.That(result.PixelData[0], Is.EqualTo(0x42));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ProducesIndexed8() {
    var file = new DaliRawFile {
      Palette = new short[16],
      PixelData = new byte[32000]
    };

    var raw = DaliRawFile.ToRawImage(file);

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
    data[0] = 0x07;
    data[1] = 0x00;
    // Set some pixel data
    data[34] = 0xAA;
    data[_EXPECTED_SIZE - 1] = 0xBB;

    var original = DaliRawReader.FromBytes(data);
    var bytes = DaliRawWriter.ToBytes(original);
    var restored = DaliRawReader.FromBytes(bytes);

    Assert.That(restored.Palette[0], Is.EqualTo(original.Palette[0]));
    Assert.That(restored.PixelData[0], Is.EqualTo(original.PixelData[0]));
    Assert.That(restored.PixelData[^1], Is.EqualTo(original.PixelData[^1]));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var data = new byte[_EXPECTED_SIZE];
    for (var i = 0; i < 32; ++i)
      data[i] = (byte)(i * 3 % 256);
    for (var i = 34; i < _EXPECTED_SIZE; ++i)
      data[i] = (byte)(i * 7 % 256);

    var original = DaliRawReader.FromBytes(data);
    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".sd0");
    try {
      File.WriteAllBytes(tempPath, DaliRawWriter.ToBytes(original));
      var restored = DaliRawReader.FromFile(new FileInfo(tempPath));

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
    var file = new DaliRawFile {
      Palette = new short[16],
      PixelData = new byte[32000]
    };

    var bytes = DaliRawWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(_EXPECTED_SIZE));
  }

  [Test]
  [Category("Unit")]
  public void DataType_DefaultPixelData_IsEmpty() {
    var file = new DaliRawFile();
    Assert.That(file.PixelData, Is.Not.Null);
    Assert.That(file.PixelData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void DataType_DefaultPalette_Has16Entries() {
    var file = new DaliRawFile();
    Assert.That(file.Palette.Length, Is.EqualTo(16));
  }

  [Test]
  [Category("Unit")]
  public void DataType_FixedDimensions() {
    var file = new DaliRawFile();
    Assert.That(file.Width, Is.EqualTo(320));
    Assert.That(file.Height, Is.EqualTo(200));
  }

  [Test]
  [Category("Unit")]
  public void DataType_ExpectedFileSize_Is32034() {
    Assert.That(DaliRawFile.ExpectedFileSize, Is.EqualTo(32034));
  }
}
