using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.HiresFliCrest.Tests;

/// <summary>
/// The layout a Hires FLI file actually has, as opposed to the one it used to be written in.
/// </summary>
/// <remarks>
/// What was written was 17002 bytes at 320 by 200 with the eight video matrices packed a thousand
/// apart. The format is 16386: the bitmap in a whole eight kilobytes, then the matrices a page
/// apart, showing 112 rows of 296 pixels.
/// </remarks>
[TestFixture]
public sealed class HiresFliCrestLayoutTests {

  private static byte[] _Build() {
    var data = new byte[HiresFliCrestFile.FileSize];
    data[0] = 0x00;
    data[1] = 0x40;

    for (var line = 0; line < Commodore64Fli.MatrixCount; ++line)
    for (var cell = 0; cell < Commodore64Fli.MatrixEntries; ++cell)
      data[HiresFliCrestFile.MatricesOffset + line * Commodore64Fli.MatrixStride + cell] = (byte)(line << 4 | 1);

    // Every other pixel lit, so both nibbles of a matrix entry are reached.
    for (var i = 0; i < HiresFliCrestFile.BitmapAreaSize; ++i)
      data[HiresFliCrestFile.BitmapOffset + i] = 0xAA;

    return data;
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_Throws()
    => Assert.Throws<ArgumentNullException>(() => HiresFliCrestReader.FromFile(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_Throws()
    => Assert.Throws<FileNotFoundException>(
      () => HiresFliCrestReader.FromFile(new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".hfc"))));

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_Throws()
    => Assert.Throws<ArgumentNullException>(() => HiresFliCrestReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void TheOldFabricatedLength_IsNoLongerWhatItWants()
    => Assert.Throws<InvalidDataException>(() => HiresFliCrestReader.FromBytes(new byte[16002]));

  [Test]
  [Category("Unit")]
  public void ThePartsAreWhereTheMachineAddressesThem() {
    Assert.Multiple(() => {
      Assert.That(HiresFliCrestFile.FileSize, Is.EqualTo(16386));
      Assert.That(HiresFliCrestFile.BitmapOffset, Is.EqualTo(2));
      Assert.That(HiresFliCrestFile.MatricesOffset, Is.EqualTo(8194));
    });
  }

  [Test]
  [Category("Unit")]
  public void ThePictureIsTwoHundredAndNinetySixByOneHundredAndTwelve() {
    var picture = HiresFliCrestFile.ToRawImage(HiresFliCrestReader.FromBytes(_Build()));

    Assert.Multiple(() => {
      Assert.That(picture.Width, Is.EqualTo(296));
      Assert.That(picture.Height, Is.EqualTo(112));
    });
  }

  [Test]
  [Category("Unit")]
  public void EachRowOfACellTakesItsColoursFromItsOwnMatrix() {
    var picture = HiresFliCrestFile.ToRawImage(HiresFliCrestReader.FromBytes(_Build()));

    Assert.Multiple(() => {
      for (var row = 0; row < Commodore64Fli.MatrixCount; ++row)
        Assert.That(picture.PixelData[row * 296], Is.EqualTo(row), $"row {row} takes matrix {row}");
    });
  }

  [Test]
  [Category("Integration")]
  public void WhatIsReadIsWhatIsWritten() {
    var original = HiresFliCrestReader.FromBytes(_Build());
    var bytes = HiresFliCrestWriter.ToBytes(original);
    var restored = HiresFliCrestReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(bytes, Has.Length.EqualTo(HiresFliCrestFile.FileSize));
      Assert.That(restored.LoadAddress, Is.EqualTo(original.LoadAddress));
      Assert.That(restored.Matrices, Is.EqualTo(original.Matrices));
      Assert.That(restored.BitmapData, Is.EqualTo(original.BitmapData));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var original = HiresFliCrestReader.FromBytes(_Build());
    var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".hfc");

    try {
      File.WriteAllBytes(path, HiresFliCrestWriter.ToBytes(original));
      var restored = HiresFliCrestReader.FromFile(new FileInfo(path));

      Assert.That(restored.BitmapData, Is.EqualTo(original.BitmapData));
    } finally {
      if (File.Exists(path))
        File.Delete(path);
    }
  }
}
