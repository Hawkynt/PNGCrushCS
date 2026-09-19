using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>Encodes RemotelyAnywhere Screen Capture (<c>RASC</c>) video.</summary>
/// <remarks>
/// RASC is a two-surface screen codec, not a bidirectionally predicted video codec. A <c>KBND</c>
/// bundle is an independently decodable I picture whose <c>KFRM</c> payload initializes both retained
/// surfaces. Later <c>BNDL</c> bundles are P pictures: this encoder writes <c>DLTA</c> command streams
/// which keep unchanged pixels and replace changed ones while the decoder retains the preceding
/// surface. The format has no B-picture or forward-reference syntax.
/// <para/>
/// All three native RASC layouts are authorable. A requested depth of 8 writes PAL8 and therefore
/// requires indexed input so the caller's palette is preserved rather than guessed; a palette change
/// starts a new key frame. A depth of 16 writes RGB555LE, explicitly reducing eight-bit RGB channels
/// to five bits. A depth of 32, and the default when no depth is requested, writes BGR0 and preserves
/// eight-bit RGB samples exactly. Alpha has no representation in RASC and is discarded.
/// <para/>
/// The packet grammar and state transitions follow FFmpeg's LGPL-2.1-or-later RASC decoder. The
/// encoder itself is independently written from that public behavior rather than translated from an
/// encoder: FFmpeg has no RASC encoder.
/// </remarks>
public sealed class RascVideoEncoder : IVideoCodecEncoder<RascVideoEncoder> {

  private const uint _KBND = (uint)'K' | ((uint)'B' << 8) | ((uint)'N' << 16) | ((uint)'D' << 24);
  private const uint _BNDL = (uint)'B' | ((uint)'N' << 8) | ((uint)'D' << 16) | ((uint)'L' << 24);
  private const uint _KFRM = (uint)'K' | ((uint)'F' << 8) | ((uint)'R' << 16) | ((uint)'M' << 24);
  private const uint _DLTA = (uint)'D' | ((uint)'L' << 8) | ((uint)'T' << 16) | ((uint)'A' << 24);

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("RASC");

  private readonly MediaStreamInfo _stream;
  private readonly int _width;
  private readonly int _height;
  private readonly int _bitsPerPixel;
  private readonly int _bytesPerPixel;
  private readonly int _stride;
  private byte[]? _previous;
  private byte[]? _palette;

