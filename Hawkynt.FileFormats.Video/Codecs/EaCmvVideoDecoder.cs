using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Codecs.Ea;
using FileFormat.Core;
using FileFormat.Ea;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes Electronic Arts CMV — the block-replacement codec behind NHL 95's own cinematics, four
/// pixels square at a time, with motion compensation reaching back either one or two pictures.
/// </summary>
/// <remarks>
/// A picture is built from two chunks read together: <c>MVIh</c> states the picture's dimensions,
/// frame rate and (some or all of) its palette, and may recur through a file to restate the palette
/// without restating a picture; only <c>MVIf</c> ever produces one. An intra picture (<c>frame_type</c>
/// zero) is a plain raster of palette indices, left to right and top to bottom. An inter picture is
/// two buffers read together: one byte a 4x4 block naming either a motion vector or an escape, and a
/// second buffer of whatever those escapes need — a further motion byte, or sixteen raw pixels — read
/// in sequence as the first buffer is walked.
/// <para/>
/// <b>Two picture buffers, not one — the same finding as RoQ's and Interplay MVE's, this time from a
/// description that states the second reference outright.</b> When a block's motion byte in the first
/// buffer is <c>0xFF</c>, the escape byte fetched from the second buffer either names a motion vector
/// into "the second-last decoded frame" or, when that byte is itself <c>0xFF</c>, sixteen raw pixels.
/// "Second-last" only means something if the decoder already keeps the two most recently completed
/// pictures apart, so this decoder keeps both — the frame just finished and the one before it — rather
/// than one frame plus a copy. The first intra picture has no second-last frame to have completed
/// before it, so its result is copied into both slots once it is painted, the same bootstrap RoQ's and
/// MVE's decoders use — confirmed here directly, since <c>TITLE.CMV</c>'s own inter pictures begin on
/// the second picture of the file and decode correctly against that bootstrap.
/// <para/>
/// <b>The palette is plain eight-bit RGB, not the six-bit VGA precision every other paletted codec in
/// this package reads and not the red/blue/green order the format's own published description
/// states.</b> Both were settled the same way: reading <c>TITLE.CMV</c>'s header bytes as red, green,
/// blue at full eight-bit precision reproduces ffmpeg's own <c>rgb24</c> decode of the intra picture
/// exactly, over all 40,000 samples; the published component order and six-bit widening both disagree
/// with tens of thousands of them.
/// <para/>
/// A motion vector is permitted to name a source pixel outside the picture — nothing in the format
/// turns that off — and the published description states such a pixel counts as zero. No block in the
/// one file this was measured against ever names one, so that reading is applied as documented but is
/// not itself something a real file exercised.
/// <para/>
/// <b>Measured.</b> The one sample known to exist for this codec, <c>TITLE.CMV</c> from
/// <c>samples.ffmpeg.org/game-formats/ea-cmv/</c> — 200x200, 194 pictures, two runs of pictures back to
/// back (the first ending in an <c>MVIe</c>, the second opening with a fresh <c>MVIh</c> that restates
/// the palette) — was decoded here and by ffmpeg and compared sample for sample against ffmpeg's own
/// <c>rgb24</c> output: every one of the 194 pictures is identical, including every picture past the
/// mid-file palette restatement. This is paletted throughout, so a direct sample comparison — no RGB
/// conversion beyond looking a decoded index up in the picture's own palette, no chroma-siting
/// convention — is exactly what settles it.
/// <para/>
/// <b>What is not implemented refuses and says so.</b> A picture whose size is not a whole number of
/// 4-pixel blocks, an inter picture arriving before any intra picture has established one, and a
/// picture size that changes part way through a stream are all refused by name; nothing this was
/// measured against carries any of them.
/// </remarks>
public sealed class EaCmvVideoDecoder : IVideoCodecDecoder<EaCmvVideoDecoder> {

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("cmv ");

  private const int _CHUNK_HEADER_LENGTH = 8;
  private const int _BLOCK = 4;

  private readonly byte[] _palette = new byte[256 * 3];

  private int _width;
  private int _height;
  private EaCmvFrame? _lastFrame;       // the most recently completed picture
  private EaCmvFrame? _secondLastFrame; // the one before that

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

    var fourCc = BinaryPrimitives.ReadUInt32LittleEndian(data);
    var payload = data[_CHUNK_HEADER_LENGTH..];

