using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.FliDesigner.Tests;

/// <summary>
/// The layout a FLI Designer file actually has, as opposed to the one it used to be written in.
/// </summary>
/// <remarks>
/// What was written was the parts in the order they are named — bitmap, then eight video matrices a
/// thousand bytes apart, then colour memory — which comes to 17002 bytes and is not the format. The
/// file is addressed the way the VIC-II addresses it: colour memory first, the matrices a page apart
/// because that is the granularity the chip's matrix pointer has, and the bitmap last. That is 17218
/// bytes, or 17409 where the picture was saved to the end of its bank.
/// </remarks>
[TestFixture]
public sealed class FliDesignerLayoutTests {

  /// <summary>Builds a picture whose eight matrices each name a different pair of colours.</summary>
  private static byte[] _Build(bool padded) {
    var data = new byte[padded ? FliDesignerFile.PaddedFileSize : FliDesignerFile.FileSize];
    data[0] = 0x00;
    data[1] = 0x3C;

    for (var cell = 0; cell < Commodore64Fli.ColorRamSize; ++cell)
      data[FliDesignerFile.ColorRamOffset + cell] = 0x0B;

    for (var line = 0; line < Commodore64Fli.MatrixCount; ++line)
    for (var cell = 0; cell < Commodore64Fli.MatrixEntries; ++cell)
      data[FliDesignerFile.MatricesOffset + line * Commodore64Fli.MatrixStride + cell] = (byte)(line << 4 | 1);

    // Pattern 01 everywhere, so the high nibble of every matrix entry is what shows.
    for (var i = 0; i < Commodore64Fli.BitmapSize; ++i)
      data[FliDesignerFile.BitmapOffset + i] = 0b01010101;

    return data;
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_Throws()
    => Assert.Throws<ArgumentNullException>(() => FliDesignerReader.FromFile(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_Throws()
    => Assert.Throws<FileNotFoundException>(
      () => FliDesignerReader.FromFile(new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".fd2"))));

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_Throws()
    => Assert.Throws<ArgumentNullException>(() => FliDesignerReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void TheOldFabricatedLength_IsNoLongerWhatItWants()
    => Assert.Throws<InvalidDataException>(() => FliDesignerReader.FromBytes(new byte[17002]));

  [Test]
  [Category("Unit")]
  public void APictureIsSeventeenThousandTwoHundredAndEighteenBytes() {
    Assert.That(FliDesignerFile.FileSize, Is.EqualTo(17218));
    Assert.Multiple(() => {
      Assert.That(FliDesignerFile.ColorRamOffset, Is.EqualTo(2));
      Assert.That(FliDesignerFile.MatricesOffset, Is.EqualTo(0x402));
      Assert.That(FliDesignerFile.BitmapOffset, Is.EqualTo(0x2402));
    });
  }

  [Test]
  [Category("Unit")]
  public void AFileRunningOnToTheEndOfItsBankIsStillRead() {
    var file = FliDesignerReader.FromBytes(_Build(padded: true));

    Assert.Multiple(() => {
      Assert.That(file.LoadAddress, Is.EqualTo(0x3C00));
      Assert.That(file.Padded, Is.True);
      Assert.That(file.Matrices, Has.Length.EqualTo(Commodore64Fli.MatrixAreaSize));
      Assert.That(file.BitmapData, Has.Length.EqualTo(Commodore64Fli.BitmapSize));
    });
  }

  [Test]
  [Category("Unit")]
  public void ThePictureIsTwoHundredAndNinetySixAcross() {
    var picture = FliDesignerFile.ToRawImage(FliDesignerReader.FromBytes(_Build(padded: false)));

    Assert.Multiple(() => {
      Assert.That(picture.Width, Is.EqualTo(296));
      Assert.That(picture.Height, Is.EqualTo(200));
    });
  }

  [Test]
  [Category("Unit")]
  public void EachRowOfACellTakesItsColoursFromItsOwnMatrix() {
    var picture = FliDesignerFile.ToRawImage(FliDesignerReader.FromBytes(_Build(padded: false)));

    Assert.Multiple(() => {
      for (var row = 0; row < Commodore64Fli.MatrixCount; ++row)
        Assert.That(picture.PixelData[row * 296], Is.EqualTo(row), $"row {row} takes matrix {row}");
    });
  }

  [Test]
  [Category("Integration")]
  public void WhatIsReadIsWhatIsWritten() {
    var original = FliDesignerReader.FromBytes(_Build(padded: true));
    var bytes = FliDesignerWriter.ToBytes(original);
    var restored = FliDesignerReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(bytes, Has.Length.EqualTo(FliDesignerFile.PaddedFileSize));
      Assert.That(restored.LoadAddress, Is.EqualTo(original.LoadAddress));
      Assert.That(restored.ColorRam, Is.EqualTo(original.ColorRam));
      Assert.That(restored.Matrices, Is.EqualTo(original.Matrices));
      Assert.That(restored.BitmapData, Is.EqualTo(original.BitmapData));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var original = FliDesignerReader.FromBytes(_Build(padded: false));
    var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".fd2");

    try {
      File.WriteAllBytes(path, FliDesignerWriter.ToBytes(original));
      var restored = FliDesignerReader.FromFile(new FileInfo(path));

      Assert.That(restored.BitmapData, Is.EqualTo(original.BitmapData));
    } finally {
      if (File.Exists(path))
        File.Delete(path);
    }
  }
}
