using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Ffli.Tests;

/// <summary>
/// The layout a Flash FLI file actually has, as opposed to the one it used to be written in.
/// </summary>
/// <remarks>
/// What was written was 17002 bytes of one field with the eight video matrices packed a thousand
/// apart. The format is 26115 and holds two fields that share a bitmap and a colour memory and
/// differ only in their video matrices and background tables, with a lower-case f in the third byte
/// saying so.
/// </remarks>
[TestFixture]
public sealed class FfliLayoutTests {

  private static byte[] _Build() {
    var data = new byte[FfliFile.FileSize];
    data[0] = 0x00;
    data[1] = 0x3B;
    data[FfliFile.SignatureOffset] = FfliFile.Signature;

    for (var cell = 0; cell < Commodore64Fli.ColorRamSize; ++cell)
      data[FfliFile.ColorRamOffset + cell] = 0x0B;

    for (var line = 0; line < Commodore64Fli.MatrixCount; ++line)
    for (var cell = 0; cell < Commodore64Fli.MatrixEntries; ++cell) {
      data[FfliFile.FirstMatricesOffset + line * Commodore64Fli.MatrixStride + cell] = (byte)(line << 4 | 1);
      data[FfliFile.SecondMatricesOffset + line * Commodore64Fli.MatrixStride + cell] = (byte)(line << 4 | 1);
    }

    for (var i = 0; i < Commodore64Fli.BitmapSize; ++i)
      data[FfliFile.BitmapOffset + i] = 0b01010101;

    return data;
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_Throws()
    => Assert.Throws<ArgumentNullException>(() => FfliReader.FromFile(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_Throws()
    => Assert.Throws<FileNotFoundException>(
      () => FfliReader.FromFile(new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".ffl"))));

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_Throws()
    => Assert.Throws<ArgumentNullException>(() => FfliReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void TheOldFabricatedLength_IsNoLongerWhatItWants()
    => Assert.Throws<InvalidDataException>(() => FfliReader.FromBytes(new byte[17002]));

  [Test]
  [Category("Unit")]
  public void AFileWithoutTheSignatureIsRefused() {
    var data = _Build();
    data[FfliFile.SignatureOffset] = 0;

    Assert.Throws<InvalidDataException>(() => FfliReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void ThePartsAreWhereTheMachineAddressesThem() {
    Assert.Multiple(() => {
      Assert.That(FfliFile.FileSize, Is.EqualTo(26115));
      Assert.That(FfliFile.FirstBackgroundsOffset, Is.EqualTo(3));
      Assert.That(FfliFile.ColorRamOffset, Is.EqualTo(259));
      Assert.That(FfliFile.FirstMatricesOffset, Is.EqualTo(1283));
      Assert.That(FfliFile.BitmapOffset, Is.EqualTo(9475));
      Assert.That(FfliFile.SecondMatricesOffset, Is.EqualTo(17667));
      Assert.That(FfliFile.SecondBackgroundsOffset, Is.EqualTo(25859));
    });
  }

  [Test]
  [Category("Unit")]
  public void ThePictureIsTwoHundredAndNinetySixAcross() {
    var picture = FfliFile.ToRawImage(FfliReader.FromBytes(_Build()));

    Assert.Multiple(() => {
      Assert.That(picture.Width, Is.EqualTo(296));
      Assert.That(picture.Height, Is.EqualTo(200));
    });
  }

  [Test]
  [Category("Unit")]
  public void TheTwoFieldsAreAveraged() {
    // With one field naming colour 1 and the other colour 2 for the same pixel, what comes out is
    // neither of them but the average — which is the whole reason the format holds two.
    var data = _Build();
    for (var line = 0; line < Commodore64Fli.MatrixCount; ++line)
    for (var cell = 0; cell < Commodore64Fli.MatrixEntries; ++cell) {
      data[FfliFile.FirstMatricesOffset + line * Commodore64Fli.MatrixStride + cell] = 0x10;
      data[FfliFile.SecondMatricesOffset + line * Commodore64Fli.MatrixStride + cell] = 0x20;
    }

    var picture = FfliFile.ToRawImage(FfliReader.FromBytes(data));
    var white = Commodore64Graphics.HexColors[1];
    var red = Commodore64Graphics.HexColors[2];

    Assert.Multiple(() => {
      Assert.That(picture.PixelData[0], Is.EqualTo((byte)(((white >> 16) & 0xFF) + ((red >> 16) & 0xFF) >> 1)));
      Assert.That(picture.Format, Is.EqualTo(PixelFormat.Rgb24));
    });
  }

  [Test]
  [Category("Integration")]
  public void WhatIsReadIsWhatIsWritten() {
    var original = FfliReader.FromBytes(_Build());
    var bytes = FfliWriter.ToBytes(original);
    var restored = FfliReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(bytes, Has.Length.EqualTo(FfliFile.FileSize));
      Assert.That(restored.ColorRam, Is.EqualTo(original.ColorRam));
      Assert.That(restored.FirstMatrices, Is.EqualTo(original.FirstMatrices));
      Assert.That(restored.SecondMatrices, Is.EqualTo(original.SecondMatrices));
      Assert.That(restored.BitmapData, Is.EqualTo(original.BitmapData));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var original = FfliReader.FromBytes(_Build());
    var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".ffl");

    try {
      File.WriteAllBytes(path, FfliWriter.ToBytes(original));
      var restored = FfliReader.FromFile(new FileInfo(path));

      Assert.That(restored.BitmapData, Is.EqualTo(original.BitmapData));
    } finally {
      if (File.Exists(path))
        File.Delete(path);
    }
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_Throws()
    => Assert.Throws<ArgumentNullException>(() => FfliReader.FromStream(null!));

  [Test]
  [Category("Unit")]
  public void FromStream_ReadsWhatFromBytesReads() {
    using var stream = new MemoryStream(_Build());
    var fromStream = FfliFile.ToRawImage(FfliReader.FromStream(stream));
    var fromBytes = FfliFile.ToRawImage(FfliReader.FromBytes(_Build()));

    Assert.Multiple(() => {
      Assert.That(fromStream.Width, Is.EqualTo(fromBytes.Width));
      Assert.That(fromStream.Height, Is.EqualTo(fromBytes.Height));
      Assert.That(fromStream.PixelData, Is.EqualTo(fromBytes.PixelData));
    });
  }
}
