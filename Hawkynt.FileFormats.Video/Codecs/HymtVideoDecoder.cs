using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FileFormat.Codecs.HuffYuv;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes HuffYUV MT: HuffYUV whose frames may be cut into horizontal slices, each coded as a picture of
/// its own, listed in a table that a sixteen-byte trailer points to.
/// </summary>
/// <remarks>
/// Converted from the slice handling in FFmpeg's <c>libavcodec/huffyuvdec.c</c>, LGPL-2.1-or-later; each slice
/// is decoded by this package's HuffYUV decoder. Slice 0 is the bottom one, as FFmpeg places it, except where
/// FFmpeg's own row loop ignores the slice's offset — median-predicted 4:2:2 and version 3's planes — so every
/// slice lands at the top, and where the slices fall short of the picture the rows above them stay empty.
/// A frame without the trailer is plain HuffYUV.
/// </remarks>
public sealed class HymtVideoDecoder : IVideoCodecDecoder<HymtVideoDecoder> {

  internal static readonly CodecTag Tag = CodecTag.FromCharacters("HYMT");

  private readonly HuffYuvDecoder _whole;
  private readonly Dictionary<int, HuffYuvDecoder> _slices = [];
  private readonly int _width;
  private readonly int _height;

  private HymtVideoDecoder(HuffYuvDecoder whole, int width, int height) {
    this._whole = whole;
    this._width = width;
    this._height = height;
  }

  public static string CodecName => "HuffYUV MT";

  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    return stream.Kind == MediaStreamKind.Video && stream.Codec.EqualsIgnoringCase(Tag);
  }

  public static HymtVideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (!Accepts(stream))
      throw new NotSupportedException($"Stream {stream.Index} is not HuffYUV MT.");
    return new(HuffYuvDecoder.Create(stream), stream.Width, stream.Height);
  }

  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    var data = packet.Data.Span;
    if (data.Length < (this._width * this._height + 7) / 8)
      throw new InvalidDataException("This HuffYUV MT frame is smaller than any picture of its size can be.");
    if (data.Length <= 32 || BinaryPrimitives.ReadUInt32LittleEndian(data[^16..]) != 0) {
      this._whole.TryDecode(packet, out frame);
      return true;
    }
    var tableAt = BinaryPrimitives.ReadUInt32LittleEndian(data[^4..]);
    var sliceHeight = BinaryPrimitives.ReadInt32LittleEndian(data[^8..]);
    var slices = BinaryPrimitives.ReadInt32LittleEndian(data[^12..]);
    if (slices * 8L + tableAt > data.Length - 16 || this._whole.Format.ChromaVerticalShift != 0 || sliceHeight <= 0 || slices * (long)sliceHeight > this._height)
      throw new InvalidDataException("This HuffYUV MT frame's slice table does not fit it.");
    if (slices <= 1) {
      // FFmpeg decodes a single slice from the frame's start, at the slice's height.
      frame = this._Compose([(this._Slice(sliceHeight).DecodeSlice(data, this._whole.FrameTables(data).Tables, this._whole.FrameTables(data).Size), 0)]);
      return true;
    }

    var (tables, size) = this._whole.FrameTables(data);
    var format = this._whole.Format;
    var ignoresOffset = format.Version >= 3 || (format.ColourSpace == HuffYuvColourSpace.Yuv && format.Predictor == HuffYuvPredictor.Median);
    var parts = new List<(RawImage, int)>();
    for (var s = 0; s < slices; ++s) {
      var offset = BinaryPrimitives.ReadInt32LittleEndian(data[(int)(tableAt + s * 8)..]);
      var length = BinaryPrimitives.ReadInt32LittleEndian(data[(int)(tableAt + s * 8 + 4)..]);
      if (offset < 0 || length <= 0 || (offset & 3) != 0 || offset + (long)length > data.Length)
        throw new InvalidDataException($"HuffYUV MT slice {s} lies outside its frame.");
      var picture = this._Slice(sliceHeight).DecodeSlice(data.Slice(offset, length & ~3), tables, size);
      parts.Add((picture, ignoresOffset ? 0 : this._height - (s + 1) * sliceHeight));
    }
    frame = this._Compose(parts);
    return true;
  }

  private HuffYuvDecoder _Slice(int height) => this._slices.TryGetValue(height, out var decoder) ? decoder : this._slices[height] = this._whole.WithHeight(height);

  /// <summary>The slices' rows laid into one picture, each plane at its slice's row offset.</summary>
  private RawImage _Compose(List<(RawImage Picture, int Row)> parts) {
    var first = parts[0].Picture;
    var planeCount = first.PlaneCount;
    var rowBytes = new int[planeCount];
    var planeOffsets = new int[planeCount];
    var total = 0;
    for (var p = 0; p < planeCount; ++p) {
      rowBytes[p] = first.GetPlaneData(p).Length / first.Height;
      planeOffsets[p] = total;
      total += rowBytes[p] * this._height;
    }
    var pixels = new byte[total];
    foreach (var (picture, row) in parts)
      for (var p = 0; p < planeCount; ++p) {
        var plane = picture.GetPlaneData(p);
        for (var y = 0; y < picture.Height && row + y < this._height; ++y)
          plane.Slice(y * rowBytes[p], rowBytes[p]).CopyTo(pixels.AsSpan(planeOffsets[p] + (row + y) * rowBytes[p]));
      }
    return new() { Width = this._width, Height = this._height, Format = first.Format, PixelData = pixels, ColorInfo = first.ColorInfo };
  }
}

