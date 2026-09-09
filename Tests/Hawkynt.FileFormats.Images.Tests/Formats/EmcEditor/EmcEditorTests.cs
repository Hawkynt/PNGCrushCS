using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.EmcEditor.Tests;

/// <summary>
/// The layout an EMC-editor file actually has, as opposed to the one it used to be written in.
/// </summary>
/// <remarks>
/// What was written was an ordinary 160 by 200 multicolour screen: one video matrix, not eight, and
/// no notion that the picture starts four raster lines down. The format is a FLI screen — the eight
/// matrices a page apart from offset 2, the bitmap after them, colour memory sixteen kilobytes in
/// and the background register last — showing 192 rows beginning at screen row 4.
/// </remarks>
[TestFixture]
public sealed class EmcEditorLayoutTests {

  private static byte[] _Build(byte background) {
    var data = new byte[EmcEditorFile.FileSize];
    data[0] = 0x00;
    data[1] = 0x40;
    data[EmcEditorFile.BackgroundOffset] = background;

    for (var cell = 0; cell < Commodore64Fli.ColorRamSize; ++cell)
      data[EmcEditorFile.ColorRamOffset + cell] = 0x0B;

    for (var line = 0; line < Commodore64Fli.MatrixCount; ++line)
    for (var cell = 0; cell < Commodore64Fli.MatrixEntries; ++cell)
      data[EmcEditorFile.MatricesOffset + line * Commodore64Fli.MatrixStride + cell] = (byte)(line << 4 | 1);

    for (var i = 0; i < Commodore64Fli.BitmapSize; ++i)
      data[EmcEditorFile.BitmapOffset + i] = 0b01010101;

    return data;
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_Throws()
    => Assert.Throws<ArgumentNullException>(() => EmcEditorReader.FromFile(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_Throws()
    => Assert.Throws<FileNotFoundException>(
      () => EmcEditorReader.FromFile(new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".emc"))));

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_Throws()
    => Assert.Throws<ArgumentNullException>(() => EmcEditorReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void TheOldPlainMulticolourLength_IsNoLongerWhatItWants()
    => Assert.Throws<InvalidDataException>(() => EmcEditorReader.FromBytes(new byte[10002]));

  [Test]
  [Category("Unit")]
  public void ThePartsAreWhereTheMachineAddressesThem() {
    Assert.Multiple(() => {
      Assert.That(EmcEditorFile.FileSize, Is.EqualTo(17412));
      Assert.That(EmcEditorFile.MatricesOffset, Is.EqualTo(2));
      Assert.That(EmcEditorFile.BitmapOffset, Is.EqualTo(8194));
      Assert.That(EmcEditorFile.ColorRamOffset, Is.EqualTo(16386));
      Assert.That(EmcEditorFile.BackgroundOffset, Is.EqualTo(17411));
    });
  }

  [Test]
  [Category("Unit")]
  public void ThePictureIsTwoHundredAndNinetySixByOneHundredAndNinetyTwo() {
    var picture = EmcEditorFile.ToRawImage(EmcEditorReader.FromBytes(_Build(0)));

    Assert.Multiple(() => {
      Assert.That(picture.Width, Is.EqualTo(296));
      Assert.That(picture.Height, Is.EqualTo(192));
    });
  }

  [Test]
  [Category("Unit")]
  public void TheFirstRowShownIsTheFifthRasterLine() {
    // Which matrix speaks for a row follows from where the row is on the screen, not from where it
    // is in the picture — so the top row of the picture takes matrix 4, not matrix 0.
    var picture = EmcEditorFile.ToRawImage(EmcEditorReader.FromBytes(_Build(0)));

    Assert.Multiple(() => {
      for (var row = 0; row < 8; ++row)
        Assert.That(picture.PixelData[row * 296], Is.EqualTo((row + EmcEditorFile.FirstRow) % 8), $"row {row}");
    });
  }

  [Test]
  [Category("Integration")]
  public void WhatIsReadIsWhatIsWritten() {
    var original = EmcEditorReader.FromBytes(_Build(0x0C));
    var bytes = EmcEditorWriter.ToBytes(original);
    var restored = EmcEditorReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(bytes, Has.Length.EqualTo(EmcEditorFile.FileSize));
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
    var original = EmcEditorReader.FromBytes(_Build(0));
    var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".emc");

    try {
      File.WriteAllBytes(path, EmcEditorWriter.ToBytes(original));
      var restored = EmcEditorReader.FromFile(new FileInfo(path));

      Assert.That(restored.BitmapData, Is.EqualTo(original.BitmapData));
    } finally {
      if (File.Exists(path))
        File.Delete(path);
    }
  }
}
