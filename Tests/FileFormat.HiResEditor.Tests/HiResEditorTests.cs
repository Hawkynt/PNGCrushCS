using System;
using System.IO;
using FileFormat.Core;
using FileFormat.HiResEditor;

namespace FileFormat.HiResEditor.Tests;

[TestFixture]
public sealed class HiResEditorTests {

  /// <summary>Two-colour blocks aligned to cell boundaries, which the format expresses exactly.</summary>
  private static RawImage _Blocks() {
    const int width = HiResEditorFile.PixelWidth;
    const int height = HiResEditorFile.PixelHeight;
    var data = new byte[width * height * 4];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var o = (y * width + x) * 4;
      var white = (x / 8 + y / 8) % 2 == 0;
      data[o] = data[o + 1] = data[o + 2] = (byte)(white ? 255 : 0);
      data[o + 3] = 255;
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgba32, PixelData = data };
  }

  [Test]
  [Category("Unit")]
  public void ExpectedFileSize_MatchesWhatReadersAccept()
    => Assert.That(HiResEditorFile.ExpectedFileSize, Is.EqualTo(9026));

  [Test]
  [Category("Unit")]
  public void ToBytes_PutsTheVideoMatrixBeforeTheBitmap() {
    // The video matrix sits right after the load address and the bitmap a kilobyte in — getting
    // these the wrong way round still produces a plausible file that decodes to noise.
    Assert.Multiple(() => {
      Assert.That(HiResEditorFile.ScreenDataOffset, Is.EqualTo(2));
      Assert.That(HiResEditorFile.BitmapDataOffset, Is.EqualTo(1026));
    });
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_PreservesBothSectionsAndTheLoadAddress() {
    var file = HiResEditorFile.FromRawImage(_Blocks());
    var restored = HiResEditorReader.FromBytes(HiResEditorWriter.ToBytes(file));

    Assert.Multiple(() => {
      Assert.That(restored.LoadAddress, Is.EqualTo(file.LoadAddress));
      Assert.That(restored.BitmapData, Is.EqualTo(file.BitmapData));
      Assert.That(restored.ScreenData, Is.EqualTo(file.ScreenData));
    });
  }

  [Test]
  [Category("Unit")]
  public void EncodeThenDecode_ReproducesATwoColourPicture() {
    var source = _Blocks();
    var decoded = HiResEditorFile.ToRawImage(HiResEditorFile.FromRawImage(source));

    for (var i = 0; i < HiResEditorFile.PixelWidth * HiResEditorFile.PixelHeight; ++i) {
      var slot = decoded.PixelData[i] * 3;
      Assert.That(decoded.Palette![slot], Is.EqualTo(source.PixelData[i * 4]), $"pixel {i}");
    }
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_UsesAtMostTwoColoursPerCell() {
    var file = HiResEditorFile.FromRawImage(_Blocks());
    var decoded = HiResEditorFile.ToRawImage(file);

    for (var row = 0; row < HiResEditorFile.Rows; ++row)
    for (var col = 0; col < HiResEditorFile.Columns; ++col) {
      var seen = new System.Collections.Generic.HashSet<byte>();
      for (var y = 0; y < 8; ++y)
      for (var x = 0; x < 8; ++x)
        seen.Add(decoded.PixelData[(row * 8 + y) * HiResEditorFile.PixelWidth + col * 8 + x]);

      Assert.That(seen, Has.Count.LessThanOrEqualTo(2), $"cell {row},{col}");
    }
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ExposesTheMachinePalette() {
    var raw = HiResEditorFile.ToRawImage(HiResEditorFile.FromRawImage(_Blocks()));

    Assert.Multiple(() => {
      Assert.That(raw.Format, Is.EqualTo(PixelFormat.Indexed8));
      Assert.That(raw.PaletteCount, Is.EqualTo(HiResEditorFile.ColorCount));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_RejectsATruncatedFile()
    => Assert.Throws<InvalidDataException>(() => HiResEditorReader.FromBytes(new byte[9009]));

  [Test]
  [Category("Unit")]
  public void FromRawImage_RejectsOtherSizes() {
    var raw = new RawImage { Width = 320, Height = 192, Format = PixelFormat.Rgba32, PixelData = new byte[320 * 192 * 4] };

    Assert.Throws<ArgumentException>(() => HiResEditorFile.FromRawImage(raw));
  }
}
