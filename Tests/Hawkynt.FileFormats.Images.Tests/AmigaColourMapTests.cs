using System;
using FileFormat.Core;

namespace Hawkynt.FileFormats.Images.Tests;

/// <summary>
/// The rule for a colour map an Amiga wrote four bits at a time, shared by the IFF readers.
/// </summary>
/// <remarks>
/// Three of them copied the map across untouched, each in its own words, so a channel of 8 arrived
/// as 0x80 where the machine shows 0x88 — every colour a shade too dark and the brightest white
/// 0xF0 rather than 0xFF. The picture looks right and no pixel of it is.
/// </remarks>
[TestFixture]
public sealed class AmigaColourMapTests {

  [Test]
  [Category("Unit")]
  public void EveryLowNibbleEmptyMeansFourBitsAChannel() {
    byte[] map = [0x80, 0x60, 0xF0, 0x00, 0x10, 0xA0];
    AmigaColourMap.WidenIfFourBit(map);

    Assert.That(map, Is.EqualTo(new byte[] { 0x88, 0x66, 0xFF, 0x00, 0x11, 0xAA }));
  }

  [Test]
  [Category("Unit")]
  public void AnythingInALowNibbleMeansItIsAlreadyEight() {
    byte[] map = [0x80, 0x61, 0xF0, 0x00, 0x00, 0x00];
    var before = (byte[])map.Clone();
    AmigaColourMap.WidenIfFourBit(map);

    Assert.That(map, Is.EqualTo(before), "a map that is already eight-bit must not be touched");
  }

  [Test]
  [Category("Unit")]
  public void TheBrightestReachesWhiteAndBlackStaysBlack() {
    byte[] map = [0xF0, 0xF0, 0xF0, 0x00, 0x00, 0x00];
    AmigaColourMap.WidenIfFourBit(map);

    Assert.Multiple(() => {
      Assert.That(map[0], Is.EqualTo(255));
      Assert.That(map[3], Is.Zero);
    });
  }

  [Test]
  [Category("Unit")]
  public void AnAllBlackMapIsWidenedWithoutChanging() {
    // Every low nibble is empty, so the rule applies, and repeating a nought changes nothing.
    byte[] map = new byte[12];
    AmigaColourMap.WidenIfFourBit(map);

    Assert.That(map, Is.All.Zero);
  }

  [Test]
  [Category("Unit")]
  public void NoMapAtAllIsNotAnError()
    => Assert.DoesNotThrow(() => AmigaColourMap.WidenIfFourBit(null));
}
