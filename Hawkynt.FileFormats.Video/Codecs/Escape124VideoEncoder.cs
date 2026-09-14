using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Encodes Eidos Escape 124 video for ARMovie/RPL: RGB555 2x2 macroblocks in 8x8 superblocks,
/// with whole-superblock reuse from the previous picture.
/// </summary>
/// <remarks>
/// The packet grammar is the inverse of FFmpeg's LGPL-2.1-or-later <c>escape124.c</c> decoder, whose
/// three codebooks, little-endian bit order, skip VLC and placement masks define the interoperable
/// syntax. The encoder decisions here are independent: every coded frame refreshes codebook 1 with
/// sixteen entries per superblock, one for each 2x2 cell, and each cell is reduced deterministically
/// to the best pair chosen from its four RGB555 source colours.
/// <para/>
/// <b>Temporal prediction is backward-only.</b> Escape 124 has no forward references, B pictures or
/// display/decode reordering. A superblock whose reconstruction is unchanged is skipped and therefore
/// copied from the immediately previous picture; an entirely unchanged picture is the format's
/// eight-byte repeat frame. A picture with no skipped superblocks is independent and is marked as a
/// key frame. Decode and presentation timestamps consequently remain identical.
/// <para/>
/// <b>Lossy.</b> A 2x2 macroblock can carry at most two RGB555 colours. Blocks already containing at
/// most two 5-5-5 colours round-trip exactly; blocks containing three or four are represented by the
/// pair minimizing squared error in five-bit RGB space. Input outside RGB24 is converted through the
/// package's shared pixel conversion pipeline before that quantisation.
/// </remarks>
[VerifiedBy(ConformanceOracle.FFmpeg)]
public sealed class Escape124VideoEncoder : IVideoCodecEncoder<Escape124VideoEncoder> {

  private static readonly CodecTag _TAG = new(124);

  private const int _SUPERBLOCK_SIDE = 8;
  private const int _MACROBLOCK_SIDE = 2;
  private const int _MACROBLOCKS_PER_SIDE = _SUPERBLOCK_SIDE / _MACROBLOCK_SIDE;
  private const int _MACROBLOCKS_PER_SUPERBLOCK = _MACROBLOCKS_PER_SIDE * _MACROBLOCKS_PER_SIDE;
  private const int _CODEBOOK_DEPTH = 4;
  private const int _MAX_SKIP_COUNT = 135 + 0xFFF;

  // One of bits 2/4/8 and one of bits 23..26 must be present for the reference decoder to treat the
  // packet as a coded picture rather than a repeat. Bit 18 says codebook 1 follows.
  private const uint _CODED_FRAME_FLAGS = 0x00800100u | (1u << 18);

  private static readonly ushort[] _MaskMatrix = [
    0x0001, 0x0002, 0x0010, 0x0020,
    0x0004, 0x0008, 0x0040, 0x0080,
    0x0100, 0x0200, 0x1000, 0x2000,
    0x0400, 0x0800, 0x4000, 0x8000,
  ];

  private readonly MediaStreamInfo _stream;
  private readonly int _width;
  private readonly int _height;
  private readonly int _superblocksPerRow;
  private readonly int _superblockCount;

  private ushort[]? _previous;

  private Escape124VideoEncoder(MediaStreamInfo stream) {
    this._width = stream.Width;
    this._height = stream.Height;
    this._superblocksPerRow = stream.Width / _SUPERBLOCK_SIDE;
    this._superblockCount = this._superblocksPerRow * (stream.Height / _SUPERBLOCK_SIDE);
    this._stream = new() {
      Index = stream.Index,
      Kind = MediaStreamKind.Video,
      Codec = _TAG,
      Handler = _TAG,
      TimeBase = stream.TimeBase,
      FrameRate = stream.FrameRate,
      DeclaredFrameCount = stream.DeclaredFrameCount,
      Width = stream.Width,
      Height = stream.Height,
      BitsPerPixel = 16,
      Language = stream.Language,
      Name = stream.Name,
    };
  }

