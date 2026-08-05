using System;
using System.IO;
using FileFormat.CfliDesigner;
using FileFormat.Core;

namespace FileFormat.CfliDesigner.Tests;

/// <summary>
/// What a CFLI Designer picture is.
/// </summary>
/// <remarks>
/// These used to build 17002 bytes of counting pattern read as an ordinary multicolour FLI — bitmap,
/// eight video matrices and colour memory — and assert it came back unchanged, which passed because
/// the reader handed its input through as one block. A CFLI is 8170 bytes and every sample is
/// exactly that, so all of them were refused for being under half the length demanded.
/// <para/>
/// It holds the eight matrices and nothing else. The C is colour: the editor paints only the two
/// colours a cell row shows and the hardware runs a fixed alternating pattern behind them, so a
/// pixel takes the foreground nibble in the even columns of a cell and the background nibble in the
/// odd ones.
/// </remarks>
[TestFixture]
public sealed class CfliDesignerReaderTests {

  /// <summary>Builds a file whose matrices each name a different pair of colours.</summary>
  private static byte[] _BuildValidFile(ushort loadAddress) {
    var data = new byte[CfliDesignerFile.ExpectedFileSize];
    data[0] = (byte)(loadAddress & 0xFF);
    data[1] = (byte)(loadAddress >> 8);

    for (var bank = 0; bank < CfliDesignerFile.ScreenBankCount; ++bank)
      for (var cell = 0; cell < CfliDesignerFile.ScreenBankSize; ++cell)
        data[CfliDesignerFile.LoadAddressSize + bank * CfliDesignerFile.ScreenBankStride + cell]
          = (byte)(bank << 4 | 1);

    return data;
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => CfliDesignerReader.FromFile(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".cfli"));

    Assert.Throws<FileNotFoundException>(() => CfliDesignerReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => CfliDesignerReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => CfliDesignerReader.FromBytes(new byte[8169]));

  [Test]
  [Category("Unit")]
  public void ExpectedFileSize_IsWhatEverySampleIs() {
    // Seven matrices padded to a page and one that is not: 2 + 7 x 1024 + 1000.
    Assert.That(CfliDesignerFile.ExpectedFileSize, Is.EqualTo(8170));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TakesEachMatrixAPageApart() {
    // 1024 apart, not 1000. Reading them packed tight puts every matrix past the first inside the
    // one before it.
    var result = CfliDesignerReader.FromBytes(_BuildValidFile(0x4000));

    Assert.Multiple(() => {
      Assert.That(result.LoadAddress, Is.EqualTo(0x4000));
      Assert.That(result.Screens, Has.Length.EqualTo(8000));
      for (var bank = 0; bank < 8; ++bank)
        Assert.That(result.Screens[bank * CfliDesignerFile.ScreenBankSize] >> 4, Is.EqualTo(bank),
          $"matrix {bank}");
    });
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_IsTwoHundredAndNinetySixAcross() {
    var picture = CfliDesignerFile.ToRawImage(CfliDesignerReader.FromBytes(_BuildValidFile(0x4000)));

    Assert.Multiple(() => {
      Assert.That(picture.Width, Is.EqualTo(296));
      Assert.That(picture.Height, Is.EqualTo(200));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_EachRowOfACellTakesItsColoursFromItsOwnMatrix() {
    var picture = CfliDesignerFile.ToRawImage(CfliDesignerReader.FromBytes(_BuildValidFile(0x4000)));

    // The picture starts 24 pixels in, which is an even column, so it shows the foreground nibble.
    Assert.Multiple(() => {
      for (var row = 0; row < 8; ++row)
        Assert.That(picture.PixelData[row * 296], Is.EqualTo(row), $"row {row} takes matrix {row}");
    });
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_AlternatesTheTwoNibblesAcrossAColumn() {
    // The format stores no bitmap; the pattern behind it never varies, which is what lets a file
    // hold only colour and still draw two of them in a cell.
    var picture = CfliDesignerFile.ToRawImage(CfliDesignerReader.FromBytes(_BuildValidFile(0x4000)));

    Assert.Multiple(() => {
      Assert.That(picture.PixelData[0], Is.EqualTo(0), "an even column takes the foreground");
      Assert.That(picture.PixelData[1], Is.EqualTo(1), "an odd one takes the background");
      Assert.That(picture.PixelData[2], Is.EqualTo(0));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => CfliDesignerReader.FromStream(null!));
}

[TestFixture]
public sealed class CfliDesignerRoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_EveryMatrixComesBack() {
    var screens = new byte[8000];
    for (var i = 0; i < screens.Length; ++i)
      screens[i] = (byte)(i * 11 % 256);

    var original = new CfliDesignerFile { LoadAddress = 0x4000, Screens = screens };

    var restored = CfliDesignerReader.FromBytes(CfliDesignerWriter.ToBytes(original));

    Assert.Multiple(() => {
      Assert.That(restored.LoadAddress, Is.EqualTo(original.LoadAddress));
      Assert.That(restored.Screens, Is.EqualTo(original.Screens));
    });
  }

  [Test]
  [Category("Integration")]
  public void ToBytes_IsTheLengthARealFileHas() {
    var file = new CfliDesignerFile { LoadAddress = 0x4000, Screens = new byte[8000] };

    Assert.That(CfliDesignerWriter.ToBytes(file), Has.Length.EqualTo(8170));
  }
}
