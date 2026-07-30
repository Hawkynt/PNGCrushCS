using System;
using System.Linq;
using FileFormat.Wad3;

namespace FileFormat.Wad3.Tests;

[TestFixture]
public sealed class Wad3HeaderTests {

  [Test]
  [Category("Unit")]
  public void StructSize_Is12() {
    Assert.That(Wad3Header.StructSize, Is.EqualTo(12));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_PreservesAllFields() {
    var original = new Wad3Header((byte)'W', (byte)'A', (byte)'D', (byte)'3', 5, 2048);
    var buffer = new byte[Wad3Header.StructSize];
    original.WriteTo(buffer);

    var restored = Wad3Header.ReadFrom(buffer);

    Assert.That(restored.Magic1, Is.EqualTo((byte)'W'));
    Assert.That(restored.Magic2, Is.EqualTo((byte)'A'));
    Assert.That(restored.Magic3, Is.EqualTo((byte)'D'));
    Assert.That(restored.Magic4, Is.EqualTo((byte)'3'));
    Assert.That(restored.NumLumps, Is.EqualTo(5));
    Assert.That(restored.DirectoryOffset, Is.EqualTo(2048));
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_HasThreeEntries() {
    var fields = Wad3Header.GetFieldMap();

    Assert.That(fields, Has.Length.EqualTo(7));
    Assert.That(fields.Any(f => f.Name == "Magic" && f.Offset == 0 && f.Size == 4), Is.True);
    Assert.That(fields.Any(f => f.Name == "Magic1" && f.Offset == 0 && f.Size == 1), Is.True);
    Assert.That(fields.Any(f => f.Name == "NumLumps" && f.Offset == 4 && f.Size == 4), Is.True);
    Assert.That(fields.Any(f => f.Name == "DirectoryOffset" && f.Offset == 8 && f.Size == 4), Is.True);
  }
}
