using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Bmp;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes uncompressed video: each packet is the pixel array of a Windows device-independent
/// bitmap, and nothing else.
/// </summary>
/// <remarks>
/// <c>BI_RGB</c> — the compression that means "these are samples". No file header, no info header,
/// rows padded to four bytes, bottom-up unless the stated height is negative.
/// <para/>
/// The layout of those samples is entirely described by the <c>BITMAPINFOHEADER</c> the container
/// carried across as codec private data, which is exactly the second half of a <c>.bmp</c> file: put
/// a fourteen-byte file header in front of it and one packet behind it and the result is a bitmap the
/// existing reader takes, palette and all. Re-describing those bytes here would mean a second place
/// for a correction to have to be applied to.
/// <para/>
/// Reading that header is the decoder's job and not the container's. An AVI hands it over because the
/// AVI specification says a video stream's <c>strf</c> is one; what the fields mean for the pixels —
/// which way the rows run, how wide a padded row is, where the palette starts — is codec knowledge,
/// and keeping it here is what lets the same decoder serve any container that carries the same
/// description.
/// </remarks>
public sealed class RawVideoDecoder : IVideoCodecDecoder<RawVideoDecoder> {

  private readonly byte[] _format;
  private readonly int _expectedPacketLength;
  private readonly int _width;
  private readonly int _height;
  private readonly int _bitsPerPixel;

  private RawVideoDecoder(byte[] format, int width, int height, int bitsPerPixel, int expectedPacketLength) {
    this._format = format;
    this._width = width;
    this._height = height;
    this._bitsPerPixel = bitsPerPixel;
    this._expectedPacketLength = expectedPacketLength;
  }

  public static string CodecName => "Uncompressed (BI_RGB)";

  /// <summary>What Matroska calls a track that describes itself with a <c>BITMAPINFOHEADER</c>.</summary>
  private const string _VFW_CODEC_ID = "V_MS/VFW/FOURCC";

  /// <summary>
  /// Takes a video stream whose codec tag is zero because a <c>BITMAPINFOHEADER</c> said so.
  /// </summary>
  /// <remarks>
  /// Zero and only zero. <c>DIB </c> is the four-character code a writer may put in the stream
  /// handler beside it, but as a compression it is not <c>BI_RGB</c>, and a stream naming it is
  /// refused by name rather than read as though it had said nothing.
  /// <para/>
  /// The tag alone is not enough, because zero means two different things depending on who is
  /// speaking. A container that names its codecs with a code and states zero is stating
  /// <c>BI_RGB</c> — the compression that means "these are samples". A container that names them
  /// with text states no code at all, and the zero is an absence: every Matroska track would
  /// otherwise arrive here as an uncompressed one, VP9 and Vorbis included, and be refused for
  /// carrying the wrong number of bytes rather than for being a codec nothing here reads. The one
  /// <c>CodecID</c> that does mean a Windows bitmap says so by name and carries the very header the
  /// zero would have come from.
  /// </remarks>
  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    return stream.Kind == MediaStreamKind.Video
           && stream.Codec.Value == 0
           && (stream.CodecId == null || string.Equals(stream.CodecId, _VFW_CODEC_ID, StringComparison.OrdinalIgnoreCase));
  }

  public static RawVideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    var format = stream.CodecPrivateData;
    if (format.Length < BitmapInfoHeader.StructSize)
      throw new InvalidOperationException(
        $"Uncompressed video stream {stream.Index} carries {format.Length} bytes of stream format where a BITMAPINFOHEADER is {BitmapInfoHeader.StructSize}.");

    var info = BitmapInfoHeader.ReadFrom(format.Span);
    _RefuseUnrenderableDepth(stream.Index, info.BitsPerPixel);

    var width = info.Width;
    var height = Math.Abs(info.Height);

    // A DIB's rows are padded out to a four-byte boundary, so a packet is not width times height
    // times depth; it is the padded row length times the number of rows.
    var bytesPerRow = (width * info.BitsPerPixel + 7) / 8;
    var expected = ((bytesPerRow + 3) & ~3) * height;

    return new(format.ToArray(), width, height, info.BitsPerPixel, expected);
  }

  /// <summary>
  /// Turns one packet into the picture it is the pixel array of.
  /// </summary>
  /// <remarks>
  /// The length is checked before the bitmap reader sees anything, because that reader fills a row it
  /// has no bytes for with zeroes and returns the picture anyway — which for a short packet would be
  /// a black band presented as a decode. Half a raster is not a picture; padding it out would return
  /// a frame that is partly invented, which is the one thing a decoder must never do quietly.
  /// </remarks>
  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    if (packet.Data.Length < this._expectedPacketLength)
      throw new InvalidDataException(
        $"Frame holds {packet.Data.Length} bytes where {this._width}x{this._height} at {this._bitsPerPixel} bits needs {this._expectedPacketLength}.");

    frame = BmpFile.ToRawImage(BmpReader.FromSpan(this._ToBitmapFile(packet.Data.Span)));
    return true;
  }

  /// <summary>Puts the fourteen-byte file header in front of the description and the packet behind it.</summary>
  private byte[] _ToBitmapFile(ReadOnlySpan<byte> pixels) {
    var pixelOffset = BitmapFileHeader.StructSize + this._format.Length;
    var file = new byte[pixelOffset + pixels.Length];

    file[0] = (byte)'B';
    file[1] = (byte)'M';
    BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(2), file.Length);
    BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(10), pixelOffset);
    this._format.CopyTo(file, BitmapFileHeader.StructSize);
    pixels.CopyTo(file.AsSpan(pixelOffset));

    return file;
  }

  /// <summary>Refuses a depth the bitmap path does not turn into the right colours.</summary>
  /// <remarks>
  /// 16 and 32 were refused here while <see cref="BmpReader"/> returned both of them wrong rather
  /// than refusing: a 32-bit <c>BI_RGB</c> bitmap came back as <c>Indexed1</c> with no palette and
  /// threw when asked for colours, and a 16-bit one was read as 5-6-5 where <c>BI_RGB</c> is 5-5-5,
  /// which put 395 of 2257 pixels of a gradient wrong against ffmpeg's own decode of it. Both were
  /// the bitmap reader's to fix, and both are now fixed: it reads the channel masks rather than
  /// guessing a layout, and a file of either depth decodes to ffmpeg's reading of it exactly. So the
  /// two depths are read here as well, and what is left is the depths a DIB has no meaning for.
  /// </remarks>
  private static void _RefuseUnrenderableDepth(int streamIndex, int bitsPerPixel) {
    if (bitsPerPixel is 1 or 4 or 8 or 16 or 24 or 32)
      return;

    throw new NotSupportedException(
      $"Video stream {streamIndex} holds uncompressed frames of {bitsPerPixel} bits per pixel, which is not a depth a device-independent bitmap is stored at. Uncompressed frames of 1, 4, 8, 16, 24 and 32 bits are read.");
  }
}
