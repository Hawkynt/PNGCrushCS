using System;
using System.Buffers.Binary;
using System.Linq;
using FileFormat.Rla;
using FileFormat.Core;

namespace FileFormat.Rla.Tests;

[TestFixture]
public sealed class RlaHeaderTests {

  [Test]
  [Category("Unit")]
  public void StructSize_Is740() {
    Assert.That(RlaHeader.StructSize, Is.EqualTo(740));
  }

  [Test]
  [Category("Unit")]
  public void ReadFrom_WriteTo_RoundTrip() {
    var original = new RlaHeader(
      WindowLeft: 0,
      WindowRight: 319,
      WindowBottom: 0,
      WindowTop: 239,
      ActiveWindowLeft: 0,
      ActiveWindowRight: 319,
      ActiveWindowBottom: 0,
      ActiveWindowTop: 239,
      FrameNumber: 1,
      StorageType: 0,
      NumChannels: 3,
      NumMatte: 1,
      NumAux: 0,
      Revision: -2,
      NumBits: 8,
      MatteType: 0,
      MatteBits: 8,
      AuxType: 0,
      AuxBits: 0,
      FieldRendered: 0,
      JobNumber: 42,
      Next: 0,
      Gamma: "2.2",
      RedChroma: string.Empty,
      GreenChroma: string.Empty,
      BlueChroma: string.Empty,
      WhitePoint: string.Empty,
      FileName: "test.rla",
      Description: "Test image",
      ProgramName: "UnitTest",
      MachineName: "localhost",
      User: "tester",
      Date: "2025-01-01",
      Aspect: string.Empty,
      AspectRatio: string.Empty,
      ColorChannel: "rgb",
      Time: string.Empty,
      Filter: string.Empty,
      AuxData: string.Empty
    );

    var buffer = new byte[RlaHeader.StructSize];
    original.WriteTo(buffer);
    var parsed = RlaHeader.ReadFrom(buffer);

    Assert.Multiple(() => {
      Assert.That(parsed.WindowLeft, Is.EqualTo(original.WindowLeft));
      Assert.That(parsed.WindowRight, Is.EqualTo(original.WindowRight));
      Assert.That(parsed.WindowBottom, Is.EqualTo(original.WindowBottom));
      Assert.That(parsed.WindowTop, Is.EqualTo(original.WindowTop));
      Assert.That(parsed.ActiveWindowLeft, Is.EqualTo(original.ActiveWindowLeft));
      Assert.That(parsed.ActiveWindowRight, Is.EqualTo(original.ActiveWindowRight));
      Assert.That(parsed.ActiveWindowBottom, Is.EqualTo(original.ActiveWindowBottom));
      Assert.That(parsed.ActiveWindowTop, Is.EqualTo(original.ActiveWindowTop));
      Assert.That(parsed.FrameNumber, Is.EqualTo(original.FrameNumber));
      Assert.That(parsed.StorageType, Is.EqualTo(original.StorageType));
      Assert.That(parsed.NumChannels, Is.EqualTo(original.NumChannels));
      Assert.That(parsed.NumMatte, Is.EqualTo(original.NumMatte));
      Assert.That(parsed.NumAux, Is.EqualTo(original.NumAux));
      Assert.That(parsed.Revision, Is.EqualTo(original.Revision));
      Assert.That(parsed.NumBits, Is.EqualTo(original.NumBits));
      Assert.That(parsed.MatteType, Is.EqualTo(original.MatteType));
      Assert.That(parsed.MatteBits, Is.EqualTo(original.MatteBits));
      Assert.That(parsed.AuxType, Is.EqualTo(original.AuxType));
      Assert.That(parsed.AuxBits, Is.EqualTo(original.AuxBits));
      Assert.That(parsed.FieldRendered, Is.EqualTo(original.FieldRendered));
      Assert.That(parsed.JobNumber, Is.EqualTo(original.JobNumber));
      Assert.That(parsed.Next, Is.EqualTo(original.Next));
      Assert.That(parsed.Gamma, Is.EqualTo(original.Gamma));
      Assert.That(parsed.FileName, Is.EqualTo(original.FileName));
      Assert.That(parsed.Description, Is.EqualTo(original.Description));
      Assert.That(parsed.ProgramName, Is.EqualTo(original.ProgramName));
      Assert.That(parsed.MachineName, Is.EqualTo(original.MachineName));
      Assert.That(parsed.User, Is.EqualTo(original.User));
      Assert.That(parsed.Date, Is.EqualTo(original.Date));
      Assert.That(parsed.ColorChannel, Is.EqualTo(original.ColorChannel));
    });
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_CoversFullStructSize() {
    var map = RlaHeader.GetFieldMap();
    var totalSize = map.Sum(f => f.Size);
    Assert.That(totalSize, Is.EqualTo(RlaHeader.StructSize));
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_HasNoOverlaps() {
    var map = RlaHeader.GetFieldMap();
    for (var i = 0; i < map.Length - 1; ++i)
      Assert.That(map[i].Offset + map[i].Size, Is.LessThanOrEqualTo(map[i + 1].Offset), $"Field {map[i].Name} overlaps with {map[i + 1].Name}");
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_HasEntries() {
    var map = RlaHeader.GetFieldMap();
    Assert.That(map.Length, Is.GreaterThan(0));
  }

  [Test]
  [Category("Unit")]
  public void ReadFrom_ParsesKnownValues() {
    var data = new byte[740];
    BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(8), 0);     // ActiveWindowLeft
    BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(10), 99);   // ActiveWindowRight
    BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(12), 0);    // ActiveWindowBottom
    BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(14), 49);   // ActiveWindowTop
    BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(16), 7);    // FrameNumber
    BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(20), 3);    // NumChannels
    BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(22), 1);    // NumMatte
    BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(658), 8);   // NumBits

    var header = RlaHeader.ReadFrom(data);
    Assert.Multiple(() => {
      Assert.That(header.ActiveWindowRight, Is.EqualTo(99));
      Assert.That(header.ActiveWindowTop, Is.EqualTo(49));
      Assert.That(header.FrameNumber, Is.EqualTo(7));
      Assert.That(header.NumChannels, Is.EqualTo(3));
      Assert.That(header.NumMatte, Is.EqualTo(1));
      Assert.That(header.NumBits, Is.EqualTo(8));
    });
  }
}
