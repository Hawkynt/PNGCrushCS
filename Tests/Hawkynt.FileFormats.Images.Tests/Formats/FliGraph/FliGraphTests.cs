using System;
using System.IO;
using FileFormat.Core;
using FileFormat.FliGraph;

namespace FileFormat.FliGraph.Tests;

/// <summary>
/// What an FLI Graph picture is.
/// </summary>
/// <remarks>
/// The reader wanted 17474 bytes and read them as a bitmap, a block of screens and colour memory
/// laid end to end. Nothing in an FLI Graph is laid end to end: every block takes a whole page of
/// address space for the thousand bytes it uses, the order is colour memory, then the eight
/// matrices, then the bitmap, and a file is 17409. Every sample was refused.
/// </remarks>
[TestFixture]
public sealed class FliGraphReaderTests {

  /// <summary>Builds a file whose matrices each name a different colour pair.</summary>
  private static byte[] _BuildValidFile() {
    var data = new byte[FliGraphFile.MinimumFileSize];
    data[0] = 0x00;
    data[1] = 0x3F;

    for (var cell = 0; cell < FliGraphFile.BankSize; ++cell)
      data[FliGraphFile.ColorRamOffset + cell] = 0x0C;

    for (var bank = 0; bank < FliGraphFile.ScreenBankCount; ++bank)
      for (var cell = 0; cell < FliGraphFile.BankSize; ++cell)
        data[FliGraphFile.ScreensOffset + bank * FliGraphFile.BankStride + cell] = (byte)(bank << 4 | 1);

    // Every cell: patterns 00, 01, 10, 11 across its four pixels.
    for (var i = 0; i < FliGraphFile.BitmapDataSize; ++i)
      data[FliGraphFile.BitmapOffset + i] = 0b00_01_10_11;

    return data;
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => FliGraphReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => FliGraphReader.FromBytes(new byte[FliGraphFile.MinimumFileSize - 1]));

  [Test]
  [Category("Unit")]
  public void MinimumFileSize_IsWhatThePictureNeeds() {
    // 2 + 1024 + 8 x 1024 + 8000. Every sample is 17409, carrying 191 bytes past the picture, and
    // the old reader wanted 17474 — which is neither.
    Assert.That(FliGraphFile.MinimumFileSize, Is.EqualTo(17218));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_AFileRunningOnPastThePictureIsStillRead() {
    var data = new byte[17409];
    _BuildValidFile().CopyTo(data, 0);

    Assert.That(FliGraphReader.FromBytes(data).BitmapData[0], Is.EqualTo(0b00_01_10_11));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TakesColourMemoryFirstAndTheBitmapLast() {
    var file = FliGraphReader.FromBytes(_BuildValidFile());

    Assert.Multiple(() => {
      Assert.That(file.LoadAddress, Is.EqualTo(0x3F00));
      Assert.That(file.ColorRam, Has.Length.EqualTo(1000));
      Assert.That(file.Screens, Has.Length.EqualTo(8000));
      Assert.That(file.BitmapData, Has.Length.EqualTo(8000));
      Assert.That(file.ColorRam[0], Is.EqualTo(0x0C));
      Assert.That(file.BitmapData[0], Is.EqualTo(0b00_01_10_11));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TakesEachMatrixAPageApart() {
    // 1024 apart, not 1000: packed tight, every matrix past the first lands inside the one before.
    var file = FliGraphReader.FromBytes(_BuildValidFile());

    Assert.Multiple(() => {
      for (var bank = 0; bank < 8; ++bank)
        Assert.That(file.Screens[bank * FliGraphFile.BankSize] >> 4, Is.EqualTo(bank), $"matrix {bank}");
    });
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_IsTwoHundredAndNinetySixAcross() {
    var picture = FliGraphFile.ToRawImage(FliGraphReader.FromBytes(_BuildValidFile()));

    Assert.Multiple(() => {
      Assert.That(picture.Width, Is.EqualTo(296));
      Assert.That(picture.Height, Is.EqualTo(200));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_DrawsEachStoredPixelTwice() {
    // Two bits a pixel, so 148 stored across become 296 drawn. Counting the hidden left margin in
    // four-pixel cells rather than stored pixels halves it and shifts the whole picture.
    var picture = FliGraphFile.ToRawImage(FliGraphReader.FromBytes(_BuildValidFile()));

    Assert.That(picture.PixelData[0], Is.EqualTo(picture.PixelData[1]));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_EachRowOfACellTakesItsColoursFromItsOwnMatrix() {
    var picture = FliGraphFile.ToRawImage(FliGraphReader.FromBytes(_BuildValidFile()));

    // The margin is twelve stored pixels, three whole cells, so the picture starts on a cell
    // boundary at pattern 00 and pattern 01 follows two drawn pixels later.
    Assert.Multiple(() => {
      for (var row = 0; row < 8; ++row)
        Assert.That(picture.PixelData[row * 296 + 2], Is.EqualTo(row), $"row {row} takes matrix {row}");
    });
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_PatternElevenTakesColourMemory() {
    var picture = FliGraphFile.ToRawImage(FliGraphReader.FromBytes(_BuildValidFile()));

    Assert.That(picture.PixelData[6], Is.EqualTo(0x0C));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_EveryBlockComesBack() {
    var original = FliGraphReader.FromBytes(_BuildValidFile());

    var restored = FliGraphReader.FromBytes(FliGraphWriter.ToBytes(original));

    Assert.Multiple(() => {
      Assert.That(restored.ColorRam, Is.EqualTo(original.ColorRam));
      Assert.That(restored.Screens, Is.EqualTo(original.Screens));
      Assert.That(restored.BitmapData, Is.EqualTo(original.BitmapData));
    });
  }
}
