using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Codecs.Mve;
using FileFormat.Core;
using FileFormat.InterplayMve;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes Interplay video (<c>IMVE</c>) — the FMV codec behind Interplay's DOS-era catalogue,
/// Baldur's Gate among them — sixteen 8x8 block encodings read against a decoding map, motion
/// compensation included.
/// </summary>
/// <remarks>
/// A picture is built from several opcodes rather than one packet: <c>INIT_VIDEO_BUFFERS</c> states
/// the size once, <c>SET_PALETTE</c> restates some or all of the palette whenever it changes,
/// <c>DECODING_MAP</c> states the coming picture's block encodings, and only <c>VIDEO_DATA</c> reads
/// that map and produces a picture — the same seam RoQ's <c>INFO</c>/<c>QUAD_CODEBOOK</c>/<c>QUAD_VQ</c>
/// use, where a decoder is handed opcodes with their own header still attached and answers "not yet"
/// until the one that actually paints something arrives.
/// <para/>
/// <b>Two picture buffers, not one — the same finding as RoQ's, arrived at independently and this
/// time from a description that states it outright.</b> Interplay's own published format description
/// says a skipped block (encoding 0x1) "has the same value it had 2 frames ago", which only makes
/// sense if the decoder is built on exactly two alternating buffers rather than one held frame plus a
/// copy: writing (encodings 0x0 and 0x2 through 0xF) always goes into the buffer being built and always
/// reads the *other* one — the most recently completed picture; encoding 0x1 writes nothing at all, so
/// whatever that same buffer slot held the last time <em>it</em> was written — two pictures back —
/// shows through. The first picture has no second buffer to have been built into two pictures ago, so
/// its result is copied into both slots once it is painted. See <see cref="MveBlockDecoder"/> for the
/// block-level detail this rests on, including the one place the format's own description is measured
/// and found wrong: which bit of a packed byte reads first.
/// <para/>
/// <b>Measured.</b> Two files from <c>samples.ffmpeg.org/game-formats/interplay-mve/</c> — 432x320 and
/// 640x272, 225 and 330 pictures, 555 in all — were decoded here and by ffmpeg and compared sample for
/// sample against ffmpeg's own <c>pal8</c> output, index and installed palette both: every picture is
/// identical. This is paletted throughout, so a direct sample comparison — no RGB conversion, no
/// chroma-siting convention — is exactly what settles it.
/// <para/>
/// <b>What is not implemented refuses and says so.</b> A true-colour video buffer, block encoding 0x6,
/// a compressed palette opcode, and a picture size that changes part way through a stream are all
/// refused by name; no sample this was measured against carries any of them.
/// </remarks>
public sealed class MveVideoDecoder : IVideoCodecDecoder<MveVideoDecoder> {
  /// <summary>Initializes a new instance of this type.</summary>
  public MveVideoDecoder() { }

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("IMVE");

  private const int _OPCODE_HEADER_LENGTH = 4;
  private const int _BLOCK = 8;

  private readonly byte[] _palette = new byte[256 * 3];

  private int _width;
  private int _height;
  private MveFrame? _bufferA;
  private MveFrame? _bufferB;
  private bool _nextTargetIsA = true;
  private bool _hasDecodedFirstPicture;
  private byte[]? _decodingMap;

  /// <summary>Gets the codec name.</summary>
  public static string CodecName => "Interplay Video";

