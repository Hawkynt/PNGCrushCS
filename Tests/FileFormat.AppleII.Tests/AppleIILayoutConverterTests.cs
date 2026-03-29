using System;
using FileFormat.AppleII;

namespace FileFormat.AppleII.Tests;

[TestFixture]
public sealed class AppleIILayoutConverterTests {

  [Test]
  [Category("Unit")]
  public void Interleave_Deinterleave_RoundTrip_Hgr() {
    var linearData = new byte[192 * 40];
    for (var i = 0; i < linearData.Length; ++i)
      linearData[i] = (byte)(i * 7 % 256);

    var interleaved = AppleIILayoutConverter.Interleave(linearData, AppleIIMode.Hgr);
    var restored = AppleIILayoutConverter.Deinterleave(interleaved, AppleIIMode.Hgr);

    Assert.That(restored, Is.EqualTo(linearData));
  }

  [Test]
  [Category("Unit")]
  public void Interleave_Deinterleave_RoundTrip_Dhgr() {
    var linearData = new byte[192 * 80];
    for (var i = 0; i < linearData.Length; ++i)
      linearData[i] = (byte)(i * 13 % 256);

    var interleaved = AppleIILayoutConverter.Interleave(linearData, AppleIIMode.Dhgr);
    var restored = AppleIILayoutConverter.Deinterleave(interleaved, AppleIIMode.Dhgr);

    Assert.That(restored, Is.EqualTo(linearData));
  }

  [Test]
  [Category("Unit")]
  public void FirstLine_AtOffset0() {
    var rawData = new byte[8192];
    rawData[0] = 0xAB;
    rawData[1] = 0xCD;

    var linear = AppleIILayoutConverter.Deinterleave(rawData, AppleIIMode.Hgr);

    Assert.That(linear[0], Is.EqualTo(0xAB));
    Assert.That(linear[1], Is.EqualTo(0xCD));
  }

  [Test]
  [Category("Unit")]
  public void LineOrder_Correct_Line1_AtOffset1024() {
    var rawData = new byte[8192];
    // Line 1: offset = (1 % 8) * 1024 + ((1 / 8) % 8) * 128 + (1 / 64) * 40
    //        = 1 * 1024 + 0 * 128 + 0 * 40 = 1024
    rawData[1024] = 0xEF;

    var linear = AppleIILayoutConverter.Deinterleave(rawData, AppleIIMode.Hgr);

    // Line 1 in linear order starts at offset 40
    Assert.That(linear[40], Is.EqualTo(0xEF));
  }

  [Test]
  [Category("Unit")]
  public void LineOrder_Correct_Line8_AtOffset128() {
    var rawData = new byte[8192];
    // Line 8: offset = (8 % 8) * 1024 + ((8 / 8) % 8) * 128 + (8 / 64) * 40
    //        = 0 * 1024 + 1 * 128 + 0 * 40 = 128
    rawData[128] = 0x42;

    var linear = AppleIILayoutConverter.Deinterleave(rawData, AppleIIMode.Hgr);

    // Line 8 in linear order starts at offset 8 * 40 = 320
    Assert.That(linear[320], Is.EqualTo(0x42));
  }

  [Test]
  [Category("Unit")]
  public void LineOrder_Correct_Line64_AtOffset40() {
    var rawData = new byte[8192];
    // Line 64: offset = (64 % 8) * 1024 + ((64 / 8) % 8) * 128 + (64 / 64) * 40
    //         = 0 * 1024 + 0 * 128 + 1 * 40 = 40
    rawData[40] = 0x99;

    var linear = AppleIILayoutConverter.Deinterleave(rawData, AppleIIMode.Hgr);

    // Line 64 in linear order starts at offset 64 * 40 = 2560
    Assert.That(linear[2560], Is.EqualTo(0x99));
  }
}
