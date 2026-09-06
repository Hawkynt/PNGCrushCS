using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>How a 4:2:0 frame lays its two chroma planes out behind its luma plane.</summary>
/// <remarks>
/// Five codes, one sample grid, four arrangements of it. <c>I420</c> and <c>IYUV</c> are the same
/// arrangement under two names; <c>YV12</c> is that arrangement with the two chroma planes exchanged;
/// <c>NV12</c> folds the two planes into one interleaved plane and <c>NV21</c> interleaves it the
/// other way round. As with the packed 4:2:2 codes, a wrong choice here does not fail — it exchanges
/// the blue and red difference signals and keeps decoding.
/// </remarks>
internal enum Yuv420Order {

  /// <summary>Luma, then a whole Cb plane, then a whole Cr plane — <c>I420</c> and <c>IYUV</c>.</summary>
  PlanarCbCr,

  /// <summary>Luma, then a whole Cr plane, then a whole Cb plane — <c>YV12</c>.</summary>
  PlanarCrCb,

  /// <summary>Luma, then one plane of Cb and Cr interleaved, Cb first — <c>NV12</c>.</summary>
  InterleavedCbCr,

  /// <summary>Luma, then one plane of Cr and Cb interleaved, Cr first — <c>NV21</c>.</summary>
  InterleavedCrCb,
}

/// <summary>
/// The packing the five 4:2:0 four-character codes share: a full-resolution luma plane and one chroma
/// pair for every two-by-two block of it, with no padding and no row alignment of any kind.
/// </summary>
/// <remarks>
/// Planes come out and go in as the tightly packed <see cref="PixelFormat.Yuv420P8"/> bytes a
/// <see cref="RawImage"/> of that format carries, which is the same grid all five codes state — so
/// unpacking a packet is a pure repack of the eight-bit samples, with nothing computed, nothing
/// interpolated and nothing lost in either direction.
/// </remarks>
internal sealed class Yuv420Packing {

  private readonly Yuv420Order _order;
  private readonly int _width;
  private readonly int _height;
  private readonly int _chromaWidth;
  private readonly int _chromaHeight;
  private readonly int _streamIndex;
  private readonly string _name;

  private Yuv420Packing(MediaStreamInfo stream, Yuv420Order order, string name) {
    this._order = order;
    this._width = stream.Width;
    this._height = stream.Height;
    this._chromaWidth = stream.Width / 2;
    this._chromaHeight = stream.Height / 2;
    this._streamIndex = stream.Index;
    this._name = name;
  }

  /// <summary>The bytes one coded frame occupies — and one picture's planes, which are the same bytes.</summary>
  public int FrameBytes => this._width * this._height + this._chromaWidth * this._chromaHeight * 2;

  /// <summary>Takes the geometry, or refuses a picture the chroma grid does not divide.</summary>
  /// <remarks>
  /// Both dimensions have to be even, and an odd one is refused rather than rounded up. 4:2:0 states
  /// one chroma pair per two-by-two block of luma; an odd width or height leaves a block that is half
  /// or a quarter of one, and which luma samples the chroma pair that covers it belongs to is not
  /// written down anywhere. ffmpeg will write such a frame — it rounds each chroma plane's dimensions
  /// up, so a 7x5 frame carries 35 luma bytes and two 4x3 chroma planes — but that is its own
  /// convention rather than anything the four-character code states, and no VfW or DirectShow renderer
  /// of these codes accepts an odd dimension at all. Writing one here would be inventing a layout, so
  /// the size is refused by name instead.
  /// </remarks>
  public static Yuv420Packing For(MediaStreamInfo stream, Yuv420Order order, string name) {
    ArgumentNullException.ThrowIfNull(stream);

    if (stream.Width <= 0 || stream.Height <= 0)
      throw new InvalidDataException(
        $"Video stream {stream.Index} states a picture size of {stream.Width}x{stream.Height}, which no frame can "
        + "be coded from or decoded into.");

    if (stream.Width % 2 != 0 || stream.Height % 2 != 0)
      throw new NotSupportedException(
        $"Video stream {stream.Index} states a picture size of {stream.Width}x{stream.Height}, which {name}'s "
        + "two-by-two chroma blocks do not divide evenly — a partial block has no stated chroma sample and no "
        + "encoder of this code writes one.");

    return new(stream, order, name);
  }