/// <summary>
/// Encodes HuffYUV MT: each frame cut into equal horizontal slices, each a HuffYUV picture of its own with
/// left prediction, slice 0 the bottom one, and the slice table and trailer after them.
/// </summary>
/// <remarks>
/// This package's own; FFmpeg has no HuffYUV MT encoder. The slice count is the smallest of two and up that
/// divides the height, so the slices cover the picture as FFmpeg places them; a height no such count divides
/// is written as plain HuffYUV frames, which HuffYUV MT also allows. The slices are written by this package's
/// HuffYUV encoder, with left prediction, whose rows FFmpeg places at each slice's offset. Lossless, as
/// HuffYUV is. FFmpeg's decoder is the oracle that reads what this writes.
/// </remarks>
[VerifiedBy(ConformanceOracle.FFmpeg)]
public sealed class HymtVideoEncoder : IVideoCodecEncoder<HymtVideoEncoder> {

  private readonly MediaStreamInfo _requested;
  private readonly HuffYuvEncoder _slice;
  private readonly int _slices;
  private readonly int _sliceHeight;

  private HymtVideoEncoder(MediaStreamInfo requested, int slices) {
    this._requested = requested;
    this._slices = slices;
    this._sliceHeight = requested.Height / slices;
    var depth = requested.BitsPerPixel is 16 or 24 or 32 ? requested.BitsPerPixel : 24;
    this._slice = HuffYuvEncoder.Create(new MediaStreamInfo {
      Index = requested.Index, Kind = MediaStreamKind.Video, Codec = CodecTag.FromCharacters("HFYU"),
      Width = requested.Width, Height = this._sliceHeight, BitsPerPixel = depth,
      TimeBase = requested.TimeBase, FrameRate = requested.FrameRate,
    }, HuffYuvPredictionMethod.Left);
  }

  public static string CodecName => HymtVideoDecoder.CodecName;
  public static CodecTag Codec => HymtVideoDecoder.Tag;

  public static bool Accepts(MediaStreamInfo stream) => stream.Codec.EqualsIgnoringCase(HymtVideoDecoder.Tag);

