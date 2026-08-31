using System;
using System.IO;
using System.IO.Compression;
using FileFormat.Bmp;
using FileFormat.Codecs.Lcl;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes the ZLIB variant of the Lossless Codec Library (LCL): a picture converted to a target
/// colour space and handed straight to zlib's DEFLATE, with the compressor reset fresh for every
/// frame, so every packet decodes on its own with nothing carried from the one before it.
/// </summary>
/// <remarks>
/// Recovered from "Description of the LCL codecs (MSZH and ZLIB)" by Roberto Togni
/// (multimedia.cx/lcl.txt, GNU FDL 1.2) — a real specification, unlike most of this family, though its
/// own author calls it "random notes... while building a decoder" and leaves several fields as
/// unfilled <c>[add ...]</c> placeholders. Codec identity, the colour-space and flag byte layout, and
/// the zlib compression itself are exactly as that document states; see <see cref="LclHeader"/> for
/// the trailer it describes. What follows is what the document leaves unstated and what was measured
/// here instead, against ffmpeg's own zlib encoder — one of the few in this package that exists — and
/// against seven real recordings from samples.ffmpeg.org.
/// <para/>
/// <b>A coded row is sometimes a whole four-byte word, and which is a property of the file rather than
/// of the format.</b> `zlib1.avi`, one of seven real recordings pulled from samples.ffmpeg.org, is 1246
/// pixels wide — the one width among any sample here not already a multiple of four — and its single
/// frame decompresses to 3,710,080 bytes where 1246 × 992 × 3 is 3,708,096: two bytes of padding on
/// every one of its 992 rows, confirmed against the file's own <c>biSizeImage</c>, which states the
/// padded total. But a stream built here with ffmpeg's own encoder at an equally unaligned width — 13,
/// 322 — decompresses to exactly the packed byte count and not one byte more; ffmpeg's own decoder
/// logs a size mismatch against the padded figure it expects and proceeds regardless. The two
/// encoders disagree about whether the padding this format's document never mentions at all is
/// written, so this decoder does not assume either answer: it reads however many bytes the zlib
/// stream actually holds and takes the row stride to be whichever of the packed or the padded byte
/// count that total equals, refusing only when it is neither.
/// <para/>
/// <b>The picture is stored bottom row first</b>, matching every AVI codec in this package. Found the
/// same way as the others: decompressing a packet and finding it a mirror image of ffmpeg's own
/// decoded frame until the rows are reversed.
/// <para/>
/// <b>Measured against ffmpeg</b> two ways at once, because ZLIB is one of the few codecs in this
/// family with a real encoder. Round-tripped through it — four streams built and encoded here, 2x2 to
/// 322x240, including widths that leave a row unaligned and ones that do not, at the compression
/// levels the encoder will choose between — every decoded frame is identical to the source frame that
/// was encoded, which for a lossless codec is the stronger of the two comparisons this package usually
/// has to choose between, being the ground truth itself rather than a second decoder's opinion. And
/// measured against seven real files from samples.ffmpeg.org, 282 frames from 64x48 to 1246x992 —
/// <b>every sample of every frame is identical</b> across all 307 frames measured either way, RGB-native
/// so the comparison is a direct one rather than a plane-by-plane approximation of anything subsampled.
/// <para/>
/// <b>What refuses.</b> An image type other than RGB24 (2): the document leaves every YUV format's byte
/// order as an unfilled placeholder, ffmpeg's encoder writes nothing but RGB24, and none of the seven
/// real files carries anything else, so there is nothing to measure a YUV byte layout against. The
/// multithread flag, whose split's own length and offset fields the document never states. The PNG
/// filter flag, whose per-colour-space structure is another of the document's unfilled placeholders
/// and whose own author states his RGB24 implementation of it does not work correctly — there being
/// nothing published and nothing working to read it against either way. And a packet whose zlib stream
/// is truncated, corrupt, or inflates to neither the picture's packed nor its padded byte count.
/// </remarks>
public sealed class LclZlibVideoDecoder : IVideoCodecDecoder<LclZlibVideoDecoder> {

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("ZLIB");

  private const byte _IMAGE_TYPE_RGB24 = 2;

