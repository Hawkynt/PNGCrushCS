using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Flimatic.Tests;

/// <summary>
/// The layout a Flimatic file actually has, as opposed to the one it used to be written in.
/// </summary>
/// <remarks>
/// This is the one whose old output appeared to be readable. At 17002 bytes the reference decoder
/// does not recognise the length, falls through to its run-length path, and unpacking bytes that
/// were never packed happened to yield a picture — so the writer looked correct and was not. At
/// 17410 the direct path runs and the picture is the one that was written.
/// </remarks>
[TestFixture]
public sealed class FlimaticLayoutTests {

  private static byte[] _Build(byte background) {
    var data = new byte[FlimaticFile.FileSize];
    data[0] = 0x00;
    data[1] = 0x3C;
    data[FlimaticFile.BackgroundOffset] = background;

    for (var cell = 0; cell < Commodore64Fli.ColorRamSize; ++cell)
      data[FlimaticFile.ColorRamOffset + cell] = 0x0B;

    for (var line = 0; line < Commodore64Fli.MatrixCount; ++line)
    for (var cell = 0; cell < Commodore64Fli.MatrixEntries; ++cell)
      data[FlimaticFile.MatricesOffset + line * Commodore64Fli.MatrixStride + cell] = (byte)(line << 4 | 1);

    for (var i = 0; i < Commodore64Fli.BitmapSize; ++i)
      data[FlimaticFile.BitmapOffset + i] = 0b01010101;

    return data;
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_Throws()
    => Assert.Throws<ArgumentNullException>(() => FlimaticReader.FromFile(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_Throws()
    => Assert.Throws<FileNotFoundException>(
      () => FlimaticReader.FromFile(new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".flm"))));

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_Throws()
    => Assert.Throws<ArgumentNullException>(() => FlimaticReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void TheLengthThatOnlyLookedReadable_IsNoLongerWhatItWants()
    => Assert.Throws<InvalidDataException>(() => FlimaticReader.FromBytes(new byte[17002]));

  [Test]
  [Category("Unit")]
  public void ThePartsAreWhereTheMachineAddressesThem() {
    Assert.Multiple(() => {
      Assert.That(FlimaticFile.FileSize, Is.EqualTo(17410));
      Assert.That(FlimaticFile.ColorRamOffset, Is.EqualTo(2));
      Assert.That(FlimaticFile.MatricesOffset, Is.EqualTo(0x402));
      Assert.That(FlimaticFile.BitmapOffset, Is.EqualTo(0x2402));
      Assert.That(FlimaticFile.BackgroundOffset, Is.EqualTo(17281));
    });
  }

  [Test]
  [Category("Unit")]
  public void ThePictureIsTwoHundredAndNinetySixAcross() {
    var picture = FlimaticFile.ToRawImage(FlimaticReader.FromBytes(_Build(0)));

    Assert.Multiple(() => {
      Assert.That(picture.Width, Is.EqualTo(296));
      Assert.That(picture.Height, Is.EqualTo(200));
    });
  }

  [Test]
  [Category("Unit")]
  public void EachRowOfACellTakesItsColoursFromItsOwnMatrix() {
    var picture = FlimaticFile.ToRawImage(FlimaticReader.FromBytes(_Build(0)));

    Assert.Multiple(() => {
      for (var row = 0; row < Commodore64Fli.MatrixCount; ++row)
        Assert.That(picture.PixelData[row * 296], Is.EqualTo(row), $"row {row} takes matrix {row}");
    });
  }

  [Test]
  [Category("Unit")]
  public void PatternZeroTakesTheBackgroundRegisterInTheTrailer() {
    var data = _Build(0x0C);
    for (var i = 0; i < Commodore64Fli.BitmapSize; ++i)
      data[FlimaticFile.BitmapOffset + i] = 0;

    var picture = FlimaticFile.ToRawImage(FlimaticReader.FromBytes(data));

    Assert.That(picture.PixelData[0], Is.EqualTo(0x0C));
  }

  [Test]
  [Category("Integration")]
  public void WhatIsReadIsWhatIsWritten() {
    var original = FlimaticReader.FromBytes(_Build(0x0C));
    var bytes = FlimaticWriter.ToBytes(original);
    var restored = FlimaticReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(bytes, Has.Length.EqualTo(FlimaticFile.FileSize));
      Assert.That(restored.LoadAddress, Is.EqualTo(original.LoadAddress));
      Assert.That(restored.Background, Is.EqualTo(original.Background));
      Assert.That(restored.ColorRam, Is.EqualTo(original.ColorRam));
      Assert.That(restored.Matrices, Is.EqualTo(original.Matrices));
      Assert.That(restored.BitmapData, Is.EqualTo(original.BitmapData));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var original = FlimaticReader.FromBytes(_Build(0));
    var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".flm");

    try {
      File.WriteAllBytes(path, FlimaticWriter.ToBytes(original));
      var restored = FlimaticReader.FromFile(new FileInfo(path));

      Assert.That(restored.BitmapData, Is.EqualTo(original.BitmapData));
    } finally {
      if (File.Exists(path))
        File.Delete(path);
    }
  }
}
