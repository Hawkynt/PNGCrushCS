using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.FliProfi.Tests;

/// <summary>
/// The layout a FLI Profi file actually has, as opposed to the one it used to be written in.
/// </summary>
/// <remarks>
/// What was written was 17002 bytes of bitmap, eight video matrices packed a thousand apart and
/// colour memory, with nothing for the sprite border. The format is 18370: sprites at 2, their
/// colours at 642, the border's pattern-11 colours at 898, the two shared registers at 1098, colour
/// memory at 1154, the matrices a page apart from 2178 and the bitmap at 10370.
/// </remarks>
[TestFixture]
public sealed class FliProfiLayoutTests {

  private static byte[] _Build() {
    var data = new byte[FliProfiFile.FileSize];
    data[0] = 0x80;
    data[1] = 0x37;

    for (var cell = 0; cell < Commodore64Fli.ColorRamSize; ++cell)
      data[FliProfiFile.ColorRamOffset + cell] = 0x0B;

    for (var line = 0; line < Commodore64Fli.MatrixCount; ++line)
    for (var cell = 0; cell < Commodore64Fli.MatrixEntries; ++cell)
      data[FliProfiFile.MatricesOffset + line * Commodore64Fli.MatrixStride + cell] = (byte)(line << 4 | 1);

    for (var i = 0; i < Commodore64Fli.BitmapSize; ++i)
      data[FliProfiFile.BitmapOffset + i] = 0b01010101;

    return data;
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_Throws()
    => Assert.Throws<ArgumentNullException>(() => FliProfiReader.FromFile(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_Throws()
    => Assert.Throws<FileNotFoundException>(
      () => FliProfiReader.FromFile(new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".fpr"))));

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_Throws()
    => Assert.Throws<ArgumentNullException>(() => FliProfiReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void TheOldFabricatedLength_IsNoLongerWhatItWants()
    => Assert.Throws<InvalidDataException>(() => FliProfiReader.FromBytes(new byte[17002]));

  [Test]
  [Category("Unit")]
  public void ThePartsAreWhereTheMachineAddressesThem() {
    Assert.Multiple(() => {
      Assert.That(FliProfiFile.FileSize, Is.EqualTo(18370));
      Assert.That(FliProfiFile.SpritesOffset, Is.EqualTo(2));
      Assert.That(FliProfiFile.SpriteColorsOffset, Is.EqualTo(642));
      Assert.That(FliProfiFile.BorderColorsOffset, Is.EqualTo(898));
      Assert.That(FliProfiFile.ColorRamOffset, Is.EqualTo(1154));
      Assert.That(FliProfiFile.MatricesOffset, Is.EqualTo(2178));
      Assert.That(FliProfiFile.BitmapOffset, Is.EqualTo(10370));
    });
  }

  [Test]
  [Category("Unit")]
  public void ThePictureIsTheWholeRowBecauseSpritesCoverTheLeftOfIt() {
    var picture = FliProfiFile.ToRawImage(FliProfiReader.FromBytes(_Build()));

    Assert.Multiple(() => {
      Assert.That(picture.Width, Is.EqualTo(320));
      Assert.That(picture.Height, Is.EqualTo(200));
    });
  }

  [Test]
  [Category("Unit")]
  public void EachRowOfACellTakesItsColoursFromItsOwnMatrix() {
    // Read past the three cells the sprites cover, which have no matrix of their own.
    var picture = FliProfiFile.ToRawImage(FliProfiReader.FromBytes(_Build()));

    Assert.Multiple(() => {
      for (var row = 0; row < Commodore64Fli.MatrixCount; ++row)
        Assert.That(picture.PixelData[row * 320 + 24], Is.EqualTo(row), $"row {row} takes matrix {row}");
    });
  }

  [Test]
  [Category("Unit")]
  public void ASpriteOverThePixelWinsOverTheBitmap() {
    var data = _Build();
    data[FliProfiFile.SpriteColorsOffset] = 0x07;

    // Pattern 01 in the sprite covering the top-left pixel.
    data[FliProfiFile.SpritesOffset] = 0b01000000;

    var picture = FliProfiFile.ToRawImage(FliProfiReader.FromBytes(data));

    Assert.That(picture.PixelData[0], Is.EqualTo(0x07));
  }

  [Test]
  [Category("Integration")]
  public void WhatIsReadIsWhatIsWritten() {
    var original = FliProfiReader.FromBytes(_Build());
    var bytes = FliProfiWriter.ToBytes(original);
    var restored = FliProfiReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(bytes, Has.Length.EqualTo(FliProfiFile.FileSize));
      Assert.That(restored.LoadAddress, Is.EqualTo(original.LoadAddress));
      Assert.That(restored.Sprites, Is.EqualTo(original.Sprites));
      Assert.That(restored.ColorRam, Is.EqualTo(original.ColorRam));
      Assert.That(restored.Matrices, Is.EqualTo(original.Matrices));
      Assert.That(restored.BitmapData, Is.EqualTo(original.BitmapData));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var original = FliProfiReader.FromBytes(_Build());
    var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".fpr");

    try {
      File.WriteAllBytes(path, FliProfiWriter.ToBytes(original));
      var restored = FliProfiReader.FromFile(new FileInfo(path));

      Assert.That(restored.BitmapData, Is.EqualTo(original.BitmapData));
    } finally {
      if (File.Exists(path))
        File.Delete(path);
    }
  }
}