  public static HymtVideoEncoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException("HuffYUV MT can only encode a video stream.");
    if (stream.Width <= 0 || stream.Height <= 0)
      throw new NotSupportedException($"Stream {stream.Index} states no picture size, which HuffYUV MT needs.");
    var slices = 1;
    for (var n = 2; n <= Math.Min(8, stream.Height / 2); ++n)
      if (stream.Height % n == 0) {
        slices = n;
        break;
      }
    return new(stream, slices);
  }

  public MediaStreamInfo DescribeStream() {
    var inner = this._slice.DescribeStream();
    var description = inner.CodecPrivateData.ToArray();
    // The description is the slices' BITMAPINFOHEADER: it names the whole picture and HuffYUV MT.
    BinaryPrimitives.WriteInt32LittleEndian(description.AsSpan(8), this._requested.Height);
    BinaryPrimitives.WriteUInt32LittleEndian(description.AsSpan(16), HymtVideoDecoder.Tag.Value);
    return new() {
      Index = inner.Index, Kind = MediaStreamKind.Video, Codec = HymtVideoDecoder.Tag, Handler = HymtVideoDecoder.Tag, CodecId = inner.CodecId,
      TimeBase = inner.TimeBase, FrameRate = inner.FrameRate, DeclaredFrameCount = this._requested.DeclaredFrameCount,
      Width = this._requested.Width, Height = this._requested.Height, BitsPerPixel = inner.BitsPerPixel,
      CodecPrivateData = description, Language = this._requested.Language, Name = this._requested.Name,
    };
  }

  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    ArgumentNullException.ThrowIfNull(frame);
    int width = this._requested.Width, height = this._requested.Height;
    if (frame.Width != width || frame.Height != height)
      throw new InvalidDataException($"HuffYUV MT geometry is fixed at {width}x{height}; received {frame.Width}x{frame.Height}.");
    // Slices are whole rows of every plane, so chroma shared between rows is spread first.
    var source = frame.Format is PixelFormat.Yuv420P8 ? FastRawImageConverter.Convert(frame, PixelFormat.Yuv422P8) : frame;

    var output = new List<byte>();
    var table = new List<(int Offset, int Length)>();
    for (var s = 0; s < this._slices; ++s) {
      var top = height - (s + 1) * this._sliceHeight;
      this._slice.TryEncode(_Rows(source, top, this._sliceHeight), presentationTimestamp, out var part);
      if (this._slices == 1) {
        packet = new(this._requested.Index, part.Data, PresentationTimestamp: presentationTimestamp, DecodeTimestamp: presentationTimestamp, IsKeyFrame: true);
        return true;
      }
      var bytes = part.Data.ToArray();
      table.Add((output.Count, (bytes.Length + 3) & ~3));
      output.AddRange(bytes);
      while (output.Count % 4 != 0)
        output.Add(0);
    }
    var tableAt = output.Count;
    void Put(int value) {
      var word = new byte[4];
      BinaryPrimitives.WriteInt32LittleEndian(word, value);
      output.AddRange(word);
    }
    foreach (var (offset, length) in table) {
      Put(offset);
      Put(length);
    }
    Put(0);
    Put(this._slices);
    Put(this._sliceHeight);
    Put(tableAt);
    packet = new(this._requested.Index, output.ToArray(), PresentationTimestamp: presentationTimestamp, DecodeTimestamp: presentationTimestamp, IsKeyFrame: true);
    return true;
  }

  /// <summary><paramref name="count"/> rows of every plane from <paramref name="top"/>.</summary>
  private static RawImage _Rows(RawImage frame, int top, int count) {
    var pixels = new List<byte>();
    for (var p = 0; p < frame.PlaneCount; ++p) {
      var plane = frame.GetPlaneData(p);
      var rowBytes = plane.Length / frame.Height;
      pixels.AddRange(plane.Slice(top * rowBytes, count * rowBytes).ToArray());
    }
    return new() { Width = frame.Width, Height = count, Format = frame.Format, PixelData = pixels.ToArray(), ColorInfo = frame.ColorInfo };
  }
}
