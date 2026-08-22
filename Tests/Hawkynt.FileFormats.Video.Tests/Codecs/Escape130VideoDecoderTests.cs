using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Codecs.Tests;

/// <summary>
/// Escape 130's block codes, skip codes and colour tables — on pictures built here bit by bit, since
/// the format's own bitstream is read least-significant-bit first and no test fixture library speaks
/// that natively.
/// </summary>
/// <remarks>
/// Four real files — 320x240, 480 pictures in all, one of them carrying genuine colour rather than the
/// greyscale most of the others open on — were decoded here and by ffmpeg and compared against
/// ffmpeg's own decoded <c>yuv420p</c> planes, sample by sample: every Y, Cb and Cr sample of every
/// picture is identical, with no difference at all. What that comparison already settled — the skip
/// code's uniform off-by-one, the four-brightness field's doubling, the "reuse" code's whole-block
/// clone, and the wiki page's own unconfirmed "correction" to the chroma adjustment table — is
/// exercised here bit pattern by bit pattern instead, on pictures small enough to compute the expected
/// colour of by hand.
/// </remarks>
[TestFixture]
public sealed class Escape130VideoDecoderTests {

  // ============================================================================================
  // Which streams it takes
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void TheEscape130CodeIsTaken()
    => Assert.That(Escape130VideoDecoder.Accepts(_Stream(2, 2)), Is.True);

  [Test]
  [Category("Unit")]
  public void AnotherCodecsNumberIsNotTaken() {
    var stream = new MediaStreamInfo { Index = 0, Kind = MediaStreamKind.Video, Codec = new(124) };

    Assert.That(Escape130VideoDecoder.Accepts(stream), Is.False);
  }

  [Test]
  [Category("Unit")]
  public void ASoundStreamIsNotTaken() {
    var stream = new MediaStreamInfo { Index = 0, Kind = MediaStreamKind.Audio, Codec = new(130) };

    Assert.That(Escape130VideoDecoder.Accepts(stream), Is.False);
  }

  [Test]
  [Category("Unit")]
  public void TheCodecIsRegistered() {
    var stream = _Stream(2, 2);

    Assert.That(VideoFormatRegistry.AllCodecs.Select(c => c.CodecName), Does.Contain("Eidos Escape 130"));
    Assert.That(VideoFormatRegistry.CanDecode(stream), Is.True);
    Assert.That(VideoFormatRegistry.CreateDecoder(stream), Is.InstanceOf<Escape130VideoDecoder>());
  }

  // ============================================================================================
  // Creation
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void AnOddWidthRefuses() {
    Assert.Throws<NotSupportedException>(() => Escape130VideoDecoder.Create(_Stream(3, 2)));
  }

  [Test]
  [Category("Unit")]
  public void AnOddHeightRefuses() {
    Assert.Throws<NotSupportedException>(() => Escape130VideoDecoder.Create(_Stream(2, 3)));
  }

  // ============================================================================================
  // The frame header
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void AWrongMagicRefuses() {
    var decoder = Escape130VideoDecoder.Create(_Stream(2, 2));
    var packet = _FrameHeader(magic: 0x0131, frameSize: 16) .Concat(Array.Empty<byte>()).ToArray();

    var failure = Assert.Throws<InvalidDataException>(() => decoder.TryDecode(new(0, packet), out _));
    Assert.That(failure!.Message, Does.Contain("0x0131"));
  }

  [Test]
  [Category("Unit")]
  public void AFrameSizeLargerThanThePacketRefuses() {
    var decoder = Escape130VideoDecoder.Create(_Stream(2, 2));
    var packet = _FrameHeader(magic: 0x0130, frameSize: 999);

    Assert.Throws<InvalidDataException>(() => decoder.TryDecode(new(0, packet), out _));
  }

  // ============================================================================================
  // Single-colour blocks
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void SetYPbPrPaintsAFlatBlockAtTheGivenColour() {
    // Skip 0 (first bit '1', decremented to 0), then single-colour "Set Y" (0,1,1), the new Y' value,
    // then the shared chroma tail set outright (1,1) and the new Pb'/Pr' values.
    var bits = new Escape130TestBitWriter();
    bits.WriteBit(1); // skip: tier1 -> 1, minus 1 = 0
    bits.WriteBit(0); bits.WriteBit(1); bits.WriteBit(1); // single-colour, Y changes, "set" family
    bits.WriteBits(40, 6); // new Y'
    bits.WriteBit(1); bits.WriteBit(1); // chroma tail: set outright
    bits.WriteBits(20, 5);
    bits.WriteBits(10, 5);

    var frame = _DecodeOneFrame(2, 2, bits);

    var (r, g, b) = _ExpectedRgb(40, 20, 10);
    Assert.That(frame.PixelData[0], Is.EqualTo(r));
    Assert.That(frame.PixelData[1], Is.EqualTo(g));
    Assert.That(frame.PixelData[2], Is.EqualTo(b));
    // All four pixels of a single-colour block share the same colour.
    for (var i = 1; i < 4; ++i)
      Assert.That(frame.PixelData.Skip(i * 3).Take(3), Is.EqualTo(new byte[] { r, g, b }));
  }

