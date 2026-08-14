using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;
using FileFormat.Core;
using FileFormat.WebP.Vp8L;

namespace FileFormat.WebP.Tests;

/// <summary>
/// An animation is not a list of independent pictures. Every frame is a rectangle placed on a
/// canvas that already holds whatever the frames before it left there, and what it leaves behind
/// depends on its own disposal method.
/// </summary>
/// <remarks>
/// The reference for every expectation here is libwebp's own animation decoder
/// (<c>src/demux/anim_decode.c</c>), which is what <c>anim_dump</c>, ffmpeg and — to within a
/// rounding step on blended pixels — ImageMagick all agree with. Two of its decisions are not
/// obvious from the container specification and are pinned here because guessing either way
/// produces a picture that looks plausible and is wrong:
/// <list type="bullet">
///   <item>disposal to background clears the rectangle to <em>transparent black</em>, not to the
///     background colour the ANIM chunk states — the colour is a hint for the application's
///     window, never painted onto the canvas;</item>
///   <item>blending is integer arithmetic with a reciprocal that truncates, so a half-transparent
///     red over opaque blue is (127, 0, 126) and not the (128, 0, 127) that floating-point
///     "source over" produces.</item>
/// </list>
/// </remarks>
[TestFixture]
public sealed class WebPAnimationCompositingTests {

  // ---------------------------------------------------------------------------------------------
  // Fixture construction — animations are assembled here rather than committed as sample files.
  // ---------------------------------------------------------------------------------------------

  private const int _CanvasWidth = 61;
  private const int _CanvasHeight = 37;

  private sealed record FrameSpec(
    int X,
    int Y,
    int Width,
    int Height,
    uint Rgba,
    bool DisposeToBackground = false,
    bool Blend = false);

  /// <summary>Encodes a solid rectangle as a VP8L chunk payload.</summary>
  private static byte[] _Lossless(int width, int height, uint rgba) {
    var argb = new uint[width * height];
    var a = (rgba >> 24) & 0xFF;
    var r = (rgba >> 16) & 0xFF;
    var g = (rgba >> 8) & 0xFF;
    var b = rgba & 0xFF;
    var packed = (a << 24) | (r << 16) | (g << 8) | b;
    Array.Fill(argb, packed);
    return Vp8LEncoder.Encode(argb, width, height, hasAlpha: a != 0xFF);
  }

  private static void _Write24(Stream to, int value) {
    to.WriteByte((byte)(value & 0xFF));
    to.WriteByte((byte)((value >> 8) & 0xFF));
    to.WriteByte((byte)((value >> 16) & 0xFF));
  }

  private static void _Chunk(Stream to, string id, byte[] payload) {
    to.Write(Encoding.ASCII.GetBytes(id));
    Span<byte> length = stackalloc byte[4];
    BinaryPrimitives.WriteInt32LittleEndian(length, payload.Length);
    to.Write(length);
    to.Write(payload);
    if ((payload.Length & 1) != 0)
      to.WriteByte(0);
  }

  /// <summary>Assembles an animated WebP from the given frame descriptions.</summary>
  /// <param name="backgroundBgra">The ANIM background colour, in the [B, G, R, A] byte order the
  /// container specification prescribes. Deliberately garish in these tests: if a decoder ever
  /// paints it, the picture says so immediately.</param>
  private static byte[] _Animation(IReadOnlyList<FrameSpec> frames, uint backgroundBgra = 0xFFFF00FF) {
    using var body = new MemoryStream();

    // VP8X: animation flag (0x02) + alpha flag (0x10), then canvas size minus one.
    using var vp8x = new MemoryStream();
    vp8x.WriteByte(0x02 | 0x10);
    vp8x.Write(new byte[3]);
    _Write24(vp8x, _CanvasWidth - 1);
    _Write24(vp8x, _CanvasHeight - 1);
    _Chunk(body, "VP8X", vp8x.ToArray());

    using var anim = new MemoryStream();
    Span<byte> bg = stackalloc byte[4];
    BinaryPrimitives.WriteUInt32LittleEndian(bg, backgroundBgra);
    anim.Write(bg);
    anim.WriteByte(0); // loop count, 16 bits little-endian — 0 means forever
    anim.WriteByte(0);
    _Chunk(body, "ANIM", anim.ToArray());

    foreach (var spec in frames) {
      using var frame = new MemoryStream();
      _Write24(frame, spec.X / 2);
      _Write24(frame, spec.Y / 2);
      _Write24(frame, spec.Width - 1);
      _Write24(frame, spec.Height - 1);
      _Write24(frame, 100); // duration, ms
      frame.WriteByte((byte)((spec.Blend ? 0 : 0x02) | (spec.DisposeToBackground ? 0x01 : 0)));
      _Chunk(frame, "VP8L", _Lossless(spec.Width, spec.Height, spec.Rgba));
      _Chunk(body, "ANMF", frame.ToArray());
    }

    using var file = new MemoryStream();
    file.Write("RIFF"u8);
    Span<byte> size = stackalloc byte[4];
    BinaryPrimitives.WriteInt32LittleEndian(size, 4 + (int)body.Length);
    file.Write(size);
    file.Write("WEBP"u8);
    file.Write(body.ToArray());
    return file.ToArray();
  }

