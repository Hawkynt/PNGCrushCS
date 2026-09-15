using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using FileFormat.Bmp;
using FileFormat.Core;

namespace FileFormat.Sdg;

/// <summary>Reads bitmap thumbnails from StarOffice/LibreOffice <c>SGA3</c> gallery objects.</summary>
public static class SdgReader {

  private const uint _ZCompress = 0x01004453;
  private const uint _BitmapExMagic1 = 0x25091962;
  private const uint _BitmapExMagic2 = 0xACB20201;
  private const int _ObjectHeaderSize = 11;
  private const int _BmpFileHeaderSize = 14;
  private const int _BmpInfoHeaderSize = 40;
  private const int _CompressedInfoSize = 12;
  private const uint _MaxUncompressedThumbnailBytes = 512 * 1024 * 1024;

  public static SdgFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < _ObjectHeaderSize || !_IsSga3(data, 0))
      throw new InvalidDataException("Data does not start with an SGA3 gallery object.");

    var images = new List<RawImage>();
    var at = 0;
    while (at < data.Length) {
      if (data.Length - at < _ObjectHeaderSize)
        throw new InvalidDataException("The final SGA3 gallery object header is truncated.");
      if (!_IsSga3(data, at))
        throw new InvalidDataException($"Expected an SGA3 gallery object at byte {at}.");
      if (BinaryPrimitives.ReadUInt16LittleEndian(data[(at + 4)..]) != 4)
        throw new InvalidDataException("The SGA3 gallery object has an unsupported compatibility header.");

      var version = BinaryPrimitives.ReadUInt16LittleEndian(data[(at + 6)..]);
      var kind = BinaryPrimitives.ReadUInt16LittleEndian(data[(at + 8)..]);
      if (!_IsKnownKind(kind))
        throw new InvalidDataException($"The SGA3 gallery object uses unknown kind {kind}.");

      var payloadAt = at + _ObjectHeaderSize;
      if (data[at + 10] == 0) {
        // Metafile thumbnails are SVM streams. They are not raster images, so keep walking to the
        // next independently framed SGA object instead of interpreting bytes inside the metafile as
        // a bitmap. The first object itself must still be valid SGA3, which avoids the old whole-file
        // byte scavenging behavior.
        var next = _FindNextObject(data, payloadAt);
        if (next < 0)
          break;
        at = next;
        continue;
      }

      var image = _DecodeBitmap(data[payloadAt..], out var bitmapLength);
      images.Add(image);

      var nextAt = checked(payloadAt + bitmapLength);
      _SkipString(data, ref nextAt, "object URL");
      _SkipKindTrailer(data, ref nextAt, version, kind);
      at = nextAt;
    }

    if (images.Count == 0)
      throw new InvalidDataException("No decodable bitmap thumbnail was found in the SDG gallery file.");

    return new SdgFile { Images = images };
  }

  private static void _SkipKindTrailer(ReadOnlySpan<byte> data, ref int at, ushort version, ushort kind) {
    switch (kind) {
      case 1: // Bitmap
      case 4: // Animation
      case 6: // Inet
        _SkipBytes(data, ref at, 10, "bitmap object fields");
        _SkipString(data, ref at, "bitmap compatibility string");
        if (version >= 5)
          _SkipString(data, ref at, "bitmap title");
        return;

      case 2: // Sound
        if (version >= 5)
          _SkipBytes(data, ref at, 2, "sound type");
        if (version >= 6)
          _SkipString(data, ref at, "sound title");
        return;

      case 5: // SvDraw
        if (version >= 5)
          _SkipString(data, ref at, "drawing title");
        return;

      default:
        throw new InvalidDataException($"The SGA3 gallery object uses unknown kind {kind}.");
    }
  }

  private static void _SkipString(ReadOnlySpan<byte> data, ref int at, string field) {
    if (data.Length - at < 2)
      throw new InvalidDataException($"The SGA3 {field} length is truncated.");

    var length = BinaryPrimitives.ReadUInt16LittleEndian(data[at..]);
    at += 2;
    _SkipBytes(data, ref at, length, field);
  }

  private static void _SkipBytes(ReadOnlySpan<byte> data, ref int at, int count, string field) {
    if (count < 0 || at < 0 || count > data.Length - at)
      throw new InvalidDataException($"The SGA3 {field} runs past the end of the file.");
    at += count;
  }

  private static RawImage _DecodeBitmap(ReadOnlySpan<byte> data, out int encodedLength) {
    var image = _DecodeBitmapBase(data, out encodedLength);

    if (data.Length - encodedLength < 9
        || BinaryPrimitives.ReadUInt32LittleEndian(data[encodedLength..]) != _BitmapExMagic1
        || BinaryPrimitives.ReadUInt32LittleEndian(data[(encodedLength + 4)..]) != _BitmapExMagic2)
      return image;

    var type = data[encodedLength + 8];
    encodedLength = checked(encodedLength + 9);
    switch (type) {
      case 0:
        break;
      case 1:
        if (data.Length - encodedLength < 2)
          throw new InvalidDataException("The SGA3 transparent-colour record is truncated.");
        var colorId = BinaryPrimitives.ReadUInt16LittleEndian(data[encodedLength..]);
        encodedLength += 2;
        if ((colorId & 0x8000) != 0)
          encodedLength = checked(encodedLength + 6);
        if (encodedLength > data.Length)
          throw new InvalidDataException("The SGA3 transparent-colour record is truncated.");
        break;
      case 2:
        var mask = _DecodeBitmapBase(data[encodedLength..], out var maskLength);
        encodedLength = checked(encodedLength + maskLength);
        image = _ApplyTransparencyMask(image, mask);
        break;
      default:
        // LibreOffice treats an unknown BitmapEx type as an empty extension after the type byte.
        break;
    }

    return image;
  }

  private static RawImage _ApplyTransparencyMask(RawImage image, RawImage mask) {
    if (image.Width != mask.Width || image.Height != mask.Height)
      throw new InvalidDataException("The SGA3 transparency mask does not match the thumbnail dimensions.");

    var color = image.EnsureFormat(PixelFormat.Bgra32);
    var transparency = mask.EnsureFormat(PixelFormat.Bgr24);
    var pixelCount = checked(image.Width * image.Height);
    var maskBytes = checked(pixelCount * 3);
    if (transparency.PixelData.Length < maskBytes)
      throw new InvalidDataException("The SGA3 transparency mask has too little pixel data.");

    var pixels = (byte[])color.PixelData.Clone();
    for (var i = 0; i < pixelCount; ++i)
      pixels[i * 4 + 3] = (byte)(255 - transparency.PixelData[i * 3]);

    return new RawImage {
      Width = image.Width,
      Height = image.Height,
      Format = PixelFormat.Bgra32,
      PixelData = pixels
    };
  }

  private static RawImage _DecodeBitmapBase(ReadOnlySpan<byte> data, out int encodedLength) {
    encodedLength = 0;
    if (data.Length < _BmpFileHeaderSize + _BmpInfoHeaderSize || data[0] != (byte)'B' || data[1] != (byte)'M')
      throw new InvalidDataException("The SGA3 thumbnail has no BMP file header.");

    var infoSize = BinaryPrimitives.ReadUInt32LittleEndian(data[_BmpFileHeaderSize..]);
    if (infoSize != _BmpInfoHeaderSize)
      throw new InvalidDataException($"The SGA3 thumbnail uses unsupported BMP info header size {infoSize}.");

    var compression = BinaryPrimitives.ReadUInt32LittleEndian(data[30..]);
    if (compression != _ZCompress) {
      var statedSize = BinaryPrimitives.ReadUInt32LittleEndian(data[2..]);
      if (statedSize < _BmpFileHeaderSize + _BmpInfoHeaderSize || statedSize > data.Length)
        throw new InvalidDataException("The SGA3 BMP thumbnail states a size outside the gallery file.");

      encodedLength = checked((int)statedSize);
      return BmpFile.ToRawImage(BmpReader.FromSpan(data[..encodedLength]));
    }

    var compressedInfoAt = _BmpFileHeaderSize + _BmpInfoHeaderSize;
    if (data.Length < compressedInfoAt + _CompressedInfoSize)
      throw new InvalidDataException("The SGA3 ZCOMPRESS header is truncated.");

    var codedSize = BinaryPrimitives.ReadUInt32LittleEndian(data[compressedInfoAt..]);
    var uncodedSize = BinaryPrimitives.ReadUInt32LittleEndian(data[(compressedInfoAt + 4)..]);
    var originalCompression = BinaryPrimitives.ReadUInt32LittleEndian(data[(compressedInfoAt + 8)..]);
    if (originalCompression > 3)
      throw new InvalidDataException($"The SGA3 thumbnail wraps unsupported BMP compression {originalCompression}.");
    if (uncodedSize > _MaxUncompressedThumbnailBytes)
      throw new InvalidDataException($"The SGA3 thumbnail expands to an implausible {uncodedSize} bytes.");

    var codedAt = compressedInfoAt + _CompressedInfoSize;
    if (codedSize > data.Length - codedAt)
      throw new InvalidDataException("The SGA3 ZCOMPRESS block runs past the gallery file.");

    var uncoded = new byte[checked((int)uncodedSize)];
    using (var compressed = new MemoryStream(data.Slice(codedAt, checked((int)codedSize)).ToArray(), writable: false))
    using (var zlib = new ZLibStream(compressed, CompressionMode.Decompress, leaveOpen: false)) {
      zlib.ReadExactly(uncoded);
      if (zlib.ReadByte() >= 0)
        throw new InvalidDataException("The SGA3 ZCOMPRESS stream expands beyond its stated size.");
    }

    var bits = BinaryPrimitives.ReadUInt16LittleEndian(data[28..]);
    var used = BinaryPrimitives.ReadUInt32LittleEndian(data[46..]);
    var paletteEntries = bits <= 8 ? used == 0 ? 1u << bits : used : 0;
    var paletteBytes = checked((int)paletteEntries * 4);
    var maskBytes = originalCompression == 3 ? 12 : 0;
    var pixelOffsetBytes = checked(paletteBytes + maskBytes);
    if (pixelOffsetBytes > uncoded.Length)
      throw new InvalidDataException("The SGA3 thumbnail color table is larger than the decompressed payload.");

    var normalized = new byte[checked(_BmpFileHeaderSize + _BmpInfoHeaderSize + uncoded.Length)];
    data[..(_BmpFileHeaderSize + _BmpInfoHeaderSize)].CopyTo(normalized);
    BinaryPrimitives.WriteUInt32LittleEndian(normalized.AsSpan(2), checked((uint)normalized.Length));
    BinaryPrimitives.WriteUInt32LittleEndian(normalized.AsSpan(10), checked((uint)(_BmpFileHeaderSize + _BmpInfoHeaderSize + pixelOffsetBytes)));
    BinaryPrimitives.WriteUInt32LittleEndian(normalized.AsSpan(30), originalCompression);
    BinaryPrimitives.WriteUInt32LittleEndian(normalized.AsSpan(34), checked((uint)(uncoded.Length - pixelOffsetBytes)));
    uncoded.CopyTo(normalized.AsSpan(_BmpFileHeaderSize + _BmpInfoHeaderSize));

    encodedLength = checked(codedAt + (int)codedSize);
    return BmpFile.ToRawImage(BmpReader.FromSpan(normalized));
  }

  private static int _FindNextObject(ReadOnlySpan<byte> data, int start) {
    for (var at = start; at + _ObjectHeaderSize <= data.Length; ++at)
      if (_IsSga3(data, at)
          && BinaryPrimitives.ReadUInt16LittleEndian(data[(at + 4)..]) == 4
          && _IsKnownKind(BinaryPrimitives.ReadUInt16LittleEndian(data[(at + 8)..])))
        return at;

    return -1;
  }

  private static bool _IsKnownKind(ushort kind) => kind is 1 or 2 or 4 or 5 or 6;

  private static bool _IsSga3(ReadOnlySpan<byte> data, int at)
    => data.Length >= at + 4
       && data[at] == (byte)'S' && data[at + 1] == (byte)'G'
       && data[at + 2] == (byte)'A' && data[at + 3] == (byte)'3';
}
