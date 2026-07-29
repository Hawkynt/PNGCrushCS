using System;
using System.IO;
using FileFormat.Core;
using FileFormat.MovieMakerBackground;

namespace FileFormat.MovieMakerBackground.Tests;

[TestFixture]
public sealed class MovieMakerBackgroundTests {

  private static MovieMakerBackgroundFile _Sample() {
    var pixels = new byte[MovieMakerBackgroundFile.BitmapWidth * MovieMakerBackgroundFile.BitmapHeight];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i % 4);

    return new() {
      BitmapData = Atari8BitGraphics.PackGr7(pixels, MovieMakerBackgroundFile.BitmapHeight),
      Colors = [0x00, 0x28, 0x4A, 0x6C],
    };
  }

  [Test]
  [Category("Unit")]
  public void FileSize_Is3856() {
    // 3840-byte Graphics 7 screen + 4 colour bytes + 12 unused.
    Assert.That(MovieMakerBackgroundFile.FileSize, Is.EqualTo(3856));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PlacesColorsAfterTheBitmap() {
    var bytes = MovieMakerBackgroundWriter.ToBytes(_Sample());

    Assert.Multiple(() => {
      Assert.That(bytes, Has.Length.EqualTo(MovieMakerBackgroundFile.FileSize));
      Assert.That(bytes[MovieMakerBackgroundFile.ColorOffset], Is.EqualTo(0x00));
      Assert.That(bytes[MovieMakerBackgroundFile.ColorOffset + 1], Is.EqualTo(0x28));
      Assert.That(bytes[MovieMakerBackgroundFile.ColorOffset + 3], Is.EqualTo(0x6C));
    });
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_PreservesBitmapAndColors() {
    var original = _Sample();
    var restored = MovieMakerBackgroundReader.FromBytes(MovieMakerBackgroundWriter.ToBytes(original));

    Assert.Multiple(() => {
      Assert.That(restored.BitmapData, Is.EqualTo(original.BitmapData));
      Assert.That(restored.Colors, Is.EqualTo(original.Colors));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_RejectsAnyOtherLength()
    => Assert.Throws<InvalidDataException>(
      () => MovieMakerBackgroundReader.FromBytes(new byte[MovieMakerBackgroundFile.FileSize - 1]));

  [Test]
  [Category("Unit")]
  public void ToRawImage_ProducesTheDisplayedResolution() {
    var raw = MovieMakerBackgroundFile.ToRawImage(_Sample());

    Assert.Multiple(() => {
      Assert.That(raw.Width, Is.EqualTo(MovieMakerBackgroundFile.DisplayWidth));
      Assert.That(raw.Height, Is.EqualTo(MovieMakerBackgroundFile.DisplayHeight));
      Assert.That(raw.PaletteCount, Is.EqualTo(MovieMakerBackgroundFile.ColorCount));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ProducesAConformantFile() {
    var data = new byte[MovieMakerBackgroundFile.DisplayWidth * MovieMakerBackgroundFile.DisplayHeight * 4];
    for (var i = 0; i < data.Length; i += 4) {
      data[i + 2] = (byte)(i % 233);
      data[i + 3] = 255;
    }

    var raw = new RawImage {
      Width = MovieMakerBackgroundFile.DisplayWidth, Height = MovieMakerBackgroundFile.DisplayHeight,
      Format = PixelFormat.Rgba32, PixelData = data,
    };

    Assert.That(MovieMakerBackgroundWriter.ToBytes(MovieMakerBackgroundFile.FromRawImage(raw)),
      Has.Length.EqualTo(MovieMakerBackgroundFile.FileSize));
  }
}