  private static (byte R, byte G, byte B, byte A) _Pixel(RawImage image, int x, int y) {
    var rgba = image.ToRgba32();
    var at = (y * image.Width + x) * 4;
    return (rgba[at], rgba[at + 1], rgba[at + 2], rgba[at + 3]);
  }

  // ---------------------------------------------------------------------------------------------
  // Tests
  // ---------------------------------------------------------------------------------------------

  private const uint _OpaqueBlue = 0xFF0000FF;
  private const uint _OpaqueGreen = 0xFF00FF00;
  private const uint _OpaqueYellow = 0xFFFFFF00;

  /// <summary>Red at alpha 128.</summary>
  private const uint _HalfRed = 0x80FF0000;

  /// <summary>Red at alpha 153, which is the alpha to reach for when the question is whether a blend
  /// happened at all. 128 divides 1&lt;&lt;24 exactly, so blending against transparent at that alpha
  /// gives the source back unchanged and a test built on it cannot tell a skipped blend from a
  /// performed one. 153 does not divide, so it can.</summary>
  private const uint _SixtyPercentRed = 0x99FF0000;

  [Test]
  [Category("Unit")]
  public void ImageCount_CountsEveryFrameAndNotJustTheFirst() {
    var bytes = _Animation([
      new FrameSpec(0, 0, _CanvasWidth, _CanvasHeight, _OpaqueBlue),
      new FrameSpec(0, 0, _CanvasWidth, _CanvasHeight, _OpaqueGreen),
    ]);

    var file = WebPReader.FromBytes(bytes);
    Assert.That(WebPFile.ImageCount(file), Is.EqualTo(2),
      "an animation with two ANMF chunks holds two frames, not one");
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ReturnsTheSecondFrameAndNotACopyOfTheFirst() {
    var bytes = _Animation([
      new FrameSpec(0, 0, _CanvasWidth, _CanvasHeight, _OpaqueBlue),
      new FrameSpec(0, 0, _CanvasWidth, _CanvasHeight, _OpaqueGreen),
    ]);

    var file = WebPReader.FromBytes(bytes);
    Assert.Multiple(() => {
      Assert.That(_Pixel(WebPFile.ToRawImage(file, 0), 30, 18), Is.EqualTo(((byte)0, (byte)0, (byte)255, (byte)255)));
      Assert.That(_Pixel(WebPFile.ToRawImage(file, 1), 30, 18), Is.EqualTo(((byte)0, (byte)255, (byte)0, (byte)255)));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_PlacesASubRectangleOnTheCanvasRatherThanReturningItAlone() {
    var bytes = _Animation([
      new FrameSpec(0, 0, _CanvasWidth, _CanvasHeight, _OpaqueBlue),
      new FrameSpec(30, 20, 20, 12, _OpaqueGreen),
    ]);

    var file = WebPReader.FromBytes(bytes);
    var second = WebPFile.ToRawImage(file, 1);

    Assert.Multiple(() => {
      Assert.That(second.Width, Is.EqualTo(_CanvasWidth), "a frame is a rectangle on the canvas, not a picture of its own size");
      Assert.That(second.Height, Is.EqualTo(_CanvasHeight));
      Assert.That(_Pixel(second, 35, 25), Is.EqualTo(((byte)0, (byte)255, (byte)0, (byte)255)), "inside the frame rectangle");
      Assert.That(_Pixel(second, 5, 5), Is.EqualTo(((byte)0, (byte)0, (byte)255, (byte)255)), "outside it the previous canvas shows through");
    });
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_BlendsWithTheIntegerArithmeticLibwebpUses() {
    // Half-transparent red over opaque blue. libwebp and ffmpeg both answer (127, 0, 126);
    // floating-point "source over" answers (128, 0, 127), and ImageMagick lands on the latter.
    var bytes = _Animation([
      new FrameSpec(0, 0, _CanvasWidth, _CanvasHeight, _OpaqueBlue),
      new FrameSpec(4, 4, 20, 12, _HalfRed, Blend: true),
    ]);

    var file = WebPReader.FromBytes(bytes);
    Assert.That(_Pixel(WebPFile.ToRawImage(file, 1), 6, 6), Is.EqualTo(((byte)127, (byte)0, (byte)126, (byte)255)));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_OverwritesRatherThanBlendsWhenTheFrameSaysDoNotBlend() {
    var bytes = _Animation([
      new FrameSpec(0, 0, _CanvasWidth, _CanvasHeight, _OpaqueBlue),
      new FrameSpec(4, 4, 20, 12, _HalfRed, Blend: false),
    ]);

    var file = WebPReader.FromBytes(bytes);
    Assert.That(_Pixel(WebPFile.ToRawImage(file, 1), 6, 6), Is.EqualTo(((byte)255, (byte)0, (byte)0, (byte)128)),
      "'do not blend' replaces the canvas pixel outright, alpha included");
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_DisposesToTransparentBlackRatherThanToTheStatedBackgroundColour() {
    // The third frame disposes its rectangle; the fourth covers a different part of the canvas, so
    // what the disposal left behind is visible in it. libwebp, ffmpeg and ImageMagick all leave
    // transparent black there — never the magenta this file's ANIM chunk names.
    var bytes = _Animation([
      new FrameSpec(0, 0, _CanvasWidth, _CanvasHeight, _OpaqueBlue),
      new FrameSpec(30, 20, 20, 12, _OpaqueGreen, DisposeToBackground: true),
      new FrameSpec(10, 2, 15, 10, _OpaqueYellow),
    ]);

    var file = WebPReader.FromBytes(bytes);
    Assert.Multiple(() => {
      Assert.That(_Pixel(WebPFile.ToRawImage(file, 1), 35, 25), Is.EqualTo(((byte)0, (byte)255, (byte)0, (byte)255)),
        "the disposal happens after the frame it belongs to has been shown");
      Assert.That(_Pixel(WebPFile.ToRawImage(file, 2), 35, 25), Is.EqualTo(((byte)0, (byte)0, (byte)0, (byte)0)),
        "and it clears to transparent black, not to the ANIM background colour");
      Assert.That(_Pixel(WebPFile.ToRawImage(file, 2), 12, 4), Is.EqualTo(((byte)255, (byte)255, (byte)0, (byte)255)));
    });
  }

  [Test]
  [Category("Unit")]
  public void ReadingTheAnimationDoesNotPaintTheBackgroundColourAnywhere() {
    // The first frame covers only part of the canvas. Everything it does not cover is transparent,
    // whatever colour the ANIM chunk states.
    var bytes = _Animation([
      new FrameSpec(10, 2, 15, 10, _OpaqueYellow),
    ]);

    var file = WebPReader.FromBytes(bytes);
    Assert.Multiple(() => {
      Assert.That(_Pixel(WebPFile.ToRawImage(file, 0), 0, 0), Is.EqualTo(((byte)0, (byte)0, (byte)0, (byte)0)));
      Assert.That(file.Animation, Is.Not.Null, "the background colour is still parsed and exposed");
      Assert.That(file.Animation!.BackgroundColorBgra, Is.EqualTo(0xFFFF00FFu));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_DoesNotBlendWhereThePreviousFrameAlreadyDisposedItsRectangle() {
    // The third frame blends, and its rectangle straddles the second frame's — which disposed, so
    // the canvas under that part is transparent. libwebp skips the blend there and writes the frame
    // straight through; ffmpeg blends anyway. In exact arithmetic the two agree, but this blend's
    // reciprocal truncates, so blending against transparent quietly takes a count off every channel
    // and the two answers differ by one. libwebp's is the right one.
    // Every expectation below was read off libwebp's anim_dump for the same arrangement built with
    // webpmux. A decoder that blends here instead of skipping answers 254 where this says 255.
    var bytes = _Animation([
      new FrameSpec(0, 0, _CanvasWidth, _CanvasHeight, _OpaqueBlue),
      new FrameSpec(20, 10, 20, 12, _OpaqueGreen, DisposeToBackground: true),
      new FrameSpec(10, 10, 40, 20, _SixtyPercentRed, Blend: true),
    ]);

    var third = WebPFile.ToRawImage(WebPReader.FromBytes(bytes), 2);
    Assert.Multiple(() => {
      Assert.That(_Pixel(third, 30, 12), Is.EqualTo(((byte)255, (byte)0, (byte)0, (byte)153)),
        "over the disposed rectangle the frame is written through untouched");
      Assert.That(_Pixel(third, 15, 12), Is.EqualTo(((byte)152, (byte)0, (byte)101, (byte)255)),
        "to the left of it the canvas still holds the first frame, so this one blends");
      Assert.That(_Pixel(third, 45, 12), Is.EqualTo(((byte)152, (byte)0, (byte)101, (byte)255)),
        "and to the right of it likewise");
      Assert.That(_Pixel(third, 30, 25), Is.EqualTo(((byte)152, (byte)0, (byte)101, (byte)255)),
        "on rows the disposed rectangle never reached, the whole row blends");
    });
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_KeepsAnsweringTheFirstFrameForCallersThatOnlyWantAPicture() {
    var bytes = _Animation([
      new FrameSpec(0, 0, _CanvasWidth, _CanvasHeight, _OpaqueBlue),
      new FrameSpec(0, 0, _CanvasWidth, _CanvasHeight, _OpaqueGreen),
    ]);

    var file = WebPReader.FromBytes(bytes);
    Assert.That(_Pixel(WebPFile.ToRawImage(file), 30, 18), Is.EqualTo(((byte)0, (byte)0, (byte)255, (byte)255)));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_RefusesAnIndexOutsideTheAnimation() {
    var bytes = _Animation([new FrameSpec(0, 0, _CanvasWidth, _CanvasHeight, _OpaqueBlue)]);
    var file = WebPReader.FromBytes(bytes);
    Assert.Throws<ArgumentOutOfRangeException>(() => WebPFile.ToRawImage(file, 1));
  }
}
