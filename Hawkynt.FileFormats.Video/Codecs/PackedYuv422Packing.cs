using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>Which byte of a 4:2:2 macropixel holds which sample.</summary>
/// <remarks>
/// Four codes, one layout, four orderings of the same four bytes. Naming the orderings rather than
/// the codes is what keeps the difference between them visible: <c>YUY2</c> and <c>UYVY</c> differ
/// only in whether luma or chroma comes first, and <c>YVYU</c> and <c>VYUY</c> are those two with the
/// chroma pair the other way round. A single wrong offset here does not fail — it swaps red for blue
/// in every frame and keeps decoding.
/// </remarks>
internal enum PackedYuv422Order {

  /// <summary>Y0 Cb Y1 Cr — <c>YUY2</c>, and what ffmpeg calls <c>yuyv422</c>.</summary>
  LumaCbLumaCr,

  /// <summary>Y0 Cr Y1 Cb — <c>YVYU</c>: <c>YUY2</c> with the chroma pair exchanged.</summary>
  LumaCrLumaCb,

  /// <summary>Cb Y0 Cr Y1 — <c>UYVY</c>, and what ffmpeg calls <c>uyvy422</c>.</summary>
  CbLumaCrLuma,

  /// <summary>Cr Y0 Cb Y1 — <c>VYUY</c>: <c>UYVY</c> with the chroma pair exchanged.</summary>
  CrLumaCbLuma,
}

/// <summary>
/// The packing the four packed 4:2:2 four-character codes share: two pixels to four bytes, two luma
/// samples and the one chroma pair that covers them, no padding and no row alignment of any kind.
/// </summary>
/// <remarks>
/// One place for the loop rather than four, because the four codes differ by nothing but the offsets
/// and a copy of the loop per code is four places for the same correction to have to be applied to.
/// <para/>
/// Planes come out and go in as the tightly packed <see cref="PixelFormat.Yuv422P8"/> bytes a
/// <see cref="RawImage"/> of that format carries — luma at the full width, each chroma plane at half
/// of it, every plane at the full height — so unpacking a packet is a pure repack of the eight-bit
/// samples with nothing computed and nothing lost.
/// </remarks>
internal sealed class PackedYuv422Packing {

  private readonly int _firstLuma;
  private readonly int _secondLuma;
  private readonly int _cb;
  private readonly int _cr;
  private readonly int _width;
  private readonly int _height;
  private readonly int _chromaWidth;
  private readonly int _stride;
  private readonly int _streamIndex;
  private readonly string _name;

  private PackedYuv422Packing(MediaStreamInfo stream, PackedYuv422Order order, string name) {
    (this._firstLuma, this._secondLuma, this._cb, this._cr) = order switch {
      PackedYuv422Order.LumaCbLumaCr => (0, 2, 1, 3),
      PackedYuv422Order.LumaCrLumaCb => (0, 2, 3, 1),
      PackedYuv422Order.CbLumaCrLuma => (1, 3, 0, 2),
      _ => (1, 3, 2, 0),
    };
    this._width = stream.Width;
    this._height = stream.Height;
    this._chromaWidth = stream.Width / 2;
    this._stride = stream.Width * 2; // Four bytes to two pixels, exactly — no row padding at all.
    this._streamIndex = stream.Index;
    this._name = name;
  }

  /// <summary>The bytes one coded frame occupies.</summary>
  public int FrameBytes => this._stride * this._height;

  /// <summary>Takes the geometry, or refuses a picture the packing has no whole macropixels for.</summary>
  /// <remarks>
  /// An odd width is refused rather than rounded. A macropixel is two pixels and there is no half of
  /// one: a row of an odd-width picture would end in two bytes stating one luma sample and one of the
  /// two chroma samples that pixel needs, and every reader would have to invent the other. ffmpeg does
  /// not write one either — asked for an odd-width 4:2:2 frame its scaler rounds the width up to the
  /// next even one — so there is no real stream to say what the last two bytes would mean.
  /// </remarks>
  public static PackedYuv422Packing For(MediaStreamInfo stream, PackedYuv422Order order, string name) {
    ArgumentNullException.ThrowIfNull(stream);

    if (stream.Width <= 0 || stream.Height <= 0)
      throw new InvalidDataException(
        $"Video stream {stream.Index} states a picture size of {stream.Width}x{stream.Height}, which no frame can "
        + "be coded from or decoded into.");

    if (stream.Width % 2 != 0)
      throw new NotSupportedException(
        $"Video stream {stream.Index} states a width of {stream.Width}, which {name}'s two-pixel macropixels do not "
        + "divide evenly — the last pixel of a row would carry one of the two chroma samples it needs and no "
        + "encoder writes one.");

    return new(stream, order, name);
  }

