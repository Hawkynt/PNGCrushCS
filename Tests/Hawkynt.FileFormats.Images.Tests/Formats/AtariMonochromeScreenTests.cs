using System;
using FileFormat.Core;

namespace Hawkynt.FileFormats.Images.Tests;

/// <summary>
/// The rule that a monochrome Atari screen takes no colours from the file.
/// </summary>
/// <remarks>
/// Four formats had this wrong independently — TINY, Paintworks, Dali and CrackArt — and each
/// painted a black-and-white picture in whatever the file happened to leave in the palette
/// registers. One sample holds red in its second entry and the whole picture was drawn in it.
/// The hardware does not use those registers for the monochrome screen at all, so the rule lives in
/// one place now rather than in four.
/// </remarks>
[TestFixture]
public sealed class AtariMonochromeScreenTests {

  [Test]
  [Category("Unit")]
  public void OnePlaneIsPaperAndInkAndNothingElse() {
    // The stored palette says red and green, neither of which a monochrome screen can show.
    short[] stored = [0x0700, 0x0070];
    var palette = AtariStGraphics.ScreenPalette(stored, 1);

    Assert.Multiple(() => {
      Assert.That(palette, Has.Length.EqualTo(6));
      Assert.That(palette[0], Is.EqualTo(255));
      Assert.That(palette[1], Is.EqualTo(255));
      Assert.That(palette[2], Is.EqualTo(255));
      Assert.That(palette[3], Is.EqualTo(0));
      Assert.That(palette[4], Is.EqualTo(0));
      Assert.That(palette[5], Is.EqualTo(0));
    });
  }

  [Test]
  [Category("Unit")]
  public void MoreThanOnePlaneStillTakesTheColoursTheFileStores() {
    short[] stored = [0x0000, 0x0700, 0x0070, 0x0007];
    var palette = AtariStGraphics.ScreenPalette(stored, 2);

    Assert.Multiple(() => {
      Assert.That(palette, Has.Length.EqualTo(12));
      Assert.That(palette[3], Is.GreaterThan((byte)200), "the second entry is red and stays red");
      Assert.That(palette[4], Is.Zero);
    });
  }
}
