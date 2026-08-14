using System;
using System.Collections.Generic;
using FileFormat.WebP.Vp8L;

namespace FileFormat.WebP.Tests;

/// <summary>
/// VP8 lossy carries no alpha, so a lossy picture that has any keeps it in an ALPH chunk beside the
/// VP8 one — and <c>cwebp</c> writes that chunk compressed by default.
/// </summary>
/// <remarks>
/// A plain <c>cwebp -q 80</c> of anything transparent produces flag byte 0x01, meaning the plane is
/// a VP8L stream whose green channel holds the alpha. Reading only the uncompressed form and calling
/// everything else opaque, which is what happened here before, turns every transparent lossy WebP
/// into a solid one — a picture that looks entirely reasonable until something is composited against it.
/// </remarks>
[TestFixture]
public sealed class WebPAlphaDecoderTests {

  private const int _Width = 7;
  private const int _Height = 5;

  private static byte[] _Plane() {
    var plane = new byte[_Width * _Height];
    for (var i = 0; i < plane.Length; ++i)
      plane[i] = (byte)(i * 9 + 3);
    return plane;
  }

  private static byte[] _Chunk(byte flags, IReadOnlyList<byte> payload) {
    var chunk = new byte[1 + payload.Count];
    chunk[0] = flags;
    for (var i = 0; i < payload.Count; ++i)
      chunk[i + 1] = payload[i];
    return chunk;
  }

  /// <summary>Applies the row filter the decoder is expected to undo.</summary>
  private static byte[] _Filter(byte[] plane, int filter) {
    var filtered = new byte[plane.Length];
    for (var y = 0; y < _Height; ++y) {
      var row = y * _Width;
      var above = row - _Width;
      var effective = y == 0 ? 1 : filter;
      for (var x = 0; x < _Width; ++x) {
        int predicted;
        switch (effective) {
          case 1:
            predicted = x > 0 ? plane[row + x - 1] : y > 0 ? plane[above] : 0;
            break;
          case 2:
            predicted = plane[above + x];
            break;
          default: {
            var left = x > 0 ? plane[row + x - 1] : plane[above];
            var top = plane[above + x];
            var topLeft = x > 0 ? plane[above + x - 1] : plane[above];
            var g = left + top - topLeft;
            predicted = (g & ~0xFF) == 0 ? g : g < 0 ? 0 : 255;
            break;
          }
        }

        filtered[row + x] = (byte)(plane[row + x] - predicted);
      }
    }

    return filtered;
  }

  [Test]
  [Category("Unit")]
  public void Decode_ReadsAnUncompressedPlane() {
    var plane = _Plane();
    Assert.That(WebPAlphaDecoder.Decode(_Chunk(0x00, plane), _Width, _Height), Is.EqualTo(plane));
  }

  [Test]
  [Category("Unit")]
  [TestCase(1, TestName = "Decode_UndoesTheHorizontalFilter")]
  [TestCase(2, TestName = "Decode_UndoesTheVerticalFilter")]
  [TestCase(3, TestName = "Decode_UndoesTheGradientFilter")]
  public void Decode_UndoesTheRowFilter(int filter) {
    var plane = _Plane();
    var chunk = _Chunk((byte)(filter << 2), _Filter(plane, filter));
    Assert.That(WebPAlphaDecoder.Decode(chunk, _Width, _Height), Is.EqualTo(plane));
  }

  [Test]
  [Category("Unit")]
  public void Decode_ReadsAPlaneCompressedAsAVp8LStream() {
    // The alpha values ride in the green channel, and the stream has no VP8L preamble — its size
    // comes from the picture the alpha belongs to. The preamble our own encoder writes is exactly
    // five bytes and the stream after it is byte-aligned, so dropping those five leaves precisely
    // the bytes an ALPH chunk holds.
    var plane = _Plane();
    var argb = new uint[plane.Length];
    for (var i = 0; i < plane.Length; ++i)
      argb[i] = 0xFF000000u | ((uint)plane[i] << 8);

    var stream = Vp8LEncoder.Encode(argb, _Width, _Height, hasAlpha: false)[5..];
    Assert.That(WebPAlphaDecoder.Decode(_Chunk(0x01, stream), _Width, _Height), Is.EqualTo(plane));
  }

  [Test]
  [Category("Unit")]
  public void Decode_RefusesLevelReductionRatherThanReturningAPlaneThatIsNearlyRight() {
    var plane = _Plane();
    Assert.Throws<NotSupportedException>(() => WebPAlphaDecoder.Decode(_Chunk(0x10, plane), _Width, _Height));
  }

  [Test]
  [Category("Unit")]
  public void Decode_RefusesAnUncompressedChunkTooShortForThePictureItClaims() {
    Assert.Throws<System.IO.InvalidDataException>(
      () => WebPAlphaDecoder.Decode(_Chunk(0x00, new byte[_Width * _Height - 1]), _Width, _Height));
  }
}