  /// <summary>Determines whether the specified media stream is supported.</summary>
  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    return stream.Kind == MediaStreamKind.Video && stream.Codec.EqualsIgnoringCase(_Tag);
  }

  /// <summary>Creates a decoder for the specified media stream.</summary>
  public static MveVideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    return new();
  }

  /// <summary>Attempts to decode the specified coded packet into a raw image frame.</summary>
  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    var data = packet.Data.Span;
    if (data.Length < _OPCODE_HEADER_LENGTH)
      throw new InvalidDataException($"An Interplay MVE packet is {data.Length} bytes, short of an opcode header's own four.");

    var length = BinaryPrimitives.ReadUInt16LittleEndian(data);
    var type = data[2];
    var payload = data.Slice(_OPCODE_HEADER_LENGTH, length);

    switch (type) {
      case MveOpcodeType.INIT_VIDEO_BUFFERS:
        this._ReadVideoBufferSize(payload);
        frame = null!;
        return false;

      case MveOpcodeType.SET_PALETTE:
        this._ReadPalette(payload);
        frame = null!;
        return false;

      case MveOpcodeType.DECODING_MAP:
        this._decodingMap = payload.ToArray();
        frame = null!;
        return false;

      case MveOpcodeType.VIDEO_DATA:
        frame = this._DecodePicture(payload);
        return true;

      default:
        throw new NotSupportedException($"An Interplay MVE video packet is opcode type 0x{type:X2}, which is not one this decoder reads.");
    }
  }

  private void _ReadVideoBufferSize(ReadOnlySpan<byte> payload) {
    if (payload.Length < 4)
      throw new InvalidDataException($"An INIT_VIDEO_BUFFERS opcode is {payload.Length} bytes, short of the four a picture size needs.");

    var widthBlocks = BinaryPrimitives.ReadUInt16LittleEndian(payload);
    var heightBlocks = BinaryPrimitives.ReadUInt16LittleEndian(payload[2..]);

    if (payload.Length >= 8 && BinaryPrimitives.ReadUInt16LittleEndian(payload[6..]) != 0)
      throw new NotSupportedException(
        "INIT_VIDEO_BUFFERS states a true-colour buffer. Only the 8-bit palettised mode every sample "
        + "this was built against uses is implemented.");

    var width = widthBlocks * _BLOCK;
    var height = heightBlocks * _BLOCK;

    if (this._width != 0 && (width != this._width || height != this._height))
      throw new NotSupportedException(
        $"This Interplay MVE stream states a picture of {this._width}x{this._height} and then, part way "
        + $"through, {width}x{height}. Decoding a stream whose picture size changes is not implemented.");

    if (this._width != 0)
      return;

    if (width == 0 || height == 0)
      throw new InvalidDataException($"INIT_VIDEO_BUFFERS states a picture of {width}x{height}, which has no pixels.");

    this._width = width;
    this._height = height;
    this._bufferA = new(width, height);
    this._bufferB = new(width, height);
  }

  /// <summary>
  /// Installs some or all of the palette. Every colour component is six-bit VGA precision, widened to
  /// eight bits the way this project's other six-bit channels are: by repeating the top two bits into
  /// the bottom rather than shifting, which is what reproduces ffmpeg's own installed palette exactly.
  /// </summary>
  private void _ReadPalette(ReadOnlySpan<byte> payload) {
    if (payload.Length < 4)
      throw new InvalidDataException($"A SET_PALETTE opcode is {payload.Length} bytes, short of the four its own header needs.");

    var start = BinaryPrimitives.ReadUInt16LittleEndian(payload);
    var count = BinaryPrimitives.ReadUInt16LittleEndian(payload[2..]);
    var colours = payload[4..];

    if (start + count > 256 || colours.Length < count * 3)
      throw new InvalidDataException(
        $"A SET_PALETTE opcode names {count} colours starting at {start}, which either runs past the "
        + "256-entry palette or states more colours than the opcode carries.");

    for (var i = 0; i < count; ++i) {
      var entry = (start + i) * 3;
      this._palette[entry] = ChannelScaling.Expand6(colours[i * 3]);
      this._palette[entry + 1] = ChannelScaling.Expand6(colours[i * 3 + 1]);
      this._palette[entry + 2] = ChannelScaling.Expand6(colours[i * 3 + 2]);
    }
  }

  private RawImage _DecodePicture(ReadOnlySpan<byte> payload) {
    if (this._bufferA == null)
      throw new InvalidDataException("A VIDEO_DATA opcode arrived before any INIT_VIDEO_BUFFERS opcode stated a picture size.");
    if (this._decodingMap == null)
      throw new InvalidDataException("A VIDEO_DATA opcode arrived before any DECODING_MAP opcode stated this picture's block encodings.");

    var target = this._nextTargetIsA ? this._bufferA : this._bufferB!;
    var reference = this._nextTargetIsA ? this._bufferB! : this._bufferA;

    MveBlockDecoder.Decode(this._decodingMap, payload, reference, target);

    if (!this._hasDecodedFirstPicture) {
      // The very first picture has no second buffer to have been building into two pictures ago, so
      // its result becomes both buffers' content — see MveBlockDecoder's remarks on encoding 0x1.
      reference.CopyFrom(target);
      this._hasDecodedFirstPicture = true;
    }

    this._nextTargetIsA = !this._nextTargetIsA;
    this._decodingMap = null;

    var palette = new byte[768];
    Array.Copy(this._palette, palette, 768);

    return new() {
      Width = this._width,
      Height = this._height,
      Format = PixelFormat.Indexed8,
      PixelData = (byte[])target.Indices.Clone(),
      Palette = palette,
      PaletteCount = 256,
    };
  }
}