  public static string CodecName => "Escape 124";

  public static CodecTag Codec => _TAG;

  public static Escape124VideoEncoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException("Escape 124 can only encode a video stream.");
    if (stream.Width <= 0 || stream.Height <= 0)
      throw new NotSupportedException(
        $"An Escape 124 encoder needs a positive picture size up front; {stream.Width}x{stream.Height} was supplied.");
    if ((stream.Width & (_SUPERBLOCK_SIDE - 1)) != 0 || (stream.Height & (_SUPERBLOCK_SIDE - 1)) != 0)
      throw new NotSupportedException(
        $"Escape 124 addresses whole {_SUPERBLOCK_SIDE}x{_SUPERBLOCK_SIDE} superblocks; "
        + $"{stream.Width}x{stream.Height} leaves a partial edge superblock.");
    if ((long)stream.Width * stream.Height * 3 > int.MaxValue)
      throw new NotSupportedException(
        $"A picture of {stream.Width}x{stream.Height} is larger than the managed buffers an Escape 124 frame can hold.");
    if (stream.BitsPerPixel is not (0 or 16))
      throw new NotSupportedException(
        $"ARMovie describes Escape 124 pictures as 16-bit RGB while the coded colours are RGB555; "
        + $"stream {stream.Index} asks for {stream.BitsPerPixel} bits per pixel.");

