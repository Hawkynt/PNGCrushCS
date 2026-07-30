using System;
using System.IO;
using System.Text;
using FileFormat.Pds;

namespace FileFormat.Pds.Tests;

[TestFixture]
public sealed class PdsReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PdsReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PdsReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pds"));
    Assert.Throws<FileNotFoundException>(() => PdsReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[4];
    Assert.Throws<InvalidDataException>(() => PdsReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    var data = Encoding.ASCII.GetBytes("NOTVALID = something else here padding to min size");
    Assert.Throws<InvalidDataException>(() => PdsReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidGrayscale() {
    var width = 4;
    var height = 2;
    var data = _BuildPdsData(width, height, 8, 1, null, null);

    var result = PdsReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(width));
    Assert.That(result.Height, Is.EqualTo(height));
    Assert.That(result.Bands, Is.EqualTo(1));
    Assert.That(result.SampleBits, Is.EqualTo(8));
    Assert.That(result.SampleType, Is.EqualTo(PdsSampleType.UnsignedByte));
    Assert.That(result.PixelData.Length, Is.EqualTo(width * height));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidRgb() {
    var width = 4;
    var height = 2;
    var data = _BuildPdsData(width, height, 8, 3, "SAMPLE_INTERLEAVED", null);

    var result = PdsReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(width));
    Assert.That(result.Height, Is.EqualTo(height));
    Assert.That(result.Bands, Is.EqualTo(3));
    Assert.That(result.SampleBits, Is.EqualTo(8));
    Assert.That(result.BandStorage, Is.EqualTo(PdsBandStorage.SampleInterleaved));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidRgbBsq() {
    var width = 3;
    var height = 2;
    var data = _BuildPdsData(width, height, 8, 3, "BAND_SEQUENTIAL", null);

    var result = PdsReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(width));
    Assert.That(result.Height, Is.EqualTo(height));
    Assert.That(result.Bands, Is.EqualTo(3));
    Assert.That(result.BandStorage, Is.EqualTo(PdsBandStorage.BandSequential));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PdsReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Valid() {
    var width = 2;
    var height = 1;
    var data = _BuildPdsData(width, height, 8, 1, null, null);

    using var ms = new MemoryStream(data);
    var result = PdsReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(2));
    Assert.That(result.Height, Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_PixelDataPreserved() {
    var width = 4;
    var height = 2;
    var pixels = new byte[width * height];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i * 31 % 256);

    var data = _BuildPdsData(width, height, 8, 1, null, pixels);

    var result = PdsReader.FromBytes(data);

    Assert.That(result.PixelData, Is.EqualTo(pixels));
  }

  private static byte[] _BuildPdsData(int width, int height, int sampleBits, int bands, string? bandStorage, byte[]? pixels) {
    var sb = new StringBuilder();
    var recordBytes = width * bands * (sampleBits / 8);

    sb.Append("PDS_VERSION_ID = PDS3\r\n");
    sb.Append($"RECORD_TYPE = FIXED_LENGTH\r\n");
    sb.Append($"RECORD_BYTES = {recordBytes}\r\n");

    var headerPlaceholder = sb.ToString();
    // estimate header size; we'll finalize after computing
    var estimatedHeaderLen = 512; // overestimate
    var labelRecords = estimatedHeaderLen / recordBytes + 1;
    var headerSize = labelRecords * recordBytes;

    sb.Append($"LABEL_RECORDS = {labelRecords}\r\n");
    sb.Append($"^IMAGE = {labelRecords + 1}\r\n");
    sb.Append("OBJECT = IMAGE\r\n");
    sb.Append($"  LINES = {height}\r\n");
    sb.Append($"  LINE_SAMPLES = {width}\r\n");
    sb.Append($"  SAMPLE_BITS = {sampleBits}\r\n");
    sb.Append($"  SAMPLE_TYPE = UNSIGNED_INTEGER\r\n");
    sb.Append($"  BANDS = {bands}\r\n");
    if (bandStorage != null)
      sb.Append($"  BAND_STORAGE_TYPE = {bandStorage}\r\n");
    sb.Append("END_OBJECT = IMAGE\r\n");
    sb.Append("END\r\n");

    var headerText = sb.ToString();
    var headerBytes = Encoding.ASCII.GetBytes(headerText);

    // recalculate to fit
    labelRecords = (headerBytes.Length / recordBytes) + 1;
    headerSize = labelRecords * recordBytes;
    while (headerSize < headerBytes.Length) {
      ++labelRecords;
      headerSize = labelRecords * recordBytes;
    }

    // rebuild with correct values
    sb.Clear();
    sb.Append("PDS_VERSION_ID = PDS3\r\n");
    sb.Append($"RECORD_TYPE = FIXED_LENGTH\r\n");
    sb.Append($"RECORD_BYTES = {recordBytes}\r\n");
    sb.Append($"LABEL_RECORDS = {labelRecords}\r\n");
    sb.Append($"^IMAGE = {labelRecords + 1}\r\n");
    sb.Append("OBJECT = IMAGE\r\n");
    sb.Append($"  LINES = {height}\r\n");
    sb.Append($"  LINE_SAMPLES = {width}\r\n");
    sb.Append($"  SAMPLE_BITS = {sampleBits}\r\n");
    sb.Append($"  SAMPLE_TYPE = UNSIGNED_INTEGER\r\n");
    sb.Append($"  BANDS = {bands}\r\n");
    if (bandStorage != null)
      sb.Append($"  BAND_STORAGE_TYPE = {bandStorage}\r\n");
    sb.Append("END_OBJECT = IMAGE\r\n");
    sb.Append("END\r\n");

    headerBytes = Encoding.ASCII.GetBytes(sb.ToString());
    var paddedHeader = new byte[headerSize];
    Array.Copy(headerBytes, paddedHeader, Math.Min(headerBytes.Length, headerSize));
    for (var i = headerBytes.Length; i < headerSize; ++i)
      paddedHeader[i] = (byte)' ';

    var pixelCount = width * height * bands * (sampleBits / 8);
    if (pixels == null) {
      pixels = new byte[pixelCount];
      for (var i = 0; i < pixels.Length; ++i)
        pixels[i] = (byte)(i * 17 % 256);
    }

    var result = new byte[headerSize + pixelCount];
    Array.Copy(paddedHeader, 0, result, 0, headerSize);
    Array.Copy(pixels, 0, result, headerSize, Math.Min(pixels.Length, pixelCount));

    return result;
  }
}
