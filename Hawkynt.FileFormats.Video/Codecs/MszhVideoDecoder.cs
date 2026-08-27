using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Bmp;
using FileFormat.Codecs.Lcl;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes the MSZH variant of the Lossless Codec Library (LCL): groups of eight commands selected
/// by one mask byte, with each command either copying four literal bytes or repeating a four-byte
/// aligned run from data already reconstructed in the same independently coded section.
/// </summary>
/// <remarks>
/// The frame wrapper is LCL's shared eight-byte trailer, already used by <see cref="LclZlibVideoDecoder"/>.
/// The compression parser is adapted from FFmpeg's <c>libavcodec/lcldec.c</c>, copyright (c) 2002-2004
/// Roberto Togni, distributed there under LGPL-2.1-or-later. This adaptation is distributed with this
/// project under LGPL-3.0-or-later. The compatible implementation source matters here: LCL's published
/// prose description leaves the actual MSZH back-reference coding as an unfilled placeholder, which is
/// why the codec was previously documented as undecodable without transcribing somebody else's decoder.
/// <para/>
/// A mask is consumed most-significant bit first. A zero bit copies the next four source bytes verbatim.
/// A one bit reads a little-endian sixteen-bit word: its low eleven bits are the backward distance and
/// its high five bits plus one are the number of four-byte groups to reproduce. Back-references may
/// overlap their destination, so they are expanded byte by byte rather than with a single block copy.
/// A zero backward distance produces zero bytes, matching the reference decoder's defined defensive
/// behaviour.
/// <para/>
/// The original codec can mark a stream as compressed and still put a complete uncompressed RGB24
/// picture in a packet; the reference decoder recognizes that case from the packet length. It can also
/// split a compressed frame into two independent sections: the packet then starts with the first
/// section's compressed byte count and decompressed byte count, both little-endian 32-bit values,
/// followed by the two compressed sections. Both forms are handled here.
/// <para/>
/// RGB24 is implemented because its byte layout is already independently established by the sibling
/// LCL ZLIB decoder and maps directly to <see cref="PixelFormat.Bgr24"/>. LCL's YUV layouts are not
/// exposed here yet; adding them should be accompanied by real-file, plane-level verification rather
/// than silently treating an implementation-derived packing as independently measured fact.
/// </remarks>
public sealed class MszhVideoDecoder : IVideoCodecDecoder<MszhVideoDecoder> {

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("MSZH");

  private const byte _IMAGE_TYPE_RGB24 = 2;
  private const sbyte _COMPRESSION_MSZH = 0;
  private const sbyte _COMPRESSION_NONE = 1;
  private const byte _CODEC_MSZH = 1;

  private readonly int _width;
  private readonly int _height;
  private readonly int _packedStride;
  private readonly int _paddedStride;
  private readonly int _decodedSize;
  private readonly int _streamIndex;
  private readonly sbyte _compression;
  private readonly bool _multithreaded;

  private MszhVideoDecoder(
    int width,
    int height,
    int packedStride,
    int paddedStride,
    int streamIndex,
    sbyte compression,
    bool multithreaded
  ) {
    this._width = width;
    this._height = height;
    this._packedStride = packedStride;
    this._paddedStride = paddedStride;
    this._decodedSize = paddedStride * height;
    this._streamIndex = streamIndex;
    this._compression = compression;
    this._multithreaded = multithreaded;
  }

