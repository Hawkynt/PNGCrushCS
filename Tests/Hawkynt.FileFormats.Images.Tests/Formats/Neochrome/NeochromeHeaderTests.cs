using System;
using System.Buffers.Binary;
using System.Linq;
using FileFormat.Neochrome;

namespace FileFormat.Neochrome.Tests;

[TestFixture]
public sealed class NeochromeHeaderTests {
  [Test]
  public void ReadFrom_UsesPublishedOffsets() {
    var data = new byte[128];
    BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(2), 2);
    BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(4), 0x0777);
    "        .   "u8.CopyTo(data.AsSpan(36, 12));
    BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(48), unchecked((short)0x8123));
    BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(50), unchecked((short)0x05FE));
    BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(52), 20);
    BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(58), 320);
    BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(60), 200);
    BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(126), 99);

    var header = NeochromeHeader.ReadFrom(data);

    Assert.Multiple(() => {
      Assert.That(header.Resolution, Is.EqualTo(2));
      Assert.That(header.Pal0, Is.EqualTo((short)0x0777));
      Assert.That(header.FileName, Is.EqualTo("        .   "u8.ToArray()));
      Assert.That(header.AnimationLimits, Is.EqualTo(unchecked((short)0x8123)));
      Assert.That(header.AnimSpeed, Is.EqualTo(5));
      Assert.That(header.AnimDirection, Is.EqualTo(0xFE));
      Assert.That(header.AnimSteps, Is.EqualTo(20));
      Assert.That(header.AnimWidth, Is.EqualTo(320));
      Assert.That(header.AnimHeight, Is.EqualTo(200));
      Assert.That(header.Reserved[32], Is.EqualTo(99));
    });
  }

  [Test]
  public void WriteTo_RoundTripsPublishedFields() {
    var fileName = "PICTURE .NEO"u8.ToArray();
    var reserved = Enumerable.Range(0, 33).Select(i => (short)i).ToArray();
    var header = new NeochromeHeader(
      0, 1,
      0x0777, 0x0700, 0x0070, 0x0007,
      0x0770, 0x0707, 0x0077, 0,
      0x0111, 0x0222, 0x0333, 0x0444,
      0x0555, 0x0666, 0x0123, 0x0456,
      fileName, unchecked((short)0x8123), unchecked((short)0x80FE),
      10, 0, 0, 320, 200, reserved
    );
    var bytes = new byte[128];
    header.WriteTo(bytes);
    var restored = NeochromeHeader.ReadFrom(bytes);

    Assert.Multiple(() => {
      Assert.That(restored.GetPalette(), Is.EqualTo(header.GetPalette()));
      Assert.That(restored.FileName, Is.EqualTo(fileName));
      Assert.That(restored.AnimationLimits, Is.EqualTo(header.AnimationLimits));
      Assert.That(restored.AnimationSpeedDirection, Is.EqualTo(header.AnimationSpeedDirection));
      Assert.That(restored.Reserved, Is.EqualTo(reserved));
    });
  }

  [Test]
  public void LegacyConstructor_MapsAnimationBytesToOffset50() {
    var header = new NeochromeHeader(
      0, 0,
      0, 0, 0, 0, 0, 0, 0, 0,
      0, 0, 0, 0, 0, 0, 0, 0,
      5, 0xFE, 10, 0, 0, 320, 200
    );
    var bytes = new byte[128];
    header.WriteTo(bytes);

    Assert.Multiple(() => {
      Assert.That(bytes[50], Is.EqualTo(5));
      Assert.That(bytes[51], Is.EqualTo(0xFE));
      Assert.That(BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(58)), Is.EqualTo(320));
      Assert.That(BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(60)), Is.EqualTo(200));
    });
  }

  [Test]
  public void GetFieldMap_CoversFullStructWithoutOverlap() {
    var map = NeochromeHeader.GetFieldMap();
    Assert.That(map.Sum(field => field.Size), Is.EqualTo(NeochromeHeader.StructSize));
    for (var i = 0; i < map.Length - 1; ++i)
      Assert.That(map[i].Offset + map[i].Size, Is.LessThanOrEqualTo(map[i + 1].Offset));
  }

  [Test]
  public void StructSize_Is128() => Assert.That(NeochromeHeader.StructSize, Is.EqualTo(128));
}
