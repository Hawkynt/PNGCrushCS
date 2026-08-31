using System;
using FileFormat.Core;
using FileFormat.Neochrome;

namespace FileFormat.Neochrome.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [TestCase((short)0, 320, 200)]
  [TestCase((short)1, 640, 200)]
  [TestCase((short)2, 640, 400)]
  public void RoundTrip_StandardModes_PreserveHeaderAndScreenMemory(short resolution, int width, int height) {
    var pixels = new byte[32_000];
    new Random(42 + resolution).NextBytes(pixels);
    var fileName = "ROUNDTRP.NEO"u8.ToArray();
    var original = new NeochromeFile {
      Width = width,
      Height = height,
      Resolution = resolution,
      Palette = _Palette(),
      FileName = fileName,
      AnimationLimits = unchecked((short)0x8123),
      AnimSpeed = 5,
      AnimDirection = 0xFE,
      AnimSteps = 10,
      AnimWidth = 320,
      AnimHeight = 200,
      Reserved = new short[33],
      PixelData = pixels,
    };

    var bytes = NeochromeWriter.ToBytes(original);
    var restored = NeochromeReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(width));
      Assert.That(restored.Height, Is.EqualTo(height));
      Assert.That(restored.Resolution, Is.EqualTo(resolution));
      Assert.That(restored.Palette, Is.EqualTo(original.Palette));
      Assert.That(restored.FileName, Is.EqualTo(fileName));
      Assert.That(restored.AnimationLimits, Is.EqualTo(original.AnimationLimits));
      Assert.That(restored.AnimSpeed, Is.EqualTo(5));
      Assert.That(restored.AnimDirection, Is.EqualTo(0xFE));
      Assert.That(restored.PixelData, Is.EqualTo(pixels));
    });
  }

  [Test]
  public void RoundTrip_VirtualCanvas_Preserves128000ByteScreenMemory() {
    var pixels = new byte[128_000];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i * 13);

    var original = new NeochromeFile {
      Width = 640,
      Height = 400,
      Flag = unchecked((short)0xBABE),
      Resolution = 0,
      Palette = _Palette(),
      FileName = "VIRTUAL .NEO"u8.ToArray(),
      AnimWidth = 640,
      AnimHeight = 400,
      Reserved = new short[33],
      PixelData = pixels,
    };

    var restored = NeochromeReader.FromBytes(NeochromeWriter.ToBytes(original));

    Assert.Multiple(() => {
      Assert.That(restored.Flag, Is.EqualTo(unchecked((short)0xBABE)));
      Assert.That(restored.Width, Is.EqualTo(640));
      Assert.That(restored.Height, Is.EqualTo(400));
      Assert.That(restored.PixelData, Is.EqualTo(pixels));
    });
  }

  [Test]
  public void ToRawImage_HighResolution_UsesMachineMonochromePalette() {
    var file = new NeochromeFile {
      Width = 640,
      Height = 400,
      Resolution = 2,
      Palette = EnumerablePalette(0x0700),
      AnimWidth = 320,
      AnimHeight = 200,
      PixelData = new byte[32_000],
    };

    var image = NeochromeFile.ToRawImage(file);

    Assert.Multiple(() => {
      Assert.That(image.Palette, Is.EqualTo(new byte[] { 255, 255, 255, 0, 0, 0 }));
      Assert.That(image.PaletteCount, Is.EqualTo(2));
    });
  }

  [TestCase(320, 200, 16, (short)0, (short)0, 32_000)]
  [TestCase(640, 200, 4, (short)0, (short)1, 32_000)]
  [TestCase(640, 400, 2, (short)0, (short)2, 32_000)]
  [TestCase(640, 400, 16, unchecked((short)0xBABE), (short)0, 128_000)]
  public void FromRawImage_SelectsMatchingNeoVariant(int width, int height, int colors, short flag, short resolution, int rasterLength) {
    var indices = new byte[width * height];
    var palette = new byte[colors * 3];
    for (var i = 0; i < colors; ++i) {
      palette[i * 3] = (byte)(i * 255 / Math.Max(1, colors - 1));
      palette[i * 3 + 1] = palette[i * 3];
      palette[i * 3 + 2] = palette[i * 3];
    }

    var file = NeochromeFile.FromRawImage(new RawImage {
      Width = width,
      Height = height,
      Format = PixelFormat.Indexed8,
      PixelData = indices,
      Palette = palette,
      PaletteCount = colors,
    });

    Assert.Multiple(() => {
      Assert.That(file.Flag, Is.EqualTo(flag));
      Assert.That(file.Resolution, Is.EqualTo(resolution));
      Assert.That(file.PixelData, Has.Length.EqualTo(rasterLength));
      Assert.That(NeochromeReader.FromBytes(NeochromeWriter.ToBytes(file)).PixelData, Is.EqualTo(file.PixelData));
    });
  }

  private static short[] _Palette() {
    var palette = new short[16];
    for (var i = 0; i < palette.Length; ++i)
      palette[i] = (short)(i & 7);
    return palette;
  }

  private static short[] EnumerablePalette(short value) {
    var palette = new short[16];
    Array.Fill(palette, value);
    return palette;
  }
}
