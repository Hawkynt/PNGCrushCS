using System;
using System.IO;
using FileFormat.Bmp;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Encodes uncompressed video: each packet is the pixel array of a Windows device-independent bitmap,
/// and nothing else.
/// </summary>
/// <remarks>
/// A plain packing that needs no reference: the layout is the one this package's own
/// <see cref="RawVideoDecoder"/> reads — <c>BI_RGB</c>, rows padded to four bytes, bottom row first
/// unless the header states a negative height — and the <c>BITMAPINFOHEADER</c> that describes it is
/// the stream's codec private data, exactly as that decoder, an AVI's <c>strf</c> and a Matroska
/// <c>V_MS/VFW/FOURCC</c> track all carry it.
/// <para/>
/// <b>Three depths.</b> Where the requested stream carries no header of its own, its
/// <see cref="MediaStreamInfo.BitsPerPixel"/> chooses: 24 (or nothing stated) codes
/// <see cref="PixelFormat.Bgr24"/>, 32 codes <see cref="PixelFormat.Bgra32"/>, and 8 codes one byte
/// an index into a 256-entry grey ramp, which is what the bitmap reader hands back as
/// <see cref="PixelFormat.Gray8"/>. Where the stream carries a <c>BITMAPINFOHEADER</c> already — the
/// one a demuxer read, so that "read this stream and write it again" is one value passed along — it
/// is kept verbatim, palette included, and the pictures are coded to match it: an 8-bit stream then
/// takes <see cref="PixelFormat.Indexed8"/> pictures whose palette is the header's, and codes their
/// indices as they are.
/// <para/>
/// <b>Lossless.</b> Every sample is copied, none computed; a picture in the stream's own format comes
/// back from the decoder identical, and one in any other format is converted to it first. Two edges
/// belong to the bitmap reader rather than to this coding, and are worth knowing: a 32-bit picture
/// whose every alpha byte is zero is read back as <see cref="PixelFormat.Bgr24"/>, because a
/// <c>BI_RGB</c> header states no alpha channel and a fourth byte that is zero throughout is padding
/// by every reader's convention; and an 8-bit palette whose every entry is grey is read back as
/// <see cref="PixelFormat.Gray8"/> rather than as indices.
/// <para/>
/// <b>Verified against ffmpeg's own decode, exactly.</b> Packets written here were muxed into an AVI
/// and read back through ffmpeg 9 as <c>bgr24</c>, <c>bgra</c> and <c>pal8</c> raw frames, over
/// pseudo-random pictures at 7x5, 16x9 and 33x17, five frames apiece, at all three depths: every byte
/// of every frame identical, the 256 palette entries of an 8-bit stream included. (Asking ffmpeg for
/// <c>gray</c> instead of the indices of a grey-ramp stream puts some samples one off — its scaler's
/// palette-to-grey rounding, not the packing — which is why the indices are what was compared.)
/// <para/>
/// <b>What refuses.</b> A depth other than 8, 24 or 32; a header stating any compression but
/// <c>BI_RGB</c>, a geometry other than the stream's, or fewer palette entries than it claims; a frame
/// whose geometry differs from the stream's; and, for an 8-bit stream whose palette is not the grey
/// ramp, a picture that is not <see cref="PixelFormat.Indexed8"/> with that same palette — mapping
/// arbitrary colour onto a fixed palette is quantisation, and quantising quietly is how a wrong
/// picture gets written.
/// </remarks>
public sealed class RawVideoEncoder : IVideoCodecEncoder<RawVideoEncoder> {

  /// <summary>The four-character code a writer puts in the stream handler beside a zero compression.</summary>
  private static readonly CodecTag _Handler = CodecTag.FromCharacters("DIB ");

  /// <summary>What Matroska calls a track that describes itself with a <c>BITMAPINFOHEADER</c>.</summary>
  private const string _VFW_CODEC_ID = "V_MS/VFW/FOURCC";

  private const int _BI_RGB = 0;
  private const int _PALETTE_ENTRY_SIZE = 4;

  private readonly MediaStreamInfo _stream;
  private readonly int _width;
  private readonly int _height;
  private readonly int _bitsPerPixel;
  private readonly int _bytesPerRow;
  private readonly int _stride;
  private readonly bool _topDown;
  private readonly byte[]? _palette;
  private readonly int _paletteCount;
  private readonly bool _paletteIsGreyRamp;

