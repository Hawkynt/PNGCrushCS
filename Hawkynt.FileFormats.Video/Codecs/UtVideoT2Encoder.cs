using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Bmp;
using FileFormat.Codecs.UtVideo;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>Encodes the six eight-bit Ut Video T2 layouts, including previous-frame delta coding.</summary>
/// <remarks>
/// T2 is the speed-oriented sibling of classic Ut Video. Frames are split into independently coded
/// horizontal bands whose rows are padded to 64-byte boundaries. An intra frame stores spatial
/// gradient residuals. With temporal compression enabled, following frames choose independently for
/// every eight samples between that spatial residual and a difference against the immediately
/// preceding frame. Consequently T2 has key frames and P-like delta frames, but no forward or
/// bidirectional references and no display reordering.
/// <para/>
/// The T2 implementation is clean-room code. The upstream Ut Video implementation is GPL and was
/// used only as a behavioural oracle for packet fields and expected output. No upstream
/// implementation code is copied or translated here. FFmpeg's LGPL decoder was additionally used
/// for the intra subset it supports.
/// </remarks>
public sealed class UtVideoT2Encoder : IVideoCodecEncoder<UtVideoT2Encoder> {

  private const int _MAX_SLICES = 256;
  private const int _MAX_KEY_FRAME_INTERVAL = 60000;
  private const byte _FRAME_TYPE_INTRA = 1;
  private const byte _FRAME_TYPE_DELTA = 2;
  private const byte _CONTROL_COMPRESSED = 1;

  private static readonly CodecTag _DefaultTag = CodecTag.FromCharacters("UMRG");
  private static readonly CodecTag[] _Tags = [
    _DefaultTag,
    CodecTag.FromCharacters("UMRA"),
    CodecTag.FromCharacters("UMY2"),
    CodecTag.FromCharacters("UMY4"),
    CodecTag.FromCharacters("UMH2"),
    CodecTag.FromCharacters("UMH4"),
  ];

  private readonly MediaStreamInfo _stream;
  private readonly UtVideoT2Format _format;
  private readonly PixelFormat _coded;
  private readonly RawImageColorInfo? _conversion;
  private readonly int _width;
  private readonly int _height;
  private readonly int _keyFrameInterval;
  private int _frameNumber;
  private byte[][]? _previous;

  private UtVideoT2Encoder(MediaStreamInfo stream, CodecTag tag, UtVideoT2Format format, int keyFrameInterval) {
    this._format = format;
    this._width = stream.Width;
    this._height = stream.Height;
    this._keyFrameInterval = keyFrameInterval;

    (this._coded, this._conversion) = format.ColourSpace switch {
      UtVideoColourSpace.Rgb => (PixelFormat.Rgb24, null),
      UtVideoColourSpace.Rgba => (PixelFormat.Rgba32, null),
      _ => (
        format.ChromaHorizontalShift > 0 ? PixelFormat.Yuv422P8 : PixelFormat.Yuv444P8,
        format.IsBt709 ? RawImageColorInfo.Bt709Limited : RawImageColorInfo.Bt601Limited),
    };

    this._stream = new() {
      Index = stream.Index,
      Kind = MediaStreamKind.Video,
      Codec = tag,
      Handler = tag,
      TimeBase = stream.TimeBase,
      FrameRate = stream.FrameRate,
      DeclaredFrameCount = stream.DeclaredFrameCount,
      Width = stream.Width,
      Height = stream.Height,
      BitsPerPixel = format.HasAlpha ? 32 : 24,
      CodecPrivateData = _PrivateData(stream.Width, stream.Height, tag, format),
      Language = stream.Language,
      Name = stream.Name,
    };
  }

