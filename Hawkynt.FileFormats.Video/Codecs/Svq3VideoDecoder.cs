using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using FileFormat.Codecs.Svq3;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes Sorenson Video 3 (<c>SVQ3</c>): I, P and B pictures, whole-, half- and third-sample motion,
/// and the watermark-keyed slices of protected streams.
/// </summary>
/// <remarks>
/// Converted from FFmpeg's <c>libavcodec/svq3.c</c> and the H.264 prediction, transform and table code
/// it calls (LGPL-2.1-or-later); see <c>Svq3/THIRD-PARTY-NOTICE.FFmpeg.txt</c>. The picture size and the
/// coding tools come from the <c>SEQH</c> atom in the sample description, found the way FFmpeg finds
/// it, by searching the description for it; a stream with a watermark has its logo inflated and its
/// CRC taken as the key every slice header is unlocked with. Pictures come out in display order, B
/// pictures straight away and anchors one anchor late, the last of them on <see cref="Flush"/>. Hands
/// out <see cref="PixelFormat.Yuv420P8"/> in full range, as FFmpeg's <c>yuvj420p</c>.
/// </remarks>
public sealed class Svq3VideoDecoder : IVideoCodecDecoder<Svq3VideoDecoder> {

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("SVQ3");

  private readonly Svq3Decoder _decoder;
  private bool _flushed;

  private Svq3VideoDecoder(Svq3Decoder decoder) => this._decoder = decoder;

  public static string CodecName => "Sorenson Video 3";

  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    return stream.Kind == MediaStreamKind.Video && stream.Codec.EqualsIgnoringCase(_Tag);
  }

  public static Svq3VideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (!Accepts(stream))
      throw new NotSupportedException($"Stream {stream.Index} is not Sorenson Video 3.");

    int width = stream.Width, height = stream.Height;
    bool halfPel = true, thirdPel = true, lowDelay = false, watermark = false;
    uint key = 0;
    var data = stream.CodecPrivateData.Span;
    for (var m = 0; m + 8 < data.Length; ++m) {
      if (data[m] != 'S' || data[m + 1] != 'E' || data[m + 2] != 'Q' || data[m + 3] != 'H')
        continue;
      (width, height, halfPel, thirdPel, lowDelay, watermark, key) = _SequenceHeader(data[m..]);
      break;
    }

    if (width <= 0 || height <= 0)
      throw new NotSupportedException($"Stream {stream.Index} states no picture size, and its sample description carries no SEQH atom.");
    return new(new(width, height, halfPel, thirdPel, lowDelay, watermark, key));
  }

  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    frame = null!;
    if (packet.Data.Length == 0)
      return false;
    Svq3Frame? picture;
    try {
      picture = this._decoder.Decode(packet.Data.ToArray());
    } catch (InvalidDataException) {
      // FFmpeg reports the packet and drops it; the stream goes on from the next one.
      return false;
    }

    if (picture == null)
      return false;
    frame = this._ToImage(picture);
    return true;
  }

  public IEnumerable<RawImage> Flush() {
    if (this._flushed)
      yield break;
    this._flushed = true;
    var last = this._decoder.Flush();
    if (last != null)
      yield return this._ToImage(last);
  }

  private RawImage _ToImage(Svq3Frame picture) {
    var width = this._decoder.Width;
    var height = this._decoder.Height;
    var chromaWidth = (width + 1) / 2;
    var chromaHeight = (height + 1) / 2;
    var data = new byte[width * height + 2 * chromaWidth * chromaHeight];
    for (var y = 0; y < height; ++y)
      Array.Copy(picture.Luma, picture.LumaOrigin + y * picture.LumaStride, data, y * width, width);
    var at = width * height;
    foreach (var plane in new[] { picture.Cb, picture.Cr })
      for (var y = 0; y < chromaHeight; ++y, at += chromaWidth)
        Array.Copy(plane, picture.ChromaOrigin + y * picture.ChromaStride, data, at, chromaWidth);
    return new() { Width = width, Height = height, Format = PixelFormat.Yuv420P8, PixelData = data };
  }

  /// <summary><c>svq3_decode_extradata</c>.</summary>
  private static (int Width, int Height, bool HalfPel, bool ThirdPel, bool LowDelay, bool Watermark, uint Key) _SequenceHeader(ReadOnlySpan<byte> atom) {
    var size = (int)System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(atom[4..]);
    if (size < 0 || size > atom.Length - 8)
      throw new InvalidDataException("This SVQ3 SEQH atom states more data than the sample description holds.");
    var body = atom.Slice(8, size).ToArray();
    var reader = new Svq3BitReader(body, size * 8);

    var (width, height) = reader.Read(3) switch {
      0 => (160, 120),
      1 => (128, 96),
      2 => (176, 144),
      3 => (352, 288),
      4 => (704, 576),
      5 => (240, 180),
      6 => (320, 240),
      _ => (reader.Read(12), reader.Read(12)),
    };

    var halfPel = reader.Read(1) != 0;
    var thirdPel = reader.Read(1) != 0;
    reader.Skip(4);
    var lowDelay = reader.Read(1) != 0;
    reader.Skip(1);
    if (!reader.SkipStopAndDataBits())
      throw new InvalidDataException("This SVQ3 SEQH atom ends inside its extension bytes.");

    var watermark = reader.Read(1) != 0;
    if (!watermark)
      return (width, height, halfPel, thirdPel, lowDelay, false, 0);

    var logoWidth = reader.ReadUe();
    var logoHeight = reader.ReadUe();
    reader.ReadUe();
    reader.Read(8);
    reader.Read(2);
    reader.ReadUe();
    var offset = (reader.Position + 7) >> 3;
    if (logoHeight == 0 || reader.BitsLeft <= 0 || (ulong)logoWidth * 4 > uint.MaxValue / logoHeight)
      throw new InvalidDataException("This SVQ3 watermark states an invalid logo.");

    var logoLength = (long)logoWidth * logoHeight * 4;
    byte[] logo;
    try {
      using var input = new MemoryStream(body, offset, size - offset, writable: false);
      using var zlib = new ZLibStream(input, CompressionMode.Decompress);
      using var output = new MemoryStream();
      zlib.CopyTo(output);
      logo = output.ToArray();
    } catch (Exception ex) when (ex is IOException or InvalidDataException) {
      throw new InvalidDataException("This SVQ3 watermark logo does not inflate.", ex);
    }

    if (logo.Length > logoLength)
      throw new InvalidDataException("This SVQ3 watermark logo inflates past its stated size.");

    // CRC-16/CCITT, MSB first from nought: FFmpeg's byte-swapped av_crc of AV_CRC_16_CCITT.
    uint crc = 0;
    foreach (var b in logo) {
      crc ^= (uint)b << 8;
      for (var i = 0; i < 8; ++i)
        crc = (crc & 0x8000) != 0 ? ((crc << 1) ^ 0x1021) & 0xFFFF : (crc << 1) & 0xFFFF;
    }

    return (width, height, halfPel, thirdPel, lowDelay, true, crc << 16 | crc);
  }
}