  private readonly int _width;
  private readonly int _height;
  private readonly int _stride;
  private readonly int _streamIndex;

  private LclZlibVideoDecoder(int width, int height, int streamIndex) {
    this._width = width;
    this._height = height;
    this._stride = (width * 3 + 3) / 4 * 4;
    this._streamIndex = streamIndex;
  }

  /// <summary>Gets the codec name.</summary>
  public static string CodecName => "LCL ZLIB";

  /// <summary>Determines whether the specified media stream is supported.</summary>
  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    return stream.Kind == MediaStreamKind.Video && stream.Codec.EqualsIgnoringCase(_Tag);
  }

  /// <summary>Creates a decoder for the specified media stream.</summary>
  public static LclZlibVideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    if (stream.Width <= 0 || stream.Height <= 0)
      throw new InvalidDataException(
        $"Video stream {stream.Index} states a picture size of {stream.Width}x{stream.Height}, which no frame can "
        + "be decoded into.");

    var format = stream.CodecPrivateData.Span;
    if (format.Length < BitmapInfoHeader.StructSize + LclHeader.ExtraBytes)
      throw new InvalidDataException(
        $"Video stream {stream.Index} carries {format.Length} byte(s) behind its BITMAPINFOHEADER, where LCL's own "
        + $"eight-byte trailer needs at least {BitmapInfoHeader.StructSize + LclHeader.ExtraBytes}.");

    var header = LclHeader.Read(format[BitmapInfoHeader.StructSize..]);

    if (header.ImageType != _IMAGE_TYPE_RGB24)
      throw new NotSupportedException(
        $"Video stream {stream.Index} states LCL image type {header.ImageType}. Only RGB24 (2) has a sample or an "
        + "encoder to measure a byte layout against; every YUV form the format defines is left unstated by its own "
        + "specification.");

    if (header.Multithreaded)
      throw new NotSupportedException(
        $"Video stream {stream.Index} sets LCL's multithread flag, whose split's own length and offset fields the "
        + "format's specification never states.");

    if (header.PngFiltered)
      throw new NotSupportedException(
        $"Video stream {stream.Index} sets LCL's PNG filter flag. Its per-colour-space structure is left unstated "
        + "by the format's own specification, whose author additionally states that his own RGB24 implementation of "
        + "it does not work correctly.");

    return new(stream.Width, stream.Height, stream.Index);
  }

  /// <summary>Attempts to decode the specified coded packet into a raw image frame.</summary>
  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    byte[] decoded;
    try {
      using var source = new MemoryStream(packet.Data.ToArray());
      using var zlib = new ZLibStream(source, CompressionMode.Decompress);
      using var output = new MemoryStream();
      zlib.CopyTo(output);
      decoded = output.ToArray();
    } catch (InvalidDataException ex) {
      throw new InvalidDataException(
        $"Video stream {this._streamIndex} carries an LCL ZLIB packet whose zlib stream is corrupt.", ex);
    }

    var packedBytes = this._width * 3 * this._height;
    var paddedBytes = this._stride * this._height;
    var rowStride = decoded.Length switch {
      _ when decoded.Length == packedBytes => this._width * 3,
      _ when decoded.Length == paddedBytes => this._stride,
      _ => throw new InvalidDataException(
        $"Video stream {this._streamIndex} carries an LCL ZLIB packet that inflates to {decoded.Length} byte(s), "
        + $"where its picture needs either {packedBytes} (rows packed tight) or {paddedBytes} (rows padded to a "
        + "four-byte word)."),
    };

    frame = new() {
      Width = this._width,
      Height = this._height,
      Format = PixelFormat.Bgr24,
      PixelData = this._Unpack(decoded, rowStride),
    };
    return true;
  }

  /// <summary>Strips whatever padding a row carries, if any, and turns the coded, bottom-up picture
  /// the right way up in the same pass.</summary>
  private byte[] _Unpack(byte[] decoded, int rowStride) {
    var rowBytes = this._width * 3;
    var picture = new byte[rowBytes * this._height];

    for (var row = 0; row < this._height; ++row) {
      var destRow = this._height - 1 - row;
      Array.Copy(decoded, row * rowStride, picture, destRow * rowBytes, rowBytes);
    }

    return picture;
  }
}