  [Test]
  [Category("Unit")]
  public void TheChromaAdjustmentTableUsedIsTheSourceDocuments() {
    // Two blocks: the first sets an absolute colour, the second adjusts Pb'/Pr' with code 3 (index 3,
    // giving (-1, +1) in the source document's own table — the exact entry MultimediaWiki's page
    // claims is wrong).
    var bits = new Escape130TestBitWriter();
    bits.WriteBit(1); // skip 0 to reach block 0
    bits.WriteBit(0); bits.WriteBit(1); bits.WriteBit(1); // single-colour, Y changes, "set" family
    bits.WriteBits(32, 6);
    bits.WriteBit(1); bits.WriteBit(1); // chroma tail: set outright
    bits.WriteBits(15, 5);
    bits.WriteBits(15, 5);
    bits.WriteBit(1); // skip 0 to reach block 1
    bits.WriteBit(0); bits.WriteBit(0); bits.WriteBit(1); bits.WriteBit(0); // "Adjust Pb/Pr" (0,0,1,0)
    bits.WriteBits(3, 3); // adjustment code 3

    var frame = _DecodeOneFrame(2, 4, bits);

    // The picture is one block wide and two tall, so the second block's top-left pixel sits at row 2.
    var (r, g, b) = _ExpectedRgb(32, 14, 16); // Pb' 15-1=14, Pr' 15+1=16
    var pixel = _PixelAt(frame, 0, 2);
    Assert.That(pixel.R, Is.EqualTo(r), "second block's red channel");
    Assert.That(pixel.G, Is.EqualTo(g), "second block's green channel");
    Assert.That(pixel.B, Is.EqualTo(b), "second block's blue channel");
  }

  // ============================================================================================
  // Four-brightness blocks
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void FourBrightnessDoublesItsFiveBitYField() {
    // Skip 0, then a four-brightness "Set Y" block: sign 0 (all zero offsets), diff 0, Y'=20 (five
    // bits) which must become 40 once doubled to the shared six-bit scale.
    var bits = new Escape130TestBitWriter();
    bits.WriteBit(1); // skip 0
    bits.WriteBit(1); // four-brightness selector
    bits.WriteBits(0, 6); // sign: all zero offsets
    bits.WriteBits(0, 2); // diff
    bits.WriteBits(20, 5); // Y' field, undoubled
    bits.WriteBit(0); // leave Pb'/Pr' alone (both default to 0)

    var frame = _DecodeOneFrame(2, 2, bits);

    var (r, g, b) = _ExpectedRgb(40, 0, 0);
    Assert.That(frame.PixelData[0], Is.EqualTo(r));
    Assert.That(frame.PixelData[1], Is.EqualTo(g));
    Assert.That(frame.PixelData[2], Is.EqualTo(b));
  }

  [Test]
  [Category("Unit")]
  public void FourBrightnessSignCodeOneOffsetsTopLeftAndTopRightOppositely() {
    // Sign code 0x01 -> (LT -1, RT +1, LB 0, RB 0). Diff code 1 -> strength 4. Base Y' doubled = 32.
    var bits = new Escape130TestBitWriter();
    bits.WriteBit(1);
    bits.WriteBit(1);
    bits.WriteBits(0x01, 6);
    bits.WriteBits(1, 2);
    bits.WriteBits(16, 5); // doubled -> 32
    bits.WriteBit(0);

    var frame = _DecodeOneFrame(2, 2, bits);

    var (topLeft, topRight, bottomLeft, bottomRight) = (
      _ExpectedRgb(32 - 4, 0, 0),
      _ExpectedRgb(32 + 4, 0, 0),
      _ExpectedRgb(32, 0, 0),
      _ExpectedRgb(32, 0, 0));

    Assert.That(_PixelAt(frame, 0, 0), Is.EqualTo(topLeft));
    Assert.That(_PixelAt(frame, 1, 0), Is.EqualTo(topRight));
    Assert.That(_PixelAt(frame, 0, 1), Is.EqualTo(bottomLeft));
    Assert.That(_PixelAt(frame, 1, 1), Is.EqualTo(bottomRight));
  }

