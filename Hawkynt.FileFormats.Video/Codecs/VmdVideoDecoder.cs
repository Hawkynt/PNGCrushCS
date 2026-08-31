using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Codecs.Vmd;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes Sierra VMD video — the FMV codec behind Phantasmagoria, Gabriel Knight 2 and Sierra's other
/// CD-ROM adventures — an LZSS-compressed run-length coding painted onto a persistent, palettised
/// picture one rectangle at a time.
/// </summary>
/// <remarks>
/// A picture is one rectangle, not the whole frame: every video packet names the corner of the canvas
/// it repaints, in <see cref="FileFormat.Vmd.VmdContainer"/>'s own sixteen-byte frame information
/// record kept in front of the compressed bytes, and the first picture's rectangle happens to be the
/// whole canvas in every sample this was measured against — which is what makes the canvas a fresh,
/// zero-filled buffer on the first packet correct without this decoder treating that packet specially.
/// The rectangle's own bytes may first need LZ decompression — see <see cref="VmdLzDecoder"/> — and
/// are then painted by one of two row-based methods; see <see cref="VmdRowCoder"/> for both, and for
/// why a skip needs no second picture buffer to reach back into the way Interplay MVE's or id RoQ's own
/// skip opcodes do.
/// <para/>
/// <b>Measured.</b> Four real files from <c>samples.ffmpeg.org/game-formats/sierra-vmd/</c> — three
/// Sierra SWAT recordings and one Lighthouse, 280x218 and 500x150, 36 to 78 pictures apiece, 197
/// pictures in all, between them exercising every path this decoder reads: method 2 on an LZ-compressed
/// intraframe, method 1 uncompressed on an ordinary interframe, and method 1 on an LZ-compressed one —
/// were decoded here and by ffmpeg and compared sample for sample against ffmpeg's own <c>pal8</c>
/// output, index and installed palette both: every picture of all four is identical. This is paletted
/// throughout, so a direct sample comparison — no RGB conversion, no chroma-siting convention — is
/// exactly what settles it.
/// <para/>
/// A fifth SWAT recording is corrupted partway through rather than refused outright: this decoder and
/// ffmpeg's own both read its first thirty-three pictures identically and then both fail — this one
/// with the row coding overrunning its own rectangle, ffmpeg's own with "Invalid data found when
/// processing input" — on the thirty-fourth, which is the sample at fault rather than either decoder.
/// A sixth file, one Leisure Suit Larry 7 recording, is not part of the measured set at all: over a
/// third of its interframes are LZ-compressed without the preload marker this decoder requires, the
/// form <see cref="VmdLzDecoder"/>'s own remarks explain was not recovered, so this decoder refuses
/// each one by name rather than decode it wrong — reached on this file's second picture already, not
/// only deep into it.
/// <para/>
/// <b>What is not implemented refuses and says so.</b> A codec version other than 1 (the eight-bit
/// palettised form — versions naming sixteen-bit, twenty-four-bit or Indeo-3-compressed video are
/// refused), render method 3 (no sample measured against this decoder uses it), a picture stating a
/// new palette mid-stream (likewise unmeasured — see below), and an LZ-compressed rectangle lacking the
/// preload marker <see cref="VmdLzDecoder"/>'s own remarks describe are all refused by name rather than
/// guessed at.
/// <para/>
/// The palette a picture can restate mid-stream is read nowhere here for the same reason: no sample
/// this decoder was measured against ever sets the flag that states one, so the 770-byte layout Sierra's
/// own published description gives it is exactly the kind of unmeasured claim this project does not
/// ship. A picture stating one is refused rather than decoded against a table nothing here confirms.
/// </remarks>
public sealed class VmdVideoDecoder : IVideoCodecDecoder<VmdVideoDecoder> {

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("VMDV");

  private const int _RECORD_LENGTH = 16;
  private const int _HEADER_CODEC_VERSION_OFFSET = 4;
  private const int _HEADER_PALETTE_OFFSET = 28;
  private const int _HEADER_PALETTE_LENGTH = 768;
  private const int _SUPPORTED_CODEC_VERSION = 1;

  private const byte _NEW_PALETTE_FLAG = 0x02;
  private const byte _LZ_FLAG = 0x80;
  private const byte _METHOD_MASK = 0x7F;
  private const byte _METHOD_ROW_RUN_LENGTH = 1;
  private const byte _METHOD_PLAIN_COPY = 2;

  private readonly byte[] _palette;
  private readonly byte[] _canvas;
  private readonly int _width;
  private readonly int _height;

  /// <summary>Gets the codec name.</summary>
  public static string CodecName => "Sierra VMD Video";

