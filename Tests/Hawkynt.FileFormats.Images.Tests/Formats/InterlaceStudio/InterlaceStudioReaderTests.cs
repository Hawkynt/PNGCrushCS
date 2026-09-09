using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.InterlaceStudio.Tests;

/// <summary>
/// What an Interlace Studio picture is.
/// </summary>
/// <remarks>
/// These used to build a Commodore 64 screen — bitmap, video matrix and colour memory twice over,
/// 19003 bytes — and assert it came back; then a pair of Atari screens of 16208 with a grey ramp
/// stood in for the colours. Every sample is 17184, and the last 800 bytes are the reason: four
/// tables of colour registers, one entry to a raster line, which is what the picture is coloured by.
/// </remarks>
[TestFixture]
public sealed class InterlaceStudioReaderTests {

  /// <summary>Builds a file whose two frames show different registers, so the blend is exercised.</summary>
  private static byte[] _BuildValidFile() {
    var data = new byte[InterlaceStudioFile.FileSize];

    data[0] = 0x11;
    data[1] = 0x35;

    // First frame: patterns 0,1,2,3 across the first four stored pixels. Second: all pattern 3.
    data[InterlaceStudioFile.FirstFrameOffset] = 0b00_01_10_11;
    data[InterlaceStudioFile.SecondFrameOffset] = 0b11_11_11_11;

    // Background black, then three playfield registers a raster line apart from each other.
    for (var y = 0; y < InterlaceStudioFile.RegisterTableSize; ++y) {
      data[InterlaceStudioFile.RegistersOffset + y] = 0x00;
      data[InterlaceStudioFile.RegistersOffset + InterlaceStudioFile.RegisterTableSize + y] = 0x24;
      data[InterlaceStudioFile.RegistersOffset + 2 * InterlaceStudioFile.RegisterTableSize + y] = 0x88;
      data[InterlaceStudioFile.RegistersOffset + 3 * InterlaceStudioFile.RegisterTableSize + y] = 0x0E;
    }

    return data;
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => InterlaceStudioReader.FromFile(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".ist"));

    Assert.Throws<FileNotFoundException>(() => InterlaceStudioReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => InterlaceStudioReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void TheLengthWithoutTheRegisterTables_IsNoLongerWhatItWants()
    => Assert.Throws<InvalidDataException>(() => InterlaceStudioReader.FromBytes(new byte[16208]));

  [Test]
  [Category("Unit")]
  public void ThePartsAreWhereTheMachineAddressesThem() {
    Assert.Multiple(() => {
      Assert.That(InterlaceStudioFile.FileSize, Is.EqualTo(17184));
      Assert.That(InterlaceStudioFile.FirstFrameOffset, Is.EqualTo(16));
      Assert.That(InterlaceStudioFile.SecondFrameOffset, Is.EqualTo(8208));
      Assert.That(InterlaceStudioFile.RegistersOffset, Is.EqualTo(16384));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TakesTheSecondFrameAWholePageAfterTheFirst() {
    // 8192 after the first starts, not 8000. Reading it early draws the second frame from the tail
    // of the first, which blends a picture with a shifted copy of itself.
    var data = _BuildValidFile();
    data[InterlaceStudioFile.SecondFrameOffset] = 0x5A;

    Assert.That(InterlaceStudioReader.FromBytes(data).SecondFrame[0], Is.EqualTo(0x5A));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_IsTheDisplayedSize() {
    var picture = InterlaceStudioFile.ToRawImage(InterlaceStudioReader.FromBytes(_BuildValidFile()));

    Assert.Multiple(() => {
      Assert.That(picture.Width, Is.EqualTo(320));
      Assert.That(picture.Height, Is.EqualTo(200));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_AveragesTheTwoFrames() {
    // A stored pixel is drawn two wide, so the first four are eight columns. Both frames show
    // pattern 3 at the fourth, and there the average is that register itself rather than a blend.
    var picture = InterlaceStudioFile.ToRawImage(InterlaceStudioReader.FromBytes(_BuildValidFile()));
    var thirdRegisterRed = Atari8BitGraphics.Palette[0x0E * 3];

    Assert.Multiple(() => {
      Assert.That(picture.PixelData[6 * 3], Is.EqualTo(thirdRegisterRed));
      Assert.That(picture.PixelData[0], Is.Not.EqualTo(picture.PixelData[6 * 3]));
    });
  }

  [Test]
  [Category("Unit")]
  public void ThePictureIsColouredByTheRegisters() {
    // Change one register table and the picture changes with it, which is what says the tables are
    // read at all — a grey ramp standing in for them cannot show this.
    var data = _BuildValidFile();
    var before = InterlaceStudioFile.ToRawImage(InterlaceStudioReader.FromBytes(data)).PixelData[6 * 3];

    for (var y = 0; y < InterlaceStudioFile.RegisterTableSize; ++y)
      data[InterlaceStudioFile.RegistersOffset + 3 * InterlaceStudioFile.RegisterTableSize + y] = 0x34;

    var after = InterlaceStudioFile.ToRawImage(InterlaceStudioReader.FromBytes(data)).PixelData[6 * 3];

    Assert.That(after, Is.Not.EqualTo(before));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => InterlaceStudioReader.FromStream(null!));
}

[TestFixture]
public sealed class InterlaceStudioRoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_BothFramesTheHeaderAndTheRegistersComeBack() {
    var first = new byte[InterlaceStudioFile.FrameSize];
    var second = new byte[InterlaceStudioFile.FrameSize];
    for (var i = 0; i < first.Length; ++i) {
      first[i] = (byte)(i * 13 % 256);
      second[i] = (byte)(i * 7 % 256);
    }

    var registers = new byte[InterlaceStudioFile.RegisterTableCount * InterlaceStudioFile.RegisterTableSize];
    for (var i = 0; i < registers.Length; ++i)
      registers[i] = (byte)(i * 2 % 256);

    var original = new InterlaceStudioFile {
      Header = [0x11, 0x35, 0xF7, 0x0B, .. new byte[12]],
      FirstFrame = first,
      SecondFrame = second,
      Registers = registers,
    };

    var bytes = InterlaceStudioWriter.ToBytes(original);
    var restored = InterlaceStudioReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(bytes, Has.Length.EqualTo(InterlaceStudioFile.FileSize));
      Assert.That(restored.Header, Is.EqualTo(original.Header));
      Assert.That(restored.FirstFrame, Is.EqualTo(original.FirstFrame));
      Assert.That(restored.SecondFrame, Is.EqualTo(original.SecondFrame));
      Assert.That(restored.Registers, Is.EqualTo(original.Registers));
    });
  }
}
