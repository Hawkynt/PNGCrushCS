using System;
using System.Buffers.Binary;
using FileFormat.JpegXr;

namespace FileFormat.JpegXr.Tests;

[TestFixture]
public sealed class JpegXrIfdTests {

  [Test]
  [Category("Unit")]
  public void ParseEntries_SingleTag() {
    var data = _BuildIfdData(1, (0xBC80, JpegXrIfd.TYPE_LONG, 1u, 100u));
    var entries = JpegXrIfd.ParseEntries(data, 0);

    Assert.That(entries.Length, Is.EqualTo(1));
    Assert.That(entries[0].Tag, Is.EqualTo(0xBC80));
    Assert.That(entries[0].Value, Is.EqualTo(100u));
  }

  [Test]
  [Category("Unit")]
  public void ParseEntries_MultipleTags() {
    var data = _BuildIfdData(3,
      (0xBC01, JpegXrIfd.TYPE_BYTE, 1u, 0x0Cu),
      (0xBC80, JpegXrIfd.TYPE_LONG, 1u, 320u),
      (0xBC81, JpegXrIfd.TYPE_LONG, 1u, 240u)
    );
    var entries = JpegXrIfd.ParseEntries(data, 0);

    Assert.That(entries.Length, Is.EqualTo(3));
    Assert.That(entries[0].Tag, Is.EqualTo(0xBC01));
    Assert.That(entries[1].Tag, Is.EqualTo(0xBC80));
    Assert.That(entries[1].Value, Is.EqualTo(320u));
    Assert.That(entries[2].Tag, Is.EqualTo(0xBC81));
    Assert.That(entries[2].Value, Is.EqualTo(240u));
  }

  [Test]
  [Category("Unit")]
  public void ParseEntries_ShortType() {
    var data = _BuildIfdData(1, (0xBC02, JpegXrIfd.TYPE_SHORT, 1u, 0u));
    var entries = JpegXrIfd.ParseEntries(data, 0);

    Assert.That(entries[0].Type, Is.EqualTo(JpegXrIfd.TYPE_SHORT));
  }

  [Test]
  [Category("Unit")]
  public void ParseEntries_ByteType() {
    var data = _BuildIfdData(1, (0xBC01, JpegXrIfd.TYPE_BYTE, 1u, 0x08u));
    var entries = JpegXrIfd.ParseEntries(data, 0);

    Assert.That(entries[0].Type, Is.EqualTo(JpegXrIfd.TYPE_BYTE));
    Assert.That(entries[0].Value, Is.EqualTo(0x08u));
  }

  [Test]
  [Category("Unit")]
  public void ParseEntries_ValueVsOffset_SmallValue() {
    // A single LONG fits in the 4-byte value field (no external offset needed)
    var data = _BuildIfdData(1, (0xBCE1, JpegXrIfd.TYPE_LONG, 1u, 42u));
    var entries = JpegXrIfd.ParseEntries(data, 0);

    Assert.That(entries[0].Value, Is.EqualTo(42u));
  }

  [Test]
  [Category("Unit")]
  public void WriteEntry_LongType_WritesCorrectTag() {
    var span = new byte[12].AsSpan();
    var pos = 0;
    JpegXrIfd.WriteEntry(span, ref pos, 0xBC80, JpegXrIfd.TYPE_LONG, 1, 256);

    var tag = BinaryPrimitives.ReadUInt16LittleEndian(span);
    Assert.That(tag, Is.EqualTo(0xBC80));
    Assert.That(pos, Is.EqualTo(12));
  }

  [Test]
  [Category("Unit")]
  public void WriteEntry_ShortType_WritesAsUInt16() {
    var span = new byte[12].AsSpan();
    var pos = 0;
    JpegXrIfd.WriteEntry(span, ref pos, 0xBC02, JpegXrIfd.TYPE_SHORT, 1, 7);

    var value = BinaryPrimitives.ReadUInt16LittleEndian(span[8..]);
    Assert.That(value, Is.EqualTo(7));
  }

  [Test]
  [Category("Unit")]
  public void TypeSize_Byte_Returns1() {
    Assert.That(JpegXrIfd.TypeSize(JpegXrIfd.TYPE_BYTE), Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void TypeSize_Short_Returns2() {
    Assert.That(JpegXrIfd.TypeSize(JpegXrIfd.TYPE_SHORT), Is.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void TypeSize_Long_Returns4() {
    Assert.That(JpegXrIfd.TypeSize(JpegXrIfd.TYPE_LONG), Is.EqualTo(4));
  }

  /// <summary>Builds a byte array containing an IFD with the given entries starting at offset 0.</summary>
  private static byte[] _BuildIfdData(int entryCount, params (ushort tag, ushort type, uint count, uint value)[] entries) {
    var size = 2 + entryCount * 12 + 4;
    var data = new byte[size];
    var span = data.AsSpan();

    BinaryPrimitives.WriteUInt16LittleEndian(span, (ushort)entryCount);
    var pos = 2;

    foreach (var (tag, type, count, value) in entries) {
      BinaryPrimitives.WriteUInt16LittleEndian(span[pos..], tag);
      BinaryPrimitives.WriteUInt16LittleEndian(span[(pos + 2)..], type);
      BinaryPrimitives.WriteUInt32LittleEndian(span[(pos + 4)..], count);
      if (type == JpegXrIfd.TYPE_BYTE && count == 1)
        span[pos + 8] = (byte)value;
      else if (type == JpegXrIfd.TYPE_SHORT && count == 1)
        BinaryPrimitives.WriteUInt16LittleEndian(span[(pos + 8)..], (ushort)value);
      else
        BinaryPrimitives.WriteUInt32LittleEndian(span[(pos + 8)..], value);
      pos += 12;
    }

    // Next IFD offset = 0
    BinaryPrimitives.WriteUInt32LittleEndian(span[pos..], 0);

    return data;
  }
}