  /// <summary>Determines whether the specified media stream is supported.</summary>
  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    return stream.Kind == MediaStreamKind.Video && stream.Codec.EqualsIgnoringCase(_Tag);
  }

  /// <summary>Creates a decoder for the specified media stream.</summary>
  public static VmdVideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    var header = stream.CodecPrivateData.Span;
    if (header.Length < _HEADER_PALETTE_OFFSET + _HEADER_PALETTE_LENGTH)
      throw new InvalidDataException(
        $"A Sierra VMD video stream's private data is {header.Length} bytes, short of the "
        + $"{_HEADER_PALETTE_OFFSET + _HEADER_PALETTE_LENGTH} the header's own initial palette needs.");

    var codecVersion = BinaryPrimitives.ReadUInt16LittleEndian(header[_HEADER_CODEC_VERSION_OFFSET..]);
    if (codecVersion != _SUPPORTED_CODEC_VERSION)
      throw new NotSupportedException(
        $"This Sierra VMD stream states codec version {codecVersion}, not the eight-bit palettised "
        + $"version {_SUPPORTED_CODEC_VERSION} this decoder reads. Sixteen-bit, twenty-four-bit and "
        + "Indeo-3-compressed VMD video are not implemented.");

    if (stream.Width <= 0 || stream.Height <= 0)
      throw new InvalidDataException($"A Sierra VMD video stream states a picture of {stream.Width}x{stream.Height}, which has no pixels.");

    var palette = new byte[_HEADER_PALETTE_LENGTH];
    var sixBit = header.Slice(_HEADER_PALETTE_OFFSET, _HEADER_PALETTE_LENGTH);
    for (var i = 0; i < _HEADER_PALETTE_LENGTH; ++i)
      palette[i] = ChannelScaling.Expand6(sixBit[i]);

    return new(palette, stream.Width, stream.Height);
  }

  private VmdVideoDecoder(byte[] palette, int width, int height) {
    this._palette = palette;
    this._width = width;
    this._height = height;
    this._canvas = new byte[width * height];
  }

  /// <summary>Attempts to decode the specified coded packet into a raw image frame.</summary>
  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    var data = packet.Data.Span;
    if (data.Length < _RECORD_LENGTH)
      throw new InvalidDataException($"A Sierra VMD video packet is {data.Length} bytes, short of the sixteen-byte frame information record it should open with.");

    var left = BinaryPrimitives.ReadUInt16LittleEndian(data[6..]);
    var top = BinaryPrimitives.ReadUInt16LittleEndian(data[8..]);
    var right = BinaryPrimitives.ReadUInt16LittleEndian(data[10..]);
    var bottom = BinaryPrimitives.ReadUInt16LittleEndian(data[12..]);
    var newPalette = (data[15] & _NEW_PALETTE_FLAG) != 0;

    if (newPalette)
      throw new NotSupportedException(
        "This picture states a new palette. No sample this decoder was measured against ever sets that "
        + "flag, so the layout is not implemented — see this type's own remarks.");

    var payload = data[_RECORD_LENGTH..];
    if (payload.Length == 0)
      throw new NotSupportedException(
        "This picture states no data at all for its rectangle. No sample this decoder was measured "
        + "against does this, so what it would mean is not implemented.");

    var width = right - left + 1;
    var height = bottom - top + 1;

    var methodByte = payload[0];
    var isCompressed = (methodByte & _LZ_FLAG) != 0;
    var method = (byte)(methodByte & _METHOD_MASK);
    var rowData = payload[1..];

    ReadOnlySpan<byte> rectangleData;
    if (isCompressed) {
      if (!VmdLzDecoder.HasPreloadMarker(rowData))
        throw new NotSupportedException(
          "This picture's rectangle is LZ-compressed without the preload marker this decoder requires "
          + "— see VmdLzDecoder's own remarks for why that form is not implemented.");

      rectangleData = VmdLzDecoder.Decode(rowData);
    } else
      rectangleData = rowData;

    switch (method) {
      case _METHOD_ROW_RUN_LENGTH:
        VmdRowCoder.DecodeMethod1(rectangleData, this._canvas, this._width, this._height, left, top, width, height);
        break;
      case _METHOD_PLAIN_COPY:
        VmdRowCoder.DecodeMethod2(rectangleData, this._canvas, this._width, this._height, left, top, width, height);
        break;
      default:
        throw new NotSupportedException($"This picture states rendering method {method}, which is not one this decoder reads.");
    }

    var palette = new byte[_HEADER_PALETTE_LENGTH];
    Array.Copy(this._palette, palette, _HEADER_PALETTE_LENGTH);

    frame = new() {
      Width = this._width,
      Height = this._height,
      Format = PixelFormat.Indexed8,
      PixelData = (byte[])this._canvas.Clone(),
      Palette = palette,
      PaletteCount = 256,
    };
    return true;
  }
}
