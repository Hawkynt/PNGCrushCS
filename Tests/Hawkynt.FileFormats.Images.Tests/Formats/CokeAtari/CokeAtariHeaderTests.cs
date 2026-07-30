using System;
using FileFormat.CokeAtari;

namespace FileFormat.CokeAtari.Tests;

[TestFixture]
public sealed class CokeAtariHeaderTests {

  [Test]
  [Category("Unit")]
  public void RoundTrip_PreservesDimensions() {
    var buffer = new byte[CokeAtariHeader.StructSize];
    CokeAtariHeader.Write(buffer, 320, 200);

    Assert.That(CokeAtariHeader.TryRead(buffer, out var width, out var height), Is.True);
    Assert.Multiple(() => {
      Assert.That(width, Is.EqualTo(320));
      Assert.That(height, Is.EqualTo(200));
    });
  }

  [Test]
  [Category("Unit")]
  public void StructSize_Is18() {
    // 12-byte signature + 4 dimension bytes + a 2-byte trailer.
    Assert.That(CokeAtariHeader.StructSize, Is.EqualTo(18));
  }

  [Test]
  [Category("Unit")]
  public void Write_EmitsTheCokeSignature() {
    var buffer = new byte[CokeAtariHeader.StructSize];
    CokeAtariHeader.Write(buffer, 320, 200);

    Assert.That(buffer[..CokeAtariHeader.Signature.Length], Is.EqualTo(CokeAtariHeader.Signature.ToArray()));
  }

  [Test]
  [Category("Unit")]
  public void Write_StoresDimensionsBigEndian() {
    var buffer = new byte[CokeAtariHeader.StructSize];
    CokeAtariHeader.Write(buffer, 0x0140, 0x00C8);

    Assert.Multiple(() => {
      Assert.That(buffer[CokeAtariHeader.DimensionsOffset], Is.EqualTo(0x01));
      Assert.That(buffer[CokeAtariHeader.DimensionsOffset + 1], Is.EqualTo(0x40));
      Assert.That(buffer[CokeAtariHeader.DimensionsOffset + 2], Is.EqualTo(0x00));
      Assert.That(buffer[CokeAtariHeader.DimensionsOffset + 3], Is.EqualTo(0xC8));
    });
  }

  [Test]
  [Category("Unit")]
  public void TryRead_RejectsDataWithoutTheSignature()
    => Assert.That(CokeAtariHeader.TryRead(new byte[CokeAtariHeader.StructSize], out _, out _), Is.False);
}
