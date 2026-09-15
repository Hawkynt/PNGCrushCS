using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Bmp;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Encodes Q-Team QPEG video (<c>QPEG</c>): palettised eight-bit pictures, bottom row first, with
/// run-length coding for intraframes and skip/run/literal coding against the frame before for deltas.
/// </summary>
/// <remarks>
/// Written from the public "Description of the QPEG Video Codec" by Mike Melanson and Konstantin
/// Shishkov, mirrored by MultimediaWiki, and cross-checked against FFmpeg's LGPL-2.1-or-later QPEG
/// decoder. FFmpeg has no QPEG encoder, so this is an independent encoder of the documented syntax,
/// not a translation of another implementation.
/// <para/>
/// <b>What this writes.</b> Intraframes use the documented short and long repeated-value runs plus
/// short literal copies; interframes use frame type zero — no motion compensation — and only the
/// literal, repeated-value and skip opcodes needed to describe any change against the previous
/// picture. The 128-byte per-frame fill table is therefore zero. The decoder accepts the other legal
/// forms too, including the larger intra opcodes, fill-table references and motion compensation.
/// <para/>
/// <b>Lossless, and only from indices.</b> QPEG frames contain palette indices while the palette lives
/// in the stream format, not in a packet. The first <see cref="PixelFormat.Indexed8"/> picture fixes
/// that palette unless the requested stream already carried one in its <c>BITMAPINFOHEADER</c>. A
/// later picture with another palette is refused rather than decoded to colours it never meant, and
/// direct-colour pictures are refused rather than silently quantised to 256 colours.
/// <para/>
/// A delta is written only when its payload is smaller than the complete intraframe. This makes the
/// first frame a key frame and makes any later frame whose complete coding wins a key frame too.
/// </remarks>
[VerifiedBy(ConformanceOracle.FFmpeg)]
public sealed class QpegVideoEncoder : IVideoCodecEncoder<QpegVideoEncoder> {

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("QPEG");

  private const int _HEADER_LENGTH = 134;
  private const byte _EXPECTED_MARKER = 0xE0;
  private const byte _FRAME_TYPE_INTER_NO_MC = 0x00;
  private const byte _FRAME_TYPE_INTRA = 0x10;
  private const byte _INTRA_END = 0xFC;
  private const byte _INTER_END = 0xE0;
  private const int _MAX_PALETTE_ENTRIES = 256;
  private const int _MAX_INTRA_SHORT_LITERAL = 128;
  private const int _MAX_INTRA_LONG_RUN = 2049;
  private const int _MAX_INTER_LITERAL = 32;
  private const int _MAX_INTER_RUN = 32;
  private const int _MAX_INTER_SKIP = 575;

  private readonly MediaStreamInfo _requested;
  private readonly int _width;
  private readonly int _height;
  private byte[]? _palette;
  private int _paletteCount;
  private MediaStreamInfo? _stream;

  /// <summary>The previous picture as palette indices in QPEG's coded order, bottom row first.</summary>
  private byte[]? _previous;

  private QpegVideoEncoder(MediaStreamInfo stream) {
    this._requested = stream;
    this._width = stream.Width;
    this._height = stream.Height;
    this._AdoptPaletteFrom(stream.CodecPrivateData.Span);
  }

  public static string CodecName => "Q-Team QPEG";

  public static CodecTag Codec => _Tag;

  /// <summary>Builds an eight-bit palettised QPEG encoder for the requested picture geometry.</summary>
  public static QpegVideoEncoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException("Q-Team QPEG can only encode a video stream.");
    if (stream.Width <= 0 || stream.Height <= 0)
      throw new NotSupportedException(
        $"A QPEG encoder needs the picture size up front; {stream.Width}x{stream.Height} was supplied.");

    var pixels = (long)stream.Width * stream.Height;
    if (pixels > (int.MaxValue - _HEADER_LENGTH - 1L) / 2)
      throw new NotSupportedException(
        $"A picture of {stream.Width}x{stream.Height} is more pixels than a QPEG packet can safely hold.");
    if (stream.BitsPerPixel is not (0 or 8))
      throw new NotSupportedException(
        $"Video stream {stream.Index} asks for {stream.BitsPerPixel} bits per pixel. QPEG stores one eight-bit "
        + "palette index per pixel and no other depth is defined.");

