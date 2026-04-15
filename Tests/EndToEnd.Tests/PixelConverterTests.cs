using System;
using FileFormat.Core;

namespace EndToEnd.Tests;

/// <summary>Validates every PixelConverter conversion path at SIMD-triggering sizes.
/// Would have caught the Vector256 cross-lane shuffle bug.</summary>
[TestFixture]
public sealed class PixelConverterTests {

  // Sizes chosen to exercise specific code paths:
  // 1 pixel = scalar only
  // 4 pixels = 16 bytes = one Vector128 iteration
  // 8 pixels = 32 bytes = one Vector256 iteration (the bug was HERE)
  // 9 pixels = 36 bytes = Vector256 + scalar remainder
  // 64 pixels = 256 bytes = multiple full Vector256 iterations
  private static readonly int[] _Sizes = [1, 4, 8, 9, 17, 64];

  [Test]
  public void RgbaToBgra_KnownValues_ChannelsSwappedCorrectly() {
    var rgba = new byte[] { 255, 0, 0, 255, 0, 255, 0, 128, 0, 0, 255, 64, 128, 64, 32, 255 };
    var result = PixelConverter.RgbaToBgra(rgba, 4);

    // RGBA [R,G,B,A] → BGRA [B,G,R,A]
    Assert.That(result[0], Is.EqualTo(0), "pixel0 B");
    Assert.That(result[1], Is.EqualTo(0), "pixel0 G");
    Assert.That(result[2], Is.EqualTo(255), "pixel0 R");
    Assert.That(result[3], Is.EqualTo(255), "pixel0 A");

    Assert.That(result[4], Is.EqualTo(0), "pixel1 B");
    Assert.That(result[5], Is.EqualTo(255), "pixel1 G");
    Assert.That(result[6], Is.EqualTo(0), "pixel1 R");
    Assert.That(result[7], Is.EqualTo(128), "pixel1 A");
  }

  [TestCaseSource(nameof(_Sizes))]
  public void RgbaToBgra_AllSizes_MatchesScalarReference(int pixelCount) {
    var rgba = _MakeTestData(pixelCount);
    var result = PixelConverter.RgbaToBgra(rgba, pixelCount);
    var expected = _ScalarRgbaToBgra(rgba, pixelCount);
    Assert.That(result, Is.EqualTo(expected), $"RGBA→BGRA mismatch at {pixelCount} pixels");
  }

  [TestCaseSource(nameof(_Sizes))]
  public void BgraToRgba_AllSizes_MatchesScalarReference(int pixelCount) {
    var bgra = _MakeTestData(pixelCount);
    var result = PixelConverter.BgraToRgba(bgra, pixelCount);
    var expected = _ScalarBgraToRgba(bgra, pixelCount);
    Assert.That(result, Is.EqualTo(expected), $"BGRA→RGBA mismatch at {pixelCount} pixels");
  }

  [TestCaseSource(nameof(_Sizes))]
  public void RgbaToBgra_RoundTrip_Identity(int pixelCount) {
    var original = _MakeTestData(pixelCount);
    var bgra = PixelConverter.RgbaToBgra(original, pixelCount);
    var back = PixelConverter.BgraToRgba(bgra, pixelCount);
    Assert.That(back, Is.EqualTo(original), $"RGBA→BGRA→RGBA round-trip failed at {pixelCount} pixels");
  }

  [Test]
  public void Rgba32ToBgra32_ViaConvert_MatchesDirectMethod() {
    var raw = TestImageFactory.Random_64x64();
    var viaDirect = PixelConverter.RgbaToBgra(raw.PixelData, raw.Width * raw.Height);
    var viaConvert = PixelConverter.Convert(raw, PixelFormat.Bgra32).PixelData;
    Assert.That(viaConvert, Is.EqualTo(viaDirect));
  }

  [Test]
  public void Rgb24ToBgra32_ViaConvert_HasCorrectAlpha() {
    var rgba = TestImageFactory.RedGreenBlueWhite_2x2();
    var rgb = PixelConverter.Convert(rgba, PixelFormat.Rgb24);
    var bgra = PixelConverter.Convert(rgb, PixelFormat.Bgra32);

    // All pixels should have A=255 (opaque) after Rgb24→Bgra32
    for (var i = 3; i < bgra.PixelData.Length; i += 4)
      Assert.That(bgra.PixelData[i], Is.EqualTo(255), $"Alpha at byte offset {i} should be 255");
  }

  [Test]
  public void NinePixels_Vector256Boundary_NoCorruption() {
    // 9 pixels = 36 bytes: Vector256 processes bytes 0-31 (pixels 0-7), scalar handles bytes 32-35 (pixel 8)
    // The old bug: Vector256.Shuffle cross-lane contamination swapped pixels 4-7 with values from pixels 0-3
    var src = TestImageFactory.NinePixels_9x1();
    var bgra = PixelConverter.Convert(src, PixelFormat.Bgra32);
    var back = PixelConverter.Convert(bgra, PixelFormat.Rgba32);
    PixelComparer.AssertEqual(src, back, tolerance: 0, "9-pixel Vector256 boundary round-trip");
  }

  private static byte[] _MakeTestData(int pixelCount) {
    var data = new byte[pixelCount * 4];
    var rng = new Random(pixelCount); // deterministic per size
    rng.NextBytes(data);
    return data;
  }

  private static byte[] _ScalarRgbaToBgra(byte[] data, int pixelCount) {
    var result = new byte[pixelCount * 4];
    for (var i = 0; i < pixelCount * 4; i += 4) {
      result[i] = data[i + 2];     // B = src.B
      result[i + 1] = data[i + 1]; // G = src.G
      result[i + 2] = data[i];     // R = src.R
      result[i + 3] = data[i + 3]; // A = src.A
    }
    return result;
  }

  private static byte[] _ScalarBgraToRgba(byte[] data, int pixelCount) {
    var result = new byte[pixelCount * 4];
    for (var i = 0; i < pixelCount * 4; i += 4) {
      result[i] = data[i + 2];     // R = src.R
      result[i + 1] = data[i + 1]; // G = src.G
      result[i + 2] = data[i];     // B = src.B
      result[i + 3] = data[i + 3]; // A = src.A
    }
    return result;
  }
}