    switch (fourCc) {
      case EaChunkType.MVIh:
        this._ReadHeader(payload);
        frame = null!;
        return false;

      case EaChunkType.MVIf:
        frame = this._DecodePicture(payload);
        return true;

      // Ends one CMV stream and produces no picture. A file may hold several back to back —
      // TITLE.CMV does, restarting with a fresh MVIh straight after — so this resets rather than
      // refuses, and the header that follows restates everything the next stream needs.
      case EaChunkType.MVIe:
        this._Reset();
        frame = null!;
        return false;

      default:
        throw new NotSupportedException($"An Electronic Arts CMV video packet opens with chunk 0x{fourCc:X8}, which is not one this decoder reads.");
    }
  }

  /// <summary>Drops everything one stream carried, so the next one starts from its own header.</summary>
  private void _Reset() {
    this._lastFrame = null;
    this._secondLastFrame = null;
    this._width = 0;
    this._height = 0;
    Array.Clear(this._palette);
  }

  /// <summary>
  /// Reads picture size, frame rate and (some or all of) the palette from an <c>MVIh</c> chunk.
  /// Offsets 0x04/0x06 are width/height, 0x0C/0x0E are where the palette restatement starts and how
  /// many entries it carries, and 0x10 is where those entries begin — all confirmed against
  /// <c>TITLE.CMV</c>, whose own two headers both restate the same 200x200 size and each covers a
  /// different slice of the 256-entry palette.
  /// </summary>
  private void _ReadHeader(ReadOnlySpan<byte> payload) {
    if (payload.Length < 0x10)
      throw new InvalidDataException($"An MVIh chunk is {payload.Length} bytes, short of the sixteen its own fixed fields need.");

    var width = BinaryPrimitives.ReadUInt16LittleEndian(payload[4..]);
    var height = BinaryPrimitives.ReadUInt16LittleEndian(payload[6..]);

    if (this._width != 0 && (width != this._width || height != this._height))
      throw new NotSupportedException(
        $"This Electronic Arts CMV stream states a picture of {this._width}x{this._height} and then, "
        + $"part way through, {width}x{height}. Decoding a stream whose picture size changes is not implemented.");

    if (width == 0 || height == 0)
      throw new InvalidDataException($"MVIh states a picture of {width}x{height}, which has no pixels.");

    if (width % _BLOCK != 0 || height % _BLOCK != 0)
      throw new NotSupportedException(
        $"MVIh states a picture of {width}x{height}, which is not a whole number of {_BLOCK}-pixel "
        + "blocks in both directions. Decoding such a picture is not implemented.");

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

    this._secondLastFrame = this._lastFrame ?? target;
    this._lastFrame = target;

    var palette = new byte[768];
    Array.Copy(this._palette, palette, 768);

    return new() {
      Width = this._width,
      Height = this._height,
      Format = PixelFormat.Indexed8,
      PixelData = target.Indices,
      Palette = palette,
      PaletteCount = 256,
    };
  }

  private void _DecodeIntra(ReadOnlySpan<byte> raster, EaCmvFrame target) {
    var pixelCount = this._width * this._height;
    if (raster.Length < pixelCount)
      throw new InvalidDataException($"An intra MVIf chunk carries {raster.Length} raster bytes, short of the {pixelCount} its {this._width}x{this._height} picture needs.");

    raster[..pixelCount].CopyTo(target.Indices);
  }

  private void _DecodeInter(ReadOnlySpan<byte> payload, EaCmvFrame target) {
    if (this._lastFrame == null)
      throw new InvalidDataException("An inter MVIf chunk arrived before any intra picture established a reference frame.");

    var blocksWide = this._width / _BLOCK;
    var blocksHigh = this._height / _BLOCK;
    var blockCount = blocksWide * blocksHigh;

    if (payload.Length < blockCount)
      throw new InvalidDataException($"An inter MVIf chunk carries {payload.Length} bytes, short of the {blockCount} its per-block motion buffer alone needs.");

    var motionBytes = payload[..blockCount];
    var escapeBytes = payload[blockCount..];
    var escapeAt = 0;

    var last = this._lastFrame;
    var secondLast = this._secondLastFrame ?? this._lastFrame;

    for (var by = 0; by < blocksHigh; ++by) {
      for (var bx = 0; bx < blocksWide; ++bx) {
        var motion = motionBytes[by * blocksWide + bx];

        EaCmvFrame source;
        int dx, dy;

        if (motion != 0xFF) {
          source = last;
          (dx, dy) = _MotionVector(motion);
        } else {
          if (escapeAt >= escapeBytes.Length)
            throw new InvalidDataException("An inter MVIf chunk's escape buffer ran out while a block still needed an escape byte.");

          var escape = escapeBytes[escapeAt++];
          if (escape != 0xFF) {
            source = secondLast;
            (dx, dy) = _MotionVector(escape);
          } else {
            if (escapeAt + _BLOCK * _BLOCK > escapeBytes.Length)
              throw new InvalidDataException("An inter MVIf chunk's escape buffer ran out while a raw block still needed its sixteen pixels.");

            for (var yy = 0; yy < _BLOCK; ++yy)
            for (var xx = 0; xx < _BLOCK; ++xx)
              target.Indices[(by * _BLOCK + yy) * this._width + (bx * _BLOCK + xx)] = escapeBytes[escapeAt++];

            continue;
          }
        }

        for (var yy = 0; yy < _BLOCK; ++yy) {
          var sy = by * _BLOCK + yy + dy;
          for (var xx = 0; xx < _BLOCK; ++xx) {
            var sx = bx * _BLOCK + xx + dx;
            var value = sx >= 0 && sx < this._width && sy >= 0 && sy < this._height
              ? source.Indices[sy * this._width + sx]
              : (byte)0;
            target.Indices[(by * _BLOCK + yy) * this._width + (bx * _BLOCK + xx)] = value;
          }
        }
      }
    }
  }

  /// <summary>A motion byte's low nibble is the horizontal component and its high nibble the vertical
  /// one, each offset by seven so that eight is zero motion.</summary>
  private static (int Dx, int Dy) _MotionVector(byte value) => ((value & 0x0F) - 7, (value >> 4) - 7);
}
