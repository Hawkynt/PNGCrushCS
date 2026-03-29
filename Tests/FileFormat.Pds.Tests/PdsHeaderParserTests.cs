using System;
using System.Text;
using FileFormat.Pds;

namespace FileFormat.Pds.Tests;

[TestFixture]
public sealed class PdsHeaderParserTests {

  [Test]
  [Category("Unit")]
  public void Parse_ExtractsDimensions() {
    var header = _BuildMinimalHeader(10, 20, 8, 1, null);

    var (labels, _) = PdsHeaderParser.Parse(header);

    Assert.That(labels["LINE_SAMPLES"], Is.EqualTo("10"));
    Assert.That(labels["LINES"], Is.EqualTo("20"));
  }

  [Test]
  [Category("Unit")]
  public void Parse_ExtractsBandStorage() {
    var header = _BuildMinimalHeader(4, 2, 8, 3, "BAND_SEQUENTIAL");

    var (labels, _) = PdsHeaderParser.Parse(header);

    Assert.That(labels["BAND_STORAGE_TYPE"], Is.EqualTo("BAND_SEQUENTIAL"));
  }

  [Test]
  [Category("Unit")]
  public void Parse_ExtractsSampleType() {
    var header = _BuildMinimalHeader(4, 2, 16, 1, null);

    var (labels, _) = PdsHeaderParser.Parse(header);

    Assert.That(labels["SAMPLE_TYPE"], Is.EqualTo("MSB_UNSIGNED_INTEGER"));
  }

  [Test]
  [Category("Unit")]
  public void Parse_ExtractsSampleBits() {
    var header = _BuildMinimalHeader(4, 2, 16, 1, null);

    var (labels, _) = PdsHeaderParser.Parse(header);

    Assert.That(labels["SAMPLE_BITS"], Is.EqualTo("16"));
  }

  [Test]
  [Category("Unit")]
  public void Parse_CalculatesImageOffset() {
    var recordBytes = 4;
    var labelRecords = 3;
    var sb = new StringBuilder();
    sb.Append("PDS_VERSION_ID = PDS3\r\n");
    sb.Append($"RECORD_BYTES = {recordBytes}\r\n");
    sb.Append($"LABEL_RECORDS = {labelRecords}\r\n");
    sb.Append($"^IMAGE = {labelRecords + 1}\r\n");
    sb.Append("OBJECT = IMAGE\r\n");
    sb.Append("  LINES = 2\r\n");
    sb.Append("  LINE_SAMPLES = 4\r\n");
    sb.Append("  SAMPLE_BITS = 8\r\n");
    sb.Append("  SAMPLE_TYPE = UNSIGNED_INTEGER\r\n");
    sb.Append("  BANDS = 1\r\n");
    sb.Append("END_OBJECT = IMAGE\r\n");
    sb.Append("END\r\n");

    var data = Encoding.ASCII.GetBytes(sb.ToString());
    var padded = new byte[Math.Max(data.Length, recordBytes * (labelRecords + 1) + 8)];
    Array.Copy(data, padded, data.Length);

    var (_, imageOffset) = PdsHeaderParser.Parse(padded);

    Assert.That(imageOffset, Is.EqualTo(labelRecords * recordBytes));
  }

  [Test]
  [Category("Unit")]
  public void Parse_ExtractsBands() {
    var header = _BuildMinimalHeader(4, 2, 8, 3, "SAMPLE_INTERLEAVED");

    var (labels, _) = PdsHeaderParser.Parse(header);

    Assert.That(labels["BANDS"], Is.EqualTo("3"));
  }

  [Test]
  [Category("Unit")]
  public void Format_ProducesValidHeader() {
    var headerBytes = PdsHeaderParser.Format(4, 2, 8, 1, PdsBandStorage.BandSequential, PdsSampleType.UnsignedByte, 8, null);
    var headerText = Encoding.ASCII.GetString(headerBytes);

    Assert.That(headerText, Does.Contain("PDS_VERSION_ID = PDS3"));
    Assert.That(headerText, Does.Contain("LINES = 2"));
    Assert.That(headerText, Does.Contain("LINE_SAMPLES = 4"));
    Assert.That(headerText, Does.Contain("SAMPLE_BITS = 8"));
    Assert.That(headerText, Does.Contain("END"));
  }

  [Test]
  [Category("Unit")]
  public void Format_HeaderSizeIsMultipleOfRecordBytes() {
    var width = 4;
    var recordBytes = width * 1;
    var headerBytes = PdsHeaderParser.Format(width, 2, 8, 1, PdsBandStorage.BandSequential, PdsSampleType.UnsignedByte, 8, null);

    Assert.That(headerBytes.Length % recordBytes, Is.EqualTo(0));
  }

  private static byte[] _BuildMinimalHeader(int width, int height, int sampleBits, int bands, string? bandStorage) {
    var sampleType = sampleBits == 8 ? "UNSIGNED_INTEGER" : "MSB_UNSIGNED_INTEGER";
    var sb = new StringBuilder();
    sb.Append("PDS_VERSION_ID = PDS3\r\n");
    sb.Append($"RECORD_BYTES = {width * bands * (sampleBits / 8)}\r\n");
    sb.Append($"LABEL_RECORDS = 50\r\n");
    sb.Append($"^IMAGE = 51\r\n");
    sb.Append("OBJECT = IMAGE\r\n");
    sb.Append($"  LINES = {height}\r\n");
    sb.Append($"  LINE_SAMPLES = {width}\r\n");
    sb.Append($"  SAMPLE_BITS = {sampleBits}\r\n");
    sb.Append($"  SAMPLE_TYPE = {sampleType}\r\n");
    sb.Append($"  BANDS = {bands}\r\n");
    if (bandStorage != null)
      sb.Append($"  BAND_STORAGE_TYPE = {bandStorage}\r\n");
    sb.Append("END_OBJECT = IMAGE\r\n");
    sb.Append("END\r\n");

    return Encoding.ASCII.GetBytes(sb.ToString());
  }
}
