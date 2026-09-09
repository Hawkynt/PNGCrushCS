using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.FliEditor.Tests;

/// <summary>
/// The layout a FLI Editor file actually has, as opposed to the one it used to be written in.
/// </summary>
/// <remarks>
/// What was written was 17002 bytes holding the bitmap first and the eight video matrices packed a
/// thousand apart, with no background table at all. The format is 17665: the background table one
/// entry to a raster line, colour memory at 258, the matrices a page apart from 1282, and the bitmap
/// at 9474.
/// </remarks>
[TestFixture]
public sealed class FliEditorLayoutTests {

  private static byte[] _Build() {
    var data = new byte[FliEditorFile.FileSize];
    data[0] = 0x00;
    data[1] = 0x3B;

    for (var y = 0; y < FliEditorFile.FixedHeight; ++y)
      data[FliEditorFile.BackgroundsOffset + y] = (byte)(y % Commodore64Graphics.ColorCount);

    for (var cell = 0; cell < Commodore64Fli.ColorRamSize; ++cell)
      data[FliEditorFile.ColorRamOffset + cell] = 0x0B;

    for (var line = 0; line < Commodore64Fli.MatrixCount; ++line)
    for (var cell = 0; cell < Commodore64Fli.MatrixEntries; ++cell)
      data[FliEditorFile.MatricesOffset + line * Commodore64Fli.MatrixStride + cell] = (byte)(line << 4 | 1);

    for (var i = 0; i < Commodore64Fli.BitmapSize; ++i)
      data[FliEditorFile.BitmapOffset + i] = 0b01010101;

    return data;
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_Throws()
    => Assert.Throws<ArgumentNullException>(() => FliEditorReader.FromFile(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_Throws()
    => Assert.Throws<FileNotFoundException>(
      () => FliEditorReader.FromFile(new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".fed"))));

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_Throws()
    => Assert.Throws<ArgumentNullException>(() => FliEditorReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void TheOldFabricatedLength_IsNoLongerWhatItWants()
    => Assert.Throws<InvalidDataException>(() => FliEditorReader.FromBytes(new byte[17002]));

  [Test]
  [Category("Unit")]
  public void ThePartsAreWhereTheMachineAddressesThem() {
    Assert.Multiple(() => {
      Assert.That(FliEditorFile.FileSize, Is.EqualTo(17665));
      Assert.That(FliEditorFile.BackgroundsOffset, Is.EqualTo(8));
      Assert.That(FliEditorFile.ColorRamOffset, Is.EqualTo(258));
      Assert.That(FliEditorFile.MatricesOffset, Is.EqualTo(1282));
      Assert.That(FliEditorFile.BitmapOffset, Is.EqualTo(9474));
    });
  }

  [Test]
  [Category("Unit")]
  public void ThePictureIsTwoHundredAndNinetySixAcross() {
    var picture = FliEditorFile.ToRawImage(FliEditorReader.FromBytes(_Build()));

    Assert.Multiple(() => {
      Assert.That(picture.Width, Is.EqualTo(296));
      Assert.That(picture.Height, Is.EqualTo(200));
    });
  }

  [Test]
  [Category("Unit")]
  public void EachRowOfACellTakesItsColoursFromItsOwnMatrix() {
    var picture = FliEditorFile.ToRawImage(FliEditorReader.FromBytes(_Build()));

    Assert.Multiple(() => {
      for (var row = 0; row < Commodore64Fli.MatrixCount; ++row)
        Assert.That(picture.PixelData[row * 296], Is.EqualTo(row), $"row {row} takes matrix {row}");
    });
  }

  [Test]
  [Category("Unit")]
  public void PatternZeroTakesTheBackgroundOfItsOwnRasterLine() {
    var data = _Build();
    for (var i = 0; i < Commodore64Fli.BitmapSize; ++i)
      data[FliEditorFile.BitmapOffset + i] = 0;

    var picture = FliEditorFile.ToRawImage(FliEditorReader.FromBytes(data));

    Assert.Multiple(() => {
      for (var y = 0; y < 16; ++y)
        Assert.That(picture.PixelData[y * 296], Is.EqualTo(y % Commodore64Graphics.ColorCount), $"line {y}");
    });
  }

  [Test]
  [Category("Integration")]
  public void WhatIsReadIsWhatIsWritten() {
    var original = FliEditorReader.FromBytes(_Build());
    var bytes = FliEditorWriter.ToBytes(original);
    var restored = FliEditorReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(bytes, Has.Length.EqualTo(FliEditorFile.FileSize));
      Assert.That(restored.LoadAddress, Is.EqualTo(original.LoadAddress));
      Assert.That(restored.Backgrounds, Is.EqualTo(original.Backgrounds));
      Assert.That(restored.ColorRam, Is.EqualTo(original.ColorRam));
      Assert.That(restored.Matrices, Is.EqualTo(original.Matrices));
      Assert.That(restored.BitmapData, Is.EqualTo(original.BitmapData));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var original = FliEditorReader.FromBytes(_Build());
    var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".fed");

    try {
      File.WriteAllBytes(path, FliEditorWriter.ToBytes(original));
      var restored = FliEditorReader.FromFile(new FileInfo(path));

      Assert.That(restored.BitmapData, Is.EqualTo(original.BitmapData));
    } finally {
      if (File.Exists(path))
        File.Delete(path);
    }
  }
}