    return new(stream);
  }

  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    ArgumentNullException.ThrowIfNull(frame);
    if (frame.Width != this._width || frame.Height != this._height)
      throw new InvalidDataException(
        $"Escape 124 geometry is fixed at {this._width}x{this._height}; received {frame.Width}x{frame.Height}.");
    if (!frame.HasEnoughPixelData)
      throw new InvalidDataException(
        "The source RawImage does not contain enough pixel data for its declared format and dimensions.");

    var source = _ToRgb555(frame.ToRgb24(), this._width, this._height);
    var reconstruction = new ushort[source.Length];
    var codebook = this._BuildCodebook(source, reconstruction);
    var skipped = this._FindSkippableSuperblocks(reconstruction);

    byte[] data;
    bool isKeyFrame;
    if (skipped == this._superblockCount) {
      data = _RepeatFrame();
      isKeyFrame = false;
    } else {
      data = this._CodedFrame(codebook, reconstruction);
      isKeyFrame = skipped == 0;
    }

    this._previous = reconstruction;
    packet = new(
      this._stream.Index,
      data,
      PresentationTimestamp: presentationTimestamp,
      DecodeTimestamp: presentationTimestamp,
      Duration: 1,
      IsKeyFrame: isKeyFrame);
    return true;
  }

  public MediaStreamInfo DescribeStream() => this._stream;

  private EncodedMacroBlock[] _BuildCodebook(ReadOnlySpan<ushort> source, Span<ushort> reconstruction) {
    var result = new EncodedMacroBlock[this._superblockCount * _MACROBLOCKS_PER_SUPERBLOCK];

    for (var superblock = 0; superblock < this._superblockCount; ++superblock) {
      var sbX = superblock % this._superblocksPerRow;
      var sbY = superblock / this._superblocksPerRow;
      var originX = sbX * _SUPERBLOCK_SIDE;
      var originY = sbY * _SUPERBLOCK_SIDE;

      for (var macroblock = 0; macroblock < _MACROBLOCKS_PER_SUPERBLOCK; ++macroblock) {
        var x = originX + (macroblock & (_MACROBLOCKS_PER_SIDE - 1)) * _MACROBLOCK_SIDE;
        var y = originY + (macroblock / _MACROBLOCKS_PER_SIDE) * _MACROBLOCK_SIDE;
        var top = y * this._width + x;
        var block = _ChoosePair(
          source[top], source[top + 1],
          source[top + this._width], source[top + this._width + 1]);

        result[superblock * _MACROBLOCKS_PER_SUPERBLOCK + macroblock] = block;
        reconstruction[top] = block.P0;
        reconstruction[top + 1] = block.P1;
        reconstruction[top + this._width] = block.P2;
        reconstruction[top + this._width + 1] = block.P3;
      }
    }

    return result;
  }

  private int _FindSkippableSuperblocks(ReadOnlySpan<ushort> reconstruction) {
    if (this._previous is null)
      return 0;

    var skipped = 0;
    for (var superblock = 0; superblock < this._superblockCount; ++superblock)
      if (this._SuperblockEqualsPrevious(reconstruction, superblock))
        ++skipped;

    return skipped;
  }

  private bool _SuperblockEqualsPrevious(ReadOnlySpan<ushort> reconstruction, int superblock) {
    var sbX = superblock % this._superblocksPerRow;
    var sbY = superblock / this._superblocksPerRow;
    var origin = sbY * _SUPERBLOCK_SIDE * this._width + sbX * _SUPERBLOCK_SIDE;

    for (var row = 0; row < _SUPERBLOCK_SIDE; ++row) {
      var at = origin + row * this._width;
      if (!reconstruction.Slice(at, _SUPERBLOCK_SIDE).SequenceEqual(this._previous!.AsSpan(at, _SUPERBLOCK_SIDE)))
        return false;
    }

    return true;
  }

  private byte[] _CodedFrame(ReadOnlySpan<EncodedMacroBlock> codebook, ReadOnlySpan<ushort> reconstruction) {
    var bits = new LittleEndianBitWriter();
    bits.WriteBits(_CODED_FRAME_FLAGS, 32);
    bits.WriteBits(0, 32); // patched with the inclusive byte length below

    bits.WriteBits(_CODEBOOK_DEPTH, 4);
    foreach (var block in codebook) {
      bits.WriteBits(block.Mask, 4);
      bits.WriteBits(block.Color0, 15);
      bits.WriteBits(block.Color1, 15);
    }

    for (var superblock = 0; superblock < this._superblockCount;) {
      if (this._previous is not null && this._SuperblockEqualsPrevious(reconstruction, superblock)) {
        var run = 1;
        while (superblock + run < this._superblockCount
               && this._SuperblockEqualsPrevious(reconstruction, superblock + run))
          ++run;

        while (run > 0) {
          var part = Math.Min(run, _MAX_SKIP_COUNT);
          _WriteSkipCount(bits, part);
          superblock += part;
          run -= part;
        }
        continue;
      }

      _WriteSkipCount(bits, 0);
      this._WriteSuperblock(bits, superblock);
      ++superblock;
    }

    var result = bits.ToArray();
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4, 4), checked((uint)result.Length));
    return result;
  }

  private void _WriteSuperblock(LittleEndianBitWriter bits, int superblock) {
    for (var macroblock = 0; macroblock < _MACROBLOCKS_PER_SUPERBLOCK; ++macroblock) {
      bits.WriteBit(0); // another broadcast block follows
      bits.WriteBit(0); // stay on codebook 1
      bits.WriteBits((uint)macroblock, _CODEBOOK_DEPTH);
      bits.WriteBits(_MaskMatrix[macroblock], 16);
    }

    bits.WriteBit(1); // end the broadcast/mask pass
    bits.WriteBit(1); // no inverse-mask pass; frame flag bit 16 is deliberately clear
  }

  private static void _WriteSkipCount(LittleEndianBitWriter bits, int count) {
    if ((uint)count > _MAX_SKIP_COUNT)
      throw new ArgumentOutOfRangeException(nameof(count));

    if (count == 0) {
      bits.WriteBit(0);
      return;
    }

    bits.WriteBit(1);
    if (count < 8) {
      bits.WriteBits((uint)(count - 1), 3);
      return;
    }

    bits.WriteBits(7, 3);
    if (count < 135) {
      bits.WriteBits((uint)(count - 8), 7);
      return;
    }

    bits.WriteBits(127, 7);
    bits.WriteBits((uint)(count - 135), 12);
  }

  private static byte[] _RepeatFrame() {
    var result = new byte[8];
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), checked((uint)result.Length));
    return result;
  }

  private static EncodedMacroBlock _ChoosePair(ushort p0, ushort p1, ushort p2, ushort p3) {
    Span<ushort> pixels = stackalloc ushort[4] { p0, p1, p2, p3 };

    var bestError = long.MaxValue;
    var best0 = pixels[0];
    var best1 = pixels[0];
    byte bestMask = 0;

    for (var first = 0; first < pixels.Length; ++first)
    for (var second = first; second < pixels.Length; ++second) {
      var color0 = pixels[first];
      var color1 = pixels[second];
      long error = 0;
      byte mask = 0;

      for (var sample = 0; sample < pixels.Length; ++sample) {
        var error0 = _DistanceSquared(pixels[sample], color0);
        var error1 = _DistanceSquared(pixels[sample], color1);
        if (error1 < error0) {
          error += error1;
          mask |= (byte)(1 << sample);
        } else {
          error += error0;
        }
      }

      if (error >= bestError)
        continue;

      bestError = error;
      best0 = color0;
      best1 = color1;
      bestMask = mask;
    }

    return new(
      bestMask,
      best0,
      best1,
      (bestMask & 1) != 0 ? best1 : best0,
      (bestMask & 2) != 0 ? best1 : best0,
      (bestMask & 4) != 0 ? best1 : best0,
      (bestMask & 8) != 0 ? best1 : best0);
  }

  private static int _DistanceSquared(ushort left, ushort right) {
    var dr = ((left >> 10) & 31) - ((right >> 10) & 31);
    var dg = ((left >> 5) & 31) - ((right >> 5) & 31);
    var db = (left & 31) - (right & 31);
    return dr * dr + dg * dg + db * db;
  }

  private static ushort[] _ToRgb555(ReadOnlySpan<byte> rgb, int width, int height) {
    var count = checked(width * height);
    var result = new ushort[count];
    for (var pixel = 0; pixel < count; ++pixel) {
      var at = pixel * 3;
      var red = _FiveBits(rgb[at]);
      var green = _FiveBits(rgb[at + 1]);
      var blue = _FiveBits(rgb[at + 2]);
      result[pixel] = checked((ushort)((red << 10) | (green << 5) | blue));
    }
    return result;
  }

  private static int _FiveBits(byte value) => (value * 31 + 127) / 255;

  private readonly record struct EncodedMacroBlock(
    byte Mask,
    ushort Color0,
    ushort Color1,
    ushort P0,
    ushort P1,
    ushort P2,
    ushort P3);

  private sealed class LittleEndianBitWriter {
    private byte[] _buffer = new byte[4096];
    private int _bitPosition;

    internal void WriteBit(int bit) => this.WriteBits((uint)bit, 1);

    internal void WriteBits(int value, int count) => this.WriteBits(checked((uint)value), count);

    internal void WriteBits(uint value, int count) {
      if ((uint)count > 32)
        throw new ArgumentOutOfRangeException(nameof(count));

      this._Ensure(count);
      for (var bit = 0; bit < count; ++bit) {
        if (((value >> bit) & 1) != 0)
          this._buffer[this._bitPosition >> 3] |= (byte)(1 << (this._bitPosition & 7));
        ++this._bitPosition;
      }
    }

    internal byte[] ToArray() => this._buffer.AsSpan(0, (this._bitPosition + 7) >> 3).ToArray();

    private void _Ensure(int bits) {
      var bytes = checked((this._bitPosition + bits + 7) >> 3);
      if (bytes <= this._buffer.Length)
        return;

      var length = this._buffer.Length;
      while (length < bytes)
        length = checked(length * 2);
      Array.Resize(ref this._buffer, length);
    }
  }
}