  private RascVideoEncoder(MediaStreamInfo stream, int bitsPerPixel) {
    this._width = stream.Width;
    this._height = stream.Height;
    this._bitsPerPixel = bitsPerPixel;
    this._bytesPerPixel = bitsPerPixel >> 3;
    this._stride = bitsPerPixel == 8
      ? checked((stream.Width + 3) & ~3)
      : checked(stream.Width * this._bytesPerPixel);
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
      BitsPerPixel = bitsPerPixel,
      Language = stream.Language,
      Name = stream.Name,
    };
  }

  public static string CodecName => "RemotelyAnywhere Screen Capture";

  public static CodecTag Codec => _Tag;

  public static RascVideoEncoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.Kind != MediaStreamKind.Video || stream.Width <= 0 || stream.Height <= 0)
      throw new NotSupportedException(
        $"RASC encoding requires a video stream with positive dimensions; stream {stream.Index} states "
        + $"{stream.Kind} at {stream.Width}x{stream.Height}.");

    var bitsPerPixel = stream.BitsPerPixel switch {
      0 or 32 => 32,
      8 => 8,
      16 => 16,
      _ => throw new NotSupportedException(
        $"RASC defines only 8-bit PAL8, 16-bit RGB555LE and 32-bit BGR0; {stream.BitsPerPixel} bits were requested."),
    };
    var bytesPerPixel = bitsPerPixel >> 3;
    var stride = bitsPerPixel == 8
      ? ((long)stream.Width + 3) & ~3L
      : (long)stream.Width * bytesPerPixel;
    if (stride * stream.Height > int.MaxValue / 2)
      throw new NotSupportedException("The requested RASC frame is too large to hold both retained key-frame surfaces in memory.");

    return new(stream, bitsPerPixel);
  }

  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    var native = this._ToNative(frame, out var paletteChanged);
    var isKeyFrame = this._previous is null || paletteChanged;
    var data = isKeyFrame
      ? this._EncodeKeyframe(native)
      : this._EncodeDelta(this._previous!, native);

    this._previous = native;
    packet = new(
      StreamIndex: this._stream.Index,
      Data: data,
      PresentationTimestamp: presentationTimestamp,
      DecodeTimestamp: presentationTimestamp,
      Duration: 1,
      IsKeyFrame: isKeyFrame);
    return true;
  }

  public MediaStreamInfo DescribeStream() => this._stream;

  private byte[] _ToNative(RawImage frame, out bool paletteChanged) {
    paletteChanged = false;
    return this._bitsPerPixel switch {
      8 => this._ToIndexed8(frame, out paletteChanged),
      16 => this._ToRgb555(frame),
      32 => this._ToBgr0(frame),
      _ => throw new InvalidOperationException("The RASC encoder was created with an impossible native depth."),
    };
  }

  private byte[] _ToIndexed8(RawImage frame, out bool paletteChanged) {
    ArgumentNullException.ThrowIfNull(frame);
    if (frame.Width != this._width || frame.Height != this._height)
      throw new InvalidDataException(
        $"RASC geometry is fixed at {this._width}x{this._height} for the life of the stream; received {frame.Width}x{frame.Height}.");
    if (frame.Format != PixelFormat.Indexed8)
      throw new NotSupportedException("8-bit RASC is PAL8 and therefore requires Indexed8 input with an explicit palette.");
    if (!frame.HasEnoughPixelData)
      throw new InvalidDataException("The source RawImage does not contain enough pixel data for its declared format and dimensions.");
    if (frame.Palette is null || frame.PaletteCount is <= 0 or > 256 || frame.Palette.Length < frame.PaletteCount * 3)
      throw new InvalidDataException("8-bit RASC input requires one to 256 RGB palette entries.");

    var palette = new byte[256 * 3];
    frame.Palette.AsSpan(0, frame.PaletteCount * 3).CopyTo(palette);
    paletteChanged = this._palette is not null && !this._palette.AsSpan().SequenceEqual(palette);
    this._palette = palette;

    var result = new byte[checked(this._stride * this._height)];
    var source = frame.PixelData.AsSpan();
    for (var row = 0; row < this._height; ++row) {
      var sourceRow = source.Slice(row * this._width, this._width);
      foreach (var index in sourceRow)
        if (index >= frame.PaletteCount)
          throw new InvalidDataException(
            $"8-bit RASC input uses palette index {index}, but only {frame.PaletteCount} palette entries are present.");
      sourceRow.CopyTo(result.AsSpan(row * this._stride, this._width));
    }
    return result;
  }

  private byte[] _ToRgb555(RawImage frame) {
    var rgb = LosslessEncoderInput.Prepare(frame, PixelFormat.Rgb24, this._width, this._height, "RASC RGB555");
    var source = rgb.PixelData.AsSpan();
    var result = new byte[checked(this._stride * this._height)];
    var sourceAt = 0;
    for (var pixel = 0; pixel < this._width * this._height; ++pixel) {
      var red = source[sourceAt++];
      var green = source[sourceAt++];
      var blue = source[sourceAt++];
      var value = (ushort)((blue >> 3) | ((green >> 3) << 5) | ((red >> 3) << 10));
      BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(pixel * 2, 2), value);
    }
    return result;
  }

  private byte[] _ToBgr0(RawImage frame) {
    var bgr = LosslessEncoderInput.Prepare(frame, PixelFormat.Bgr24, this._width, this._height, "RASC");
    var source = bgr.PixelData.AsSpan();
    var result = new byte[checked(this._stride * this._height)];
    var sourceAt = 0;
    var destinationAt = 0;
    while (destinationAt < result.Length) {
      result[destinationAt++] = source[sourceAt++];
      result[destinationAt++] = source[sourceAt++];
      result[destinationAt++] = source[sourceAt++];
      result[destinationAt++] = 0;
    }
    return result;
  }

  private byte[] _EncodeKeyframe(ReadOnlySpan<byte> native) {
    var surfaceBytes = native.Length;
    var uncompressed = new byte[checked(surfaceBytes * 2)];
    this._CopyBottomUp(native, uncompressed.AsSpan(0, surfaceBytes));
    this._CopyBottomUp(native, uncompressed.AsSpan(surfaceBytes));
    var compressed = _Deflate(uncompressed);

    var formatBytes = this._bitsPerPixel == 8 ? 72 + 256 * 4 : 72;
    var payload = new byte[checked(formatBytes + compressed.Length)];
    BinaryPrimitives.WriteUInt32LittleEndian(payload, 0x65);
    BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(8), checked((uint)this._width));
    BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(12), checked((uint)this._height));
    BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(46), checked((ushort)this._bitsPerPixel));

    if (this._bitsPerPixel == 8)
      for (var i = 0; i < 256; ++i) {
        var paletteAt = i * 3;
        var value = (uint)(this._palette![paletteAt + 2]
                           | (this._palette[paletteAt + 1] << 8)
                           | (this._palette[paletteAt] << 16));
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(72 + i * 4, 4), value);
      }

    compressed.CopyTo(payload, formatBytes);
    return _Bundle(_KBND, _KFRM, payload);
  }

  private byte[] _EncodeDelta(ReadOnlySpan<byte> previous, ReadOnlySpan<byte> current) {
    using var commands = new MemoryStream();
    var unitBytes = this._bytesPerPixel == 4 ? 4 : 1;
    var skipType = this._bytesPerPixel == 4 ? (byte)10 : (byte)1;
    var literalType = this._bytesPerPixel == 4 ? (byte)13 : (byte)3;

    for (var row = this._height - 1; row >= 0; --row) {
      var previousRow = previous.Slice(row * this._stride, this._stride);
      var currentRow = current.Slice(row * this._stride, this._stride);
      var units = this._width * this._bytesPerPixel / unitBytes;
      var unit = 0;
      while (unit < units) {
        var unchanged = previousRow.Slice(unit * unitBytes, unitBytes)
          .SequenceEqual(currentRow.Slice(unit * unitBytes, unitBytes));
        var run = 1;
        while (run < byte.MaxValue && unit + run < units &&
               previousRow.Slice((unit + run) * unitBytes, unitBytes)
                 .SequenceEqual(currentRow.Slice((unit + run) * unitBytes, unitBytes)) == unchanged)
          ++run;

        commands.WriteByte(unchanged ? skipType : literalType);
        commands.WriteByte(checked((byte)run));
        if (!unchanged)
          commands.Write(currentRow.Slice(unit * unitBytes, run * unitBytes));
        unit += run;
      }
    }

    var commandBytes = commands.ToArray();
    var compressed = _Deflate(commandBytes);
    var payload = new byte[checked(40 + compressed.Length)];
    BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(12), checked((uint)commandBytes.Length));
    BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(16), 0);
    BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(20), 0);
    BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(24), checked((uint)this._width));
    BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(28), checked((uint)this._height));
    BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(36), 1);
    compressed.CopyTo(payload, 40);
    return _Bundle(_BNDL, _DLTA, payload);
  }

  private void _CopyBottomUp(ReadOnlySpan<byte> source, Span<byte> destination) {
    var destinationAt = 0;
    for (var row = this._height - 1; row >= 0; --row) {
      source.Slice(row * this._stride, this._stride).CopyTo(destination[destinationAt..]);
      destinationAt += this._stride;
    }
  }

  private static byte[] _Bundle(uint bundleType, uint recordType, ReadOnlySpan<byte> payload) {
    var result = new byte[checked(12 + payload.Length)];
    BinaryPrimitives.WriteUInt32LittleEndian(result, bundleType);
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), recordType);
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(8), checked((uint)payload.Length));
    payload.CopyTo(result.AsSpan(12));
    return result;
  }

  private static byte[] _Deflate(ReadOnlySpan<byte> source) {
    using var output = new MemoryStream();
    using (var zlib = new ZLibStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
      zlib.Write(source);
    return output.ToArray();
  }
}
