using System;
using FileFormat.AmstradCpc;

namespace FileFormat.AmstradCpc.Tests;

[TestFixture]
public sealed class AmstradCpcPixelPackerTests {

  [Test]
  [Category("Unit")]
  public void Mode2_UnpackByte_AllOnes() {
    var pixels = AmstradCpcPixelPacker.UnpackByte(0xFF, AmstradCpcMode.Mode2);

    Assert.That(pixels.Length, Is.EqualTo(8));
    for (var i = 0; i < 8; ++i)
      Assert.That(pixels[i], Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void Mode2_UnpackByte_AllZeros() {
    var pixels = AmstradCpcPixelPacker.UnpackByte(0x00, AmstradCpcMode.Mode2);

    Assert.That(pixels.Length, Is.EqualTo(8));
    for (var i = 0; i < 8; ++i)
      Assert.That(pixels[i], Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void Mode2_UnpackByte_Alternating() {
    // 0b10101010 = MSB first: 1,0,1,0,1,0,1,0
    var pixels = AmstradCpcPixelPacker.UnpackByte(0xAA, AmstradCpcMode.Mode2);

    Assert.That(pixels[0], Is.EqualTo(1));
    Assert.That(pixels[1], Is.EqualTo(0));
    Assert.That(pixels[2], Is.EqualTo(1));
    Assert.That(pixels[3], Is.EqualTo(0));
    Assert.That(pixels[4], Is.EqualTo(1));
    Assert.That(pixels[5], Is.EqualTo(0));
    Assert.That(pixels[6], Is.EqualTo(1));
    Assert.That(pixels[7], Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void Mode2_PackByte_RoundTrip() {
    var original = new byte[] { 1, 0, 1, 1, 0, 0, 1, 0 };
    var packed = AmstradCpcPixelPacker.PackByte(original, AmstradCpcMode.Mode2);
    var unpacked = AmstradCpcPixelPacker.UnpackByte(packed, AmstradCpcMode.Mode2);

    Assert.That(unpacked, Is.EqualTo(original));
  }

  [Test]
  [Category("Unit")]
  public void Mode0_UnpackByte_Zero() {
    var pixels = AmstradCpcPixelPacker.UnpackByte(0x00, AmstradCpcMode.Mode0);

    Assert.That(pixels.Length, Is.EqualTo(2));
    Assert.That(pixels[0], Is.EqualTo(0));
    Assert.That(pixels[1], Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void Mode0_PackByte_RoundTrip() {
    // Test all 16 color values for pixel 0
    for (byte p0 = 0; p0 < 16; ++p0) {
      for (byte p1 = 0; p1 < 16; ++p1) {
        var original = new byte[] { p0, p1 };
        var packed = AmstradCpcPixelPacker.PackByte(original, AmstradCpcMode.Mode0);
        var unpacked = AmstradCpcPixelPacker.UnpackByte(packed, AmstradCpcMode.Mode0);

        Assert.That(unpacked, Is.EqualTo(original), $"Failed for p0={p0}, p1={p1}");
      }
    }
  }

  [Test]
  [Category("Unit")]
  public void Mode1_UnpackByte_Zero() {
    var pixels = AmstradCpcPixelPacker.UnpackByte(0x00, AmstradCpcMode.Mode1);

    Assert.That(pixels.Length, Is.EqualTo(4));
    for (var i = 0; i < 4; ++i)
      Assert.That(pixels[i], Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void Mode1_PackByte_RoundTrip() {
    // Test all 4 color values for each of the 4 pixels
    for (byte p0 = 0; p0 < 4; ++p0)
      for (byte p1 = 0; p1 < 4; ++p1)
        for (byte p2 = 0; p2 < 4; ++p2)
          for (byte p3 = 0; p3 < 4; ++p3) {
            var original = new byte[] { p0, p1, p2, p3 };
            var packed = AmstradCpcPixelPacker.PackByte(original, AmstradCpcMode.Mode1);
            var unpacked = AmstradCpcPixelPacker.UnpackByte(packed, AmstradCpcMode.Mode1);

            Assert.That(unpacked, Is.EqualTo(original), $"Failed for p0={p0}, p1={p1}, p2={p2}, p3={p3}");
          }
  }

  [Test]
  [Category("Unit")]
  public void Mode0_KnownByte_UnpacksCorrectly() {
    // Byte 0xFF: all bits set
    // pixel0 bits 7,3,5,1 = 1,1,1,1 => 15
    // pixel1 bits 6,2,4,0 = 1,1,1,1 => 15
    var pixels = AmstradCpcPixelPacker.UnpackByte(0xFF, AmstradCpcMode.Mode0);

    Assert.That(pixels[0], Is.EqualTo(15));
    Assert.That(pixels[1], Is.EqualTo(15));
  }
}
