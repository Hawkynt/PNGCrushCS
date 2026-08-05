using System;
using System.IO;
using FileFormat.Afli;
using FileFormat.AtariGraphics10;
using FileFormat.Core;

namespace FileFormat.Afli.Tests;

/// <summary>
/// Two readers that turned down the only files their formats have.
/// </summary>
/// <remarks>
/// AFLI wanted 9218 bytes read as an ordinary high-resolution screen, which is neither the shape nor
/// the length of any AFLI; its sample is 16385 and was refused. Atari Graphics 10 wanted exactly
/// 7680 and its sample is 7689 — the nine extra bytes being the colour registers the picture was
/// drawn with, which the reader then substituted a stock palette for. Both now match RECOIL to the
/// pixel.
/// </remarks>
[TestFixture]
public class AfliLayoutTests {

  /// <summary>Builds an AFLI whose eight matrices each name a different pair of colours.</summary>
  private static byte[] _BuildAfli(int trailing) {
    var data = new byte[AfliFile.MinimumFileSize + trailing];
    data[0] = 0x00;
    data[1] = 0x40;

    for (var screen = 0; screen < AfliFile.ScreenCount; ++screen)
      for (var cell = 0; cell < 1000; ++cell)
        data[AfliFile.ScreensOffset + screen * AfliFile.ScreenStride + cell] = (byte)(screen << 4 | 1);

    // Every other pixel lit, so both nibbles of a matrix entry are reached.
    for (var i = 0; i < AfliFile.BitmapDataSize; ++i)
      data[AfliFile.BitmapOffset + i] = 0xAA;

    return data;
  }

  [Test]
  public void Afli_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => AfliReader.FromBytes(new byte[AfliFile.MinimumFileSize - 1]));

  [Test]
  public void Afli_TheOldFabricatedLength_IsNoLongerWhatItWants()
    => Assert.Throws<InvalidDataException>(() => AfliReader.FromBytes(new byte[9218]));

  [Test]
  public void Afli_AFileRunningOnToTheEndOfItsBlockIsStillRead() {
    // The sample is 16385 against the 16194 the picture needs, the rest being whatever was in memory.
    var file = AfliReader.FromBytes(_BuildAfli(191));

    Assert.Multiple(() => {
      Assert.That(file.LoadAddress, Is.EqualTo(0x4000));
      Assert.That(file.Screens, Has.Length.EqualTo(8 * 1024));
      Assert.That(file.BitmapData, Has.Length.EqualTo(8000));
    });
  }

  [Test]
  public void Afli_IsTwoHundredAndNinetySixAcross() {
    // The colour switch cannot be ready before three cells are drawn, so the left of every row is
    // not part of the picture.
    var picture = AfliFile.ToRawImage(AfliReader.FromBytes(_BuildAfli(0)));

    Assert.Multiple(() => {
      Assert.That(picture.Width, Is.EqualTo(296));
      Assert.That(picture.Height, Is.EqualTo(200));
    });
  }

  [Test]
  public void Afli_EachRowOfACellTakesItsColoursFromItsOwnMatrix() {
    // The whole of what FLI is: matrix n speaks for raster line n, so eight rows of one cell can
    // show eight different foregrounds.
    var picture = AfliFile.ToRawImage(AfliReader.FromBytes(_BuildAfli(0)));

    Assert.Multiple(() => {
      for (var row = 0; row < 8; ++row)
        Assert.That(picture.PixelData[row * 296], Is.EqualTo(row), $"row {row} takes matrix {row}");
    });
  }

  [Test]
  public void Afli_RoundTripsThroughItsOwnWriter() {
    var original = AfliReader.FromBytes(_BuildAfli(191));

    var restored = AfliReader.FromBytes(AfliWriter.ToBytes(original));

    Assert.Multiple(() => {
      Assert.That(restored.Screens, Is.EqualTo(original.Screens));
      Assert.That(restored.BitmapData, Is.EqualTo(original.BitmapData));
      Assert.That(restored.LoadAddress, Is.EqualTo(original.LoadAddress));
    });
  }

  [Test]
  public void AtariGraphics10_TakesAFileCarryingItsColourRegisters() {
    var data = new byte[7680 + 9];
    for (var i = 0; i < 9; ++i)
      data[7680 + i] = (byte)(i * 2);

    var file = AtariGraphics10Reader.FromBytes(data);

    Assert.That(file.Registers, Is.Not.Null);
  }

  [Test]
  public void AtariGraphics10_StillTakesAFileWithoutThem() {
    var file = AtariGraphics10Reader.FromBytes(new byte[7680]);

    Assert.That(file.Registers, Is.Null);
  }

  [Test]
  public void AtariGraphics10_ANeitherLengthIsRefused()
    => Assert.Throws<InvalidDataException>(() => AtariGraphics10Reader.FromBytes(new byte[7685]));

  [Test]
  public void AtariGraphics10_TheStatedRegistersDecideTheColours() {
    // A register is an index into the machine's palette, not a colour, and its lowest bit never
    // reaches the screen — so 0x0E and 0x0F must come out the same.
    var even = new byte[7689];
    var odd = new byte[7689];
    even[7680] = 0x0E;
    odd[7680] = 0x0F;

    var fromEven = AtariGraphics10File.ToRawImage(AtariGraphics10Reader.FromBytes(even));
    var fromOdd = AtariGraphics10File.ToRawImage(AtariGraphics10Reader.FromBytes(odd));

    Assert.Multiple(() => {
      Assert.That(fromEven.Palette![..3], Is.EqualTo(fromOdd.Palette![..3]));
      Assert.That(fromEven.Palette![..3], Is.Not.EqualTo(new byte[] { 0, 0, 0 }), "and is not the stock black");
    });
  }
}
