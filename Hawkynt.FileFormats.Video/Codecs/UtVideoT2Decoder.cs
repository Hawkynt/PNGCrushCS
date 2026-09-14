using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Bmp;
using FileFormat.Codecs.UtVideo;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>Decodes Ut Video T2 intra and previous-frame delta packets.</summary>
/// <remarks>
/// T2 temporal packets refer only to the immediately preceding decoded frame. There are no future
/// references, B-frames or decode/display reordering: an intra packet refreshes the reference and a
/// delta packet reconstructs each eight-sample group either spatially or from that reference.
/// <para/>
/// The packet implementation is clean-room code based on externally observable behaviour of the
/// GPL upstream implementation. FFmpeg's LGPL decoder is used as a second oracle for T2 intra
/// packets; its decoder does not implement the temporal packet type.
/// </remarks>
public sealed class UtVideoT2Decoder : IVideoCodecDecoder<UtVideoT2Decoder> {

  private const byte _FRAME_TYPE_INTRA = 1;
  private const byte _FRAME_TYPE_DELTA = 2;
  private const byte _CONTROL_COMPRESSED = 1;

  private readonly int _width;
  private readonly int _height;
  private readonly int _streamIndex;
  private readonly UtVideoT2Format _format;
  private byte[][]? _previous;

  private UtVideoT2Decoder(int width, int height, int streamIndex, UtVideoT2Format format) {
    this._width = width;
    this._height = height;
    this._streamIndex = streamIndex;
    this._format = format;
  }

