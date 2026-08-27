using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Codecs.Tests;

[TestFixture]
public sealed class Escape124VideoDecoderTests {

  private const uint _VALID_FLAGS = 0x00800100;

  [Test]
  [Category("Unit")]
  public void TheRplCodecNumber124IsTaken()
    => Assert.That(Escape124VideoDecoder.Accepts(_Stream(8, 8)), Is.True);

  [Test]
  [Category("Unit")]
  public void AnotherCodecNumberIsNotTaken() {
    var stream = new MediaStreamInfo {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = new CodecTag(130),
      Width = 8,
      Height = 8,
      BitsPerPixel = 16,
    };
    Assert.That(Escape124VideoDecoder.Accepts(stream), Is.False);
  }

  [Test]
  [Category("Unit")]
  public void TheCodecIsRegistered() {
    var stream = _Stream(8, 8);
    Assert.That(VideoFormatRegistry.AllCodecs.Select(codec => codec.CodecName), Does.Contain("Escape 124"));
    Assert.That(VideoFormatRegistry.CanDecode(stream), Is.True);
    Assert.That(VideoFormatRegistry.CreateDecoder(stream), Is.InstanceOf<Escape124VideoDecoder>());
  }

  [Test]
  [Category("Unit")]
  public void OneCodebookMacroblockCanPaintAWholeSuperblock() {
    var decoder = Escape124VideoDecoder.Create(_Stream(8, 8));
    var packet = _SolidAlternatingFrame();

    Assert.That(decoder.TryDecode(new(0, packet), out var frame), Is.True);
    Assert.That(frame.Width, Is.EqualTo(8));
    Assert.That(frame.Height, Is.EqualTo(8));
    Assert.That(frame.Format, Is.EqualTo(PixelFormat.Rgb24));

    var expected = new byte[8 * 8 * 3];
    var at = 0;
    for (var row = 0; row < 8; ++row)
      for (var column = 0; column < 8; ++column) {
        var red = (column & 1) == 0;
        expected[at++] = red ? (byte)255 : (byte)0;
        expected[at++] = 0;
        expected[at++] = red ? (byte)0 : (byte)255;
      }

    Assert.That(frame.PixelData, Is.EqualTo(expected));
  }

  [Test]
  [Category("Unit")]
  public void APositiveSkipCountCopiesThePreviousSuperblock() {
    var decoder = Escape124VideoDecoder.Create(_Stream(8, 8));
    Assert.That(decoder.TryDecode(new(0, _SolidAlternatingFrame()), out var first), Is.True);

    var writer = new LittleEndianBitWriter();
    writer.WriteBits(_VALID_FLAGS, 32);
    writer.WriteBits(0, 32);
    writer.WriteBit(1); // skip code enters the 3-bit tier
    writer.WriteBits(0, 3); // value = 1: copy exactly this superblock

    Assert.That(decoder.TryDecode(new(0, writer.ToArray()), out var second), Is.True);
    Assert.That(second.PixelData, Is.EqualTo(first.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void ARepeatFrameBeforeAnyPictureRefuses() {
    var decoder = Escape124VideoDecoder.Create(_Stream(8, 8));
    var writer = new LittleEndianBitWriter();
    writer.WriteBits(0, 32);
    writer.WriteBits(0, 32);

    Assert.Throws<InvalidDataException>(() => decoder.TryDecode(new(0, writer.ToArray()), out _));
  }

  [Test]
  [Category("Unit")]
  public void CodebookTwoMayNotHaveZeroEntries() {
    var decoder = Escape124VideoDecoder.Create(_Stream(8, 8));
    var writer = new LittleEndianBitWriter();
    writer.WriteBits(_VALID_FLAGS | (1u << 19), 32);
    writer.WriteBits(0, 32);
    writer.WriteBits(0, 20);

    Assert.Throws<InvalidDataException>(() => decoder.TryDecode(new(0, writer.ToArray()), out _));
  }

  [Test]
  [Category("Unit")]
  public void PartialEdgeSuperblocksRefuseInsteadOfLeavingPixelsUndefined()
    => Assert.Throws<NotSupportedException>(() => Escape124VideoDecoder.Create(_Stream(10, 8)));

  [Test]
  [Category("Unit")]
  public void ATruncatedHeaderRefuses() {
    var decoder = Escape124VideoDecoder.Create(_Stream(8, 8));
    Assert.Throws<InvalidDataException>(() => decoder.TryDecode(new(0, new byte[7]), out _));
  }

  private static byte[] _SolidAlternatingFrame() {
    var writer = new LittleEndianBitWriter();

    // A valid coded frame updating codebook 1. Codebook 1 is the default starting book for each frame.
    writer.WriteBits(_VALID_FLAGS | (1u << 18), 32);
    writer.WriteBits(0, 32); // diagnostic frame_size; reconstruction does not use it

    writer.WriteBits(0, 4); // codebook 1 depth = 0 => one entry for this one-superblock picture
    writer.WriteBits(0b1010, 4); // P0/P2 use colour 0; P1/P3 use colour 1
    writer.WriteBits(0x7C00, 15); // RGB555 red
    writer.WriteBits(0x001F, 15); // RGB555 blue

    writer.WriteBit(0); // skip count = 0: code this superblock

    writer.WriteBit(0); // enter the repeated-macroblock/mask loop once
    writer.WriteBit(0); // keep codebook 1
    // depth is zero, so no codebook index bits follow
    writer.WriteBits(0xFFFF, 16); // paint all sixteen 2x2 cells with the same macroblock
    writer.WriteBit(1); // leave the repeated-macroblock/mask loop

    writer.WriteBit(1); // no inverse-mask pass; bit 16 of frame_flags is clear, so coding ends here
    return writer.ToArray();
  }

  private static MediaStreamInfo _Stream(int width, int height) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = new CodecTag(124),
    Width = width,
    Height = height,
    BitsPerPixel = 16,
  };

  private sealed class LittleEndianBitWriter {
    private readonly List<byte> _bytes = [];
    private int _bitPosition;

    internal void WriteBit(int bit) => this.WriteBits((uint)bit, 1);

    internal void WriteBits(uint value, int count) {
      for (var i = 0; i < count; ++i) {
        var byteIndex = this._bitPosition >> 3;
        var bitIndex = this._bitPosition & 7;
        if (byteIndex == this._bytes.Count)
          this._bytes.Add(0);
        if (((value >> i) & 1) != 0)
          this._bytes[byteIndex] |= (byte)(1 << bitIndex);
        ++this._bitPosition;
      }
    }

    internal byte[] ToArray() => [.. this._bytes];
  }
}
