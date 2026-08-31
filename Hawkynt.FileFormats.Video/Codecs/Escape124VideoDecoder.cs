using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes Eidos Escape 124 video carried by ARMovie/RPL files.
/// </summary>
/// <remarks>
/// Adapted from FFmpeg's <c>libavcodec/escape124.c</c>, copyright (C) 2008 Eli Friedman,
/// distributed there under LGPL-2.1-or-later. This C# adaptation is distributed with this project
/// under LGPL-3.0-or-later.
/// <para/>
/// This closes the specific gap recorded in <c>undecodable-codecs.md</c>: the skip count is not a
/// conventional Rice code. It is a tiered little-endian code: one bit, then three further bits when
/// that bit is one, then seven more only when the first tier saturates, and finally twelve more when
/// the second tier saturates. The rest of the decoder follows the same compatible reference rather
/// than guessing around that missing operation.
/// <para/>
/// Frames are reconstructed natively as 15-bit RGB words and converted to <see cref="PixelFormat.Rgb24"/>
/// only at the public boundary. Escape 124 works in 8x8 superblocks, so dimensions not divisible by
/// eight are refused rather than silently leaving a fringe undecoded.
/// </remarks>
public sealed class Escape124VideoDecoder : IVideoCodecDecoder<Escape124VideoDecoder> {

  private const uint _CODEC_ID = 124;

  private static readonly ushort[] _MaskMatrix = [
    0x0001, 0x0002, 0x0010, 0x0020,
    0x0004, 0x0008, 0x0040, 0x0080,
    0x0100, 0x0200, 0x1000, 0x2000,
    0x0400, 0x0800, 0x4000, 0x8000,
  ];

  private static readonly int[,] _Transitions = {
    { 2, 1 },
    { 0, 2 },
    { 1, 0 },
  };

  private readonly int _width;
  private readonly int _height;
  private readonly int _superblocksPerRow;
  private readonly int _superblockCount;
  private readonly CodeBook[] _codebooks = [new(), new(), new()];

  private ushort[]? _previous;

  private Escape124VideoDecoder(int width, int height) {
    this._width = width;
    this._height = height;
    this._superblocksPerRow = width / 8;
    this._superblockCount = this._superblocksPerRow * (height / 8);
  }

  /// <summary>Gets the codec name.</summary>
  public static string CodecName => "Escape 124";

