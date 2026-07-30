using FileFormat.Core;

namespace FileFormat.WebP.Tests;

/// <summary>
/// Verifies VP8 lossy now preserves the alpha channel via the ALPH chunk.
/// Before this fix, FromRawImageLossy on Rgba32 silently dropped alpha;
/// now alpha is preserved bit-exactly (RGB still goes through lossy DCT).
/// </summary>
[TestFixture]
public sealed class Vp8AlphaRoundTripTests {

  private static RawImage _MakeRgbaPattern(int width, int height) {
    var data = new byte[width * height * 4];
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var off = (y * width + x) * 4;
        data[off + 0] = (byte)((x * 255) / (width - 1));
        data[off + 1] = (byte)((y * 255) / (height - 1));
        data[off + 2] = (byte)(((x + y) * 255) / (width + height - 2));
        // Alpha gradient: opaque on left, transparent on right
        data[off + 3] = (byte)(255 - ((x * 255) / (width - 1)));
      }
    return new() { Width = width, Height = height, Format = PixelFormat.Rgba32, PixelData = data };
  }

  [Test]
  public void Vp8Lossy_Rgba32_PreservesAlphaPlane() {
    var src = _MakeRgbaPattern(32, 16);
    var file = WebPFile.FromRawImageLossy(src, quality: 75);
    var bytes = WebPFile.ToBytes(file);

    var decoded = WebPFile.FromBytes(bytes);
    var raw = WebPFile.ToRawImage(decoded);

    Assert.Multiple(() => {
      Assert.That(raw.Width, Is.EqualTo(32));
      Assert.That(raw.Height, Is.EqualTo(16));
      Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgba32),
        "VP8 lossy + ALPH chunk should round-trip as Rgba32, not Rgb24.");
      Assert.That(decoded.Features.HasAlpha, Is.True);
    });

    // Alpha plane is stored uncompressed in ALPH method 0, so it must survive bit-exact.
    for (var i = 0; i < src.Width * src.Height; ++i)
      Assert.That(raw.PixelData[i * 4 + 3], Is.EqualTo(src.PixelData[i * 4 + 3]),
        $"Alpha at pixel {i} differs (lossy VP8 should preserve alpha bit-exactly via ALPH method 0).");
  }

  [Test]
  public void Vp8Lossy_FullyOpaqueRgba_DropsAlphaChunk() {
    // Optimization: when every pixel is fully opaque, FromRawImageLossy skips the ALPH chunk
    // and reports HasAlpha=false. Decoded output is Rgb24, not Rgba32.
    var data = new byte[16 * 16 * 4];
    for (var i = 0; i < 16 * 16; ++i) {
      data[i * 4 + 0] = 128;
      data[i * 4 + 1] = 64;
      data[i * 4 + 2] = 200;
      data[i * 4 + 3] = 255; // fully opaque
    }
    var src = new RawImage { Width = 16, Height = 16, Format = PixelFormat.Rgba32, PixelData = data };

    var file = WebPFile.FromRawImageLossy(src, quality: 75);
    Assert.That(file.Features.HasAlpha, Is.False,
      "Fully opaque images should not carry an ALPH chunk.");
    Assert.That(file.AlphaData, Is.Null);

    var bytes = WebPFile.ToBytes(file);
    var raw = WebPFile.ToRawImage(WebPFile.FromBytes(bytes));
    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgb24));
  }

  [Test]
  public void Vp8Lossy_Rgb24_StaysAlphaless() {
    // Rgb24 input must keep the existing behavior: no ALPH chunk, Rgb24 output.
    var data = new byte[16 * 16 * 3];
    for (var i = 0; i < data.Length; ++i) data[i] = (byte)(i & 0xFF);
    var src = new RawImage { Width = 16, Height = 16, Format = PixelFormat.Rgb24, PixelData = data };

    var file = WebPFile.FromRawImageLossy(src, quality: 75);
    Assert.That(file.Features.HasAlpha, Is.False);
    Assert.That(file.AlphaData, Is.Null);

    var raw = WebPFile.ToRawImage(WebPFile.FromBytes(WebPFile.ToBytes(file)));
    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgb24));
  }

  [Test]
  public void Vp8Lossy_AlphaIsPresent_WhenAnyPixelTransparent() {
    var data = new byte[8 * 8 * 4];
    for (var i = 0; i < 8 * 8; ++i) {
      data[i * 4 + 0] = 255;
      data[i * 4 + 1] = 128;
      data[i * 4 + 2] = 0;
      data[i * 4 + 3] = 255;
    }
    // Single transparent pixel
    data[3] = 0;
    var src = new RawImage { Width = 8, Height = 8, Format = PixelFormat.Rgba32, PixelData = data };

    var file = WebPFile.FromRawImageLossy(src, quality: 75);
    Assert.That(file.Features.HasAlpha, Is.True);
    Assert.That(file.AlphaData, Is.Not.Null);
    Assert.That(file.AlphaData!.Length, Is.EqualTo(64));
    Assert.That(file.AlphaData[0], Is.EqualTo(0));
    Assert.That(file.AlphaData[1], Is.EqualTo(255));
  }
}
