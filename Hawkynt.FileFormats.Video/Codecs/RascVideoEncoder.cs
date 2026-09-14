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
/// The authoring profile is the format's 32-bit BGR0 mode. Input is converted through
/// <see cref="LosslessEncoderInput"/> to BGR24 and the unused fourth byte is written as zero, so RGB
/// sample values are preserved exactly. Alpha has no representation in RASC and is discarded by the
/// same shared policy used by the package's other lossless RGB codecs.
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
  private byte[]? _previous;

  private RascVideoEncoder(MediaStreamInfo stream) {
    this._width = stream.Width;
    this._height = stream.Height;
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
      BitsPerPixel = 32,
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
    if ((long)stream.Width * stream.Height * 4 > int.MaxValue)
      throw new NotSupportedException("The requested RASC frame is too large to hold its BGR0 surface in memory.");

    return new(stream);
  }

  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    var native = this._ToBgr0(frame);
    var isKeyFrame = this._previous is null;
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

  private byte[] _ToBgr0(RawImage frame) {
    var bgr = LosslessEncoderInput.Prepare(frame, PixelFormat.Bgr24, this._width, this._height, "RASC");
    var source = bgr.PixelData.AsSpan();
    var result = new byte[checked(this._width * this._height * 4)];
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

    var payload = new byte[checked(72 + compressed.Length)];
    BinaryPrimitives.WriteUInt32LittleEndian(payload, 0x65);
    BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(8), checked((uint)this._width));
    BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(12), checked((uint)this._height));
    BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(46), 32);
    compressed.CopyTo(payload, 72);
    return _Bundle(_KBND, _KFRM, payload);
  }

  private byte[] _EncodeDelta(ReadOnlySpan<byte> previous, ReadOnlySpan<byte> current) {
    using var commands = new MemoryStream();
    var rowBytes = checked(this._width * 4);

    for (var row = this._height - 1; row >= 0; --row) {
      var previousRow = previous.Slice(row * rowBytes, rowBytes);
      var currentRow = current.Slice(row * rowBytes, rowBytes);
      var pixel = 0;
      while (pixel < this._width) {
        var unchanged = previousRow.Slice(pixel * 4, 4).SequenceEqual(currentRow.Slice(pixel * 4, 4));
        var run = 1;
        while (run < byte.MaxValue && pixel + run < this._width &&
               previousRow.Slice((pixel + run) * 4, 4).SequenceEqual(currentRow.Slice((pixel + run) * 4, 4)) == unchanged)
          ++run;

        commands.WriteByte(unchanged ? (byte)10 : (byte)13);
        commands.WriteByte(checked((byte)run));
        if (!unchanged)
          commands.Write(currentRow.Slice(pixel * 4, run * 4));
        pixel += run;
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
    var stride = checked(this._width * 4);
    var destinationAt = 0;
    for (var row = this._height - 1; row >= 0; --row) {
      source.Slice(row * stride, stride).CopyTo(destination[destinationAt..]);
      destinationAt += stride;
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