  // ============================================================================================
  // Reuse clones the whole block, pattern included
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void ReuseClonesAFourBrightnessBlocksWholePatternNotJustItsBaseColour() {
    var bits = new Escape130TestBitWriter();
    // Block 0: four-brightness, sign 0x01, diff 1, Y' doubled = 32.
    bits.WriteBit(1);
    bits.WriteBit(1);
    bits.WriteBits(0x01, 6);
    bits.WriteBits(1, 2);
    bits.WriteBits(16, 5);
    bits.WriteBit(0);
    // Block 1: reuse (0, 0, 0).
    bits.WriteBit(1); // skip 0
    bits.WriteBit(0); bits.WriteBit(0); bits.WriteBit(0);

    var frame = _DecodeOneFrame(4, 2, bits);

    // Block 1 occupies columns 2-3. Its top-left and top-right pixels must differ from each other by
    // the same +/-4 the sign pattern gives block 0 — a flat repaint of the base colour would make them
    // equal instead.
    var topLeft = _PixelAt(frame, 2, 0);
    var topRight = _PixelAt(frame, 3, 0);
    Assert.That(topLeft, Is.Not.EqualTo(topRight), "a cloned four-brightness block still varies pixel to pixel");
    Assert.That(topLeft, Is.EqualTo(_PixelAt(frame, 0, 0)), "block 1 is an exact clone of block 0");
    Assert.That(topRight, Is.EqualTo(_PixelAt(frame, 1, 0)));
  }

  // ============================================================================================
  // Skip codes persist a block across frames
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void ASkippedBlockKeepsWhateverThePreviousFramePaintedThere() {
    var decoder = Escape130VideoDecoder.Create(_Stream(4, 2));

    // Frame 0 (keyframe): block 0 set to Y'=48, block 1 set to Y'=8.
    var first = new Escape130TestBitWriter();
    first.WriteBit(1);
    first.WriteBit(0); first.WriteBit(1); first.WriteBit(1);
    first.WriteBits(48, 6); first.WriteBit(1); first.WriteBit(1); first.WriteBits(16, 5); first.WriteBits(16, 5);
    first.WriteBit(1); // skip 0 to reach block 1
    first.WriteBit(0); first.WriteBit(1); first.WriteBit(1);
    first.WriteBits(8, 6); first.WriteBit(1); first.WriteBit(1); first.WriteBits(16, 5); first.WriteBits(16, 5);
    decoder.TryDecode(new(0, _FrameHeader(0x0130, first.ByteLength + 16, keyFrame: true).Concat(first.ToArray()).ToArray()), out _);

    // Frame 1: skip block 0 entirely (tier2 code for skip value 1, decremented to actual skip 1),
    // then explicitly repaint block 1 to Y'=32.
    var second = new Escape130TestBitWriter();
    second.WriteBit(0); second.WriteBits(1, 3); // tier2: v=1 -> skipped blocks = 2, minus 1 = actual skip 1
    second.WriteBit(0); second.WriteBit(1); second.WriteBit(1);
    second.WriteBits(32, 6); second.WriteBit(1); second.WriteBit(1); second.WriteBits(16, 5); second.WriteBits(16, 5);
    decoder.TryDecode(new(0, _FrameHeader(0x0130, second.ByteLength + 16, keyFrame: false).Concat(second.ToArray()).ToArray()), out var frame);

    var block0 = _ExpectedRgb(48, 16, 16);
    var block1 = _ExpectedRgb(32, 16, 16);
    Assert.That(_PixelAt(frame, 0, 0), Is.EqualTo(block0), "block 0 was skipped, so frame 0's paint survives");
    Assert.That(_PixelAt(frame, 2, 0), Is.EqualTo(block1));
  }

  // ============================================================================================
  // A picture's first block resets against a fixed default every frame
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void APicturesFirstBlockAdjustsAgainstZeroEveryFrameNotAgainstAnEarlierFramesLastBlock() {
    var decoder = Escape130VideoDecoder.Create(_Stream(2, 2));

    var first = new Escape130TestBitWriter();
    first.WriteBit(1);
    first.WriteBit(0); first.WriteBit(1); first.WriteBit(1);
    first.WriteBits(60, 6); first.WriteBit(1); first.WriteBit(1); first.WriteBits(16, 5); first.WriteBits(16, 5);
    decoder.TryDecode(new(0, _FrameHeader(0x0130, first.ByteLength + 16, keyFrame: true).Concat(first.ToArray()).ToArray()), out _);

    // Frame 1's own first (and only) block is an Adjust-Y code, +2 (code index 5).
    var second = new Escape130TestBitWriter();
    second.WriteBit(1); // skip 0
    second.WriteBit(0); second.WriteBit(1); second.WriteBit(0); // Adjust Y
    second.WriteBits(5, 3); // +2
    second.WriteBit(0); // Pb'/Pr' reused (default 0, since this is a fresh frame-start reference)
    decoder.TryDecode(new(0, _FrameHeader(0x0130, second.ByteLength + 16, keyFrame: false).Concat(second.ToArray()).ToArray()), out var frame);

    // If the reference were frame 0's last block (Y'=60), the result would be 62. Against this type's
    // own fresh-zero reference it is 0 + 2 = 2.
    var expected = _ExpectedRgb(2, 0, 0);
    Assert.That(_PixelAt(frame, 0, 0), Is.EqualTo(expected));
  }