  /// <summary>
  /// Unpacks one frame into tightly packed luma, Cb and Cr planes — the bytes of a
  /// <see cref="PixelFormat.Yuv420P8"/> picture, and the form <c>-pix_fmt yuv420p</c> writes.
  /// </summary>
  public byte[] Unpack(ReadOnlySpan<byte> data) {
    var expected = this.FrameBytes;
    if (data.Length < expected)
      throw new InvalidDataException(
        $"Video stream {this._streamIndex} carries a {this._name} packet of {data.Length} byte(s), where a "
        + $"{this._width}x{this._height} frame needs {expected}.");

    var lumaSamples = this._width * this._height;
    var chromaSamples = this._chromaWidth * this._chromaHeight;
    var planes = new byte[expected];
    data[..lumaSamples].CopyTo(planes);

    var first = planes.AsSpan(lumaSamples, chromaSamples);
    var second = planes.AsSpan(lumaSamples + chromaSamples, chromaSamples);
    var coded = data.Slice(lumaSamples, chromaSamples * 2);

    switch (this._order) {
      case Yuv420Order.PlanarCbCr:
        coded[..chromaSamples].CopyTo(first);
        coded[chromaSamples..].CopyTo(second);
        break;
      case Yuv420Order.PlanarCrCb:
        coded[chromaSamples..].CopyTo(first);
        coded[..chromaSamples].CopyTo(second);
        break;
      default:
        var cbFirst = this._order == Yuv420Order.InterleavedCbCr;
        for (var i = 0; i < chromaSamples; ++i) {
          first[i] = coded[i * 2 + (cbFirst ? 0 : 1)];
          second[i] = coded[i * 2 + (cbFirst ? 1 : 0)];
        }

        break;
    }

    return planes;
  }

  /// <summary>The three planes on their own, for a comparison that names which plane disagrees.</summary>
  public (byte[] Luma, byte[] Cb, byte[] Cr) UnpackPlanes(ReadOnlySpan<byte> data) {
    var planes = this.Unpack(data);
    var lumaSamples = this._width * this._height;
    var chromaSamples = this._chromaWidth * this._chromaHeight;

    return (
      planes[..lumaSamples],
      planes.AsSpan(lumaSamples, chromaSamples).ToArray(),
      planes.AsSpan(lumaSamples + chromaSamples, chromaSamples).ToArray());
  }

  /// <summary>Packs one frame from the same tightly packed planes, byte for byte the reverse.</summary>
  public byte[] Pack(ReadOnlySpan<byte> planes) {
    var expected = this.FrameBytes;
    if (planes.Length < expected)
      throw new InvalidDataException(
        $"A {this._width}x{this._height} {this._name} frame needs {expected} bytes of luma and chroma planes; "
        + $"received {planes.Length}.");

    var lumaSamples = this._width * this._height;
    var chromaSamples = this._chromaWidth * this._chromaHeight;
    var data = new byte[expected];
    planes[..lumaSamples].CopyTo(data);

    var cb = planes.Slice(lumaSamples, chromaSamples);
    var cr = planes.Slice(lumaSamples + chromaSamples, chromaSamples);
    var coded = data.AsSpan(lumaSamples, chromaSamples * 2);

    switch (this._order) {
      case Yuv420Order.PlanarCbCr:
        cb.CopyTo(coded);
        cr.CopyTo(coded[chromaSamples..]);
        break;
      case Yuv420Order.PlanarCrCb:
        cr.CopyTo(coded);
        cb.CopyTo(coded[chromaSamples..]);
        break;
      default:
        var cbFirst = this._order == Yuv420Order.InterleavedCbCr;
        for (var i = 0; i < chromaSamples; ++i) {
          coded[i * 2 + (cbFirst ? 0 : 1)] = cb[i];
          coded[i * 2 + (cbFirst ? 1 : 0)] = cr[i];
        }

        break;
    }

    return data;
  }

  /// <summary>The picture one unpacked frame is: 4:2:0 planes, stated as such rather than converted.</summary>
  public RawImage ToImage(byte[] planes) => new() {
    Width = this._width,
    Height = this._height,
    Format = PixelFormat.Yuv420P8,
    PixelData = planes,
  };

  /// <summary>The planes of a picture on the way in, at this packing's own sample siting.</summary>
  public byte[] PlanesOf(RawImage frame)
    => RawYuvPlanes.Subsampled(frame, PixelFormat.Yuv420P8, this._chromaWidth, this._chromaHeight);
}