  public static string CodecName => "Ut Video T2";

  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    return stream.Kind == MediaStreamKind.Video && UtVideoT2Format.IsTag(stream.Codec);
  }

  public static UtVideoT2Decoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.Width <= 0 || stream.Height <= 0)
      throw new InvalidDataException(
        $"Video stream {stream.Index} states a Ut Video T2 picture size of {stream.Width}x{stream.Height}.");

    var description = stream.CodecPrivateData.Span;
    var extra = description.Length > BitmapInfoHeader.StructSize
      ? description[BitmapInfoHeader.StructSize..]
      : default;
    var format = UtVideoT2Format.Parse(stream.Codec, extra, stream.Index);

    if (format.ChromaHorizontalShift > 0 && (stream.Width & 1) != 0)
      throw new InvalidDataException(
        $"Video stream {stream.Index} is {stream.Codec}, a 4:2:2 T2 layout, but states an odd width of {stream.Width}.");
    if (format.SliceCount > stream.Height)
      throw new InvalidDataException(
        $"Video stream {stream.Index} states {format.SliceCount} T2 bands for only {stream.Height} picture rows.");

    return new(stream.Width, stream.Height, stream.Index, format);
  }

  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    frame = this._Compose(this.DecodePlanes(packet.Data.Span));
    return true;
  }

  /// <summary>Decodes a packet to active, unpadded planes and advances the previous-frame reference.</summary>
  internal byte[][] DecodePlanes(ReadOnlySpan<byte> data) {
    if (data.Length < 8)
      throw new InvalidDataException("A Ut Video T2 packet is shorter than its eight-byte frame header.");
    if (data[2] != 0 || data[3] != 0)
      throw new InvalidDataException("A Ut Video T2 packet sets reserved frame-header bytes.");
    if ((data[1] & ~_CONTROL_COMPRESSED) != 0)
      throw new InvalidDataException($"A Ut Video T2 packet sets reserved frame flags 0x{data[1] & 0xFE:X2}.");

    var delta = data[0] switch {
      _FRAME_TYPE_INTRA => false,
      _FRAME_TYPE_DELTA => true,
      _ => throw new InvalidDataException($"Ut Video T2 frame type {data[0]} is neither intra nor delta."),
    };

    if (delta && !this._format.UseTemporalCompression)
      throw new InvalidDataException("A Ut Video T2 delta packet appears in a stream that does not enable temporal compression.");
    if (delta && this._previous == null)
      throw new InvalidDataException("A Ut Video T2 delta packet has no preceding reference frame.");

    var compressedControls = (data[1] & _CONTROL_COMPRESSED) != 0;
    if (compressedControls && !this._format.UseControlCompression)
      throw new InvalidDataException("A Ut Video T2 packet compresses its control streams although the stream description does not enable that mode.");

    var entries = checked(this._format.PlaneCount * this._format.SliceCount);
    var arraysLength = checked(4 + entries * 8);
    var offset = BinaryPrimitives.ReadUInt32LittleEndian(data[4..]);
    if (offset > int.MaxValue - 8)
      throw new InvalidDataException("A Ut Video T2 packet places its size arrays outside the addressable packet.");
    var arraysStart = 8 + (int)offset;
    if (arraysStart < 8 || arraysStart > data.Length - arraysLength)
      throw new InvalidDataException("A Ut Video T2 packet's size arrays lie outside the packet.");

    var controlPadded = _ReadSize(data, arraysStart, "control-stream region");
    if (controlPadded > arraysStart - 8)
      throw new InvalidDataException("A Ut Video T2 packet's control-stream region overlaps its frame header.");
    var controlStart = arraysStart - controlPadded;
    var packedPadded = controlStart - 8;

    var packedSizes = new int[entries];
    var controlSizes = new int[entries];
    var sizeAt = arraysStart + 4;
    var packedTotal = 0;
    var controlTotal = 0;
    for (var i = 0; i < entries; ++i) {
      packedSizes[i] = _ReadSize(data, sizeAt, $"packed stream {i}");
      sizeAt += 4;
      packedTotal = checked(packedTotal + packedSizes[i]);
    }
    for (var i = 0; i < entries; ++i) {
      controlSizes[i] = _ReadSize(data, sizeAt, $"control stream {i}");
      sizeAt += 4;
      controlTotal = checked(controlTotal + controlSizes[i]);
    }

    if (packedTotal > packedPadded)
      throw new InvalidDataException(
        $"Ut Video T2 packed streams require {packedTotal} bytes but their region contains only {packedPadded}.");
    if (controlTotal > controlPadded)
      throw new InvalidDataException(
        $"Ut Video T2 control streams require {controlTotal} bytes but their region contains only {controlPadded}.");

    var current = new byte[this._format.PlaneCount][];
    for (var plane = 0; plane < current.Length; ++plane)
      current[plane] = new byte[checked(this._format.PlaneStride(plane, this._width) * this._height)];

    var packedAt = 8;
    var controlAt = controlStart;
    var entry = 0;
    for (var plane = 0; plane < current.Length; ++plane) {
      var stride = this._format.PlaneStride(plane, this._width);
      var previous = delta ? this._previous![plane] : Array.Empty<byte>();

      for (var slice = 0; slice < this._format.SliceCount; ++slice) {
        var packedSize = packedSizes[entry];
        var controlSize = controlSizes[entry];
        if (packedAt > controlStart - packedSize)
          throw new InvalidDataException($"Ut Video T2 packed stream {entry} crosses into the control region.");
        if (controlAt > arraysStart - controlSize)
          throw new InvalidDataException($"Ut Video T2 control stream {entry} crosses into the size arrays.");

        var firstRow = this._format.SliceStart(slice, this._height);
        var lastRow = this._format.SliceStart(slice + 1, this._height);
        var expectedControl = UtVideoT2Packing.ControlLength(checked((lastRow - firstRow) * stride), delta);
        var storedControl = data.Slice(controlAt, controlSize);
        ReadOnlySpan<byte> control = compressedControls
          ? Lz4Block.Unpack(storedControl, expectedControl)
          : storedControl;
        if (!compressedControls && control.Length != expectedControl)
          throw new InvalidDataException(
            $"Ut Video T2 control stream {entry} is {control.Length} bytes where {expectedControl} are required.");

        UtVideoT2Packing.Decode(
          data.Slice(packedAt, packedSize),
          control,
          current[plane],
          previous,
          stride,
          firstRow,
          lastRow,
          delta,
          plane,
          slice);

        packedAt += packedSize;
        controlAt += controlSize;
        ++entry;
      }
    }

    if (this._format.UseTemporalCompression)
      this._previous = current;

    return this._ActivePlanes(current);
  }

  private byte[][] _ActivePlanes(byte[][] padded) {
    var result = new byte[padded.Length][];
    for (var plane = 0; plane < padded.Length; ++plane) {
      var width = this._format.PlaneWidth(plane, this._width);
      var stride = this._format.PlaneStride(plane, this._width);
      var active = new byte[checked(width * this._height)];
      for (var y = 0; y < this._height; ++y)
        padded[plane].AsSpan(y * stride, width).CopyTo(active.AsSpan(y * width));
      result[plane] = active;
    }

    if (this._format.ColourSpace != UtVideoColourSpace.Yuv)
      _Correlate(result);
    return result;
  }

  private static void _Correlate(byte[][] planes) {
    var green = planes[0];
    var blue = planes[1];
    var red = planes[2];
    for (var i = 0; i < green.Length; ++i) {
      var g = green[i];
      blue[i] = (byte)(blue[i] + g - 128);
      red[i] = (byte)(red[i] + g - 128);
    }
  }

  private RawImage _Compose(byte[][] planes) => this._format.ColourSpace switch {
    UtVideoColourSpace.Rgb => this._FromColour(planes, false),
    UtVideoColourSpace.Rgba => this._FromColour(planes, true),
    _ => this._FromYuv(planes),
  };

  private RawImage _FromColour(byte[][] planes, bool hasAlpha) {
    var count = checked(this._width * this._height);
    var channels = hasAlpha ? 4 : 3;
    var pixels = new byte[checked(count * channels)];
    var alpha = hasAlpha ? planes[3] : null;

    for (var i = 0; i < count; ++i) {
      var at = i * channels;
      pixels[at] = planes[2][i];
      pixels[at + 1] = planes[0][i];
      pixels[at + 2] = planes[1][i];
      if (alpha != null)
        pixels[at + 3] = alpha[i];
    }

    return new() {
      Width = this._width,
      Height = this._height,
      Format = hasAlpha ? PixelFormat.Rgba32 : PixelFormat.Rgb24,
      PixelData = pixels,
    };
  }

  private RawImage _FromYuv(byte[][] planes) {
    var luma = planes[0];
    var cb = planes[1];
    var cr = planes[2];
    var chromaWidth = this._width >> this._format.ChromaHorizontalShift;
    var pixels = new byte[checked(this._width * this._height * 3)];
    var (toRed, toGreenFromBlue, toGreenFromRed, toBlue) = this._format.IsBt709
      ? (459, -55, -136, 541)
      : (409, -100, -208, 516);

    for (var y = 0; y < this._height; ++y) {
      var lumaRow = y * this._width;
      var chromaRow = y * chromaWidth;
      var target = lumaRow * 3;
      for (var x = 0; x < this._width; ++x) {
        var chroma = chromaRow + (x >> this._format.ChromaHorizontalShift);
        var scaledLuma = 298 * (luma[lumaRow + x] - 16);
        var blueDifference = cb[chroma] - 128;
        var redDifference = cr[chroma] - 128;
        pixels[target] = _Clamp(scaledLuma + toRed * redDifference + 128);
        pixels[target + 1] = _Clamp(scaledLuma + toGreenFromBlue * blueDifference + toGreenFromRed * redDifference + 128);
        pixels[target + 2] = _Clamp(scaledLuma + toBlue * blueDifference + 128);
        target += 3;
      }
    }

    return new() {
      Width = this._width,
      Height = this._height,
      Format = PixelFormat.Rgb24,
      PixelData = pixels,
    };
  }

  private static byte _Clamp(int scaled) {
    var value = scaled >> 8;
    return (byte)Math.Clamp(value, 0, 255);
  }

  private static int _ReadSize(ReadOnlySpan<byte> data, int offset, string what) {
    var value = BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);
    if (value > int.MaxValue)
      throw new InvalidDataException($"A Ut Video T2 {what} size of {value} bytes is not addressable.");
    return (int)value;
  }
}
