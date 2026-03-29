using System;
using System.Buffers.Binary;
using System.Linq;
using FileFormat.Msp;
using FileFormat.Core;

namespace FileFormat.Msp.Tests;

[TestFixture]
public sealed class MspHeaderTests {

  [Test]
  [Category("Unit")]
  public void RoundTrip_PreservesAllFields() {
    var original = new MspHeader(
      Key1: MspHeader.V1Key1,
      Key2: MspHeader.V1Key2,
      Width: 640,
      Height: 480,
      XAspect: 1,
      YAspect: 1,
      XAspectPrinter: 2,
      YAspectPrinter: 3,
      PrinterWidth: 100,
      PrinterHeight: 200,
      XAspectCorr: 4,
      YAspectCorr: 5,
      Checksum: 0x1234,
      Padding1: 0,
      Padding2: 0,
      Padding3: 0
    );

    var buffer = new byte[MspHeader.StructSize];
    original.WriteTo(buffer);
    var parsed = MspHeader.ReadFrom(buffer);

    Assert.That(parsed, Is.EqualTo(original));
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_CoversFullStructSize() {
    var map = MspHeader.GetFieldMap();
    var totalSize = map.Sum(f => f.Size);
    Assert.That(totalSize, Is.EqualTo(MspHeader.StructSize));
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_HasNoOverlaps() {
    var map = MspHeader.GetFieldMap();
    for (var i = 0; i < map.Length - 1; ++i)
      Assert.That(map[i].Offset + map[i].Size, Is.LessThanOrEqualTo(map[i + 1].Offset), $"Field {map[i].Name} overlaps with {map[i + 1].Name}");
  }

  [Test]
  [Category("Unit")]
  public void StructSize_Is32() {
    Assert.That(MspHeader.StructSize, Is.EqualTo(32));
  }
}
