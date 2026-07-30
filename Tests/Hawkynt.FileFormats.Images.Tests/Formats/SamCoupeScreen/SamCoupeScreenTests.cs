using System;
using System.IO;
using FileFormat.Core;
using FileFormat.SamCoupeMode4;
using FileFormat.SamCoupeScreen;

namespace FileFormat.SamCoupeScreen.Tests;

[TestFixture]
public sealed class SamCoupeScreenTests {

  private static byte[] _Empty(SamCoupeScreenMode mode, int records = 0) {
    var offset = SamCoupeScreenFile.InterruptOffsetFor(mode);
    var data = new byte[offset + records * SamCoupeScreenFile.InterruptRecordSize + 1];
    data[^1] = SamCoupeScreenFile.InterruptTerminator;

    return data;
  }

  private static void _SetPalette(byte[] data, SamCoupeScreenMode mode, int entry, byte color)
    => data[SamCoupeScreenFile.PaletteOffsetFor(mode) + entry] = color;

  private static (byte R, byte G, byte B) _PixelAt(RawImage image, int x, int y) {
    var o = (y * image.Width + x) * 3;
    return (image.PixelData[o], image.PixelData[o + 1], image.PixelData[o + 2]);
  }

  private static (byte R, byte G, byte B) _Expected(byte color) {
    var rgb = SamCoupePalette.ToRgb(color);
    return ((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
  }

  [Test]
  public void Dimensions_FollowTheMode() {
    Assert.Multiple(() => {
      foreach (var mode in new[] { SamCoupeScreenMode.Mode1, SamCoupeScreenMode.Mode2 }) {
        var image = SamCoupeScreenFile.ToRawImage(SamCoupeScreenReader.FromSpan(_Empty(mode), mode));
        Assert.That((image.Width, image.Height), Is.EqualTo((256, 192)), mode.ToString());
      }

      var mode3 = SamCoupeScreenFile.ToRawImage(SamCoupeScreenReader.FromSpan(_Empty(SamCoupeScreenMode.Mode3), SamCoupeScreenMode.Mode3));
      Assert.That((mode3.Width, mode3.Height), Is.EqualTo((512, 384)));
    });
  }

  [Test]
  public void Mode1_UsesTheSpectrumsShuffledLineOrder() {
    var data = _Empty(SamCoupeScreenMode.Mode1);
    _SetPalette(data, SamCoupeScreenMode.Mode1, 1, 0x7F);   // ink
    _SetPalette(data, SamCoupeScreenMode.Mode1, 0, 0x00);   // paper
    data[6144] = 0x01;                                     // cell 0: ink 1, paper 0, not bright
    // Scanline 1 does not follow scanline 0 in the file: it is 256 bytes further on.
    data[ZxSpectrumGraphics.LineOffset(1)] = 0x80;

    var image = SamCoupeScreenFile.ToRawImage(SamCoupeScreenReader.FromSpan(data, SamCoupeScreenMode.Mode1));

    Assert.Multiple(() => {
      Assert.That(_PixelAt(image, 0, 1), Is.EqualTo(_Expected(0x7F)), "lit pixel on scanline 1");
      Assert.That(_PixelAt(image, 0, 0), Is.EqualTo(_Expected(0x00)), "scanline 0 stays paper");
    });
  }

  [Test]
  public void Mode2_GivesEveryScanlineItsOwnAttribute() {
    var data = _Empty(SamCoupeScreenMode.Mode2);
    _SetPalette(data, SamCoupeScreenMode.Mode2, 1, 0x7F);
    _SetPalette(data, SamCoupeScreenMode.Mode2, 2, 0x02);
    data[0] = 0x80;          // scanline 0, pixel 0 set
    data[32] = 0x80;         // scanline 1, pixel 0 set
    data[8192] = 0x01;       // scanline 0 attribute: ink 1
    data[8192 + 32] = 0x02;  // scanline 1 attribute: ink 2 — impossible in mode 1

    var image = SamCoupeScreenFile.ToRawImage(SamCoupeScreenReader.FromSpan(data, SamCoupeScreenMode.Mode2));

    Assert.Multiple(() => {
      Assert.That(_PixelAt(image, 0, 0), Is.EqualTo(_Expected(0x7F)));
      Assert.That(_PixelAt(image, 0, 1), Is.EqualTo(_Expected(0x02)));
    });
  }

  [Test]
  public void Attributes_PutBrightIntoThePaletteEntrysHighBit() {
    var data = _Empty(SamCoupeScreenMode.Mode2);
    _SetPalette(data, SamCoupeScreenMode.Mode2, 9, 0x7F);
    data[0] = 0x80;
    data[8192] = 0x41;  // bright set, ink 1 -> entry 9

    var image = SamCoupeScreenFile.ToRawImage(SamCoupeScreenReader.FromSpan(data, SamCoupeScreenMode.Mode2));

    Assert.That(_PixelAt(image, 0, 0), Is.EqualTo(_Expected(0x7F)));
  }

  [Test]
  public void Mode3_StoresItsTwoBitPairsBackToFront() {
    var data = _Empty(SamCoupeScreenMode.Mode3);
    for (var i = 0; i < 4; ++i)
      _SetPalette(data, SamCoupeScreenMode.Mode3, i, (byte)(i * 16 + 1));

    data[0] = 0b01_10_00_11;  // pixels 0..3

    var image = SamCoupeScreenFile.ToRawImage(SamCoupeScreenReader.FromSpan(data, SamCoupeScreenMode.Mode3));

    // Each pair is read low-bit-first, so 01 selects entry 2 and 10 selects entry 1.
    Assert.Multiple(() => {
      Assert.That(_PixelAt(image, 0, 0), Is.EqualTo(_Expected(2 * 16 + 1)));
      Assert.That(_PixelAt(image, 1, 0), Is.EqualTo(_Expected(1 * 16 + 1)));
      Assert.That(_PixelAt(image, 2, 0), Is.EqualTo(_Expected(0 * 16 + 1)));
      Assert.That(_PixelAt(image, 3, 0), Is.EqualTo(_Expected(3 * 16 + 1)));
    });
  }

  [Test]
  public void Mode3_DrawsEveryStoredRowTwice() {
    var data = _Empty(SamCoupeScreenMode.Mode3);
    _SetPalette(data, SamCoupeScreenMode.Mode3, 3, 0x7F);
    data[0] = 0xFF;

    var image = SamCoupeScreenFile.ToRawImage(SamCoupeScreenReader.FromSpan(data, SamCoupeScreenMode.Mode3));

    Assert.That(_PixelAt(image, 0, 1), Is.EqualTo(_PixelAt(image, 0, 0)));
  }

  [Test]
  public void AnInterrupt_ChangesAPaletteEntryFromTheNextScanlineOn() {
    var data = _Empty(SamCoupeScreenMode.Mode2, records: 1);
    _SetPalette(data, SamCoupeScreenMode.Mode2, 1, 0x02);
    for (var y = 0; y < 192; ++y) {
      data[y * 32] = 0x80;
      data[8192 + y * 32] = 0x01;
    }

    var interrupt = SamCoupeScreenFile.InterruptOffsetFor(SamCoupeScreenMode.Mode2);
    data[interrupt] = 99;      // takes effect on scanline 100
    data[interrupt + 1] = 1;   // entry 1
    data[interrupt + 2] = 0x7F;

    var image = SamCoupeScreenFile.ToRawImage(SamCoupeScreenReader.FromSpan(data, SamCoupeScreenMode.Mode2));

    Assert.Multiple(() => {
      Assert.That(_PixelAt(image, 0, 99), Is.EqualTo(_Expected(0x02)), "before the interrupt");
      Assert.That(_PixelAt(image, 0, 100), Is.EqualTo(_Expected(0x7F)), "from the interrupt on");
      Assert.That(_PixelAt(image, 0, 191), Is.EqualTo(_Expected(0x7F)), "and it stays changed");
    });
  }

  [Test]
  public void SeveralInterrupts_CanLandOnTheSameScanline() {
    var data = _Empty(SamCoupeScreenMode.Mode2, records: 2);
    for (var y = 0; y < 192; ++y) {
      data[y * 32] = 0x80;
      data[8192 + y * 32] = 0x01;
    }

    var interrupt = SamCoupeScreenFile.InterruptOffsetFor(SamCoupeScreenMode.Mode2);
    data[interrupt] = 9;
    data[interrupt + 1] = 1;
    data[interrupt + 2] = 0x02;
    data[interrupt + 4] = 9;
    data[interrupt + 5] = 1;
    data[interrupt + 6] = 0x7F;

    var image = SamCoupeScreenFile.ToRawImage(SamCoupeScreenReader.FromSpan(data, SamCoupeScreenMode.Mode2));

    Assert.That(_PixelAt(image, 0, 10), Is.EqualTo(_Expected(0x7F)), "the later record wins");
  }

  [Test]
  public void ModeFromExtension_MapsEachScreen() {
    Assert.Multiple(() => {
      Assert.That(SamCoupeScreenFile.ModeFromExtension(".ss1"), Is.EqualTo(SamCoupeScreenMode.Mode1));
      Assert.That(SamCoupeScreenFile.ModeFromExtension(".SS2"), Is.EqualTo(SamCoupeScreenMode.Mode2));
      Assert.That(SamCoupeScreenFile.ModeFromExtension(".ss3"), Is.EqualTo(SamCoupeScreenMode.Mode3));
    });
  }

  [Test]
  public void ModeIsRecoveredFromTheLength_WhenNoExtensionIsAvailable() {
    Assert.Multiple(() => {
      foreach (var mode in new[] { SamCoupeScreenMode.Mode1, SamCoupeScreenMode.Mode2, SamCoupeScreenMode.Mode3 })
        Assert.That(SamCoupeScreenReader.FromBytes(_Empty(mode)).Mode, Is.EqualTo(mode), mode.ToString());
    });
  }

  [Test]
  public void AModeOneScreenStretchedToModeTwosLength_ReadsAsModeTwo() {
    // Every interrupt offset is four-byte aligned, so a mode 1 walk finds a terminator wherever a
    // mode 2 one does. Preferring the larger mode is a choice, and this pins it down: the mode 1
    // reading would need 1856 interrupt records to explain the same bytes.
    var data = _Empty(SamCoupeScreenMode.Mode2);

    Assert.That(SamCoupeScreenReader.FromBytes(data).Mode, Is.EqualTo(SamCoupeScreenMode.Mode2));
    Assert.That(SamCoupeScreenReader.FromSpan(data, SamCoupeScreenMode.Mode1).Mode, Is.EqualTo(SamCoupeScreenMode.Mode1),
      "naming the mode explicitly still works");
  }

  [Test]
  public void Reader_RejectsAnUnterminatedInterruptList() {
    var data = _Empty(SamCoupeScreenMode.Mode1);
    data[^1] = 0;

    Assert.Throws<InvalidDataException>(() => SamCoupeScreenReader.FromSpan(data, SamCoupeScreenMode.Mode1));
  }

  [Test]
  public void Reader_RejectsAFileTooShortToHoldAScreen() {
    Assert.Throws<InvalidDataException>(() => SamCoupeScreenReader.FromBytes(new byte[100]));
  }
}
