using System;
using System.IO;
using FileFormat.Core;
using FileFormat.InterlaceStudio;

namespace FileFormat.InterlaceStudio.Tests;

/// <summary>
/// What an Interlace Studio picture is.
/// </summary>
/// <remarks>
/// These used to build a Commodore 64 screen — bitmap, video matrix and colour memory twice over,
/// 19003 bytes — and assert it came back. Interlace Studio is an Atari program. Every sample is
/// 17184 bytes and all were refused, and the giveaway was not the length but the colours: of the
/// seven the reference tool draws, two are in the Commodore's sixteen and all seven are in the
/// Atari's, so no arrangement of a C64 screen could ever have matched.
/// </remarks>
[TestFixture]
public sealed class InterlaceStudioReaderTests {

  /// <summary>Builds a file whose two frames show different levels, so the blend is exercised.</summary>
  private static byte[] _BuildValidFile() {
    var data = new byte[InterlaceStudioFile.MinimumFileSize];

    // Four bytes that look like colour registers, as every sample has.
    data[0] = 0x11;
    data[1] = 0x35;

    // First frame: patterns 0,1,2,3 across the first four pixels. Second: all pattern 3.
    data[InterlaceStudioFile.FirstFrameOffset] = 0b00_01_10_11;
    data[InterlaceStudioFile.SecondFrameOffset] = 0b11_11_11_11;

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
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => InterlaceStudioReader.FromBytes(new byte[16207]));

  [Test]
  [Category("Unit")]
  public void MinimumFileSize_FitsWhatEverySampleIs() {
    // Sixteen of header, a frame taking a whole page, and a second frame: 16 + 8192 + 8000.
    Assert.That(InterlaceStudioFile.MinimumFileSize, Is.EqualTo(16208));
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
  public void ToRawImage_IsTheStoredSize() {
    var picture = InterlaceStudioFile.ToRawImage(InterlaceStudioReader.FromBytes(_BuildValidFile()));

    Assert.Multiple(() => {
      Assert.That(picture.Width, Is.EqualTo(160));
      Assert.That(picture.Height, Is.EqualTo(200));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_AveragesTheTwoFrames() {
    // Four levels a frame, blending to the seven the picture shows. Frame two is at level three
    // throughout, so the first four pixels average (0,3), (1,3), (2,3) and (3,3).
    var picture = InterlaceStudioFile.ToRawImage(InterlaceStudioReader.FromBytes(_BuildValidFile()));
    var rgb = picture.PixelData;

    Assert.Multiple(() => {
      Assert.That(rgb[0], Is.EqualTo(102), "levels 0 and 3");
      Assert.That(rgb[3], Is.EqualTo(136), "levels 1 and 3");
      Assert.That(rgb[6], Is.EqualTo(170), "levels 2 and 3");
      Assert.That(rgb[9], Is.EqualTo(204), "levels 3 and 3");
    });
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_IsGrey() {
    var rgb = InterlaceStudioFile.ToRawImage(InterlaceStudioReader.FromBytes(_BuildValidFile())).PixelData;

    Assert.Multiple(() => {
      Assert.That(rgb[0], Is.EqualTo(rgb[1]));
      Assert.That(rgb[1], Is.EqualTo(rgb[2]));
    });
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
  public void RoundTrip_BothFramesAndTheHeaderComeBack() {
    var first = new byte[InterlaceStudioFile.FrameSize];
    var second = new byte[InterlaceStudioFile.FrameSize];
    for (var i = 0; i < first.Length; ++i) {
      first[i] = (byte)(i * 13 % 256);
      second[i] = (byte)(i * 7 % 256);
    }

    var original = new InterlaceStudioFile {
      Header = [0x11, 0x35, 0xF7, 0x0B, .. new byte[12]],
      FirstFrame = first,
      SecondFrame = second,
    };

    var restored = InterlaceStudioReader.FromBytes(InterlaceStudioWriter.ToBytes(original));

    Assert.Multiple(() => {
      Assert.That(restored.Header, Is.EqualTo(original.Header));
      Assert.That(restored.FirstFrame, Is.EqualTo(original.FirstFrame));
      Assert.That(restored.SecondFrame, Is.EqualTo(original.SecondFrame));
    });
  }
}
