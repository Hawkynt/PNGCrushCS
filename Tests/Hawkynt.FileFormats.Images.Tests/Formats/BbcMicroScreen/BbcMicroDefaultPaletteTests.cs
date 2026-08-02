using System;
using FileFormat.BbcMicroScreen;
using FileFormat.Core;

namespace FileFormat.BbcMicroScreen.Tests;

/// <summary>
/// Which physical colours a BBC screen starts on, and how its mode is known at all.
/// </summary>
/// <remarks>
/// A four-colour screen begins on black, red, yellow and white — not the first four of the physical
/// list. Taking them in order puts green where yellow belongs and yellow where white does, which is
/// a picture in plausible colours and none of them right.
/// <para/>
/// The mode is the other half. A 20480-byte dump is mode 0, 1 or 2 and nothing inside it says which;
/// only the extension does. The reader has always known that, and only its by-bytes entry was wired
/// up, so every such file took the monochrome reading and a 320 by 256 picture of four colours came
/// back 640 by 512 in black and white.
/// <para/>
/// Checked against RECOIL: all five samples — one per mode — match on every pixel.
/// </remarks>
[TestFixture]
public sealed class BbcMicroDefaultPaletteTests {

  private static BbcMicroScreenFile _Screen(BbcMicroMode mode, int bytes) => new() {
    Mode = mode,
    ScreenData = new byte[bytes],
  };

  [Test]
  [Category("Unit")]
  public void FourColoursAreBlackRedYellowAndWhite() {
    var image = BbcMicroScreenFile.ToRawImage(_Screen(BbcMicroMode.Mode1, 20480));

    Assert.That(image.Palette, Is.Not.Null);
    Assert.Multiple(() => {
      Assert.That((image.Palette![0], image.Palette![1], image.Palette![2]), Is.EqualTo(((byte)0, (byte)0, (byte)0)));
      Assert.That((image.Palette![3], image.Palette![4], image.Palette![5]), Is.EqualTo(((byte)255, (byte)0, (byte)0)));
      Assert.That((image.Palette![6], image.Palette![7], image.Palette![8]), Is.EqualTo(((byte)255, (byte)255, (byte)0)), "yellow, not green");
      Assert.That((image.Palette![9], image.Palette![10], image.Palette![11]), Is.EqualTo(((byte)255, (byte)255, (byte)255)), "white, not yellow");
    });
  }

  [Test]
  [Category("Unit")]
  public void TwoColoursAreBlackAndWhite() {
    var image = BbcMicroScreenFile.ToRawImage(_Screen(BbcMicroMode.Mode4, 10240));

    Assert.Multiple(() => {
      Assert.That(image.Palette![0], Is.Zero);
      Assert.That(image.Palette![3], Is.EqualTo(255));
      Assert.That(image.Palette![4], Is.EqualTo(255));
      Assert.That(image.Palette![5], Is.EqualTo(255));
    });
  }

  [Test]
  [Category("Unit")]
  public void SixteenColoursTakeThePhysicalListInOrder() {
    var image = BbcMicroScreenFile.ToRawImage(_Screen(BbcMicroMode.Mode2, 20480));

    Assert.Multiple(() => {
      Assert.That(image.PaletteCount, Is.EqualTo(16));
      Assert.That((image.Palette![3], image.Palette![4], image.Palette![5]), Is.EqualTo(((byte)255, (byte)0, (byte)0)));
      Assert.That((image.Palette![6], image.Palette![7], image.Palette![8]), Is.EqualTo(((byte)0, (byte)255, (byte)0)), "the sixteen are not permuted");
    });
  }

  [Test]
  [Category("Unit")]
  public void EachModeDrawsItsOwnSize() {
    // Modes 2 and 5 store half as many pixels across and show each of them twice as wide, so what
    // is drawn is 320 for all but mode 0. RECOIL draws the same, which the samples confirm.
    Assert.Multiple(() => {
      Assert.That(BbcMicroScreenFile.ToRawImage(_Screen(BbcMicroMode.Mode0, 20480)).Width, Is.EqualTo(640));
      Assert.That(BbcMicroScreenFile.ToRawImage(_Screen(BbcMicroMode.Mode1, 20480)).Width, Is.EqualTo(320));
      Assert.That(BbcMicroScreenFile.ToRawImage(_Screen(BbcMicroMode.Mode2, 20480)).Width, Is.EqualTo(320));
      Assert.That(BbcMicroScreenFile.ToRawImage(_Screen(BbcMicroMode.Mode4, 10240)).Width, Is.EqualTo(320));
    });
  }
}
