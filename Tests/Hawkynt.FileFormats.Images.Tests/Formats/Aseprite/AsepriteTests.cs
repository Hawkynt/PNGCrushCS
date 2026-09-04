using System;
using System.IO;
using FileFormat.Aseprite;
using NUnit.Framework;

namespace FileFormat.Aseprite.Tests;

/// <summary>Aseprite sprites, read and written.</summary>
/// <remarks>
/// The sprite the reader is measured against was written by ImageMagick from a picture this project
/// also has, and it is that picture the decode is compared with rather than ImageMagick's own decode
/// of the sprite. ImageMagick cannot read an Aseprite sprite back: handed its own file, or one this
/// writer produced, it returns a fully transparent canvas of zeroes in both cases. So it is a usable
/// producer of test input and no use at all as a judge of the result.
/// </remarks>
[TestFixture]
public sealed class AsepriteTests {

  private static byte[] _Fixture(string name) {
    var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "Aseprite", name);
    Assert.That(File.Exists(path), Is.True, $"Test fixture missing: {path}");
    return File.ReadAllBytes(path);
  }

  [Test]
  public void SpriteWrittenByAnotherToolDecodesToThePictureItWasMadeFrom() {
    var sprite = AsepriteReader.FromBytes(_Fixture("imagemagick_gradient.ase"));
    var (width, height, expected) = _ReadPpm(_Fixture("imagemagick_gradient.ppm"));

    Assert.Multiple(() => {
      Assert.That(sprite.Width, Is.EqualTo(width));
      Assert.That(sprite.Height, Is.EqualTo(height));
      Assert.That(sprite.ColorDepth, Is.EqualTo(AsepriteColorDepth.Indexed));
      Assert.That(sprite.PaletteColorCount, Is.EqualTo(48));
    });

    Assert.That(sprite.PixelData.Length, Is.EqualTo(width * height));
    for (var pixel = 0; pixel < width * height; ++pixel) {
      var entry = sprite.PixelData[pixel] * 3;
      var at = pixel * 3;
      if (sprite.Palette![entry] != expected[at]
          || sprite.Palette[entry + 1] != expected[at + 1]
          || sprite.Palette[entry + 2] != expected[at + 2])
        Assert.Fail(
          $"Pixel {pixel} is index {sprite.PixelData[pixel]} = "
          + $"({sprite.Palette[entry]}, {sprite.Palette[entry + 1]}, {sprite.Palette[entry + 2]}), "
          + $"where the picture has ({expected[at]}, {expected[at + 1]}, {expected[at + 2]}).");
    }
  }

  [Test]
  public void AnIndexedSpriteThisWritesReadsBackWithTheSameIndicesAndPalette() {
    var sprite = AsepriteReader.FromBytes(_Fixture("imagemagick_gradient.ase"));
    var again = AsepriteReader.FromBytes(AsepriteWriter.ToBytes(sprite));

    Assert.Multiple(() => {
      Assert.That(again.Width, Is.EqualTo(sprite.Width));
      Assert.That(again.Height, Is.EqualTo(sprite.Height));
      Assert.That(again.ColorDepth, Is.EqualTo(AsepriteColorDepth.Indexed));
      Assert.That(again.PaletteColorCount, Is.EqualTo(sprite.PaletteColorCount));
      Assert.That(again.PixelData, Is.EqualTo(sprite.PixelData));
    });

    for (var entry = 0; entry < sprite.PaletteColorCount * 3; ++entry)
      Assert.That(again.Palette![entry], Is.EqualTo(sprite.Palette![entry]), $"Palette byte {entry}");
  }

  [Test]
  public void AnRgbaSpriteThisWritesReadsBackSampleForSample() {
    var pixels = new byte[8 * 4 * 4];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i * 7 % 251);

    var written = AsepriteWriter.ToBytes(new AsepriteFile {
      Width = 8,
      Height = 4,
      ColorDepth = AsepriteColorDepth.Rgba,
      PixelData = pixels,
      FrameCount = 1,
    });

    var again = AsepriteReader.FromBytes(written);
    Assert.Multiple(() => {
      Assert.That(again.Width, Is.EqualTo(8));
      Assert.That(again.Height, Is.EqualTo(4));
      Assert.That(again.ColorDepth, Is.EqualTo(AsepriteColorDepth.Rgba));
      Assert.That(again.PixelData, Is.EqualTo(pixels));
    });
  }

  /// <summary>
  /// The background flag is what keeps an opaque picture opaque, so the writer has to set it.
  /// </summary>
  /// <remarks>
  /// The header nominates index zero as the transparent one, which is what Aseprite's own sprites
  /// carry. On an ordinary layer that would make every pixel holding index zero disappear — here the
  /// whole top of the gradient. The layer chunk this writes states 0x09, visible and background, on
  /// which the nomination does not apply.
  /// </remarks>
  [Test]
  public void TheLayerThisWritesIsMarkedAsTheBackground() {
    var sprite = AsepriteReader.FromBytes(_Fixture("imagemagick_gradient.ase"));
    var written = AsepriteWriter.ToBytes(sprite);

    // File header 128, frame header 16, then the layer chunk: 6 bytes of chunk header, then flags.
    var flags = written[128 + 16 + 6] | (written[128 + 16 + 7] << 8);
    Assert.That(flags & 8, Is.Not.Zero, "the layer this writes has to be a background layer");
    Assert.That(flags & 1, Is.Not.Zero, "the layer this writes has to be visible");
  }

  [Test]
  public void DataThatDoesNotStateTheSpriteMagicIsRefused() {
    var data = new byte[200];
    Assert.Throws<InvalidDataException>(() => AsepriteReader.FromBytes(data));
  }

  [Test]
  public void ASpriteWhoseFrameDoesNotStateItsMagicIsRefused() {
    var data = _Fixture("imagemagick_gradient.ase");
    data[128 + 4] ^= 0xFF;
    Assert.Throws<InvalidDataException>(() => AsepriteReader.FromBytes(data));
  }

  [Test]
  public void ASpriteStatingAFrameLongerThanTheFileIsRefused() {
    var data = _Fixture("imagemagick_gradient.ase");
    data[128] = 0xFF;
    data[129] = 0xFF;
    Assert.Throws<InvalidDataException>(() => AsepriteReader.FromBytes(data));
  }

  private static (int Width, int Height, byte[] Pixels) _ReadPpm(byte[] ppm) {
    var at = 0;
    string Token() {
      while (at < ppm.Length && char.IsWhiteSpace((char)ppm[at]))
        ++at;
      var start = at;
      while (at < ppm.Length && !char.IsWhiteSpace((char)ppm[at]))
        ++at;
      return System.Text.Encoding.ASCII.GetString(ppm, start, at - start);
    }

    Assert.That(Token(), Is.EqualTo("P6"));
    var width = int.Parse(Token());
    var height = int.Parse(Token());
    Assert.That(Token(), Is.EqualTo("255"));
    ++at;

    var pixels = new byte[width * height * 3];
    Array.Copy(ppm, at, pixels, 0, pixels.Length);
    return (width, height, pixels);
  }
}