  public static string CodecName => "Ut Video T2";
  public static CodecTag Codec => _DefaultTag;

  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.Kind != MediaStreamKind.Video)
      return false;

    foreach (var tag in _Tags)
      if (stream.Codec.EqualsIgnoringCase(tag))
        return true;
    return false;
  }

  /// <summary>Creates an intra-only encoder using one band per available processor, capped by height.</summary>
  public static UtVideoT2Encoder Create(MediaStreamInfo stream) => Create(stream, 0, 1);

  /// <summary>
  /// Creates a T2 encoder.
  /// </summary>
  /// <param name="stream">The stream and one of the six <c>UM*</c> codes to write.</param>
  /// <param name="sliceCount">One to 256 horizontal bands, or zero for processor-count automatic selection.</param>
  /// <param name="keyFrameInterval">
  /// One disables temporal compression. Two through 60000 write an intra frame at that interval and
  /// previous-frame delta packets between them.
  /// </param>
  public static UtVideoT2Encoder Create(MediaStreamInfo stream, int sliceCount, int keyFrameInterval = 1) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException("Ut Video T2 can only encode video streams.");
    if (stream.Width <= 0 || stream.Height <= 0)
      throw new NotSupportedException($"Ut Video T2 needs positive dimensions; {stream.Width}x{stream.Height} was supplied.");
    if (sliceCount is < 0 or > _MAX_SLICES)
      throw new ArgumentOutOfRangeException(nameof(sliceCount), sliceCount, "A T2 frame has from one to 256 bands, or zero for automatic selection.");
    if (keyFrameInterval is < 1 or > _MAX_KEY_FRAME_INTERVAL)
      throw new ArgumentOutOfRangeException(nameof(keyFrameInterval), keyFrameInterval, "The T2 key-frame interval is from one to 60000 frames.");

    var tag = _TagOf(stream);
    var probe = UtVideoT2Format.ForEncoding(tag, 1, keyFrameInterval > 1, stream.Index);
    if (probe.ChromaHorizontalShift > 0 && (stream.Width & 1) != 0)
      throw new NotSupportedException($"{tag} is 4:2:2 and therefore requires an even width; {stream.Width} is odd.");

    sliceCount = sliceCount == 0
      ? Math.Min(stream.Height, Math.Clamp(Environment.ProcessorCount, 1, _MAX_SLICES))
      : sliceCount;
    if (sliceCount > stream.Height)
      throw new NotSupportedException($"{sliceCount} T2 bands cannot be cut out of a picture only {stream.Height} rows high.");

    var format = UtVideoT2Format.ForEncoding(tag, sliceCount, keyFrameInterval > 1, stream.Index);
    return new(stream, tag, format, keyFrameInterval);
  }

  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    ArgumentNullException.ThrowIfNull(frame);
    if (frame.Width != this._width || frame.Height != this._height)
      throw new InvalidDataException(
        $"Ut Video T2 geometry is fixed at {this._width}x{this._height}; received {frame.Width}x{frame.Height}.");
    if (!frame.HasEnoughPixelData)
      throw new InvalidDataException("The source RawImage does not contain enough pixel data for its declared format and dimensions.");

    var planes = this._Planes(frame);
    var intra = this._frameNumber == 0 || !this._format.UseTemporalCompression;
    var delta = !intra;
    var entries = checked(this._format.PlaneCount * this._format.SliceCount);
    var packed = new byte[entries][];
    var control = new byte[entries][];
    var entry = 0;

    for (var plane = 0; plane < this._format.PlaneCount; ++plane) {
      var stride = this._format.PlaneStride(plane, this._width);
      var previous = delta ? this._previous![plane] : Array.Empty<byte>();

      for (var slice = 0; slice < this._format.SliceCount; ++slice) {
        var firstRow = this._format.SliceStart(slice, this._height);
        var lastRow = this._format.SliceStart(slice + 1, this._height);
        var streams = UtVideoT2Packing.Encode(planes[plane], previous, stride, firstRow, lastRow, delta);
        packed[entry] = streams.Packed;
        control[entry] = delta ? Lz4Block.PackLiteralOnly(streams.Control) : streams.Control;
        ++entry;
      }
    }

    var data = _Packet(packed, control, delta);
    packet = new(
      this._stream.Index,
      data,
      PresentationTimestamp: presentationTimestamp,
      DecodeTimestamp: presentationTimestamp,
      IsKeyFrame: intra);

    if (this._format.UseTemporalCompression)
      this._previous = planes;

    this._frameNumber = this._format.UseTemporalCompression
      ? (this._frameNumber + 1) % this._keyFrameInterval
      : 0;
    return true;
  }

  public MediaStreamInfo DescribeStream() => this._stream;

  private byte[] _Packet(byte[][] packed, byte[][] control, bool delta) {
    var packedLength = _SumLengths(packed);
    var controlLength = _SumLengths(control);
    var packedPadded = _RoundUp8(packedLength);
    var controlPadded = _RoundUp8(controlLength);
    var entries = packed.Length;
    var arraysLength = checked(4 + entries * 8);
    var controlStart = checked(8 + packedPadded);
    var arraysStart = checked(controlStart + controlPadded);
    var end = checked(arraysStart + arraysLength);
    var length = Math.Max(end, checked(controlStart + 256));
    var result = new byte[length];

    result[0] = delta ? _FRAME_TYPE_DELTA : _FRAME_TYPE_INTRA;
    result[1] = delta ? _CONTROL_COMPRESSED : (byte)0;
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), checked((uint)(packedPadded + controlPadded)));

    var at = 8;
    foreach (var stream in packed) {
      stream.CopyTo(result, at);
      at += stream.Length;
    }

    at = controlStart;
    foreach (var stream in control) {
      stream.CopyTo(result, at);
      at += stream.Length;
    }

    at = arraysStart;
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(at), checked((uint)controlPadded));
    at += 4;
    foreach (var stream in packed) {
      BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(at), checked((uint)stream.Length));
      at += 4;
    }
    foreach (var stream in control) {
      BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(at), checked((uint)stream.Length));
      at += 4;
    }

    return result;
  }

  private byte[][] _Planes(RawImage frame) {
    var picture = this._Prepared(frame);
    var planes = new byte[this._format.PlaneCount][];

    if (this._format.ColourSpace == UtVideoColourSpace.Yuv) {
      for (var plane = 0; plane < planes.Length; ++plane) {
        var width = this._format.PlaneWidth(plane, this._width);
        var planeStride = this._format.PlaneStride(plane, this._width);
        var source = picture.GetPlaneData(plane);
        planes[plane] = _PadPlane(source, width, this._height, planeStride);
      }
      return planes;
    }

    var channels = this._format.HasAlpha ? 4 : 3;
    var stride = this._format.PlaneStride(0, this._width);
    for (var plane = 0; plane < planes.Length; ++plane)
      planes[plane] = new byte[checked(stride * this._height)];

    var pixels = picture.PixelData;
    for (var y = 0; y < this._height; ++y) {
      var pixelAt = y * this._width * channels;
      var rowAt = y * stride;
      for (var x = 0; x < this._width; ++x) {
        var at = pixelAt + x * channels;
        var g = pixels[at + 1];
        planes[0][rowAt + x] = g;
        planes[1][rowAt + x] = (byte)(pixels[at + 2] - g + 128);
        planes[2][rowAt + x] = (byte)(pixels[at] - g + 128);
        if (this._format.HasAlpha)
          planes[3][rowAt + x] = pixels[at + 3];
      }

      for (var plane = 0; plane < planes.Length; ++plane)
        planes[plane].AsSpan(rowAt + this._width, stride - this._width).Fill(planes[plane][rowAt + this._width - 1]);
    }

    return planes;
  }

  private RawImage _Prepared(RawImage frame) {
    if (frame.Format == this._coded)
      return frame;

    if (!_IsEightBitColour(frame.Format))
      throw new NotSupportedException(
        $"{this._stream.Codec} codes {this._coded} losslessly; converting {frame.Format} would resample or change sample precision.");

    return FastRawImageConverter.Convert(frame, this._coded, this._conversion);
  }

  private static byte[] _PadPlane(ReadOnlySpan<byte> source, int width, int height, int stride) {
    if (source.Length < checked(width * height))
      throw new InvalidDataException("A planar source is shorter than its declared Ut Video T2 plane.");

    var result = new byte[checked(stride * height)];
    for (var y = 0; y < height; ++y) {
      var sourceRow = source.Slice(y * width, width);
      var targetRow = result.AsSpan(y * stride, stride);
      sourceRow.CopyTo(targetRow);
      targetRow[width..].Fill(sourceRow[^1]);
    }
    return result;
  }

  private static bool _IsEightBitColour(PixelFormat format) => format is
    PixelFormat.Bgr24 or PixelFormat.Rgb24
    or PixelFormat.Bgra32 or PixelFormat.Rgba32 or PixelFormat.Argb32
    or PixelFormat.Gray8 or PixelFormat.GrayAlpha16
    or PixelFormat.Indexed8 or PixelFormat.Indexed4 or PixelFormat.Indexed1 or PixelFormat.Indexed16
    or PixelFormat.Rgb565;

  private static CodecTag _TagOf(MediaStreamInfo stream) {
    foreach (var tag in _Tags)
      if (stream.Codec.EqualsIgnoringCase(tag))
        return tag;
    throw new NotSupportedException(
      $"Stream {stream.Index} asks the Ut Video T2 encoder for {stream.Codec}; use UMRG, UMRA, UMY2, UMY4, UMH2 or UMH4.");
  }

  private static int _SumLengths(byte[][] streams) {
    var result = 0;
    foreach (var stream in streams)
      result = checked(result + stream.Length);
    return result;
  }

  private static int _RoundUp8(int value) => checked((value + 7) & ~7);

  private static byte[] _PrivateData(int width, int height, CodecTag tag, UtVideoT2Format format) {
    var extra = format.Describe();
    var data = new byte[BitmapInfoHeader.StructSize + extra.Length];
    var span = data.AsSpan();
    var bitsPerPixel = format.HasAlpha ? 32 : 24;

    BinaryPrimitives.WriteInt32LittleEndian(span, data.Length);
    BinaryPrimitives.WriteInt32LittleEndian(span[4..], width);
    BinaryPrimitives.WriteInt32LittleEndian(span[8..], height);
    BinaryPrimitives.WriteInt16LittleEndian(span[12..], 1);
    BinaryPrimitives.WriteInt16LittleEndian(span[14..], checked((short)bitsPerPixel));
    BinaryPrimitives.WriteUInt32LittleEndian(span[16..], tag.Value);
    BinaryPrimitives.WriteInt32LittleEndian(span[20..], checked(width * height * (bitsPerPixel / 8)));
    extra.CopyTo(span[BitmapInfoHeader.StructSize..]);
    return data;
  }
}