  /// <summary>Determines whether the specified media stream is supported.</summary>
  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    return stream.Kind == MediaStreamKind.Video && stream.Codec.Value == _CODEC_ID;
  }

  /// <summary>Creates a decoder for the specified media stream.</summary>
  public static Escape124VideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    if (stream.Width <= 0 || stream.Height <= 0)
      throw new InvalidDataException(
        $"Escape 124 stream {stream.Index} states a picture of {stream.Width}x{stream.Height}, which has no samples.");

    if ((stream.Width & 7) != 0 || (stream.Height & 7) != 0)
      throw new NotSupportedException(
        $"Escape 124 stream {stream.Index} states a picture of {stream.Width}x{stream.Height}. The codec addresses "
        + "8x8 superblocks and this decoder does not invent pixels for a partial edge superblock.");

    if ((long)stream.Width * stream.Height > int.MaxValue)
      throw new InvalidDataException(
        $"Escape 124 stream {stream.Index} states a picture too large to hold in one managed frame.");

    return new(stream.Width, stream.Height);
  }

  /// <summary>Attempts to decode the specified coded packet into a raw image frame.</summary>
  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    var words = this._Decode(packet.Data.Span);
    frame = new() {
      Width = this._width,
      Height = this._height,
      Format = PixelFormat.Rgb24,
      PixelData = _ToRgb24(words),
    };
    return true;
  }

  private ushort[] _Decode(ReadOnlySpan<byte> data) {
    var bits = new LittleEndianBitReader(data);
    if (bits.BitsRemaining < 64)
      throw new InvalidDataException("An Escape 124 frame is shorter than its two 32-bit header fields.");

    var frameFlags = bits.ReadBits(32);
    _ = bits.ReadBits(32); // frame_size: diagnostic in the reference decoder, not part of reconstruction.

    // The reference decoder treats this combination as a repeat of the previous picture.
    if ((frameFlags & 0x114) == 0 || (frameFlags & 0x07800000) == 0) {
      if (this._previous is null)
        throw new InvalidDataException("An Escape 124 stream begins with a frame that only repeats a previous picture.");

      return (ushort[])this._previous.Clone();
    }

    for (var i = 0; i < 3; ++i)
      if ((frameFlags & (1u << (17 + i))) != 0)
        this._ReadCodeBook(ref bits, i);

    var current = new ushort[this._width * this._height];
    var codebookIndex = 1;
    var skip = -1;

    for (var superblockIndex = 0; superblockIndex < this._superblockCount; ++superblockIndex) {
      if (skip < 0)
        skip = _ReadSkipCount(ref bits);

      var sbX = superblockIndex % this._superblocksPerRow;
      var sbY = superblockIndex / this._superblocksPerRow;
      var origin = sbY * 8 * this._width + sbX * 8;

      if (skip != 0) {
        this._CopyPreviousSuperblock(current, origin);
      } else {
        Span<ushort> superblock = stackalloc ushort[64];
        this._LoadPreviousSuperblock(superblock, origin);

        var multiMask = 0;
        while (bits.BitsRemaining >= 1 && bits.ReadBit() == 0) {
          var block = this._ReadMacroBlock(ref bits, ref codebookIndex, superblockIndex);
          var mask = (int)bits.ReadBits(16);
          multiMask |= mask;
          for (var i = 0; i < 16; ++i)
            if ((mask & _MaskMatrix[i]) != 0)
              _Insert(superblock, block, i);
        }

        bits.Require(1, "the superblock's secondary coding flag");
        if (bits.ReadBit() == 0) {
          var inverseMask = (int)bits.ReadBits(4);
          for (var i = 0; i < 4; ++i)
            multiMask ^= (inverseMask & (1 << i)) != 0
              ? 0xF << (i * 4)
              : (int)bits.ReadBits(4) << (i * 4);

          for (var i = 0; i < 16; ++i) {
            if ((multiMask & _MaskMatrix[i]) == 0)
              continue;

            var block = this._ReadMacroBlock(ref bits, ref codebookIndex, superblockIndex);
            _Insert(superblock, block, i);
          }
        } else if ((frameFlags & (1u << 16)) != 0) {
          while (bits.BitsRemaining >= 1 && bits.ReadBit() == 0) {
            var block = this._ReadMacroBlock(ref bits, ref codebookIndex, superblockIndex);
            var index = (int)bits.ReadBits(4);
            _Insert(superblock, block, index);
          }
        }

        _StoreSuperblock(current, origin, this._width, superblock);
      }

      --skip;
    }

    this._previous = current;
    return (ushort[])current.Clone();
  }

  private void _ReadCodeBook(ref LittleEndianBitReader bits, int index) {
    int depth;
    int size;

    if (index == 2) {
      size = checked((int)bits.ReadBits(20));
      if (size == 0)
        throw new InvalidDataException("Escape 124 codebook 2 states zero entries.");
      depth = _CeilLog2(size);
    } else {
      depth = checked((int)bits.ReadBits(4));
      var multiplier = index == 0 ? 1 : this._superblockCount;
      var sizeLong = (long)multiplier << depth;
      if (sizeLong > int.MaxValue)
        throw new InvalidDataException($"Escape 124 codebook {index} is too large to allocate.");
      size = (int)sizeLong;
    }

    if ((long)size * 34 > bits.BitsRemaining)
      throw new InvalidDataException(
        $"Escape 124 codebook {index} announces {size} entries but the packet does not contain their 34-bit records.");

    var blocks = new MacroBlock[size];
    for (var i = 0; i < size; ++i) {
      var mask = bits.ReadBits(4);
      var color0 = checked((ushort)bits.ReadBits(15));
      var color1 = checked((ushort)bits.ReadBits(15));
      blocks[i] = new(
        (mask & 1) != 0 ? color1 : color0,
        (mask & 2) != 0 ? color1 : color0,
        (mask & 4) != 0 ? color1 : color0,
        (mask & 8) != 0 ? color1 : color0);
    }

    this._codebooks[index] = new(depth, blocks);
  }

  private MacroBlock _ReadMacroBlock(ref LittleEndianBitReader bits, ref int codebookIndex, int superblockIndex) {
    if (bits.ReadBit() != 0) {
      var transition = bits.ReadBit();
      codebookIndex = _Transitions[codebookIndex, transition];
    }

    var codebook = this._codebooks[codebookIndex];
    var blockIndex = codebook.Depth == 0 ? 0 : checked((int)bits.ReadBits(codebook.Depth));
    if (codebookIndex == 1)
      blockIndex += superblockIndex << codebook.Depth;

    return (uint)blockIndex < (uint)codebook.Blocks.Length ? codebook.Blocks[blockIndex] : default;
  }

  private static int _ReadSkipCount(ref LittleEndianBitReader bits) {
    if (bits.BitsRemaining < 1)
      return int.MaxValue;

    var value = bits.ReadBit();
    if (value == 0)
      return 0;

    value += checked((int)bits.ReadBits(3));
    if (value != 8)
      return value;

    value += checked((int)bits.ReadBits(7));
    if (value != 135)
      return value;

    return value + checked((int)bits.ReadBits(12));
  }

  private void _CopyPreviousSuperblock(ushort[] destination, int origin) {
    if (this._previous is null)
      return;

    for (var row = 0; row < 8; ++row)
      Array.Copy(this._previous, origin + row * this._width, destination, origin + row * this._width, 8);
  }

  private void _LoadPreviousSuperblock(Span<ushort> destination, int origin) {
    if (this._previous is null) {
      destination.Clear();
      return;
    }

    for (var row = 0; row < 8; ++row)
      this._previous.AsSpan(origin + row * this._width, 8).CopyTo(destination.Slice(row * 8, 8));
  }

  private static void _StoreSuperblock(ushort[] destination, int origin, int stride, ReadOnlySpan<ushort> source) {
    for (var row = 0; row < 8; ++row)
      source.Slice(row * 8, 8).CopyTo(destination.AsSpan(origin + row * stride, 8));
  }

  private static void _Insert(Span<ushort> superblock, MacroBlock block, int index) {
    var row = (index >> 2) * 2;
    var column = (index & 3) * 2;
    var at = row * 8 + column;
    superblock[at] = block.P0;
    superblock[at + 1] = block.P1;
    superblock[at + 8] = block.P2;
    superblock[at + 9] = block.P3;
  }

  private static int _CeilLog2(int value) {
    var result = 0;
    --value;
    while (value > 0) {
      ++result;
      value >>= 1;
    }
    return result;
  }

  private static byte[] _ToRgb24(ReadOnlySpan<ushort> words) {
    var result = new byte[words.Length * 3];
    var at = 0;
    foreach (var word in words) {
      var red5 = (word >> 10) & 31;
      var green5 = (word >> 5) & 31;
      var blue5 = word & 31;
      result[at++] = (byte)((red5 << 3) | (red5 >> 2));
      result[at++] = (byte)((green5 << 3) | (green5 >> 2));
      result[at++] = (byte)((blue5 << 3) | (blue5 >> 2));
    }
    return result;
  }

  private readonly record struct MacroBlock(ushort P0, ushort P1, ushort P2, ushort P3);

  private readonly record struct CodeBook(int Depth, MacroBlock[] Blocks) {
    public CodeBook() : this(0, []) { }
  }

  private ref struct LittleEndianBitReader {
    private readonly ReadOnlySpan<byte> _data;
    private int _bitPosition;

    internal LittleEndianBitReader(ReadOnlySpan<byte> data) {
      this._data = data;
      this._bitPosition = 0;
    }

    internal int BitsRemaining => this._data.Length * 8 - this._bitPosition;

    internal int ReadBit() => checked((int)this.ReadBits(1));

    internal uint ReadBits(int count) {
      if ((uint)count > 32)
        throw new ArgumentOutOfRangeException(nameof(count));
      this.Require(count, $"a {count}-bit value");

      uint value = 0;
      for (var i = 0; i < count; ++i) {
        var absolute = this._bitPosition++;
        value |= (uint)((this._data[absolute >> 3] >> (absolute & 7)) & 1) << i;
      }
      return value;
    }

    internal void Require(int count, string what) {
      if (count < 0 || this.BitsRemaining < count)
        throw new InvalidDataException($"An Escape 124 frame runs out of bits while reading {what}.");
    }
  }
}