  // ============================================================================================
  // Helpers
  // ============================================================================================

  private static MediaStreamInfo _Stream(int width, int height)
    => new() { Index = 0, Kind = MediaStreamKind.Video, Codec = new(130), Width = width, Height = height };

  private static byte[] _FrameHeader(ushort magic, int frameSize, bool keyFrame = true) {
    var header = new byte[16];
    BinaryPrimitives.WriteUInt16LittleEndian(header, magic);
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(2), keyFrame ? (ushort)0x8001 : (ushort)0x0001);
    BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), (uint)frameSize);
    return header;
  }

  private static RawImage _DecodeOneFrame(int width, int height, Escape130TestBitWriter bits) {
    var decoder = Escape130VideoDecoder.Create(_Stream(width, height));
    var payload = bits.ToArray();
    var packet = _FrameHeader(0x0130, payload.Length + 16).Concat(payload).ToArray();
    decoder.TryDecode(new(0, packet), out var frame);
    return frame;
  }

  private static (byte R, byte G, byte B) _PixelAt(RawImage frame, int x, int y) {
    var offset = ((y * frame.Width) + x) * 3;
    return (frame.PixelData[offset], frame.PixelData[offset + 1], frame.PixelData[offset + 2]);
  }

  /// <summary>The same full-range BT.601 (Kb = 0.114, Kr = 0.299) conversion the decoder itself uses,
  /// recomputed independently here so a test does not simply restate the implementation.</summary>
  private static (byte R, byte G, byte B) _ExpectedRgb(int rawY, int pb, int pr) {
    double[] fraction = [
      -0.421875, -0.390625, -0.359375, -0.328125, -0.296875, -0.265625, -0.234375, -0.203125,
      -0.171875, -0.140625, -0.109375, -0.0859375, -0.0625, -0.046875, -0.03125, -0.015625,
      0.0, 0.015625, 0.03125, 0.046875, 0.0625, 0.0859375, 0.109375, 0.140625,
      0.171875, 0.203125, 0.234375, 0.265625, 0.296875, 0.328125, 0.359375, 0.390625,
    ];

    var clampedY = Math.Clamp(rawY, 0, 63);
    var lumaSample = clampedY * 4;
    var u = (int)Math.Round((256.0 * fraction[Math.Clamp(pb, 0, 31)]) + 128.0);
    var v = (int)Math.Round((256.0 * fraction[Math.Clamp(pr, 0, 31)]) + 128.0);
    var uOffset = u - 128;
    var vOffset = v - 128;

    var r = (int)Math.Round(lumaSample + (1.402 * vOffset));
    var g = (int)Math.Round(lumaSample - (0.344136 * uOffset) - (0.714136 * vOffset));
    var b = (int)Math.Round(lumaSample + (1.772 * uOffset));

    return ((byte)Math.Clamp(r, 0, 255), (byte)Math.Clamp(g, 0, 255), (byte)Math.Clamp(b, 0, 255));
  }
}

/// <summary>
/// Writes bits the way Escape 130's own bitstream states them: little-endian throughout, the first bit
/// written landing in the lowest, not-yet-used bit of the current byte.
/// </summary>
internal sealed class Escape130TestBitWriter {

  private readonly List<byte> _bytes = [];
  private int _bitCount;

  internal void WriteBit(int bit) {
    var byteIndex = this._bitCount >> 3;
    var bitIndex = this._bitCount & 7;
    if (byteIndex == this._bytes.Count)
      this._bytes.Add(0);

    if (bit != 0)
      this._bytes[byteIndex] |= (byte)(1 << bitIndex);

    ++this._bitCount;
  }

  internal void WriteBits(int value, int count) {
    for (var i = 0; i < count; ++i)
      this.WriteBit((value >> i) & 1);
  }

  internal int ByteLength => this._bytes.Count;

  internal byte[] ToArray() => [.. this._bytes];
}
