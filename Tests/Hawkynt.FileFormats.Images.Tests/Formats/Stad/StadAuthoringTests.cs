using System;
using FileFormat.Core;
using FileFormat.MsxScreen5;
using FileFormat.Stad;

namespace FileFormat.Stad.Tests;

/// <summary>
/// Building a STAD screen and an MSX Screen 5 picture.
/// </summary>
/// <remarks>
/// Both are checked against RECOIL in the conformance fixture, which needs the tool present; these
/// hold the same facts without it. The Screen 5 one also pins why no palette is written: a saved
/// page runs past the lines that are drawn, so thirty-two bytes appended to the drawn part land
/// where nothing but our own reader looks.
/// </remarks>
[TestFixture]
public class StadAuthoringTests {

  private static RawImage _Flat(int width, int height, byte value) {
    var pixels = new byte[width * height * 3];
    Array.Fill(pixels, value);

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  [Test]
  public void Stad_FromRawImage_NullImage_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => StadFile.FromRawImage(null!));

  [Test]
  public void Stad_FromRawImage_IsAWholeScreen()
    => Assert.That(StadFile.FromRawImage(_Flat(640, 400, 0)).RawData, Has.Length.EqualTo(32000));

  [Test]
  public void Stad_BlackSetsEveryBitAndWhiteClearsThem() {
    Assert.Multiple(() => {
      Assert.That(StadFile.FromRawImage(_Flat(640, 400, 0)).RawData, Is.All.EqualTo(0xFF));
      Assert.That(StadFile.FromRawImage(_Flat(640, 400, 255)).RawData, Is.All.EqualTo(0x00));
    });
  }

  [Test]
  public void Stad_ToBytes_PacksAFlatScreenFarSmallerThanItUnpacksTo() {
    var bytes = StadWriter.ToBytes(StadFile.FromRawImage(_Flat(640, 400, 0)));

    Assert.That(bytes.Length, Is.LessThan(1000), "thirty-two thousand alike bytes must pack small");
  }

  [Test]
  public void Stad_RoundTrip_ComesBackTheSameScreen() {
    var original = StadFile.FromRawImage(_Flat(640, 400, 0));

    var restored = StadReader.FromBytes(StadWriter.ToBytes(original));

    Assert.That(restored.RawData[..32000], Is.EqualTo(original.RawData));
  }

  [Test]
  public void MsxScreen5_FromRawImage_StatesNoPalette() {
    // Writing one makes a file only this project reads correctly.
    var file = MsxScreen5File.FromRawImage(_Flat(256, 212, 0));

    Assert.That(file.Palette, Is.Null);
  }

  [Test]
  public void MsxScreen5_ToBytes_IsTheLengthARealFileHas() {
    // The only sample in the corpus is 27143 bytes: seven of header and the drawn page.
    var bytes = MsxScreen5Writer.ToBytes(MsxScreen5File.FromRawImage(_Flat(256, 212, 0)));

    Assert.That(bytes, Has.Length.EqualTo(27143));
  }

  [Test]
  public void MsxScreen5_ToBytes_OpensWithTheBsaveMagic() {
    var bytes = MsxScreen5Writer.ToBytes(MsxScreen5File.FromRawImage(_Flat(256, 212, 0)));

    Assert.That(bytes[0], Is.EqualTo(MsxScreen5File.BsaveMagic));
  }

  [Test]
  public void MsxScreen5_WhiteAndBlackLandOnTheEntriesTheMachineHasForThem() {
    var white = MsxScreen5File.FromRawImage(_Flat(256, 212, 255));
    var black = MsxScreen5File.FromRawImage(_Flat(256, 212, 0));

    // Entry 15 is white and entry 1 is black in the machine's own sixteen; two pixels to the byte.
    Assert.Multiple(() => {
      Assert.That(white.PixelData[0], Is.EqualTo(0xFF));
      Assert.That(black.PixelData[0], Is.EqualTo(0x00).Or.EqualTo(0x11));
    });
  }

  [Test]
  public void MsxScreen5_RoundTrip_ComesBackAsOneFlatColour() {
    var file = MsxScreen5File.FromRawImage(_Flat(256, 212, 255));

    var drawn = MsxScreen5File.ToRawImage(MsxScreen5Reader.FromBytes(MsxScreen5Writer.ToBytes(file))).ToRgb24();

    Assert.Multiple(() => {
      Assert.That(drawn[..3], Is.EqualTo(new byte[] { 255, 255, 255 }));
      Assert.That(drawn[^3..], Is.EqualTo(new byte[] { 255, 255, 255 }));
    });
  }
}
