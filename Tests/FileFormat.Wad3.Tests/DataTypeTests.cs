using System;
using FileFormat.Wad3;

namespace FileFormat.Wad3.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void Wad3LumpType_HasExpectedValues() {
    Assert.That((byte)Wad3LumpType.StatusBar, Is.EqualTo(0x42));
    Assert.That((byte)Wad3LumpType.MipTex, Is.EqualTo(0x43));
    Assert.That((byte)Wad3LumpType.Font, Is.EqualTo(0x45));

    var values = Enum.GetValues<Wad3LumpType>();
    Assert.That(values, Has.Length.EqualTo(3));
  }

  [Test]
  [Category("Unit")]
  public void Wad3Entry_StructSize_Is32() {
    Assert.That(Wad3Entry.StructSize, Is.EqualTo(32));
  }

  [Test]
  [Category("Unit")]
  public void Wad3Entry_RoundTrip_PreservesAllFields() {
    var original = new Wad3Entry(512, 2048, 2048, 0x43, 0, 0, "MYENTRY");
    var buffer = new byte[Wad3Entry.StructSize];
    original.WriteTo(buffer);

    var restored = Wad3Entry.ReadFrom(buffer);

    Assert.That(restored.FilePos, Is.EqualTo(512));
    Assert.That(restored.DiskSize, Is.EqualTo(2048));
    Assert.That(restored.Size, Is.EqualTo(2048));
    Assert.That(restored.Type, Is.EqualTo(0x43));
    Assert.That(restored.Compression, Is.EqualTo(0));
    Assert.That(restored.Name, Is.EqualTo("MYENTRY"));
  }

  [Test]
  [Category("Unit")]
  public void Wad3Entry_NameTruncatedTo16Chars() {
    var original = new Wad3Entry(0, 0, 0, 0x43, 0, 0, "LONGERTEXNAME01");
    var buffer = new byte[Wad3Entry.StructSize];
    original.WriteTo(buffer);

    var restored = Wad3Entry.ReadFrom(buffer);

    Assert.That(restored.Name, Has.Length.LessThanOrEqualTo(16));
    Assert.That(restored.Name, Is.EqualTo("LONGERTEXNAME01"));
  }

  [Test]
  [Category("Unit")]
  public void Wad3Texture_DefaultValues() {
    var texture = new Wad3Texture();

    Assert.That(texture.Name, Is.EqualTo(""));
    Assert.That(texture.Width, Is.EqualTo(0));
    Assert.That(texture.Height, Is.EqualTo(0));
    Assert.That(texture.PixelData, Is.Empty);
    Assert.That(texture.MipMaps, Is.Null);
    Assert.That(texture.Palette, Is.Empty);
  }
}
