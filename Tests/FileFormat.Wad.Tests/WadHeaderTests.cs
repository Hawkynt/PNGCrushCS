using System;
using System.Linq;
using FileFormat.Wad;

namespace FileFormat.Wad.Tests;

[TestFixture]
public sealed class WadHeaderTests {

  [Test]
  [Category("Unit")]
  public void StructSize_Is12() {
    Assert.That(WadHeader.StructSize, Is.EqualTo(12));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_PreservesAllFields() {
    var original = new WadHeader((byte)'I', (byte)'W', (byte)'A', (byte)'D', 42, 1024);
    var buffer = new byte[WadHeader.StructSize];
    original.WriteTo(buffer);

    var restored = WadHeader.ReadFrom(buffer);

    Assert.That(restored.Id1, Is.EqualTo(original.Id1));
    Assert.That(restored.Id2, Is.EqualTo(original.Id2));
    Assert.That(restored.Id3, Is.EqualTo(original.Id3));
    Assert.That(restored.Id4, Is.EqualTo(original.Id4));
    Assert.That(restored.NumLumps, Is.EqualTo(42));
    Assert.That(restored.DirectoryOffset, Is.EqualTo(1024));
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_HasThreeEntries() {
    var fields = WadHeader.GetFieldMap();

    Assert.That(fields, Has.Length.EqualTo(7));
    Assert.That(fields.Any(f => f.Name == "Identification" && f.Offset == 0 && f.Size == 4), Is.True);
    Assert.That(fields.Any(f => f.Name == "Id1" && f.Offset == 0 && f.Size == 1), Is.True);
    Assert.That(fields.Any(f => f.Name == "NumLumps" && f.Offset == 4 && f.Size == 4), Is.True);
    Assert.That(fields.Any(f => f.Name == "DirectoryOffset" && f.Offset == 8 && f.Size == 4), Is.True);
  }
}
