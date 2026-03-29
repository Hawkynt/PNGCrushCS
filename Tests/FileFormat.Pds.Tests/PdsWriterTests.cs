using System;
using System.Text;
using FileFormat.Pds;

namespace FileFormat.Pds.Tests;

[TestFixture]
public sealed class PdsWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_StartsWithPdsVersionId() {
    var file = new PdsFile {
      Width = 4,
      Height = 2,
      SampleBits = 8,
      Bands = 1,
      SampleType = PdsSampleType.UnsignedByte,
      BandStorage = PdsBandStorage.BandSequential,
      PixelData = new byte[4 * 2]
    };

    var bytes = PdsWriter.ToBytes(file);
    var prefix = Encoding.ASCII.GetString(bytes, 0, 14);

    Assert.That(prefix, Is.EqualTo("PDS_VERSION_ID"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PdsWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsImagePointer() {
    var file = new PdsFile {
      Width = 4,
      Height = 2,
      SampleBits = 8,
      Bands = 1,
      SampleType = PdsSampleType.UnsignedByte,
      BandStorage = PdsBandStorage.BandSequential,
      PixelData = new byte[4 * 2]
    };

    var bytes = PdsWriter.ToBytes(file);
    var headerText = Encoding.ASCII.GetString(bytes);

    Assert.That(headerText, Does.Contain("^IMAGE"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsSampleBits() {
    var file = new PdsFile {
      Width = 4,
      Height = 2,
      SampleBits = 8,
      Bands = 1,
      SampleType = PdsSampleType.UnsignedByte,
      BandStorage = PdsBandStorage.BandSequential,
      PixelData = new byte[4 * 2]
    };

    var bytes = PdsWriter.ToBytes(file);
    var headerText = Encoding.ASCII.GetString(bytes);

    Assert.That(headerText, Does.Contain("SAMPLE_BITS = 8"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsEndKeyword() {
    var file = new PdsFile {
      Width = 4,
      Height = 2,
      SampleBits = 8,
      Bands = 1,
      SampleType = PdsSampleType.UnsignedByte,
      BandStorage = PdsBandStorage.BandSequential,
      PixelData = new byte[4 * 2]
    };

    var bytes = PdsWriter.ToBytes(file);
    var headerText = Encoding.ASCII.GetString(bytes);

    Assert.That(headerText, Does.Contain("\r\nEND\r\n"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsBandStorageForMultiBand() {
    var file = new PdsFile {
      Width = 4,
      Height = 2,
      SampleBits = 8,
      Bands = 3,
      SampleType = PdsSampleType.UnsignedByte,
      BandStorage = PdsBandStorage.SampleInterleaved,
      PixelData = new byte[4 * 2 * 3]
    };

    var bytes = PdsWriter.ToBytes(file);
    var headerText = Encoding.ASCII.GetString(bytes);

    Assert.That(headerText, Does.Contain("BAND_STORAGE_TYPE = SAMPLE_INTERLEAVED"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PixelDataAtCorrectOffset() {
    var pixels = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0xCA, 0xFE, 0xBA, 0xBE };
    var file = new PdsFile {
      Width = 4,
      Height = 2,
      SampleBits = 8,
      Bands = 1,
      SampleType = PdsSampleType.UnsignedByte,
      BandStorage = PdsBandStorage.BandSequential,
      PixelData = pixels
    };

    var bytes = PdsWriter.ToBytes(file);

    // read it back to verify
    var parsed = PdsReader.FromBytes(bytes);
    Assert.That(parsed.PixelData, Is.EqualTo(pixels));
  }
}
