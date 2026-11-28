using System;
using FileFormat.Codecs.Svq1;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>Decodes Sorenson Video 1 (<c>SVQ1</c>, <c>svq1</c>, <c>svqi</c>) to <see cref="PixelFormat.Yuv410P8"/>.</summary>
/// <remarks>
/// Adapted from FFmpeg's <c>libavcodec/svq1dec.c</c>, LGPL-2.1-or-later; see
/// <c>Svq1/THIRD-PARTY-NOTICE.FFmpeg.txt</c>. Key frames, predicted frames — skipped, one-vector and
/// four-vector macroblocks with half-sample motion — disposable frames, the scrambled and checksummed
/// header forms and the embedded message are all read.
/// <para/>
/// One quirk is FFmpeg's and is kept: a stream with no codec extra data (QuickTime's <c>SMI </c> atom,
/// or anything after an AVI's BITMAPINFOHEADER) whose frames all carry temporal reference zero came
/// from an encoder that swapped the inter mean codes for +128 and -128, and those two are swapped back.
/// </remarks>
public sealed class Svq1VideoDecoder : IVideoCodecDecoder<Svq1VideoDecoder> {

  private static readonly CodecTag[] _Tags = [
    CodecTag.FromCharacters("SVQ1"), CodecTag.FromCharacters("svq1"), CodecTag.FromCharacters("svqi"),
  ];

  private readonly Svq1Decoder _decoder;

  private Svq1VideoDecoder(MediaStreamInfo stream) {
    var data = stream.CodecPrivateData.Span;
    // A VfW stream's private data is its BITMAPINFOHEADER, and anything after the forty bytes is extra
    // data; a QuickTime one's is the whole sample entry, whose extra data is an SMI atom inside it.
    var hasExtraData = stream.CodecId == "V_MS/VFW/FOURCC" ? data.Length > 40 : data.IndexOf("SMI "u8) >= 0;
    this._decoder = new(stream.Width, stream.Height, hasExtraData);
  }

  public static string CodecName => "Sorenson Video 1";

  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    return stream.Kind == MediaStreamKind.Video && Array.Exists(_Tags, t => stream.Codec.EqualsIgnoringCase(t));
  }

  public static Svq1VideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (!Accepts(stream))
      throw new NotSupportedException($"Stream {stream.Index} is not Sorenson Video 1.");
    return new(stream);
  }

  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    var planes = this._decoder.Decode(packet.Data.ToArray());
    var width = this._decoder.Width;
    var height = this._decoder.Height;
    var chromaWidth = (width + 3) >> 2;
    var chromaHeight = (height + 3) >> 2;

    var pixels = new byte[width * height + 2 * chromaWidth * chromaHeight];
    _Crop(planes[0], width, height, pixels.AsSpan(0, width * height));
    _Crop(planes[1], chromaWidth, chromaHeight, pixels.AsSpan(width * height, chromaWidth * chromaHeight));
    _Crop(planes[2], chromaWidth, chromaHeight, pixels.AsSpan(width * height + chromaWidth * chromaHeight));
    frame = new() { Width = width, Height = height, Format = PixelFormat.Yuv410P8, PixelData = pixels };
    return true;
  }

  /// <summary>The top-left corner of a coded plane; a column or row past the coded plane repeats its edge.</summary>
  private static void _Crop(Svq1Decoder.Plane plane, int width, int height, Span<byte> destination) {
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x)
      destination[y * width + x] = plane.Samples[Math.Min(y, plane.Height - 1) * plane.Width + Math.Min(x, plane.Width - 1)];
  }
}
