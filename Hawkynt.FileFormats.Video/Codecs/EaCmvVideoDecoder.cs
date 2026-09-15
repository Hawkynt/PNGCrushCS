using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Codecs.Ea;
using FileFormat.Core;
using EaChunks = FileFormat.Ea.EaChunkType;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes Electronic Arts CMV: eight-bit palette indices painted four pixels square at a time,
/// either literally or from one of the two pictures completed immediately before this one.
/// </summary>
/// <remarks>
/// <c>MVIh</c> states geometry, frame rate and a palette span; <c>MVIf</c> carries one picture; and
/// <c>MVIe</c> ends one run. A packet may contain more than one of those chunks, which is useful when
/// an encoder has to put the state header immediately in front of the picture it belongs to.
/// <para/>
/// Frame type zero is an intra picture and is simply width*height palette indices. Every non-zero
/// frame type is inter-coded, matching the original NHL 95 decoder behaviour documented for values
/// greater than one as well as the ordinary value one. An inter picture has one primary byte per 4x4
/// block. A byte other than <c>0xFF</c> is a motion vector into the immediately previous picture. An
/// <c>0xFF</c> escape consumes another byte: a value other than <c>0xFF</c> is the same motion vector
/// into the second-last picture, while a second <c>0xFF</c> is followed by sixteen literal indices.
/// There are therefore backward references only; CMV has no B-picture or forward-reference form.
/// <para/>
/// Motion vectors use four-bit signed-by-offset components: low nibble X and high nibble Y, each
/// minus seven. A source pixel outside the picture is zero. Palette bytes are kept as the eight-bit
/// RGB order established against <c>TITLE.CMV</c> and FFmpeg rather than the six-bit/RBG description
/// found in older secondary documentation.
/// </remarks>
public sealed class EaCmvVideoDecoder : IVideoCodecDecoder<EaCmvVideoDecoder> {

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("cmv ");

  private const int _CHUNK_HEADER_LENGTH = 8;
  private const int _BLOCK = 4;

  private readonly byte[] _palette = new byte[256 * 3];

  private int _width;
  private int _height;
  private EaCmvFrame? _lastFrame;
  private EaCmvFrame? _secondLastFrame;