  public static string CodecName => "LCL MSZH";

  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    return stream.Kind == MediaStreamKind.Video && stream.Codec.EqualsIgnoringCase(_Tag);
  }

  public static MszhVideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    if (stream.Width <= 0 || stream.Height <= 0)
      throw new InvalidDataException(
        $"Video stream {stream.Index} states a picture size of {stream.Width}x{stream.Height}, which no frame can "
        + "be decoded into.");

    var format = stream.CodecPrivateData.Span;
    if (format.Length < BitmapInfoHeader.StructSize + LclHeader.ExtraBytes)
      throw new InvalidDataException(
        $"Video stream {stream.Index} carries {format.Length} byte(s) of stream format, where LCL's BITMAPINFOHEADER "
        + $"and eight-byte trailer need at least {BitmapInfoHeader.StructSize + LclHeader.ExtraBytes}.");

    var header = LclHeader.Read(format[BitmapInfoHeader.StructSize..]);
    if (header.Codec != _CODEC_MSZH)
      throw new InvalidDataException(
        $"Video stream {stream.Index} is tagged MSZH but its LCL trailer identifies codec {header.Codec}, not MSZH "
        + $"({_CODEC_MSZH}).");

    if (header.ImageType != _IMAGE_TYPE_RGB24)
      throw new NotSupportedException(
        $"Video stream {stream.Index} states LCL image type {header.ImageType}. This MSZH decoder currently reads "
        + "RGB24 (2) only; the YUV packings need real-file, plane-level verification before they are exposed.");

    if (header.PngFiltered)
      throw new NotSupportedException(
        $"Video stream {stream.Index} sets LCL's PNG-filter flag. That transform belongs to the sibling ZLIB codec "
        + "and is not a defined MSZH operation.");

    if (header.Compression is not (_COMPRESSION_MSZH or _COMPRESSION_NONE))
      throw new NotSupportedException(
        $"Video stream {stream.Index} states unsupported MSZH compression mode {header.Compression}; known modes are "
        + $"{_COMPRESSION_MSZH} (MSZH) and {_COMPRESSION_NONE} (uncompressed).");

    var packedStrideLong = (long)stream.Width * 3;
    var paddedStrideLong = (packedStrideLong + 3) & ~3L;
    var decodedSizeLong = paddedStrideLong * stream.Height;
    if (packedStrideLong > int.MaxValue || paddedStrideLong > int.MaxValue || decodedSizeLong > int.MaxValue)
      throw new InvalidDataException(
        $"Video stream {stream.Index} states a picture size of {stream.Width}x{stream.Height}, whose RGB24 frame is "
        + "too large to hold in one managed byte array.");

    return new(
      stream.Width,
      stream.Height,
      (int)packedStrideLong,
      (int)paddedStrideLong,
      stream.Index,
      header.Compression,
      header.Multithreaded
    );
  }

  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    var source = packet.Data.Span;
    byte[] decoded;
    int rowStride;

    // Some LCL encoders leave the compression mode at MSZH while storing a complete raw RGB24 frame.
    // The reference decoder distinguishes that form by the padded frame size rather than by another flag.
    if (this._compression == _COMPRESSION_NONE || source.Length == this._decodedSize) {
      (decoded, rowStride) = this._ReadRaw(source);
    } else {
      decoded = new byte[this._decodedSize];
      if (this._multithreaded)
        this._DecodeSplit(source, decoded);
      else
        _Decompress(source, decoded, 0, decoded.Length, this._streamIndex);
      rowStride = this._paddedStride;
    }

    frame = new() {
      Width = this._width,
      Height = this._height,
      Format = PixelFormat.Bgr24,
      PixelData = this._Unpack(decoded, rowStride),
    };
    return true;
  }

  private (byte[] Data, int RowStride) _ReadRaw(ReadOnlySpan<byte> source) {
    var packedBytes = checked(this._packedStride * this._height);
    var paddedBytes = this._decodedSize;
    var rowStride = source.Length switch {
      _ when source.Length == packedBytes => this._packedStride,
      _ when source.Length == paddedBytes => this._paddedStride,
      _ => throw new InvalidDataException(
        $"Video stream {this._streamIndex} carries an uncompressed MSZH packet of {source.Length} byte(s), where its "
        + $"RGB24 picture needs either {packedBytes} byte(s) with packed rows or {paddedBytes} with four-byte row "
        + "alignment."),
    };

    return (source.ToArray(), rowStride);
  }

  private void _DecodeSplit(ReadOnlySpan<byte> source, byte[] destination) {
    if (source.Length < 8)
      throw new InvalidDataException(
        $"Video stream {this._streamIndex} carries a split MSZH packet shorter than its eight-byte split header.");

    var firstCompressedLength = BinaryPrimitives.ReadUInt32LittleEndian(source);
    var firstDecodedLength = BinaryPrimitives.ReadUInt32LittleEndian(source[4..]);
    if (firstCompressedLength > int.MaxValue || firstDecodedLength > int.MaxValue)
      throw new InvalidDataException(
        $"Video stream {this._streamIndex} carries a split MSZH packet whose section lengths do not fit in memory.");

    var firstInput = (int)firstCompressedLength;
    var firstOutput = (int)firstDecodedLength;
    if (firstInput > source.Length - 8)
      throw new InvalidDataException(
        $"Video stream {this._streamIndex} states a first MSZH section of {firstInput} compressed byte(s), but only "
        + $"{source.Length - 8} byte(s) follow the split header.");
    if (firstOutput > destination.Length)
      throw new InvalidDataException(
        $"Video stream {this._streamIndex} states a first MSZH section of {firstOutput} decoded byte(s), larger than "
        + $"the complete {destination.Length}-byte frame.");

    _Decompress(source.Slice(8, firstInput), destination, 0, firstOutput, this._streamIndex);
    _Decompress(source[(8 + firstInput)..], destination, firstOutput, destination.Length - firstOutput, this._streamIndex);
  }

  private static void _Decompress(
    ReadOnlySpan<byte> source,
    byte[] destination,
    int destinationOffset,
    int destinationLength,
    int streamIndex
  ) {
    if (destinationLength == 0)
      return;

    var sourcePosition = 0;
    var written = 0;

    while (written < destinationLength) {
      if (sourcePosition >= source.Length)
        throw new InvalidDataException(
          $"Video stream {streamIndex} carries an MSZH packet that ends before its {destinationLength}-byte section "
          + "has been reconstructed.");

      var mask = source[sourcePosition++];
      for (var maskBit = 0x80; maskBit != 0 && written < destinationLength; maskBit >>= 1) {
        if ((mask & maskBit) == 0) {
          if (source.Length - sourcePosition < 4)
            throw new InvalidDataException(
              $"Video stream {streamIndex} carries an MSZH literal whose four bytes run past the end of the packet.");

          var count = Math.Min(4, destinationLength - written);
          source.Slice(sourcePosition, count).CopyTo(destination.AsSpan(destinationOffset + written, count));
          sourcePosition += 4;
          written += count;
          continue;
        }

        if (source.Length - sourcePosition < 2)
          throw new InvalidDataException(
            $"Video stream {streamIndex} carries an MSZH back-reference whose two-byte descriptor runs past the end "
            + "of the packet.");

        var descriptor = BinaryPrimitives.ReadUInt16LittleEndian(source[sourcePosition..]);
        sourcePosition += 2;

        var distance = descriptor & 0x07ff;
        var countBytes = ((descriptor >> 11) + 1) * 4;
        countBytes = Math.Min(countBytes, destinationLength - written);

        // FFmpeg's reference decoder clamps an over-large distance to the bytes already reconstructed and
        // defines distance zero as zero-fill. Preserve those semantics because existing LCL bitstreams can
        // therefore rely on them, while still keeping every managed access inside the destination section.
        distance = Math.Min(distance, written);
        if (distance == 0) {
          destination.AsSpan(destinationOffset + written, countBytes).Clear();
          written += countBytes;
          continue;
        }

        for (var i = 0; i < countBytes; ++i)
          destination[destinationOffset + written + i] = destination[destinationOffset + written - distance + i];
        written += countBytes;
      }
    }
  }

  private byte[] _Unpack(byte[] decoded, int rowStride) {
    var picture = new byte[this._packedStride * this._height];
    for (var row = 0; row < this._height; ++row) {
      var destinationRow = this._height - 1 - row;
      Array.Copy(decoded, row * rowStride, picture, destinationRow * this._packedStride, this._packedStride);
    }

    return picture;
  }
}
