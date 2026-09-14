using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>Encodes Eidos Escape 124 video carried by ARMovie/RPL files.</summary>
/// <remarks>
/// The packet syntax mirrors the LGPL-2.1-or-later FFmpeg Escape 124 decoder: RGB555 2x2 macroblocks,
/// 8x8 superblocks, persistent codebooks and the tiered skip code. Encoder decisions are original.
/// Each 2x2 source block chooses the best pair from its four RGB555 colours. Unchanged superblocks are
/// copied from the previous reconstruction; a wholly unchanged picture uses the eight-byte repeat form.
/// Escape 124 has no forward reference or B-picture mode, so PTS and DTS are never reordered.
/// </remarks>
public sealed class Escape124VideoEncoder : IVideoCodecEncoder<Escape124VideoEncoder> {
  private static readonly CodecTag _Tag = new(124);
  private const int _SuperblockSide = 8;
  private const int _BlocksPerSuperblock = 16;
  private const int _CodebookDepth = 4;
  private const int _MaxSkip = 135 + 0xFFF;
  private const uint _CodedFlags = 0x00800100u | (1u << 18);

  private static readonly ushort[] _Masks = [
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
    this._superblocksPerRow = stream.Width / _SuperblockSide;
    this._superblockCount = this._superblocksPerRow * (stream.Height / _SuperblockSide);
    this._stream = new() {
      Index = stream.Index,
      Kind = MediaStreamKind.Video,
      Codec = _Tag,
      Handler = _Tag,
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
  public static CodecTag Codec => _Tag;

  public static Escape124VideoEncoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException("Escape 124 can only encode a video stream.");
    if (stream.Width <= 0 || stream.Height <= 0)
      throw new NotSupportedException($"Escape 124 needs a positive picture size; {stream.Width}x{stream.Height} was supplied.");
    if ((stream.Width & 7) != 0 || (stream.Height & 7) != 0)
      throw new NotSupportedException($"Escape 124 addresses whole 8x8 superblocks; {stream.Width}x{stream.Height} leaves a partial edge block.");
    if ((long)stream.Width * stream.Height * 3 > int.MaxValue)
      throw new NotSupportedException($"A picture of {stream.Width}x{stream.Height} is too large for one managed Escape 124 frame.");
    if (stream.BitsPerPixel is not (0 or 16))
      throw new NotSupportedException($"ARMovie describes Escape 124 as 16-bit RGB; stream {stream.Index} asks for {stream.BitsPerPixel} bits per pixel.");
    return new(stream);
  }

  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    ArgumentNullException.ThrowIfNull(frame);
    if (frame.Width != this._width || frame.Height != this._height)
      throw new InvalidDataException($"Escape 124 geometry is {this._width}x{this._height}; received {frame.Width}x{frame.Height}.");
    if (!frame.HasEnoughPixelData)
      throw new InvalidDataException("The source RawImage does not contain enough pixel data.");

    var source = _Rgb555(frame.ToRgb24(), this._width, this._height);
    var reconstruction = new ushort[source.Length];
    var entries = this._BuildEntries(source, reconstruction);
    var skipped = this._CountSkippable(reconstruction);
    var data = skipped == this._superblockCount ? _RepeatFrame() : this._CodedFrame(entries, reconstruction);
    var isKeyFrame = skipped == 0;
    if (skipped == this._superblockCount)
      isKeyFrame = false;

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

  private Entry[] _BuildEntries(ReadOnlySpan<ushort> source, Span<ushort> reconstruction) {
    var result = new Entry[this._superblockCount * _BlocksPerSuperblock];
    for (var sb = 0; sb < this._superblockCount; ++sb) {
      var originX = sb % this._superblocksPerRow * _SuperblockSide;
      var originY = sb / this._superblocksPerRow * _SuperblockSide;
      for (var block = 0; block < _BlocksPerSuperblock; ++block) {
        var x = originX + (block & 3) * 2;
        var y = originY + (block >> 2) * 2;
        var at = y * this._width + x;
        var entry = _BestPair(source[at], source[at + 1], source[at + this._width], source[at + this._width + 1]);
        result[sb * _BlocksPerSuperblock + block] = entry;
        reconstruction[at] = entry.Pixel(0);
        reconstruction[at + 1] = entry.Pixel(1);
        reconstruction[at + this._width] = entry.Pixel(2);
        reconstruction[at + this._width + 1] = entry.Pixel(3);
      }
    }
    return result;
  }

  private int _CountSkippable(ReadOnlySpan<ushort> reconstruction) {
    if (this._previous is null)
      return 0;
    var result = 0;
    for (var sb = 0; sb < this._superblockCount; ++sb)
      if (this._MatchesPrevious(reconstruction, sb))
        ++result;
    return result;
  }

  private bool _MatchesPrevious(ReadOnlySpan<ushort> reconstruction, int sb) {
    var origin = sb / this._superblocksPerRow * _SuperblockSide * this._width
      + sb % this._superblocksPerRow * _SuperblockSide;
    for (var row = 0; row < _SuperblockSide; ++row) {
      var at = origin + row * this._width;
      if (!reconstruction.Slice(at, _SuperblockSide).SequenceEqual(this._previous!.AsSpan(at, _SuperblockSide)))
        return false;
    }
    return true;
  }

  private byte[] _CodedFrame(ReadOnlySpan<Entry> entries, ReadOnlySpan<ushort> reconstruction) {
    var bits = new BitWriter();
    bits.Write(_CodedFlags, 32);
    bits.Write(0u, 32);
    bits.Write(_CodebookDepth, 4);
    foreach (var entry in entries) {
      bits.Write(entry.Mask, 4);
      bits.Write(entry.Color0, 15);
      bits.Write(entry.Color1, 15);
    }

    // A positive skip count leaves the decoder's counter at zero, so the superblock immediately after
    // the run is coded without another count. For long unchanged runs that exceed the largest count,
    // deliberately code one unchanged superblock between chunks to return the decoder to count-reading state.
    for (var sb = 0; sb < this._superblockCount;) {
      var run = 0;
      if (this._previous is not null)
        while (sb + run < this._superblockCount && this._MatchesPrevious(reconstruction, sb + run))
          ++run;

      if (run == 0) {
        _WriteSkip(bits, 0);
        _WriteSuperblock(bits);
        ++sb;
        continue;
      }

      var skipped = Math.Min(run, _MaxSkip);
      _WriteSkip(bits, skipped);
      sb += skipped;
      if (sb == this._superblockCount)
        break;

      _WriteSuperblock(bits);
      ++sb;
    }

    var result = bits.ToArray();
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4, 4), checked((uint)result.Length));
    return result;
  }

  private static void _WriteSuperblock(BitWriter bits) {
    for (var block = 0; block < _BlocksPerSuperblock; ++block) {
      bits.WriteBit(0);
      bits.WriteBit(0);
      bits.Write((uint)block, _CodebookDepth);
      bits.Write(_Masks[block], 16);
    }
    bits.WriteBit(1);
    bits.WriteBit(1);
  }

  private static void _WriteSkip(BitWriter bits, int count) {
    if ((uint)count > _MaxSkip)
      throw new ArgumentOutOfRangeException(nameof(count));
    if (count == 0) {
      bits.WriteBit(0);
      return;
    }
    bits.WriteBit(1);
    if (count < 8) {
      bits.Write((uint)(count - 1), 3);
      return;
    }
    bits.Write(7u, 3);
    if (count < 135) {
      bits.Write((uint)(count - 8), 7);
      return;
    }
    bits.Write(127u, 7);
    bits.Write((uint)(count - 135), 12);
  }

  private static byte[] _RepeatFrame() {
    var result = new byte[8];
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), 8u);
    return result;
  }

  private static Entry _BestPair(ushort p0, ushort p1, ushort p2, ushort p3) {
    Span<ushort> pixels = stackalloc ushort[] { p0, p1, p2, p3 };
    var bestError = long.MaxValue;
    var best = new Entry(0, p0, p0);
    for (var a = 0; a < 4; ++a)
    for (var b = a; b < 4; ++b) {
      long error = 0;
      byte mask = 0;
      for (var i = 0; i < 4; ++i) {
        var e0 = _Distance(pixels[i], pixels[a]);
        var e1 = _Distance(pixels[i], pixels[b]);
        if (e1 < e0) {
          error += e1;
          mask |= (byte)(1 << i);
        } else {
          error += e0;
        }
      }
      if (error < bestError) {
        bestError = error;
        best = new(mask, pixels[a], pixels[b]);
      }
    }
    return best;
  }

  private static int _Distance(ushort a, ushort b) {
    var dr = ((a >> 10) & 31) - ((b >> 10) & 31);
    var dg = ((a >> 5) & 31) - ((b >> 5) & 31);
    var db = (a & 31) - (b & 31);
    return dr * dr + dg * dg + db * db;
  }

  private static ushort[] _Rgb555(ReadOnlySpan<byte> rgb, int width, int height) {
    var result = new ushort[checked(width * height)];
    for (var i = 0; i < result.Length; ++i) {
      var at = i * 3;
      var r = (rgb[at] * 31 + 127) / 255;
      var g = (rgb[at + 1] * 31 + 127) / 255;
      var b = (rgb[at + 2] * 31 + 127) / 255;
      result[i] = checked((ushort)((r << 10) | (g << 5) | b));
    }
    return result;
  }

  private readonly record struct Entry(byte Mask, ushort Color0, ushort Color1) {
    internal ushort Pixel(int index) => (this.Mask & (1 << index)) != 0 ? this.Color1 : this.Color0;
  }

  private sealed class BitWriter {
    private readonly List<byte> _bytes = [];
    private int _bitPosition;
    internal void WriteBit(int bit) => this.Write((uint)bit, 1);
    internal void Write(uint value, int count) {
      if ((uint)count > 32)
        throw new ArgumentOutOfRangeException(nameof(count));
      for (var i = 0; i < count; ++i) {
        var byteIndex = this._bitPosition >> 3;
        if (byteIndex == this._bytes.Count)
          this._bytes.Add(0);
        if (((value >> i) & 1) != 0)
          this._bytes[byteIndex] |= (byte)(1 << (this._bitPosition & 7));
        ++this._bitPosition;
      }
    }
    internal byte[] ToArray() => [.. this._bytes];
  }
}