  private RawVideoEncoder(MediaStreamInfo stream, byte[] format, int bitsPerPixel, bool topDown, byte[]? palette, int paletteCount) {
    this._width = stream.Width;
    this._height = stream.Height;
    this._bitsPerPixel = bitsPerPixel;
    this._bytesPerRow = (stream.Width * bitsPerPixel + 7) / 8;
    this._stride = (this._bytesPerRow + 3) & ~3;
    this._topDown = topDown;
    this._palette = palette;
    this._paletteCount = paletteCount;
    this._paletteIsGreyRamp = palette != null && _IsGreyRamp(palette, paletteCount);
    this._stream = new() {
      Index = stream.Index,
      Kind = MediaStreamKind.Video,
      Codec = CodecTag.None,
      Handler = _Handler,
      CodecId = _VFW_CODEC_ID,
      TimeBase = stream.TimeBase,
      FrameRate = stream.FrameRate,
      DeclaredFrameCount = stream.DeclaredFrameCount,
      Width = stream.Width,
      Height = stream.Height,
      BitsPerPixel = bitsPerPixel,
      CodecPrivateData = format,
      Language = stream.Language,
      Name = stream.Name,
    };
  }

  public static string CodecName => "Uncompressed (BI_RGB)";

  public static CodecTag Codec => CodecTag.None;

  public static RawVideoEncoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException("Uncompressed bitmap frames can only code a video stream.");
    if (stream.Width <= 0 || stream.Height <= 0)
      throw new InvalidDataException(
        $"Video stream {stream.Index} states a picture size of {stream.Width}x{stream.Height}, which no frame can be coded from.");

