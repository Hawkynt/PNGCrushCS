using System;
using System.Buffers.Binary;
using System.Linq;
using FileFormat.Core;
using FileFormat.Emf;

namespace FileFormat.Emf.Tests;

[TestFixture]
public sealed class EmfHeaderRecordTests {

  [Test]
  [Category("Unit")]
  public void StructSize_Is88() {
    Assert.That(EmfHeaderRecord.StructSize, Is.EqualTo(88));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_PreservesAllFields() {
    var original = new EmfHeaderRecord(
      RecordType: 1,
      RecordSize: 88,
      BoundsLeft: 0,
      BoundsTop: 0,
      BoundsRight: 319,
      BoundsBottom: 239,
      FrameLeft: 10,
      FrameTop: 20,
      FrameRight: 8466,
      FrameBottom: 6350,
      Signature: 0x464D4520,
      Version: 0x00010000,
      FileSize: 1024,
      RecordCount: 3,
      NumHandles: 1,
      Reserved: 0
    );

    var buffer = new byte[EmfHeaderRecord.StructSize];
    original.WriteTo(buffer);
    var parsed = EmfHeaderRecord.ReadFrom(buffer);

    Assert.That(parsed, Is.EqualTo(original));
  }

  [Test]
  [Category("Unit")]
  public void ReadFrom_ParsesKnownValues() {
    var data = new byte[EmfHeaderRecord.StructSize];
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0), 1);            // RecordType
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), 88);           // RecordSize
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(8), 0);             // BoundsLeft
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(12), 0);            // BoundsTop
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(16), 255);          // BoundsRight
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(20), 127);          // BoundsBottom
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(24), 0);            // FrameLeft
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(28), 0);            // FrameTop
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(32), 6773);         // FrameRight
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(36), 3386);         // FrameBottom
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(40), 0x464D4520);  // Signature
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(44), 0x00010000);  // Version
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(48), 2048);        // FileSize
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(52), 3);           // RecordCount
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(56), 1);           // NumHandles
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(58), 0);           // Reserved

    var header = EmfHeaderRecord.ReadFrom(data);

    Assert.Multiple(() => {
      Assert.That(header.RecordType, Is.EqualTo(1u));
      Assert.That(header.RecordSize, Is.EqualTo(88u));
      Assert.That(header.BoundsRight, Is.EqualTo(255));
      Assert.That(header.BoundsBottom, Is.EqualTo(127));
      Assert.That(header.FrameRight, Is.EqualTo(6773));
      Assert.That(header.FrameBottom, Is.EqualTo(3386));
      Assert.That(header.Signature, Is.EqualTo(0x464D4520u));
      Assert.That(header.Version, Is.EqualTo(0x00010000u));
      Assert.That(header.FileSize, Is.EqualTo(2048u));
      Assert.That(header.RecordCount, Is.EqualTo(3u));
      Assert.That(header.NumHandles, Is.EqualTo((ushort)1));
      Assert.That(header.Reserved, Is.EqualTo((ushort)0));
    });
  }

  [Test]
  [Category("Unit")]
  public void WriteTo_ProducesCorrectBytes() {
    var header = new EmfHeaderRecord(
      RecordType: 1,
      RecordSize: 88,
      BoundsLeft: 0,
      BoundsTop: 0,
      BoundsRight: 639,
      BoundsBottom: 479,
      FrameLeft: 0,
      FrameTop: 0,
      FrameRight: 16933,
      FrameBottom: 12700,
      Signature: 0x464D4520,
      Version: 0x00010000,
      FileSize: 4096,
      RecordCount: 3,
      NumHandles: 1,
      Reserved: 0
    );

    var buffer = new byte[EmfHeaderRecord.StructSize];
    header.WriteTo(buffer);

    Assert.Multiple(() => {
      Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(0)), Is.EqualTo(1u));
      Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(4)), Is.EqualTo(88u));
      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(16)), Is.EqualTo(639));
      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(20)), Is.EqualTo(479));
      Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(40)), Is.EqualTo(0x464D4520u));
      Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(48)), Is.EqualTo(4096u));
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(56)), Is.EqualTo((ushort)1));
    });
  }

  [Test]
  [Category("Unit")]
  public void WriteTo_ZerosUnusedRegion() {
    var header = new EmfHeaderRecord(
      RecordType: 1,
      RecordSize: 88,
      BoundsLeft: 100,
      BoundsTop: 200,
      BoundsRight: 300,
      BoundsBottom: 400,
      FrameLeft: 500,
      FrameTop: 600,
      FrameRight: 700,
      FrameBottom: 800,
      Signature: 0x464D4520,
      Version: 0x00010000,
      FileSize: 9999,
      RecordCount: 5,
      NumHandles: 2,
      Reserved: 0
    );

    // Fill buffer with 0xFF to ensure zeros are actually written
    var buffer = new byte[EmfHeaderRecord.StructSize];
    Array.Fill(buffer, (byte)0xFF);
    header.WriteTo(buffer);

    // Bytes 60-87 (28 bytes) should all be zero
    for (var i = 60; i < 88; ++i)
      Assert.That(buffer[i], Is.EqualTo(0), $"Byte at offset {i} should be zero (unused region).");
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_CoversFullStructSize() {
    var map = EmfHeaderRecord.GetFieldMap();
    var totalSize = map.Sum(f => f.Size);
    Assert.That(totalSize, Is.EqualTo(EmfHeaderRecord.StructSize));
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_HasNoOverlaps() {
    var map = EmfHeaderRecord.GetFieldMap();
    for (var i = 0; i < map.Length - 1; ++i)
      Assert.That(map[i].Offset + map[i].Size, Is.LessThanOrEqualTo(map[i + 1].Offset), $"Field {map[i].Name} overlaps with {map[i + 1].Name}");
  }
}