  public static string CodecName => "Electronic Arts CMV";

  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    return stream.Kind == MediaStreamKind.Video && stream.Codec.EqualsIgnoringCase(_Tag);
  }

  public static EaCmvVideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    return new();
  }

  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    var data = packet.Data.Span;
    if (data.Length < _CHUNK_HEADER_LENGTH)
      throw new InvalidDataException($"An Electronic Arts CMV packet is {data.Length} bytes, short of a chunk header's own eight.");

    RawImage? decoded = null;
    for (var at = 0; at < data.Length;) {
      if (data.Length - at < _CHUNK_HEADER_LENGTH)
        throw new InvalidDataException($"An Electronic Arts CMV packet ends with only {data.Length - at} byte(s), short of another chunk header.");

      var chunk = data[at..];
      var fourCc = BinaryPrimitives.ReadUInt32LittleEndian(chunk);
      var statedLength = BinaryPrimitives.ReadUInt32LittleEndian(chunk[4..]);
      if (statedLength < _CHUNK_HEADER_LENGTH)
        throw new InvalidDataException($"An Electronic Arts CMV chunk at packet byte {at} states {statedLength} bytes, shorter than its header.");
      if (statedLength > (uint)chunk.Length)
        throw new InvalidDataException(
          $"An Electronic Arts CMV chunk at packet byte {at} states {statedLength} bytes, but only {chunk.Length} remain.");

      var chunkLength = checked((int)statedLength);
      var payload = chunk.Slice(_CHUNK_HEADER_LENGTH, chunkLength - _CHUNK_HEADER_LENGTH);

      switch (fourCc) {
        case EaChunks.MVIh:
          this._ReadHeader(payload);
          break;

        case EaChunks.MVIf:
          if (decoded != null)
            throw new NotSupportedException(
              "One Electronic Arts CMV packet contains more than one MVIf picture. The decoder contract can return only one picture per packet.");
          decoded = this._DecodePicture(payload);
          break;

        case EaChunks.MVIe:
          this._Reset();
          break;

        default:
          throw new NotSupportedException(
            $"An Electronic Arts CMV packet contains chunk 0x{fourCc:X8}, which is not MVIh, MVIf or MVIe.");
      }

      at += chunkLength;
    }

    if (decoded == null) {
      frame = null!;
      return false;
    }

    frame = decoded;
    return true;
  }

  private void _Reset() {
    this._lastFrame = null;
    this._secondLastFrame = null;
    this._width = 0;
    this._height = 0;
    Array.Clear(this._palette);
  }

  private void _ReadHeader(ReadOnlySpan<byte> payload) {
    if (payload.Length < 0x10)
      throw new InvalidDataException($"An MVIh chunk is {payload.Length} bytes, short of the sixteen its own fixed fields need.");

    var width = BinaryPrimitives.ReadUInt16LittleEndian(payload[4..]);
    var height = BinaryPrimitives.ReadUInt16LittleEndian(payload[6..]);
    if (width == 0 || height == 0)
      throw new InvalidDataException($"MVIh states a picture of {width}x{height}, which has no pixels.");
    if ((long)width * height > int.MaxValue)
      throw new InvalidDataException(
        $"MVIh states a {width}x{height} picture whose palette-index raster cannot fit in one managed CMV frame.");

    if (this._width != 0 && (width != this._width || height != this._height)) {
      // Motion vectors are coordinates in the old geometry. The new header starts a new reference
      // history but does not start a new palette: MVIh may update only a palette slice.
      this._lastFrame = null;
      this._secondLastFrame = null;
    }

    this._width = width;
    this._height = height;

    var palStart = BinaryPrimitives.ReadUInt16LittleEndian(payload[0xC..]);
    var palCount = BinaryPrimitives.ReadUInt16LittleEndian(payload[0xE..]);
    if (palStart + palCount > 256)
      throw new InvalidDataException($"MVIh names {palCount} palette entries starting at {palStart}, which runs past the 256-entry palette.");

    var colours = payload[0x10..];
    if (colours.Length < palCount * 3)
      throw new InvalidDataException($"MVIh names {palCount} palette entries but carries only {colours.Length} bytes for them.");

    for (var i = 0; i < palCount; ++i) {
      var entry = (palStart + i) * 3;
      colours.Slice(i * 3, 3).CopyTo(this._palette.AsSpan(entry, 3));
    }
  }

  private RawImage _DecodePicture(ReadOnlySpan<byte> payload) {
    if (this._width == 0)
      throw new InvalidDataException("An MVIf chunk arrived before any MVIh chunk stated a picture size.");
    if (payload.Length < 2)
      throw new InvalidDataException($"An MVIf chunk is {payload.Length} bytes, short of the two its own frame type field needs.");

    var frameType = BinaryPrimitives.ReadUInt16LittleEndian(payload);
    var target = new EaCmvFrame(this._width, this._height);

    if (frameType == 0)
      this._DecodeIntra(payload[2..], target);
    else
      this._DecodeInter(payload[2..], target);

    this._secondLastFrame = this._lastFrame;
    this._lastFrame = target;

    return new() {
      Width = this._width,
      Height = this._height,
      Format = PixelFormat.Indexed8,
      PixelData = target.Indices,
      Palette = (byte[])this._palette.Clone(),
      PaletteCount = 256,
    };
  }

  private void _DecodeIntra(ReadOnlySpan<byte> raster, EaCmvFrame target) {
    var pixelCount = target.Indices.Length;
    if (raster.Length < pixelCount)
      throw new InvalidDataException(
        $"An intra MVIf chunk carries {raster.Length} raster bytes, short of the {pixelCount} its {this._width}x{this._height} picture needs.");

    raster[..pixelCount].CopyTo(target.Indices);
  }

  private void _DecodeInter(ReadOnlySpan<byte> payload, EaCmvFrame target) {
    if (this._width % _BLOCK != 0 || this._height % _BLOCK != 0)
      throw new NotSupportedException(
        $"An inter CMV picture is a grid of {_BLOCK}x{_BLOCK} blocks, but the current {this._width}x{this._height} geometry "
        + "does not end on a complete block. No edge-block spelling is defined by the available format description.");

    var blocksWide = this._width / _BLOCK;
    var blocksHigh = this._height / _BLOCK;
    var blockCount = checked(blocksWide * blocksHigh);

    if (payload.Length < blockCount)
      throw new InvalidDataException(
        $"An inter MVIf chunk carries {payload.Length} bytes, short of the {blockCount} its per-block motion buffer alone needs.");

    var motionBytes = payload[..blockCount];
    var escapeBytes = payload[blockCount..];
    var escapeAt = 0;

    for (var by = 0; by < blocksHigh; ++by) {
      for (var bx = 0; bx < blocksWide; ++bx) {
        var motion = motionBytes[by * blocksWide + bx];

        EaCmvFrame? source;
        int dx, dy;

        if (motion != 0xFF) {
          source = this._lastFrame ?? throw new InvalidDataException(
            "An inter MVIf block references the previous picture before any previous picture has been decoded.");
          (dx, dy) = _MotionVector(motion);
        } else {
          if (escapeAt >= escapeBytes.Length)
            throw new InvalidDataException("An inter MVIf chunk's escape buffer ran out while a block still needed an escape byte.");

          var escape = escapeBytes[escapeAt++];
          if (escape != 0xFF) {
            source = this._secondLastFrame ?? throw new InvalidDataException(
              "An inter MVIf block references the second-last picture before two previous pictures have been decoded.");
            (dx, dy) = _MotionVector(escape);
          } else {
            if (escapeAt + _BLOCK * _BLOCK > escapeBytes.Length)
              throw new InvalidDataException("An inter MVIf chunk's escape buffer ran out while a raw block still needed its sixteen pixels.");

            for (var yy = 0; yy < _BLOCK; ++yy)
            for (var xx = 0; xx < _BLOCK; ++xx)
              target.Indices[(by * _BLOCK + yy) * this._width + bx * _BLOCK + xx] = escapeBytes[escapeAt++];

            continue;
          }
        }

        for (var yy = 0; yy < _BLOCK; ++yy) {
          var sy = by * _BLOCK + yy + dy;
          for (var xx = 0; xx < _BLOCK; ++xx) {
            var sx = bx * _BLOCK + xx + dx;
            var value = sx >= 0 && sx < source.Width && sy >= 0 && sy < source.Height
              ? source.Indices[sy * source.Width + sx]
              : (byte)0;
            target.Indices[(by * _BLOCK + yy) * this._width + bx * _BLOCK + xx] = value;
          }
        }
      }
    }
  }

  private static (int Dx, int Dy) _MotionVector(byte value) => ((value & 0x0F) - 7, (value >> 4) - 7);
}
