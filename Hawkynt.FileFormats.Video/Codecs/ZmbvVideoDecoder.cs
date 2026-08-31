using System;
using System.IO;
using FileFormat.Codecs.Zmbv;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes Zip Motion Blocks Video (ZMBV), the screen-capture codec DOSBox writes: rectangular
/// blocks copied from wherever a motion vector points in the frame before, optionally XOR'ed with a
/// correction, the whole of it — block vectors, corrections and an intraframe's raw picture alike —
/// riding one continuous zlib stream.
/// </summary>
/// <remarks>
/// Lossless, and stateful in a way that is easy to get half right. Two things carry from one packet
/// to the next and neither is optional: the picture a block's motion vector is copied out of, and the
/// zlib dictionary the compressed bytes of every frame but the first are meaningless without. See
/// <see cref="ZmbvInflater"/> for the second of those — it is the harder one to get right, because a
/// decoder that opens a fresh zlib stream per packet decodes an intraframe correctly and then
/// diverges on the very next one, silently, without any packet ever failing to decompress.
/// <para/>
/// <b>The picture is not what the container states it is until the first intraframe says so.</b> An
/// intraframe carries its own six-byte header — version, whether the payload is even compressed,
/// which of the format's pixel layouts this stream uses, and the block grid it is cut into — and none
/// of that is repeated in the interframes after it, so it is kept here rather than read again. A
/// stream that opens on an interframe has no picture to predict from and no zlib stream to continue,
/// and is refused rather than guessed at.
/// <para/>
/// <b>Palettised frames carry their own palette</b>, 256 entries of red, green and blue with no bias
/// and no swap. An intraframe states it outright; an interframe with the palette-change bit set states
/// the bytes to XOR into the one already held, and one without the bit leaves it untouched. There is
/// no palette anywhere in the container for this codec — <see cref="MediaStreamInfo.CodecPrivateData"/>
/// is not read at all — because the stream states its own.
/// <para/>
/// <b>Measured against ffmpeg's own encoder</b>, since ZMBV is one of the few codecs in this package
/// ffmpeg can write as well as read. Every pixel layout it will encode — 8-bit palettised, 15-bit,
/// 16-bit and 32-bit — decoded here matches ffmpeg's own decode of the same file sample for sample,
/// across pictures that are not a whole number of blocks in either direction, several intraframes in
/// one stream, and sequences long enough that a dictionary carried wrongly across one packet would
/// have shown up by the frame after it.
/// <para/>
/// <b>What refuses.</b> A stream that opens on an interframe; a version other than the only one the
/// format defines, 0.1; a block width or height of zero; a pixel layout the format defines but no
/// encoder writes — 1, 2 and 4 bits a pixel palettised, and 24 bits a pixel — since there is nothing
/// to measure a guess at their byte packing against; and a packet whose compressed data runs out
/// before its frame does.
/// </remarks>
public sealed class ZmbvVideoDecoder : IVideoCodecDecoder<ZmbvVideoDecoder> {

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("ZMBV");

  private const int _PALETTE_BYTES = 768;

  private readonly int _width;
  private readonly int _height;
  private readonly int _streamIndex;
  private readonly ZmbvInflater _inflater = new();

  private bool _started;
  private bool _compressed;
  private bool _paletteMode;
  private int _bytesPerPixel;
  private PixelFormat _outputFormatWhenNotWidened;
  private int _blockWidth;
  private int _blockHeight;
  private int _blocksX;
  private int _blocksY;

  private byte[]? _palette;
  private byte[]? _previous;
  private byte[]? _current;

  private ZmbvVideoDecoder(int width, int height, int streamIndex) {
    this._width = width;
    this._height = height;
    this._streamIndex = streamIndex;
  }

  /// <summary>Gets the codec name.</summary>
  public static string CodecName => "Zip Motion Blocks Video";

