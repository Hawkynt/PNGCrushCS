using System;
using FileFormat.Mng;
using FileFormat.Core;

namespace FileFormat.Mng.Tests;

[TestFixture]
public sealed class MngHeaderTests {

  [Test]
  [Category("Unit")]
  public void StructSize_Is28() {
    Assert.That(MngHeader.StructSize, Is.EqualTo(28));
  }

  [Test]
  [Category("Unit")]
  public void ReadFrom_WriteTo_RoundTrip() {
    var original = new MngHeader(320, 240, 1000, 1, 5, 50, 1);
    var buffer = new byte[MngHeader.StructSize];
    original.WriteTo(buffer);
    var restored = MngHeader.ReadFrom(buffer);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.TicksPerSecond, Is.EqualTo(original.TicksPerSecond));
    Assert.That(restored.NominalLayerCount, Is.EqualTo(original.NominalLayerCount));
    Assert.That(restored.NominalFrameCount, Is.EqualTo(original.NominalFrameCount));
    Assert.That(restored.NominalPlayTime, Is.EqualTo(original.NominalPlayTime));
    Assert.That(restored.Profile, Is.EqualTo(original.Profile));
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_Returns7Fields() {
    var fields = MngHeader.GetFieldMap();
    Assert.That(fields, Has.Length.EqualTo(7));
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_FieldsMatchStructLayout() {
    var fields = MngHeader.GetFieldMap();

    Assert.That(fields[0].Name, Is.EqualTo("Width"));
    Assert.That(fields[0].Offset, Is.EqualTo(0));
    Assert.That(fields[0].Size, Is.EqualTo(4));

    Assert.That(fields[6].Name, Is.EqualTo("Profile"));
    Assert.That(fields[6].Offset, Is.EqualTo(24));
    Assert.That(fields[6].Size, Is.EqualTo(4));
  }
}
