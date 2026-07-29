using System;
using FileFormat.Core;
using FileFormat.WebP;

namespace FileFormat.WebP.Tests;

/// <summary>
/// Round-trips patterns that each drive a different corner of the VP8L bitstream. These previously
/// produced streams that libwebp rejected outright, or that decoded to the wrong pixels:
/// single-symbol Huffman trees are resolved without consuming bits, the code-length code needs the
/// <c>use_length</c> flag, colour-cache info precedes the meta-Huffman flag, and back-reference
/// distances are plane codes rather than raw pixel distances.
/// </summary>
[TestFixture]
public sealed class Vp8LConformanceTests {

  private static RawImage _Make(int width, int height, Func<int, int, (int R, int G, int B)> shade) {
    var data = new byte[width * height * 4];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var (r, g, b) = shade(x, y);
      var o = (y * width + x) * 4;
      data[o] = (byte)r;
      data[o + 1] = (byte)g;
      data[o + 2] = (byte)b;
      data[o + 3] = 255;
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgba32, PixelData = data };
  }

  private static void _AssertLosslessRoundTrip(string because, RawImage source) {
    var encoded = FormatIO.Encode<WebPFile>(source);
    Assert.That(encoded, Is.Not.Null.And.Not.Empty, $"{because}: encoder produced nothing");

    var decoded = FormatIO.Decode<WebPFile>(encoded);
    Assert.That(decoded, Is.Not.Null, $"{because}: decode returned null");
    Assert.That(decoded!.Width, Is.EqualTo(source.Width), $"{because}: width changed");
    Assert.That(decoded.Height, Is.EqualTo(source.Height), $"{because}: height changed");

    var expected = source.ToRgba32();
    var actual = decoded.ToRgba32();

    for (var i = 0; i < source.Width * source.Height; ++i)
      for (var c = 0; c < 3; ++c)
        if (expected[i * 4 + c] != actual[i * 4 + c])
          Assert.Fail(
            $"{because}: pixel {i} ({i % source.Width},{i / source.Width}) channel {c} — " +
            $"expected {expected[i * 4 + c]}, got {actual[i * 4 + c]}");
  }

  [Test]
  [Category("Unit")]
  public void Vp8L_SolidColour_RoundTripsExactly()
    // Every channel collapses to one symbol, so all five Huffman trees are single-symbol.
    => _AssertLosslessRoundTrip("solid", _Make(8, 8, (_, _) => (255, 0, 0)));

  [Test]
  [Category("Unit")]
  public void Vp8L_TwoColours_RoundTripsExactly()
    // Green stays single-symbol while red and blue carry two — the case where a stray bit from the
    // green tree used to shift red and blue by one symbol.
    => _AssertLosslessRoundTrip("two colours", _Make(8, 8, (x, _) => x < 4 ? (255, 0, 0) : (0, 0, 255)));

  [Test]
  [Category("Unit")]
  public void Vp8L_HorizontalStripes_RoundTripsExactly()
    // Highly repetitive: exercises long back-references at distance == width.
    => _AssertLosslessRoundTrip("stripes", _Make(8, 8, (_, y) => y % 2 == 0 ? (255, 255, 255) : (0, 0, 0)));

  [Test]
  [Category("Unit")]
  public void Vp8L_Gradient_RoundTripsExactly()
    // Many distinct symbols per channel, so every tree takes the normal (non-simple) path.
    => _AssertLosslessRoundTrip("gradient", _Make(8, 8, (x, y) => (x * 36, y * 36, 0)));

  [Test]
  [Category("Unit")]
  public void Vp8L_LargeMixedImage_RoundTripsExactly()
    => _AssertLosslessRoundTrip("mixed 64x64",
      _Make(64, 64, (x, y) => (x * 4, y * 4, (x / 4 + y / 4) % 2 == 0 ? 255 : 0)));

  [Test]
  [Category("Unit")]
  public void Vp8L_SinglePixel_RoundTripsExactly()
    => _AssertLosslessRoundTrip("1x1", _Make(1, 1, (_, _) => (17, 34, 51)));

  [Test]
  [Category("Unit")]
  public void Vp8L_EncodedStream_DeclaresTheSourceDimensions() {
    var source = _Make(37, 23, (x, y) => (x * 7, y * 11, x ^ y));
    var encoded = FormatIO.Encode<WebPFile>(source);

    // VP8L stores width-1/height-1 as 14-bit fields right after the 0x2F signature; a decoder that
    // trusts the RIFF container alone would not catch a mismatch here.
    var vp8l = _FindChunk(encoded, "VP8L");
    Assert.That(vp8l, Is.Not.Null, "no VP8L chunk");
    Assert.That(vp8l![0], Is.EqualTo(0x2F), "missing VP8L signature byte");

    var bits = (uint)vp8l[1] | ((uint)vp8l[2] << 8) | ((uint)vp8l[3] << 16) | ((uint)vp8l[4] << 24);
    Assert.That((int)(bits & 0x3FFF) + 1, Is.EqualTo(37), "width mismatch");
    Assert.That((int)((bits >> 14) & 0x3FFF) + 1, Is.EqualTo(23), "height mismatch");
  }

  private static byte[]? _FindChunk(byte[] riff, string fourCc) {
    var offset = 12;
    while (offset + 8 <= riff.Length) {
      var id = System.Text.Encoding.ASCII.GetString(riff, offset, 4);
      var size = BitConverter.ToInt32(riff, offset + 4);
      if (size < 0 || offset + 8 + size > riff.Length)
        return null;

      if (id == fourCc)
        return riff[(offset + 8)..(offset + 8 + size)];

      offset += 8 + size + (size & 1);
    }

    return null;
  }
}
