using System;
using FileFormat.Core;
using FileFormat.EggPaint;
using FileFormat.FunGraphicsMachine;
using FileFormat.Gigacad;

namespace FileFormat.EggPaint.Tests;

/// <summary>
/// Three formats that gained authoring, two of which were writing a shape no file has.
/// </summary>
/// <remarks>
/// Fun Graphics Machine wrote 9009 bytes with a screen and Gigacad wrote the Atari length of 32000,
/// and RECOIL accepts neither at those extensions: every sample is the other shape, 8002 and 8194
/// bytes. Both writers now produce what the samples are, and RECOIL draws all three exactly as we
/// do.
/// </remarks>
[TestFixture]
public class EggPaintAuthoringTests {

  private static RawImage _Flat(int width, int height, byte red, byte green, byte blue) {
    var pixels = new byte[width * height * 3];
    for (var i = 0; i < pixels.Length; i += 3) {
      pixels[i] = red;
      pixels[i + 1] = green;
      pixels[i + 2] = blue;
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  [Test]
  public void EggPaint_FromRawImage_NullImage_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => EggPaintFile.FromRawImage(null!));

  [Test]
  public void EggPaint_PacksSixBitsOfGreenAndFiveOfTheRest() {
    // Full green only: six bits in the middle of a big-endian word.
    var file = EggPaintFile.FromRawImage(_Flat(1, 1, 0, 255, 0));

    Assert.That(file.PixelData, Is.EqualTo(new byte[] { 0x07, 0xE0 }));
  }

  [Test]
  public void EggPaint_WritesTheWordBigEndian() {
    var file = EggPaintFile.FromRawImage(_Flat(1, 1, 255, 0, 0));

    Assert.That(file.PixelData, Is.EqualTo(new byte[] { 0xF8, 0x00 }), "red sits in the high bits");
  }

  [Test]
  public void EggPaint_ToBytes_OpensWithItsSignature() {
    var bytes = EggPaintWriter.ToBytes(EggPaintFile.FromRawImage(_Flat(8, 8, 0, 0, 0)));

    Assert.That(bytes[..4], Is.EqualTo("TRUP"u8.ToArray()));
  }

  [Test]
  public void EggPaint_RoundTrip_KeepsWhatSixteenBitsCanHold() {
    var source = _Flat(16, 16, 255, 255, 255);

    var restored = EggPaintFile.ToRawImage(EggPaintReader.FromBytes(EggPaintWriter.ToBytes(EggPaintFile.FromRawImage(source))));

    Assert.That(restored.ToRgb24(), Is.All.EqualTo(255));
  }

  [Test]
  public void FunGraphicsMachine_ToBytes_IsTheLengthTheSamplesAre() {
    // 8002: a load address and the bitmap, with no screen memory at all.
    var bytes = FunGraphicsMachineWriter.ToBytes(FunGraphicsMachineFile.FromRawImage(_Flat(320, 200, 0, 0, 0)));

    Assert.That(bytes, Has.Length.EqualTo(FunGraphicsMachineFile.BitmapOnlyFileSize));
  }

  [Test]
  public void FunGraphicsMachine_ToBytes_KeepsTheLongerShapeForAFileThatNeedsIt() {
    var coloured = new FunGraphicsMachineFile {
      LoadAddress = 0x4000,
      BitmapData = new byte[8000],
      ScreenRam = _Filled(1000, 0x71),
    };

    Assert.That(FunGraphicsMachineWriter.ToBytes(coloured), Has.Length.EqualTo(FunGraphicsMachineFile.ExpectedFileSize));
  }

  [Test]
  public void FunGraphicsMachine_RoundTrip_AWhitePictureComesBackWhite() {
    var bytes = FunGraphicsMachineWriter.ToBytes(FunGraphicsMachineFile.FromRawImage(_Flat(320, 200, 255, 255, 255)));

    var drawn = FunGraphicsMachineFile.ToRawImage(FunGraphicsMachineReader.FromBytes(bytes)).ToRgb24();

    Assert.That(drawn[..3], Is.EqualTo(new byte[] { 255, 255, 255 }));
  }

  [Test]
  public void Gigacad_ToBytes_IsTheLengthTheSamplesAre() {
    var bytes = GigacadWriter.ToBytes(GigacadFile.FromRawImage(_Flat(320, 200, 0, 0, 0)));

    Assert.That(bytes, Has.Length.EqualTo(GigacadFile.CommodoreFileSize));
  }

  [Test]
  public void Gigacad_RoundTrip_BlackAndWhiteComeBackTheRightWayRound() {
    // A set bit is paper in this format, which is the opposite of most screens of the period.
    var white = GigacadFile.ToRawImage(GigacadReader.FromBytes(
      GigacadWriter.ToBytes(GigacadFile.FromRawImage(_Flat(320, 200, 255, 255, 255))))).ToRgb24();
    var black = GigacadFile.ToRawImage(GigacadReader.FromBytes(
      GigacadWriter.ToBytes(GigacadFile.FromRawImage(_Flat(320, 200, 0, 0, 0))))).ToRgb24();

    Assert.Multiple(() => {
      Assert.That(white[..3], Is.EqualTo(new byte[] { 255, 255, 255 }));
      Assert.That(black[..3], Is.EqualTo(new byte[] { 0, 0, 0 }));
    });
  }

  [Test]
  public void Gigacad_RowsAndCellsAreInverses() {
    var rows = new byte[8000];
    for (var i = 0; i < rows.Length; ++i)
      rows[i] = (byte)(i * 31 % 256);

    var back = GigacadFile.CellsToRows(GigacadFile.RowsToCells(rows, 320, 200), 320, 200);

    Assert.That(back, Is.EqualTo(rows));
  }

  private static byte[] _Filled(int length, byte value) {
    var data = new byte[length];
    Array.Fill(data, value);
    return data;
  }
}