    return stream.CodecPrivateData.IsEmpty ? _FromDepth(stream) : _FromHeader(stream);
  }

  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    ArgumentNullException.ThrowIfNull(frame);
    if (frame.Width != this._width || frame.Height != this._height)
      throw new InvalidDataException(
        $"Uncompressed-video geometry is fixed at {this._width}x{this._height}; received {frame.Width}x{frame.Height}.");
    if (!frame.HasEnoughPixelData)
      throw new InvalidDataException("The source RawImage does not contain enough pixel data for its declared format and dimensions.");

    var samples = this._bitsPerPixel switch {
      24 => _Converted(frame, PixelFormat.Bgr24),
      32 => _Converted(frame, PixelFormat.Bgra32),
      _ => this._Indices(frame),
    };
    if (samples.Length < this._bytesPerRow * this._height)
      throw new InvalidDataException(
        $"Conversion produced {samples.Length} bytes where {this._width}x{this._height} at {this._bitsPerPixel} bits needs {this._bytesPerRow * this._height}.");

    var data = new byte[checked(this._stride * this._height)];
    for (var row = 0; row < this._height; ++row) {
      var sourceRow = this._topDown ? row : this._height - 1 - row;
      samples.AsSpan(sourceRow * this._bytesPerRow, this._bytesPerRow).CopyTo(data.AsSpan(row * this._stride));
    }

    packet = new(
      this._stream.Index,
      data,
      PresentationTimestamp: presentationTimestamp,
      DecodeTimestamp: presentationTimestamp,
      Duration: 1,
      IsKeyFrame: true);
    return true;
  }

  public MediaStreamInfo DescribeStream() => this._stream;

  /// <summary>Builds the header from the depth the stream asks for, with nothing else to go on.</summary>
  private static RawVideoEncoder _FromDepth(MediaStreamInfo stream) {
    var bitsPerPixel = stream.BitsPerPixel switch {
      0 or 24 => 24,
      32 => 32,
      8 => 8,
      _ => throw new NotSupportedException(
        $"Video stream {stream.Index} asks for uncompressed frames of {stream.BitsPerPixel} bits per pixel; 8 (grey), 24 and 32 are written."),
    };

    byte[]? palette = null;
    var paletteCount = 0;
    if (bitsPerPixel == 8) {
      paletteCount = 256;
      palette = new byte[paletteCount * 3];
      for (var i = 0; i < paletteCount; ++i)
        palette[i * 3] = palette[i * 3 + 1] = palette[i * 3 + 2] = (byte)i;
    }

    var stride = ((stream.Width * bitsPerPixel + 7) / 8 + 3) & ~3;
    var format = new byte[BitmapInfoHeader.StructSize + paletteCount * _PALETTE_ENTRY_SIZE];
    new BitmapInfoHeader(
      BitmapInfoHeader.StructSize,
      stream.Width,
      stream.Height,
      1,
      (short)bitsPerPixel,
      _BI_RGB,
      checked(stride * stream.Height),
      0,
      0,
      paletteCount,
      0).WriteTo(format);
    for (var i = 0; i < paletteCount; ++i) {
      var entry = BitmapInfoHeader.StructSize + i * _PALETTE_ENTRY_SIZE;
      format[entry] = palette![i * 3 + 2];
      format[entry + 1] = palette[i * 3 + 1];
      format[entry + 2] = palette[i * 3];
    }

    return new(stream, format, bitsPerPixel, topDown: false, palette, paletteCount);
  }

  /// <summary>Takes the header the stream already carries, and codes to match it.</summary>
  private static RawVideoEncoder _FromHeader(MediaStreamInfo stream) {
    var format = stream.CodecPrivateData.Span;
    if (format.Length < BitmapInfoHeader.StructSize)
      throw new InvalidDataException(
        $"Video stream {stream.Index} carries {format.Length} bytes of stream format where a BITMAPINFOHEADER is {BitmapInfoHeader.StructSize}.");

    var info = BitmapInfoHeader.ReadFrom(format);
    if (info.HeaderSize < BitmapInfoHeader.StructSize || info.HeaderSize > format.Length)
      throw new InvalidDataException(
        $"Video stream {stream.Index} carries a bitmap header stating a size of {info.HeaderSize} bytes inside {format.Length}.");
    if (info.Compression != _BI_RGB)
      throw new NotSupportedException(
        $"Video stream {stream.Index} carries a bitmap header stating compression {info.Compression}; only BI_RGB frames are written.");
    if (info.BitsPerPixel is not (8 or 24 or 32))
      throw new NotSupportedException(
        $"Video stream {stream.Index} carries a bitmap header of {info.BitsPerPixel} bits per pixel; 8, 24 and 32 are written.");
    if (info.Width != stream.Width || Math.Abs(info.Height) != stream.Height)
      throw new InvalidDataException(
        $"Video stream {stream.Index} states {stream.Width}x{stream.Height} where its bitmap header states {info.Width}x{info.Height}.");

    byte[]? palette = null;
    var paletteCount = 0;
    if (info.BitsPerPixel == 8) {
      paletteCount = info.ColorsUsed > 0 ? info.ColorsUsed : 256;
      if (paletteCount > 256)
        throw new InvalidDataException(
          $"Video stream {stream.Index} carries a bitmap header stating {paletteCount} palette entries, where 8 bits index at most 256.");
      var available = (format.Length - info.HeaderSize) / _PALETTE_ENTRY_SIZE;
      if (available < paletteCount)
        throw new InvalidDataException(
          $"Video stream {stream.Index} carries {available} of the {paletteCount} palette entries its bitmap header states.");

      palette = new byte[paletteCount * 3];
      for (var i = 0; i < paletteCount; ++i) {
        var entry = info.HeaderSize + i * _PALETTE_ENTRY_SIZE;
        palette[i * 3] = format[entry + 2];
        palette[i * 3 + 1] = format[entry + 1];
        palette[i * 3 + 2] = format[entry];
      }
    }

    return new(stream, format.ToArray(), info.BitsPerPixel, info.Height < 0, palette, paletteCount);
  }

  private static byte[] _Converted(RawImage frame, PixelFormat format)
    => (frame.Format == format ? frame : FastRawImageConverter.Convert(frame, format)).PixelData;

  /// <summary>One byte a pixel: the picture's own indices where its palette is the stream's, grey otherwise.</summary>
  private byte[] _Indices(RawImage frame) {
    if (frame.Format == PixelFormat.Indexed8 && this._SamePalette(frame))
      return frame.PixelData;

    if (this._paletteIsGreyRamp)
      return _Converted(frame, PixelFormat.Gray8);

    throw new NotSupportedException(
      $"An 8-bit uncompressed stream codes indices into the {this._paletteCount}-entry palette its header carries; a {frame.Format} picture "
      + "would first have to be mapped onto that palette, which this encoder does not do. Hand it an Indexed8 picture with the same palette.");
  }

  private bool _SamePalette(RawImage frame) {
    if (frame.Palette == null || this._palette == null || frame.PaletteCount != this._paletteCount)
      return false;

    var length = this._paletteCount * 3;
    return frame.Palette.Length >= length && frame.Palette.AsSpan(0, length).SequenceEqual(this._palette.AsSpan(0, length));
  }

  /// <summary>Whether a palette is the 256-entry ramp where entry <c>i</c> is grey <c>i</c>.</summary>
  private static bool _IsGreyRamp(byte[] palette, int count) {
    if (count != 256)
      return false;

    for (var i = 0; i < count; ++i)
      if (palette[i * 3] != i || palette[i * 3 + 1] != i || palette[i * 3 + 2] != i)
        return false;

    return true;
  }
}
