using System;
using System.IO;
using FileFormat.Core;
using FileFormat.LogoPainter;

namespace FileFormat.LogoPainter.Tests;

/// <summary>
/// What a Logo Painter 3 picture is.
/// </summary>
/// <remarks>
/// These used to build 10002 bytes of counting pattern read as an ordinary multicolour screen —
/// bitmap, video matrix, colour memory — and assert it came back unchanged, which passed because the
/// reader handed its input through as one block. Logo Painter saves no bitmap at all. It saves a
/// character set and a screen of character codes, which is how a logo stays small: 2 + 2048 + 2048 =
/// 4098, and every sample is that or a little more. All were refused for being under half the
/// expected length.
/// <para/>
/// The screen is forty columns by fifty rows rather than the usual twenty-five, so the picture is
/// 320 by 400 with each character four pixels wide shown doubled.
/// </remarks>
[TestFixture]
public sealed class LogoPainterReaderTests {

  /// <summary>
  /// Builds a picture whose first cell uses character one, which shows all four patterns in turn.
  /// </summary>
  private static byte[] _BuildValidFile(ushort loadAddress, byte[]? colors = null) {
    var data = new byte[LogoPainterFile.ExpectedFileSize];
    data[0] = (byte)(loadAddress & 0xFF);
    data[1] = (byte)(loadAddress >> 8);

    // The screen's unused tail is 0xFF where no display routine saved any colours.
    for (var i = LogoPainterFile.Columns * LogoPainterFile.Rows; i < LogoPainterFile.ScreenStride; ++i)
      data[LogoPainterFile.ScreenOffset + i] = 0xFF;

    data[LogoPainterFile.ScreenOffset] = 1;

    // Character one, row nought: patterns 00, 01, 10, 11 across its four pixels.
    data[LogoPainterFile.CharacterSetOffset + 8] = 0b00_01_10_11;

    if (colors != null) {
      data[LogoPainterFile.BackgroundRegisterOffset] = colors[0];
      data[LogoPainterFile.MulticolorRegister1Offset] = colors[1];
      data[LogoPainterFile.MulticolorRegister2Offset] = colors[2];
      data[LogoPainterFile.ColorMemoryOffset] = (byte)(colors[3] | 0x08);
    }

    return data;
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => LogoPainterReader.FromFile(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".lp3"));

    Assert.Throws<FileNotFoundException>(() => LogoPainterReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => LogoPainterReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => LogoPainterReader.FromBytes(new byte[4097]));

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidData_ParsesDimensions() {
    var result = LogoPainterReader.FromBytes(_BuildValidFile(0x1800));

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(320));
      Assert.That(result.Height, Is.EqualTo(400), "fifty character rows, not twenty-five");
      Assert.That(result.LoadAddress, Is.EqualTo(0x1800));
      Assert.That(result.Screen, Has.Length.EqualTo(2000));
      Assert.That(result.CharacterSet, Has.Length.EqualTo(2048));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TheCharacterSetFollowsAWholeScreenPage() {
    // 2048 after the screen starts, not 2000: reading it 48 bytes early draws the logo from the
    // wrong characters entirely.
    var data = _BuildValidFile(0x1800);
    data[LogoPainterFile.CharacterSetOffset] = 0x5A;

    Assert.That(LogoPainterReader.FromBytes(data).CharacterSet[0], Is.EqualTo(0x5A));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_AFileNamingNoColoursGetsTheStockFour() {
    // A tail of 0xFF throughout is not four colours, it is none.
    var result = LogoPainterReader.FromBytes(_BuildValidFile(0x1800));

    Assert.That(result.Colors, Is.EqualTo(new byte[] { 0, 10, 2, 1 }));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_AFileNamingColoursGetsThose() {
    var result = LogoPainterReader.FromBytes(_BuildValidFile(0x1800, [0, 8, 9, 7]));

    Assert.That(result.Colors, Is.EqualTo(new byte[] { 0, 8, 9, 7 }));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ColourMemoryIsMaskedToThreeBitsNotFour() {
    // Its fourth bit is the flag putting the cell in multicolour at all, so 0xFF is seven.
    var data = _BuildValidFile(0x1800, [0, 8, 9, 7]);
    data[LogoPainterFile.ColorMemoryOffset] = 0xFF;

    Assert.That(LogoPainterReader.FromBytes(data).Colors[3], Is.EqualTo(7));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ShowsTheFourPatternsAndDoublesEachPixel() {
    var picture = LogoPainterFile.ToRawImage(LogoPainterReader.FromBytes(_BuildValidFile(0x1800)));

    // Character one's first row is patterns 00, 01, 10, 11, and each is two pixels wide.
    Assert.Multiple(() => {
      Assert.That(picture.PixelData[0], Is.EqualTo(0));
      Assert.That(picture.PixelData[1], Is.EqualTo(0));
      Assert.That(picture.PixelData[2], Is.EqualTo(10));
      Assert.That(picture.PixelData[4], Is.EqualTo(2));
      Assert.That(picture.PixelData[6], Is.EqualTo(1));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => LogoPainterReader.FromStream(null!));
}

[TestFixture]
public sealed class LogoPainterRoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_ScreenCharactersAndColoursAllComeBack() {
    var screen = new byte[2000];
    var characters = new byte[2048];
    for (var i = 0; i < screen.Length; ++i)
      screen[i] = (byte)(i % 256);
    for (var i = 0; i < characters.Length; ++i)
      characters[i] = (byte)(i * 7 % 256);

    var original = new LogoPainterFile {
      LoadAddress = 0x1800, Screen = screen, CharacterSet = characters, Colors = [0, 8, 9, 7],
    };

    var restored = LogoPainterReader.FromBytes(LogoPainterWriter.ToBytes(original));

    Assert.Multiple(() => {
      Assert.That(restored.LoadAddress, Is.EqualTo(original.LoadAddress));
      Assert.That(restored.Screen, Is.EqualTo(original.Screen));
      Assert.That(restored.CharacterSet, Is.EqualTo(original.CharacterSet));
      Assert.That(restored.Colors, Is.EqualTo(original.Colors));
    });
  }

  [Test]
  [Category("Integration")]
  public void ToBytes_IsTheLengthARealFileHas() {
    var file = new LogoPainterFile {
      LoadAddress = 0x1800, Screen = new byte[2000], CharacterSet = new byte[2048],
    };

    Assert.That(LogoPainterWriter.ToBytes(file), Has.Length.EqualTo(4098));
  }
}