    return new(stream);
  }

  /// <summary>Encodes one picture, choosing a delta only when it is smaller than a complete frame.</summary>
  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    ArgumentNullException.ThrowIfNull(frame);
    if (frame.Width != this._width || frame.Height != this._height)
      throw new InvalidDataException(
        $"QPEG geometry is fixed at {this._width}x{this._height}; received {frame.Width}x{frame.Height}.");
    if (frame.Format != PixelFormat.Indexed8)
      throw new NotSupportedException(
        $"QPEG codes palette indices and takes only Indexed8 pictures; a {frame.Format} picture would have to be "
        + "quantised first, and which colours to reduce it to is not this codec's decision.");
    if (!frame.HasEnoughPixelData)
      throw new InvalidDataException(
        "The source RawImage does not contain enough pixel data for its declared format and dimensions.");
    if (frame.HasAlpha)
      throw new NotSupportedException(
        "QPEG has no alpha channel; an Indexed8 picture with transparent palette entries cannot be written losslessly.");

    this._TakePaletteFrom(frame);
    var current = this._CodedIndices(frame);
    var intra = _EncodeIntra(current);

    byte frameType;
    byte[] payload;
    var keyFrame = true;
    if (this._previous != null) {
      var inter = _EncodeInter(current, this._previous);
      if (inter.Length < intra.Length) {
        frameType = _FRAME_TYPE_INTER_NO_MC;
        payload = inter;
        keyFrame = false;
      } else {
        frameType = _FRAME_TYPE_INTRA;
        payload = intra;
      }
    } else {
      frameType = _FRAME_TYPE_INTRA;
      payload = intra;
    }

    this._previous = current;
    var data = _Frame(frameType, payload);
    packet = new(
      this._requested.Index,
      data,
      PresentationTimestamp: presentationTimestamp,
      DecodeTimestamp: presentationTimestamp,
      IsKeyFrame: keyFrame);
    return true;
  }

  /// <summary>
  /// Describes the eight-bit VFW-style stream, including its RGBQUAD palette behind the
  /// <c>BITMAPINFOHEADER</c>.
  /// </summary>
  public MediaStreamInfo DescribeStream() {
    if (this._stream != null)
      return this._stream;
    if (this._palette == null)
      throw new InvalidOperationException(
        "A QPEG stream cannot be described before its palette is known. Encode the first Indexed8 picture first, "
        + "or hand Create a stream whose CodecPrivateData is a BITMAPINFOHEADER with an eight-bit palette behind it.");

    var header = new BitmapInfoHeader(
      HeaderSize: BitmapInfoHeader.StructSize,
      Width: this._width,
      Height: this._height,
      Planes: 1,
      BitsPerPixel: 8,
      Compression: unchecked((int)_Tag.Value),
      ImageSize: 0,
      XPixelsPerMeter: 0,
      YPixelsPerMeter: 0,
      ColorsUsed: this._paletteCount,
      ImportantColors: 0);

    var format = new byte[BitmapInfoHeader.StructSize + this._paletteCount * 4];
    header.WriteTo(format);
    for (var entry = 0; entry < this._paletteCount; ++entry) {
      var at = BitmapInfoHeader.StructSize + entry * 4;
      format[at] = this._palette[entry * 3 + 2];
      format[at + 1] = this._palette[entry * 3 + 1];
      format[at + 2] = this._palette[entry * 3];
    }

    return this._stream = new() {
      Index = this._requested.Index,
      Kind = MediaStreamKind.Video,
      Codec = _Tag,
      Handler = _Tag,
      CodecId = "V_MS/VFW/FOURCC",
      TimeBase = this._requested.TimeBase,
      FrameRate = this._requested.FrameRate,
      DeclaredFrameCount = this._requested.DeclaredFrameCount,
      Width = this._width,
      Height = this._height,
      BitsPerPixel = 8,
      CodecPrivateData = format,
      Language = this._requested.Language,
      Name = this._requested.Name,
    };
  }

  // ============================================================================================
  // What goes in
  // ============================================================================================

  /// <summary>Fixes the stream palette from the first picture, or verifies that it did not change.</summary>
  private void _TakePaletteFrom(RawImage frame) {
    if (frame.Palette == null || frame.PaletteCount <= 0)
      throw new InvalidDataException(
        "An Indexed8 picture without a palette cannot be coded: QPEG packets hold indices and the stream header "
        + "holds their colours.");
    if (frame.PaletteCount > _MAX_PALETTE_ENTRIES)
      throw new InvalidDataException(
        $"An Indexed8 picture states {frame.PaletteCount} palette entries; an eight-bit QPEG index can address at most 256.");
    if (frame.Palette.Length < frame.PaletteCount * 3)
      throw new InvalidDataException(
        $"The picture states a palette of {frame.PaletteCount} entries but carries {frame.Palette.Length / 3}.");

    var bytes = frame.PaletteCount * 3;
    if (this._palette == null) {
      this._palette = frame.Palette.AsSpan(0, bytes).ToArray();
      this._paletteCount = frame.PaletteCount;
      return;
    }

    if (frame.PaletteCount == this._paletteCount && frame.Palette.AsSpan(0, bytes).SequenceEqual(this._palette))
      return;

    throw new InvalidDataException(
      "The picture carries a different palette from the one the QPEG stream was described with. The palette is "
      + "stored once in the stream header and cannot change between frames.");
  }

  /// <summary>Takes a valid eight-bit palette out of an existing VFW stream description where present.</summary>
  private void _AdoptPaletteFrom(ReadOnlySpan<byte> format) {
    if (format.Length < BitmapInfoHeader.StructSize)
      return;

    var info = BitmapInfoHeader.ReadFrom(format);
    if (info.BitsPerPixel != 8 || info.HeaderSize < BitmapInfoHeader.StructSize || info.HeaderSize > format.Length)
      return;

    var entries = info.ColorsUsed > 0 ? info.ColorsUsed : _MAX_PALETTE_ENTRIES;
    if (entries is <= 0 or > _MAX_PALETTE_ENTRIES || format.Length < info.HeaderSize + entries * 4)
      return;

    var palette = new byte[entries * 3];
    for (var entry = 0; entry < entries; ++entry) {
      var at = info.HeaderSize + entry * 4;
      palette[entry * 3] = format[at + 2];
      palette[entry * 3 + 1] = format[at + 1];
      palette[entry * 3 + 2] = format[at];
    }

    this._palette = palette;
    this._paletteCount = entries;
  }

  /// <summary>Copies the top-down RawImage index plane into QPEG's bottom-row-first coded order.</summary>
  private byte[] _CodedIndices(RawImage frame) {
    var current = new byte[this._width * this._height];
    for (var y = 0; y < this._height; ++y) {
      var source = frame.PixelData.AsSpan(y * this._width, this._width);
      var destination = current.AsSpan((this._height - 1 - y) * this._width, this._width);
      source.CopyTo(destination);
    }

    for (var i = 0; i < current.Length; ++i)
      if (current[i] >= this._paletteCount)
        throw new InvalidDataException(
          $"Pixel {i % this._width},{this._height - 1 - i / this._width} is palette index {current[i]} and the "
          + $"QPEG stream palette has {this._paletteCount} entries.");

    return current;
  }

  // ============================================================================================
  // Intraframes
  // ============================================================================================

  /// <summary>Codes a complete picture with repeated-value runs and literal copies.</summary>
  private static byte[] _EncodeIntra(ReadOnlySpan<byte> pixels) {
    using var output = new MemoryStream();
    var at = 0;
    while (at < pixels.Length) {
      var run = _RunLength(pixels, at);
      if (run >= 2) {
        _WriteIntraRun(output, run, pixels[at]);
        at += run;
        continue;
      }

      var start = at++;
      while (at < pixels.Length && at - start < _MAX_INTRA_SHORT_LITERAL) {
        if (at + 1 < pixels.Length && pixels[at] == pixels[at + 1])
          break;
        ++at;
      }

      var literal = pixels[start..at];
      output.WriteByte((byte)(literal.Length - 1));
      output.Write(literal);
    }

    output.WriteByte(_INTRA_END);
    return output.ToArray();
  }

  /// <summary>Writes one or more short/long intra run opcodes for a repeated palette index.</summary>
  private static void _WriteIntraRun(Stream output, int count, byte value) {
    while (count > _MAX_INTRA_LONG_RUN) {
      var chunk = _MAX_INTRA_LONG_RUN;
      if (count - chunk == 1)
        --chunk;
      _WriteIntraRunOpcode(output, chunk, value);
      count -= chunk;
    }

    if (count == 1) {
      output.WriteByte(0x00);
      output.WriteByte(value);
    } else
      _WriteIntraRunOpcode(output, count, value);
  }

  private static void _WriteIntraRunOpcode(Stream output, int count, byte value) {
    if (count <= 17) {
      output.WriteByte((byte)(0xE0 | (count - 2)));
      output.WriteByte(value);
      return;
    }

    var encoded = count - 2;
    output.WriteByte((byte)(0xF0 | (encoded >> 8)));
    output.WriteByte((byte)encoded);
    output.WriteByte(value);
  }

  // ============================================================================================
  // Interframes
  // ============================================================================================

  /// <summary>Codes only what changed against <paramref name="previous"/>, skipping what did not.</summary>
  private static byte[] _EncodeInter(ReadOnlySpan<byte> current, ReadOnlySpan<byte> previous) {
    using var output = new MemoryStream();
    var at = 0;
    while (at < current.Length) {
      if (current[at] == previous[at]) {
        var start = at++;
        while (at < current.Length && current[at] == previous[at])
          ++at;
        _WriteInterSkip(output, at - start);
        continue;
      }

      var changedStart = at++;
      while (at < current.Length && current[at] != previous[at])
        ++at;
      _WriteInterChanged(output, current[changedStart..at]);
    }

    output.WriteByte(_INTER_END);
    return output.ToArray();
  }

  /// <summary>Writes a changed stretch as repeated-value runs and literal copies.</summary>
  private static void _WriteInterChanged(Stream output, ReadOnlySpan<byte> pixels) {
    var at = 0;
    while (at < pixels.Length) {
      var run = _RunLength(pixels, at);
      if (run >= 2) {
        _WriteInterRun(output, run, pixels[at]);
        at += run;
        continue;
      }

      var start = at++;
      while (at < pixels.Length && at - start < _MAX_INTER_LITERAL) {
        if (at + 1 < pixels.Length && pixels[at] == pixels[at + 1])
          break;
        ++at;
      }

      var literal = pixels[start..at];
      output.WriteByte((byte)(0xC0 | (literal.Length - 1)));
      output.Write(literal);
    }
  }

  private static void _WriteInterRun(Stream output, int count, byte value) {
    while (count > _MAX_INTER_RUN) {
      var chunk = _MAX_INTER_RUN;
      if (count - chunk == 1)
        --chunk;
      _WriteInterRunOpcode(output, chunk, value);
      count -= chunk;
    }

    if (count == 1) {
      output.WriteByte(0xC0);
      output.WriteByte(value);
    } else
      _WriteInterRunOpcode(output, count, value);
  }

  private static void _WriteInterRunOpcode(Stream output, int count, byte value) {
    output.WriteByte((byte)(0xE0 | (count - 1)));
    output.WriteByte(value);
  }

  /// <summary>Writes an unchanged stretch in the shortest of QPEG's four skip forms.</summary>
  private static void _WriteInterSkip(Stream output, int count) {
    while (count > 0) {
      if (count >= 320) {
        var chunk = Math.Min(count, _MAX_INTER_SKIP);
        output.WriteByte(0x81);
        output.WriteByte((byte)(chunk - 320));
        count -= chunk;
      } else if (count >= 64) {
        output.WriteByte(0x80);
        output.WriteByte((byte)(count - 64));
        return;
      } else if (count >= 2) {
        output.WriteByte((byte)(0x80 | count));
        return;
      } else {
        output.WriteByte(0x00);
        return;
      }
    }
  }

  private static int _RunLength(ReadOnlySpan<byte> pixels, int start) {
    var value = pixels[start];
    var at = start + 1;
    while (at < pixels.Length && pixels[at] == value)
      ++at;
    return at - start;
  }

  // ============================================================================================
  // Packet
  // ============================================================================================

  private static byte[] _Frame(byte frameType, ReadOnlySpan<byte> payload) {
    var data = new byte[checked(_HEADER_LENGTH + payload.Length)];
    BinaryPrimitives.WriteUInt32LittleEndian(data, (uint)data.Length);
    // Bytes 4..131 are the per-frame fill table. This encoder does not emit fill-table references,
    // so all 128 entries intentionally stay zero.
    data[132] = _EXPECTED_MARKER;
    data[133] = frameType;
    payload.CopyTo(data.AsSpan(_HEADER_LENGTH));
    return data;
  }
}
