using System;
using FileFormat.AmicaPaint;
using FileFormat.Core;
using FileFormat.InterPaintHi;
using FileFormat.KoalaCompressed;
using FileFormat.MicroIllustrator;
using FileFormat.RunPaint;
using FileFormat.Vidcom64;

namespace FileFormat.C64Authoring.Tests;

/// <summary>
/// The Commodore 64 formats that gained a way to build a screen from a picture.
/// </summary>
/// <remarks>
/// Each of these was already able to serialize a screen it had read and unable to make one, so the
/// registry listed them as read-only. What is checked here is the part a round-trip through our own
/// reader cannot check on its own: that the file comes out the length a real one is, and that a flat
/// picture of one of the machine's own colours survives the reduction unchanged.
/// <para/>
/// RECOIL reads every one of these and draws what we draw; that comparison lives in the conformance
/// fixture, which needs the tool present. These hold the same facts without it.
/// </remarks>
[TestFixture]
public class MulticolourAuthoringTests {

  /// <summary>A screen of one colour the machine has exactly, so the reduction costs nothing.</summary>
  private static RawImage _Flat(int width, int height, int machineColour) {
    var colour = Commodore64Graphics.HexColors[machineColour];
    var pixels = new byte[width * height * 3];
    for (var i = 0; i < pixels.Length; i += 3) {
      pixels[i] = (byte)((colour >> 16) & 0xFF);
      pixels[i + 1] = (byte)((colour >> 8) & 0xFF);
      pixels[i + 2] = (byte)(colour & 0xFF);
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  [Test]
  public void AmicaPaint_WritesTheLengthARealFileHas() {
    var bytes = AmicaPaintWriter.ToBytes(AmicaPaintFile.FromRawImage(_Flat(160, 200, 6)));

    Assert.That(bytes, Has.Length.EqualTo(AmicaPaintFile.ExpectedFileSize));
  }

  [Test]
  public void AmicaPaint_AFlatScreenSurvives() {
    var file = AmicaPaintFile.FromRawImage(_Flat(160, 200, 6));

    var drawn = AmicaPaintFile.ToRawImage(AmicaPaintReader.FromBytes(AmicaPaintWriter.ToBytes(file))).ToRgb24();

    Assert.That(drawn[..3], Is.EqualTo(_Bytes(Commodore64Graphics.HexColors[6])));
  }

  [Test]
  public void RunPaint_AFlatScreenSurvives() {
    var file = RunPaintFile.FromRawImage(_Flat(160, 200, 2));

    var drawn = RunPaintFile.ToRawImage(RunPaintReader.FromBytes(RunPaintWriter.ToBytes(file))).ToRgb24();

    Assert.That(drawn[..3], Is.EqualTo(_Bytes(Commodore64Graphics.HexColors[2])));
  }

  [Test]
  public void Vidcom64_WritesTheLengthARealFileHasWithItsHeaderEmpty() {
    var file = Vidcom64File.FromRawImage(_Flat(160, 200, 5));

    Assert.Multiple(() => {
      Assert.That(Vidcom64Writer.ToBytes(file), Has.Length.EqualTo(Vidcom64File.ExpectedFileSize));
      Assert.That(file.HeaderData, Is.All.EqualTo(0));
    });
  }

  [Test]
  public void Vidcom64_AFlatScreenSurvives() {
    var file = Vidcom64File.FromRawImage(_Flat(160, 200, 5));

    var drawn = Vidcom64File.ToRawImage(Vidcom64Reader.FromBytes(Vidcom64Writer.ToBytes(file))).ToRgb24();

    Assert.That(drawn[..3], Is.EqualTo(_Bytes(Commodore64Graphics.HexColors[5])));
  }

  [Test]
  public void KoalaCompressed_PacksAFlatScreenFarSmallerThanItUnpacksTo() {
    var bytes = KoalaCompressedWriter.ToBytes(KoalaCompressedFile.FromRawImage(_Flat(160, 200, 1)));

    Assert.That(bytes.Length, Is.LessThan(2000), "ten thousand bytes of one colour must pack small");
  }

  [Test]
  public void KoalaCompressed_AFlatScreenSurvives() {
    var file = KoalaCompressedFile.FromRawImage(_Flat(160, 200, 1));

    var drawn = KoalaCompressedFile.ToRawImage(KoalaCompressedReader.FromBytes(KoalaCompressedWriter.ToBytes(file))).ToRgb24();

    Assert.That(drawn[..3], Is.EqualTo(_Bytes(Commodore64Graphics.HexColors[1])));
  }

  [Test]
  public void MicroIllustrator_AFlatScreenSurvives() {
    var file = MicroIllustratorFile.FromRawImage(_Flat(160, 200, 4));

    var drawn = MicroIllustratorFile.ToRawImage(MicroIllustratorReader.FromBytes(MicroIllustratorWriter.ToBytes(file))).ToRgb24();

    Assert.That(drawn[..3], Is.EqualTo(_Bytes(Commodore64Graphics.HexColors[4])));
  }

  [Test]
  public void InterPaintHi_IsThreeHundredAndTwentyAcross() {
    // The only one of these that is high resolution rather than multicolour.
    var file = InterPaintHiFile.FromRawImage(_Flat(320, 200, 1));

    var drawn = InterPaintHiFile.ToRawImage(InterPaintHiReader.FromBytes(InterPaintHiWriter.ToBytes(file)));

    Assert.Multiple(() => {
      Assert.That(drawn.Width, Is.EqualTo(320));
      Assert.That(drawn.ToRgb24()[..3], Is.EqualTo(_Bytes(Commodore64Graphics.HexColors[1])));
    });
  }

  [Test]
  public void EveryOneOfThemAcceptsAPictureOfAnotherSize() {
    // The screens are one fixed shape, so anything else is resampled rather than refused.
    var small = new RawImage { Width = 7, Height = 5, Format = PixelFormat.Rgb24, PixelData = new byte[7 * 5 * 3] };

    Assert.Multiple(() => {
      Assert.That(AmicaPaintWriter.ToBytes(AmicaPaintFile.FromRawImage(small)), Is.Not.Empty);
      Assert.That(RunPaintWriter.ToBytes(RunPaintFile.FromRawImage(small)), Is.Not.Empty);
      Assert.That(Vidcom64Writer.ToBytes(Vidcom64File.FromRawImage(small)), Is.Not.Empty);
      Assert.That(KoalaCompressedWriter.ToBytes(KoalaCompressedFile.FromRawImage(small)), Is.Not.Empty);
      Assert.That(MicroIllustratorWriter.ToBytes(MicroIllustratorFile.FromRawImage(small)), Is.Not.Empty);
      Assert.That(InterPaintHiWriter.ToBytes(InterPaintHiFile.FromRawImage(small)), Is.Not.Empty);
    });
  }

  private static byte[] _Bytes(int colour)
    => [(byte)((colour >> 16) & 0xFF), (byte)((colour >> 8) & 0xFF), (byte)(colour & 0xFF)];
}
