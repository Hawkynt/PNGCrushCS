using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.HiresManager.Tests;

/// <summary>
/// The layout a Hires Manager file actually has, as opposed to the one it used to be written in.
/// </summary>
/// <remarks>
/// The format is 16385 bytes — the whole bank the picture lived in — with the bitmap 320 bytes in
/// and the eight video matrices 8232 bytes in, a page apart. It shows 192 rows of 296 pixels, and
/// the fourth byte has to say $FF or the file is read as a run-length stream and refused.
/// </remarks>
[TestFixture]
public sealed class HiresManagerLayoutTests {

  private static byte[] _Build() {
    var data = new byte[HiresManagerFile.FileSize];
    data[0] = 0x00;
    data[1] = 0x40;
    data[HiresManagerFile.PackingFlagOffset] = HiresManagerFile.NotPacked;

    for (var line = 0; line < Commodore64Fli.MatrixCount; ++line)
    for (var cell = 0; cell < HiresManagerFile.MatrixEntries; ++cell)
      data[HiresManagerFile.MatricesOffset + line * Commodore64Fli.MatrixStride + cell] = (byte)(line << 4 | 1);

    for (var i = 0; i < HiresManagerFile.BitmapSize; ++i)
      data[HiresManagerFile.BitmapOffset + i] = 0xAA;

    return data;
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_Throws()
    => Assert.Throws<ArgumentNullException>(() => HiresManagerReader.FromFile(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_Throws()
    => Assert.Throws<FileNotFoundException>(
      () => HiresManagerReader.FromFile(new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".him"))));

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_Throws()
    => Assert.Throws<ArgumentNullException>(() => HiresManagerReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void AnythingShorterThanTheWholeBankIsRefused()
    => Assert.Throws<InvalidDataException>(() => HiresManagerReader.FromBytes(new byte[16384]));

  [Test]
  [Category("Unit")]
  public void ThePartsAreWhereTheMachineAddressesThem() {
    Assert.Multiple(() => {
      Assert.That(HiresManagerFile.FileSize, Is.EqualTo(16385));
      Assert.That(HiresManagerFile.BitmapOffset, Is.EqualTo(322));
      Assert.That(HiresManagerFile.MatricesOffset, Is.EqualTo(8234));
      Assert.That(HiresManagerFile.MatrixEntries, Is.EqualTo(960));
    });
  }

  [Test]
  [Category("Unit")]
  public void ThePictureIsTwoHundredAndNinetySixByOneHundredAndNinetyTwo() {
    var picture = HiresManagerFile.ToRawImage(HiresManagerReader.FromBytes(_Build()));

    Assert.Multiple(() => {
      Assert.That(picture.Width, Is.EqualTo(296));
      Assert.That(picture.Height, Is.EqualTo(192));
    });
  }

  [Test]
  [Category("Unit")]
  public void EachRowOfACellTakesItsColoursFromItsOwnMatrix() {
    var picture = HiresManagerFile.ToRawImage(HiresManagerReader.FromBytes(_Build()));

    Assert.Multiple(() => {
      for (var row = 0; row < Commodore64Fli.MatrixCount; ++row)
        Assert.That(picture.PixelData[row * 296], Is.EqualTo(row), $"row {row} takes matrix {row}");
    });
  }

  [Test]
  [Category("Integration")]
  public void WhatIsWrittenSaysItIsNotPacked() {
    var bytes = HiresManagerWriter.ToBytes(HiresManagerReader.FromBytes(_Build()));

    Assert.Multiple(() => {
      Assert.That(bytes, Has.Length.EqualTo(HiresManagerFile.FileSize));
      Assert.That(bytes[0], Is.EqualTo(0x00));
      Assert.That(bytes[1], Is.EqualTo(0x40));
      Assert.That(bytes[HiresManagerFile.PackingFlagOffset], Is.EqualTo(HiresManagerFile.NotPacked));
    });
  }

  [Test]
  [Category("Integration")]
  public void WhatIsReadIsWhatIsWritten() {
    var original = HiresManagerReader.FromBytes(_Build());
    var restored = HiresManagerReader.FromBytes(HiresManagerWriter.ToBytes(original));

    Assert.Multiple(() => {
      Assert.That(restored.Matrices, Is.EqualTo(original.Matrices));
      Assert.That(restored.BitmapData, Is.EqualTo(original.BitmapData));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var original = HiresManagerReader.FromBytes(_Build());
    var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".him");

    try {
      File.WriteAllBytes(path, HiresManagerWriter.ToBytes(original));
      var restored = HiresManagerReader.FromFile(new FileInfo(path));

      Assert.That(restored.BitmapData, Is.EqualTo(original.BitmapData));
    } finally {
      if (File.Exists(path))
        File.Delete(path);
    }
  }
}
