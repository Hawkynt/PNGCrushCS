using System;
using System.IO;
using FileFormat.Core;
using FileFormat.InterlaceHiresEditor;

namespace FileFormat.InterlaceHiresEditor.Tests;

/// <summary>
/// What an Interlace Hires Editor picture is.
/// </summary>
/// <remarks>
/// These used to build 18000 bytes of counting pattern and assert it came back unchanged, which
/// passed because the reader handed its input through as one undifferentiated block. The layout it
/// assumed — bitmap, video matrix, bitmap, video matrix — is not this format: there is no video
/// matrix anywhere in one of these files. A file is a load address and two bitmaps, the first taking
/// a whole eight-kilobyte page for the 8000 bytes it uses, which is 16194 in all. The only sample is
/// exactly that and was refused for being 1808 bytes short of what was demanded.
/// </remarks>
[TestFixture]
public sealed class InterlaceHiresEditorReaderTests {

  /// <summary>Builds a file whose two frames light different pixels, so all three levels appear.</summary>
  private static byte[] _BuildValidFile(ushort loadAddress) {
    var data = new byte[InterlaceHiresEditorFile.ExpectedFileSize];
    data[0] = (byte)(loadAddress & 0xFF);
    data[1] = (byte)(loadAddress >> 8);

    // The first cell: frame one lights its top row, frame two its top two. So the top row is lit in
    // both, the second in one, the rest in neither.
    data[InterlaceHiresEditorFile.FirstBitmapOffset] = 0xFF;
    data[InterlaceHiresEditorFile.SecondBitmapOffset] = 0xFF;
    data[InterlaceHiresEditorFile.SecondBitmapOffset + 1] = 0xFF;

    return data;
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => InterlaceHiresEditorReader.FromFile(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".ihe"));

    Assert.Throws<FileNotFoundException>(() => InterlaceHiresEditorReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => InterlaceHiresEditorReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => InterlaceHiresEditorReader.FromBytes(new byte[16193]));

  [Test]
  [Category("Unit")]
  public void FromBytes_TakesTheSecondBitmapAWholePageAfterTheFirst() {
    // 8192 bytes after, not 8000. Reading it 192 bytes early gives a picture of the right shape
    // drawn from the wrong bytes, which nothing downstream would question.
    var result = InterlaceHiresEditorReader.FromBytes(_BuildValidFile(0x2000));

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(320));
      Assert.That(result.Height, Is.EqualTo(200));
      Assert.That(result.LoadAddress, Is.EqualTo(0x2000));
      Assert.That(result.FirstBitmap, Has.Length.EqualTo(8000));
      Assert.That(result.SecondBitmap, Has.Length.EqualTo(8000));
      Assert.That(result.SecondBitmap[1], Is.EqualTo(0xFF));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_HasThreeLevelsRatherThanTwo() {
    // The frames are shown one after the other fast enough that the eye adds them, so a pixel is
    // lit in neither, in one, or in both.
    var picture = InterlaceHiresEditorFile.ToRawImage(InterlaceHiresEditorReader.FromBytes(_BuildValidFile(0x2000)));

    Assert.Multiple(() => {
      Assert.That(picture.PaletteCount, Is.EqualTo(3));
      Assert.That(picture.PixelData[0], Is.EqualTo(0), "lit in both frames is darkest");
      Assert.That(picture.PixelData[320], Is.EqualTo(1), "lit in one is the middle");
      Assert.That(picture.PixelData[320 * 2], Is.EqualTo(2), "lit in neither is lightest");
    });
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_TheThreeLevelsAreEvenlySpaced() {
    // A scale rather than three unrelated colours, which is what makes them a blend at all.
    var palette = InterlaceHiresEditorFile
      .ToRawImage(InterlaceHiresEditorReader.FromBytes(_BuildValidFile(0x2000))).Palette!;

    Assert.That(palette[3], Is.EqualTo((palette[0] + palette[6]) / 2));
  }
}

[TestFixture]
public sealed class InterlaceHiresEditorRoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_BothFramesComeBack() {
    var first = new byte[8000];
    var second = new byte[8000];
    for (var i = 0; i < first.Length; ++i) {
      first[i] = (byte)(i * 13 % 256);
      second[i] = (byte)(i * 7 % 256);
    }

    var original = new InterlaceHiresEditorFile { LoadAddress = 0x2000, FirstBitmap = first, SecondBitmap = second };

    var restored = InterlaceHiresEditorReader.FromBytes(InterlaceHiresEditorWriter.ToBytes(original));

    Assert.Multiple(() => {
      Assert.That(restored.LoadAddress, Is.EqualTo(original.LoadAddress));
      Assert.That(restored.FirstBitmap, Is.EqualTo(original.FirstBitmap));
      Assert.That(restored.SecondBitmap, Is.EqualTo(original.SecondBitmap));
    });
  }

  [Test]
  [Category("Integration")]
  public void ToBytes_IsTheLengthARealFileHas() {
    var file = new InterlaceHiresEditorFile {
      LoadAddress = 0x2000, FirstBitmap = new byte[8000], SecondBitmap = new byte[8000],
    };

    Assert.That(InterlaceHiresEditorWriter.ToBytes(file), Has.Length.EqualTo(16194));
  }
}