  /// <summary>
  /// Unpacks one frame into tightly packed luma, Cb and Cr planes — the bytes of a
  /// <see cref="PixelFormat.Yuv422P8"/> picture, and the form <c>-pix_fmt yuv422p</c> writes.
  /// </summary>
  public byte[] Unpack(ReadOnlySpan<byte> data) {
    var expected = (long)this._stride * this._height;
    if (data.Length < expected)
      throw new InvalidDataException(
        $"Video stream {this._streamIndex} carries a {this._name} packet of {data.Length} byte(s), where a "
        + $"{this._width}x{this._height} frame at a stride of {this._stride} needs {expected}.");

    var lumaSamples = this._width * this._height;
    var chromaSamples = this._chromaWidth * this._height;
    var planes = new byte[lumaSamples + chromaSamples * 2];
    var crBase = lumaSamples + chromaSamples;

    for (var row = 0; row < this._height; ++row) {
      var line = data.Slice(row * this._stride, this._stride);
      var luma = row * this._width;
      var chroma = row * this._chromaWidth;

      for (var offset = 0; offset < this._stride; offset += 4) {
        planes[luma] = line[offset + this._firstLuma];
        planes[luma + 1] = line[offset + this._secondLuma];
        planes[lumaSamples + chroma] = line[offset + this._cb];
        planes[crBase + chroma] = line[offset + this._cr];
        luma += 2;
        ++chroma;
      }
    }

    return planes;
  }

  /// <summary>The three planes on their own, for a comparison that names which plane disagrees.</summary>
  public (byte[] Luma, byte[] Cb, byte[] Cr) UnpackPlanes(ReadOnlySpan<byte> data) {
    var planes = this.Unpack(data);
    var lumaSamples = this._width * this._height;
    var chromaSamples = this._chromaWidth * this._height;

    return (
      planes[..lumaSamples],
      planes.AsSpan(lumaSamples, chromaSamples).ToArray(),
      planes.AsSpan(lumaSamples + chromaSamples, chromaSamples).ToArray());
  }

  /// <summary>Packs one frame from the same tightly packed planes, byte for byte the reverse.</summary>
  public byte[] Pack(ReadOnlySpan<byte> planes) {
    var lumaSamples = this._width * this._height;
    var chromaSamples = this._chromaWidth * this._height;
    if (planes.Length < lumaSamples + chromaSamples * 2)
      throw new InvalidDataException(
        $"A {this._width}x{this._height} {this._name} frame needs {lumaSamples + chromaSamples * 2} bytes of "
        + $"luma and chroma planes; received {planes.Length}.");

    var crBase = lumaSamples + chromaSamples;
    var data = new byte[this.FrameBytes];

    for (var row = 0; row < this._height; ++row) {
      var line = data.AsSpan(row * this._stride, this._stride);
      var luma = row * this._width;
      var chroma = row * this._chromaWidth;

      for (var offset = 0; offset < this._stride; offset += 4) {
        line[offset + this._firstLuma] = planes[luma];
        line[offset + this._secondLuma] = planes[luma + 1];
        line[offset + this._cb] = planes[lumaSamples + chroma];
        line[offset + this._cr] = planes[crBase + chroma];
        luma += 2;
        ++chroma;
      }
    }

    return data;
  }

  /// <summary>The picture one unpacked frame is: 4:2:2 planes, stated as such rather than converted.</summary>
  public RawImage ToImage(byte[] planes) => new() {
    Width = this._width,
    Height = this._height,
    Format = PixelFormat.Yuv422P8,
    PixelData = planes,
  };

  /// <summary>The planes of a picture on the way in, at this packing's own sample siting.</summary>
  public byte[] PlanesOf(RawImage frame)
    => RawYuvPlanes.Subsampled(frame, PixelFormat.Yuv422P8, this._chromaWidth, this._height);
}
