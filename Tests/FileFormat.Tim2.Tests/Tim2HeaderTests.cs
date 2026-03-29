using System;
using System.Linq;
using FileFormat.Tim2;

namespace FileFormat.Tim2.Tests;

[TestFixture]
public sealed class Tim2HeaderTests {

  [Test]
  [Category("Unit")]
  public void Tim2Header_RoundTrip() {
    var original = new Tim2Header((byte)'T', (byte)'I', (byte)'M', (byte)'2', 4, 1, 3);

    Span<byte> buf = stackalloc byte[Tim2Header.StructSize];
    original.WriteTo(buf);
    var restored = Tim2Header.ReadFrom(buf);

    Assert.That(restored.Sig0, Is.EqualTo(original.Sig0));
    Assert.That(restored.Sig1, Is.EqualTo(original.Sig1));
    Assert.That(restored.Sig2, Is.EqualTo(original.Sig2));
    Assert.That(restored.Sig3, Is.EqualTo(original.Sig3));
    Assert.That(restored.Version, Is.EqualTo(original.Version));
    Assert.That(restored.Alignment, Is.EqualTo(original.Alignment));
    Assert.That(restored.PictureCount, Is.EqualTo(original.PictureCount));
  }

  [Test]
  [Category("Unit")]
  public void Tim2Header_GetFieldMap_ReturnsExpectedFields() {
    var fields = Tim2Header.GetFieldMap();

    Assert.That(fields, Has.Length.EqualTo(9));
    Assert.That(fields[0].Name, Is.EqualTo("Sig0"));
    Assert.That(fields[0].Offset, Is.EqualTo(0));
    Assert.That(fields[0].Size, Is.EqualTo(1));
    Assert.That(fields.Any(f => f.Name == "Signature" && f.Offset == 0 && f.Size == 4), Is.True);
    Assert.That(fields.Any(f => f.Name == "Version" && f.Offset == 4 && f.Size == 1), Is.True);
    Assert.That(fields.Any(f => f.Name == "Padding" && f.Offset == 8 && f.Size == 8), Is.True);
  }

  [Test]
  [Category("Unit")]
  public void Tim2Header_StructSize_Is16() {
    Assert.That(Tim2Header.StructSize, Is.EqualTo(16));
  }

  [Test]
  [Category("Unit")]
  public void Tim2PictureHeader_RoundTrip() {
    var original = new Tim2PictureHeader(
      1024, 512, 256, 48, 16,
      3, 1, 0, 0,
      64, 32,
      0x1234567890ABCDEF, 0xFEDCBA0987654321,
      0xAABBCCDD, 0x11223344
    );

    Span<byte> buf = stackalloc byte[Tim2PictureHeader.StructSize];
    original.WriteTo(buf);
    var restored = Tim2PictureHeader.ReadFrom(buf);

    Assert.That(restored.TotalSize, Is.EqualTo(original.TotalSize));
    Assert.That(restored.PaletteSize, Is.EqualTo(original.PaletteSize));
    Assert.That(restored.ImageDataSize, Is.EqualTo(original.ImageDataSize));
    Assert.That(restored.HeaderSize, Is.EqualTo(original.HeaderSize));
    Assert.That(restored.PaletteColors, Is.EqualTo(original.PaletteColors));
    Assert.That(restored.PictureFormat, Is.EqualTo(original.PictureFormat));
    Assert.That(restored.Mipmaps, Is.EqualTo(original.Mipmaps));
    Assert.That(restored.PaletteType, Is.EqualTo(original.PaletteType));
    Assert.That(restored.ImageType, Is.EqualTo(original.ImageType));
    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.GsTex0, Is.EqualTo(original.GsTex0));
    Assert.That(restored.GsTex1, Is.EqualTo(original.GsTex1));
    Assert.That(restored.GsFlags, Is.EqualTo(original.GsFlags));
    Assert.That(restored.GsTexClut, Is.EqualTo(original.GsTexClut));
  }

  [Test]
  [Category("Unit")]
  public void Tim2PictureHeader_StructSize_Is48() {
    Assert.That(Tim2PictureHeader.StructSize, Is.EqualTo(48));
  }
}
