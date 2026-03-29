using System;
using FileFormat.BbcMicro;

namespace FileFormat.BbcMicro.Tests;

[TestFixture]
public sealed class BbcMicroLayoutConverterTests {

  [Test]
  [Category("Unit")]
  public void CharacterBlockToLinear_Mode1_RoundTrip() {
    var blockData = new byte[BbcMicroFile.ScreenSizeModes012];
    for (var i = 0; i < blockData.Length; ++i)
      blockData[i] = (byte)(i * 7 % 256);

    var linear = BbcMicroLayoutConverter.CharacterBlockToLinear(blockData, 320, 256, BbcMicroMode.Mode1);
    var restored = BbcMicroLayoutConverter.LinearToCharacterBlock(linear, 320, 256, BbcMicroMode.Mode1);

    Assert.That(restored, Is.EqualTo(blockData));
  }

  [Test]
  [Category("Unit")]
  public void LinearToCharacterBlock_Mode1_RoundTrip() {
    var charCols = 80;
    var linearData = new byte[256 * charCols];
    for (var i = 0; i < linearData.Length; ++i)
      linearData[i] = (byte)(i * 13 % 256);

    var block = BbcMicroLayoutConverter.LinearToCharacterBlock(linearData, 320, 256, BbcMicroMode.Mode1);
    var restored = BbcMicroLayoutConverter.CharacterBlockToLinear(block, 320, 256, BbcMicroMode.Mode1);

    Assert.That(restored, Is.EqualTo(linearData));
  }

  [Test]
  [Category("Unit")]
  public void CharacterBlockToLinear_Mode0_RoundTrip() {
    var blockData = new byte[BbcMicroFile.ScreenSizeModes012];
    for (var i = 0; i < blockData.Length; ++i)
      blockData[i] = (byte)(i * 3 % 256);

    var linear = BbcMicroLayoutConverter.CharacterBlockToLinear(blockData, 640, 256, BbcMicroMode.Mode0);
    var restored = BbcMicroLayoutConverter.LinearToCharacterBlock(linear, 640, 256, BbcMicroMode.Mode0);

    Assert.That(restored, Is.EqualTo(blockData));
  }

  [Test]
  [Category("Unit")]
  public void CharacterBlockToLinear_Mode4_RoundTrip() {
    var blockData = new byte[BbcMicroFile.ScreenSizeModes45];
    for (var i = 0; i < blockData.Length; ++i)
      blockData[i] = (byte)(i * 11 % 256);

    var linear = BbcMicroLayoutConverter.CharacterBlockToLinear(blockData, 320, 256, BbcMicroMode.Mode4);
    var restored = BbcMicroLayoutConverter.LinearToCharacterBlock(linear, 320, 256, BbcMicroMode.Mode4);

    Assert.That(restored, Is.EqualTo(blockData));
  }

  [Test]
  [Category("Unit")]
  public void CharacterBlockToLinear_Mode1_CorrectLinearLayout() {
    // Set up a known character block pattern
    // Character at (col=0, row=0): bytes 0..7 represent 8 pixel rows
    var charCols = 80;
    var blockData = new byte[BbcMicroFile.ScreenSizeModes012];
    // Place value 0xAA at character (col=0, row=0), pixel row 0
    blockData[0] = 0xAA;
    // Place value 0xBB at character (col=0, row=0), pixel row 1
    blockData[1] = 0xBB;
    // Place value 0xCC at character (col=1, row=0), pixel row 0
    blockData[8] = 0xCC;

    var linear = BbcMicroLayoutConverter.CharacterBlockToLinear(blockData, 320, 256, BbcMicroMode.Mode1);

    // In linear layout: scanline 0, byte 0 should be 0xAA
    Assert.That(linear[0], Is.EqualTo(0xAA));
    // Scanline 1, byte 0 should be 0xBB
    Assert.That(linear[charCols], Is.EqualTo(0xBB));
    // Scanline 0, byte 1 should be 0xCC
    Assert.That(linear[1], Is.EqualTo(0xCC));
  }

  [Test]
  [Category("Unit")]
  public void LinearToCharacterBlock_AllZeros_ProducesCorrectSize() {
    var linearData = new byte[256 * 80];

    var block = BbcMicroLayoutConverter.LinearToCharacterBlock(linearData, 320, 256, BbcMicroMode.Mode1);

    Assert.That(block.Length, Is.EqualTo(BbcMicroFile.ScreenSizeModes012));
  }

  [Test]
  [Category("Unit")]
  public void LinearToCharacterBlock_Mode5_ProducesCorrectSize() {
    var linearData = new byte[256 * 40];

    var block = BbcMicroLayoutConverter.LinearToCharacterBlock(linearData, 160, 256, BbcMicroMode.Mode5);

    Assert.That(block.Length, Is.EqualTo(BbcMicroFile.ScreenSizeModes45));
  }
}
