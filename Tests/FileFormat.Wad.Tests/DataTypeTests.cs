using System;
using FileFormat.Wad;

namespace FileFormat.Wad.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void WadType_HasExpectedValues() {
    Assert.That((int)WadType.Iwad, Is.EqualTo(0));
    Assert.That((int)WadType.Pwad, Is.EqualTo(1));

    var values = Enum.GetValues<WadType>();
    Assert.That(values, Has.Length.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void WadEntry_StructSize_Is16() {
    Assert.That(WadEntry.StructSize, Is.EqualTo(16));
  }

  [Test]
  [Category("Unit")]
  public void WadEntry_RoundTrip_PreservesAllFields() {
    var original = new WadEntry(256, 1024, "MYENTRY");
    var buffer = new byte[WadEntry.StructSize];
    original.WriteTo(buffer);

    var restored = WadEntry.ReadFrom(buffer);

    Assert.That(restored.FilePos, Is.EqualTo(256));
    Assert.That(restored.Size, Is.EqualTo(1024));
    Assert.That(restored.Name, Is.EqualTo("MYENTRY"));
  }

  [Test]
  [Category("Unit")]
  public void WadEntry_NameTruncatedTo8Chars() {
    var original = new WadEntry(0, 0, "LONGNAME");
    var buffer = new byte[WadEntry.StructSize];
    original.WriteTo(buffer);

    var restored = WadEntry.ReadFrom(buffer);

    Assert.That(restored.Name, Has.Length.LessThanOrEqualTo(8));
    Assert.That(restored.Name, Is.EqualTo("LONGNAME"));
  }

  [Test]
  [Category("Unit")]
  public void WadLump_DefaultValues() {
    var lump = new WadLump();

    Assert.That(lump.Name, Is.EqualTo(""));
    Assert.That(lump.Data, Is.Empty);
  }
}
