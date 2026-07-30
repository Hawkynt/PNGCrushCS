using System;
using System.Buffers.Binary;
using System.Text;
using FileFormat.Dicom;

namespace FileFormat.Dicom.Tests;

[TestFixture]
public sealed class DicomTagReaderTests {

  [Test]
  [Category("Unit")]
  public void ReadTag_UsTag_ReturnsCorrectValue() {
    // Build a US tag: (0028,0010) US with value 256
    var data = _BuildShortTag(0x0028, 0x0010, "US", _UInt16Bytes(256));

    var (group, element, vr, value, nextOffset) = DicomTagReader.ReadTag(data, 0);

    Assert.Multiple(() => {
      Assert.That(group, Is.EqualTo(0x0028));
      Assert.That(element, Is.EqualTo(0x0010));
      Assert.That(vr, Is.EqualTo("US"));
      Assert.That(DicomTagReader.ReadUS(value), Is.EqualTo(256));
      Assert.That(nextOffset, Is.EqualTo(data.Length));
    });
  }

  [Test]
  [Category("Unit")]
  public void ReadTag_CsTag_ReturnsCorrectString() {
    var csValue = Encoding.ASCII.GetBytes("MONOCHROME2 ");
    var data = _BuildShortTag(0x0028, 0x0004, "CS", csValue);

    var (group, element, vr, value, nextOffset) = DicomTagReader.ReadTag(data, 0);

    Assert.Multiple(() => {
      Assert.That(group, Is.EqualTo(0x0028));
      Assert.That(element, Is.EqualTo(0x0004));
      Assert.That(vr, Is.EqualTo("CS"));
      Assert.That(DicomTagReader.ReadCS(value), Is.EqualTo("MONOCHROME2"));
    });
  }

  [Test]
  [Category("Unit")]
  public void ReadTag_OwTag_ReturnsPixelData() {
    var pixelBytes = new byte[] { 0x01, 0x02, 0x03, 0x04 };
    var data = _BuildLongTag(0x7FE0, 0x0010, "OW", pixelBytes);

    var (group, element, vr, value, nextOffset) = DicomTagReader.ReadTag(data, 0);

    Assert.Multiple(() => {
      Assert.That(group, Is.EqualTo(0x7FE0));
      Assert.That(element, Is.EqualTo(0x0010));
      Assert.That(vr, Is.EqualTo("OW"));
      Assert.That(value, Is.EqualTo(pixelBytes));
    });
  }

  [Test]
  [Category("Unit")]
  public void ReadTag_DsTag_ReturnsCorrectDouble() {
    var dsValue = Encoding.ASCII.GetBytes("128.5   ");
    var data = _BuildShortTag(0x0028, 0x1050, "DS", dsValue);

    var (group, element, vr, value, nextOffset) = DicomTagReader.ReadTag(data, 0);

    Assert.Multiple(() => {
      Assert.That(vr, Is.EqualTo("DS"));
      Assert.That(DicomTagReader.ReadDS(value), Is.EqualTo(128.5).Within(0.001));
    });
  }

  private static byte[] _BuildShortTag(ushort group, ushort element, string vr, byte[] value) {
    // Short VR: group(2) + element(2) + VR(2) + length(2) + value
    var data = new byte[8 + value.Length];
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0), group);
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(2), element);
    Encoding.ASCII.GetBytes(vr, data.AsSpan(4, 2));
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(6), (ushort)value.Length);
    Array.Copy(value, 0, data, 8, value.Length);
    return data;
  }

  private static byte[] _BuildLongTag(ushort group, ushort element, string vr, byte[] value) {
    // Long VR: group(2) + element(2) + VR(2) + reserved(2) + length(4) + value
    var data = new byte[12 + value.Length];
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0), group);
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(2), element);
    Encoding.ASCII.GetBytes(vr, data.AsSpan(4, 2));
    // reserved 2 bytes at offset 6 (already zero)
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(8), (uint)value.Length);
    Array.Copy(value, 0, data, 12, value.Length);
    return data;
  }

  private static byte[] _UInt16Bytes(ushort value) {
    var buf = new byte[2];
    BinaryPrimitives.WriteUInt16LittleEndian(buf, value);
    return buf;
  }
}
