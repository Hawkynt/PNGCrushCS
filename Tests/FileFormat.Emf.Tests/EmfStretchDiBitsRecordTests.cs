using System;
using System.Buffers.Binary;
using System.Linq;
using FileFormat.Core;
using FileFormat.Emf;

namespace FileFormat.Emf.Tests;

[TestFixture]
public sealed class EmfStretchDiBitsRecordTests {

  [Test]
  [Category("Unit")]
  public void StructSize_Is80() {
    Assert.That(EmfStretchDiBitsRecord.StructSize, Is.EqualTo(80));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_PreservesAllFields() {
    var original = new EmfStretchDiBitsRecord(
      RecordType: 81,
      RecordSize: 200,
      BoundsLeft: 0,
      BoundsTop: 0,
      BoundsRight: 99,
      BoundsBottom: 49,
      XDest: 10,
      YDest: 20,
      XSrc: 0,
      YSrc: 0,
      CxSrc: 100,
      CySrc: 50,
      OffBmiSrc: 80,
      CbBmiSrc: 40,
      OffBitsSrc: 120,
      CbBitsSrc: 15000,
      UsageSrc: 0,
      DwRop: 0x00CC0020
    );

    var buffer = new byte[EmfStretchDiBitsRecord.StructSize];
    original.WriteTo(buffer);
    var parsed = EmfStretchDiBitsRecord.ReadFrom(buffer);

    Assert.That(parsed, Is.EqualTo(original));
  }

  [Test]
  [Category("Unit")]
  public void ReadFrom_ParsesKnownValues() {
    var data = new byte[EmfStretchDiBitsRecord.StructSize];
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0), 81);           // RecordType
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), 300);          // RecordSize
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(8), 0);             // BoundsLeft
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(12), 0);            // BoundsTop
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(16), 63);           // BoundsRight
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(20), 47);           // BoundsBottom
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(24), 5);            // XDest
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(28), 10);           // YDest
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(32), 0);            // XSrc
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(36), 0);            // YSrc
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(40), 64);           // CxSrc
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(44), 48);           // CySrc
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(56), 80);          // OffBmiSrc
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(60), 40);          // CbBmiSrc
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(64), 120);         // OffBitsSrc
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(68), 9216);        // CbBitsSrc
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(72), 0);           // UsageSrc
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(76), 0x00CC0020);  // DwRop

    var record = EmfStretchDiBitsRecord.ReadFrom(data);

    Assert.Multiple(() => {
      Assert.That(record.RecordType, Is.EqualTo(81u));
      Assert.That(record.RecordSize, Is.EqualTo(300u));
      Assert.That(record.BoundsRight, Is.EqualTo(63));
      Assert.That(record.BoundsBottom, Is.EqualTo(47));
      Assert.That(record.XDest, Is.EqualTo(5));
      Assert.That(record.YDest, Is.EqualTo(10));
      Assert.That(record.CxSrc, Is.EqualTo(64));
      Assert.That(record.CySrc, Is.EqualTo(48));
      Assert.That(record.OffBmiSrc, Is.EqualTo(80u));
      Assert.That(record.CbBmiSrc, Is.EqualTo(40u));
      Assert.That(record.OffBitsSrc, Is.EqualTo(120u));
      Assert.That(record.CbBitsSrc, Is.EqualTo(9216u));
      Assert.That(record.DwRop, Is.EqualTo(0x00CC0020u));
    });
  }

  [Test]
  [Category("Unit")]
  public void WriteTo_ZerosReservedGap() {
    var record = new EmfStretchDiBitsRecord(
      RecordType: 81,
      RecordSize: 200,
      BoundsLeft: 1,
      BoundsTop: 2,
      BoundsRight: 3,
      BoundsBottom: 4,
      XDest: 5,
      YDest: 6,
      XSrc: 7,
      YSrc: 8,
      CxSrc: 9,
      CySrc: 10,
      OffBmiSrc: 80,
      CbBmiSrc: 40,
      OffBitsSrc: 120,
      CbBitsSrc: 5000,
      UsageSrc: 0,
      DwRop: 0x00CC0020
    );

    // Fill buffer with 0xFF to ensure zeros are actually written
    var buffer = new byte[EmfStretchDiBitsRecord.StructSize];
    Array.Fill(buffer, (byte)0xFF);
    record.WriteTo(buffer);

    // Bytes 48-55 (8 bytes) should all be zero
    for (var i = 48; i < 56; ++i)
      Assert.That(buffer[i], Is.EqualTo(0), $"Byte at offset {i} should be zero (reserved gap).");
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_CoversFullStructSize() {
    var map = EmfStretchDiBitsRecord.GetFieldMap();
    var totalSize = map.Sum(f => f.Size);
    Assert.That(totalSize, Is.EqualTo(EmfStretchDiBitsRecord.StructSize));
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_HasNoOverlaps() {
    var map = EmfStretchDiBitsRecord.GetFieldMap();
    for (var i = 0; i < map.Length - 1; ++i)
      Assert.That(map[i].Offset + map[i].Size, Is.LessThanOrEqualTo(map[i + 1].Offset), $"Field {map[i].Name} overlaps with {map[i + 1].Name}");
  }
}