  /// <summary>Determines whether the specified media stream is supported.</summary>
  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    return stream.Kind == MediaStreamKind.Video && stream.Codec.EqualsIgnoringCase(_Tag);
  }

  /// <summary>Creates a decoder for the specified media stream.</summary>
  public static ZmbvVideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    if (stream.Width <= 0 || stream.Height <= 0)
      throw new InvalidDataException(
        $"Video stream {stream.Index} states a picture size of {stream.Width}x{stream.Height}, which no frame can be decoded into.");

    return new(stream.Width, stream.Height, stream.Index);
  }

  /// <summary>Reads the payload one frame draws from — the continuing zlib stream, or the raw bytes
  /// of an uncompressed packet — without either the caller or this having to know which.</summary>
  private sealed class _FrameSource(ZmbvInflater? inflater, ReadOnlyMemory<byte> raw, int streamIndex) {
    private int _position;

    public void ReadExactly(Span<byte> destination) {
      if (inflater != null) {
        inflater.ReadExactly(destination);
        return;
      }

      var remaining = raw.Length - this._position;
      if (destination.Length > remaining)
        throw new InvalidDataException(
          $"Video stream {streamIndex} carries an uncompressed ZMBV packet with {remaining} byte(s) left where its "
          + $"frame needs {destination.Length} more.");

      raw.Span.Slice(this._position, destination.Length).CopyTo(destination);
      this._position += destination.Length;
    }
  }

  /// <summary>Attempts to decode the specified coded packet into a raw image frame.</summary>
  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    var data = packet.Data.Span;
    if (data.Length < 1)
      throw new InvalidDataException($"Video stream {this._streamIndex} carries an empty ZMBV packet.");

    var flags = data[0];
    var isIntra = (flags & 1) != 0;
    var paletteChange = (flags & 2) != 0;

    if (!this._started && !isIntra)
      throw new NotSupportedException(
        $"Video stream {this._streamIndex} opens on a ZMBV interframe. This codec has no picture to predict from "
        + "and no zlib stream to continue until an intraframe supplies both, so decoding cannot begin here.");

    var offset = 1;
    if (isIntra)
      offset = this._ReadIntraHeader(data);

    var payload = packet.Data[offset..];

    _FrameSource source;
    if (this._compressed) {
      if (isIntra)
        this._inflater.Reset(payload);
      else
        this._inflater.Continue(payload);

      source = new(this._inflater, default, this._streamIndex);
    } else
      source = new(null, payload, this._streamIndex);

    if (this._paletteMode) {
      if (isIntra)
        source.ReadExactly(this._palette!);
      else if (paletteChange)
        this._XorPalette(source);
    }

    if (isIntra)
      source.ReadExactly(this._current!);
    else
      this._DecodeInterframe(source);

    frame = this._ComposeFrame();

    (this._previous, this._current) = (this._current, this._previous);
    this._started = true;
    return true;
  }

  // ============================================================================================
  // The intraframe header
  // ============================================================================================

  private int _ReadIntraHeader(ReadOnlySpan<byte> data) {
    if (data.Length < 7)
      throw new InvalidDataException(
        $"Video stream {this._streamIndex} carries a ZMBV intraframe of {data.Length} byte(s), where the header "
        + "alone is seven.");

    var majorVersion = data[1];
    var minorVersion = data[2];
    if (majorVersion != 0 || minorVersion != 1)
      throw new NotSupportedException(
        $"Video stream {this._streamIndex} states ZMBV version {majorVersion}.{minorVersion}. The only version this "
        + "format defines is 0.1.");

    var compressionType = data[3];
    if (compressionType is not (0 or 1))
      throw new NotSupportedException(
        $"Video stream {this._streamIndex} states ZMBV compression type {compressionType}. Only 0 (uncompressed) "
        + "and 1 (zlib) are defined.");

    this._compressed = compressionType == 1;

    var videoFormat = data[4];
    (this._bytesPerPixel, this._paletteMode, this._outputFormatWhenNotWidened) = videoFormat switch {
      4 => (1, true, PixelFormat.Indexed8),
      5 => (2, false, PixelFormat.Rgb24), // 15 bpp — widened, see _ComposeFrame
      6 => (2, false, PixelFormat.Rgb565),
      8 => (4, false, PixelFormat.Bgra32),
      1 or 2 or 3 or 7 => throw new NotSupportedException(
        $"Video stream {this._streamIndex} states ZMBV video format {videoFormat}. The format defines it, but no "
        + "encoder in existence writes it, so there is nothing to measure this codec's byte packing of it against; "
        + "it is refused rather than guessed at."),
      _ => throw new NotSupportedException(
        $"Video stream {this._streamIndex} states ZMBV video format {videoFormat}, which the format does not define."),
    };

    var blockWidth = data[5];
    var blockHeight = data[6];
    if (blockWidth == 0 || blockHeight == 0)
      throw new InvalidDataException(
        $"Video stream {this._streamIndex} states a ZMBV block size of {blockWidth}x{blockHeight}, which is invalid.");

    this._blockWidth = blockWidth;
    this._blockHeight = blockHeight;
    this._blocksX = (this._width + blockWidth - 1) / blockWidth;
    this._blocksY = (this._height + blockHeight - 1) / blockHeight;

    var frameBytes = this._width * this._height * this._bytesPerPixel;
    this._previous = new byte[frameBytes];
    this._current = new byte[frameBytes];
    if (this._paletteMode)
      this._palette ??= new byte[_PALETTE_BYTES];

    return 7;
  }

  // ============================================================================================
  // The palette
  // ============================================================================================

  private void _XorPalette(_FrameSource source) {
    Span<byte> delta = stackalloc byte[_PALETTE_BYTES];
    source.ReadExactly(delta);

    var palette = this._palette!;
    for (var i = 0; i < _PALETTE_BYTES; ++i)
      palette[i] ^= delta[i];
  }

  // ============================================================================================
  // The interframe: a block grid, each block copied from a motion vector and maybe XOR'ed
  // ============================================================================================

  private void _DecodeInterframe(_FrameSource source) {
    var blockCount = this._blocksX * this._blocksY;
    var infoBytes = blockCount * 2;
    var paddedInfoBytes = (infoBytes + 3) / 4 * 4;
    var blockInfo = paddedInfoBytes <= 256
      ? stackalloc byte[paddedInfoBytes]
      : new byte[paddedInfoBytes];
    source.ReadExactly(blockInfo);

    var bpp = this._bytesPerPixel;
    var width = this._width;
    var height = this._height;
    var stride = width * bpp;
    var previous = this._previous!;
    var current = this._current!;
    var blockBuffer = new byte[this._blockWidth * this._blockHeight * bpp];

    for (var by = 0; by < this._blocksY; ++by) {
      var y0 = by * this._blockHeight;
      var blockHeight = Math.Min(this._blockHeight, height - y0);

      for (var bx = 0; bx < this._blocksX; ++bx) {
        var x0 = bx * this._blockWidth;
        var blockWidth = Math.Min(this._blockWidth, width - x0);

        var index = by * this._blocksX + bx;
        var a = unchecked((sbyte)blockInfo[index * 2]);
        var b = unchecked((sbyte)blockInfo[index * 2 + 1]);
        var dx = a >> 1;
        var dy = b >> 1;
        var xored = (blockInfo[index * 2] & 1) != 0;

        _CopyBlock(previous, current, width, height, stride, bpp, x0, y0, dx, dy, blockWidth, blockHeight);

        if (!xored)
          continue;

        var xorLength = blockWidth * blockHeight * bpp;
        var xorSpan = blockBuffer.AsSpan(0, xorLength);
        source.ReadExactly(xorSpan);
        _XorBlock(current, stride, bpp, x0, y0, blockWidth, blockHeight, xorSpan);
      }
    }
  }

  /// <summary>
  /// Copies one block from wherever its motion vector points in the frame before, zero-filling
  /// whatever part of the source that offset puts outside the picture.
  /// </summary>
  private static void _CopyBlock(
    ReadOnlySpan<byte> previous, Span<byte> current, int width, int height, int stride, int bpp,
    int x0, int y0, int dx, int dy, int blockWidth, int blockHeight) {
    for (var row = 0; row < blockHeight; ++row) {
      var destRow = current.Slice((y0 + row) * stride + x0 * bpp, blockWidth * bpp);
      var sourceY = y0 + row + dy;
      if (sourceY < 0 || sourceY >= height) {
        destRow.Clear();
        continue;
      }

      var sourceX0 = x0 + dx;
      for (var column = 0; column < blockWidth; ++column) {
        var sourceX = sourceX0 + column;
        var destPixel = destRow.Slice(column * bpp, bpp);
        if (sourceX < 0 || sourceX >= width) {
          destPixel.Clear();
          continue;
        }

        previous.Slice(sourceY * stride + sourceX * bpp, bpp).CopyTo(destPixel);
      }
    }
  }

  private static void _XorBlock(
    Span<byte> current, int stride, int bpp, int x0, int y0, int blockWidth, int blockHeight, ReadOnlySpan<byte> xor) {
    var rowBytes = blockWidth * bpp;
    for (var row = 0; row < blockHeight; ++row) {
      var destRow = current.Slice((y0 + row) * stride + x0 * bpp, rowBytes);
      var xorRow = xor.Slice(row * rowBytes, rowBytes);
      for (var i = 0; i < rowBytes; ++i)
        destRow[i] ^= xorRow[i];
    }
  }

  // ============================================================================================
  // What comes out
  // ============================================================================================

  private RawImage _ComposeFrame() {
    var current = this._current!;

    if (this._paletteMode)
      return new() {
        Width = this._width,
        Height = this._height,
        Format = PixelFormat.Indexed8,
        PixelData = (byte[])current.Clone(),
        Palette = (byte[])this._palette!.Clone(),
        PaletteCount = 256,
      };

    if (this._bytesPerPixel == 2 && this._outputFormatWhenNotWidened == PixelFormat.Rgb24)
      return new() {
        Width = this._width,
        Height = this._height,
        Format = PixelFormat.Rgb24,
        PixelData = _Widen555(current, this._width * this._height),
      };

    return new() {
      Width = this._width,
      Height = this._height,
      Format = this._outputFormatWhenNotWidened,
      PixelData = (byte[])current.Clone(),
    };
  }

  /// <summary>
  /// Widens a packed 5-5-5 picture to eight bits a channel by repeating each channel's five bits
  /// rather than shifting them, the same rule Microsoft Video 1's identical layout was measured
  /// against in this package.
  /// </summary>
  private static byte[] _Widen555(byte[] packed, int pixelCount) {
    var picture = new byte[pixelCount * 3];
    for (var i = 0; i < pixelCount; ++i) {
      var colour = packed[i * 2] | (packed[i * 2 + 1] << 8);
      picture[i * 3] = _Widen((colour >> 10) & 0x1F);
      picture[i * 3 + 1] = _Widen((colour >> 5) & 0x1F);
      picture[i * 3 + 2] = _Widen(colour & 0x1F);
    }

    return picture;
  }

  private static byte _Widen(int channel) => (byte)((channel << 3) | (channel >> 2));
}
