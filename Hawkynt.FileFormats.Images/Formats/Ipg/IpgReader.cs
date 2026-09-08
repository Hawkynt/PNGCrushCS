using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FileFormat.Bmp;
using FileFormat.Core;
using FileFormat.Gif;
using FileFormat.Jpeg;
using FileFormat.Tga;

namespace FileFormat.Ipg;

/// <summary>Reads the IPK01 and IPK03 generations of Mindjongg IPG tilesets.</summary>
public static class IpgReader {

  private static ReadOnlySpan<byte> _V1Signature => [0x05, 0x49, 0x50, 0x4B, 0x30, 0x31, 0x01, 0x00, 0x00, 0x00];
  private static ReadOnlySpan<byte> _V3Signature => [0x05, 0x00, 0x00, 0x00, 0x49, 0x00, 0x50, 0x00, 0x4B, 0x00, 0x30, 0x00, 0x33, 0x00, 0x03, 0x00, 0x00, 0x00];

  public static bool? MatchesSignature(ReadOnlySpan<byte> header) {
    if (header.Length == 0)
      return null;
    if (header[0] != 0x05)
      return false;
    if (header.Length >= _V1Signature.Length && header[.._V1Signature.Length].SequenceEqual(_V1Signature))
      return true;
    if (header.Length < _V3Signature.Length)
      return null;
    return header[.._V3Signature.Length].SequenceEqual(_V3Signature);
  }

  public static IpgFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length >= _V1Signature.Length && data[.._V1Signature.Length].SequenceEqual(_V1Signature))
      return new IpgFile { Version = 1, Images = _ReadV1(data) };
    if (data.Length >= _V3Signature.Length && data[.._V3Signature.Length].SequenceEqual(_V3Signature))
      return new IpgFile { Version = 3, Images = _ReadV3(data) };

    throw new InvalidDataException("The data is neither a Mindjongg IPK01 nor IPK03 tileset.");
  }

  private static List<RawImage> _ReadV1(ReadOnlySpan<byte> data) {
    var pos = 10;
    for (var i = 0; i < 2; ++i) {
      if (pos >= data.Length)
        throw new InvalidDataException("The IPK01 string header is truncated.");
      var length = data[pos++];
      if (length > data.Length - pos)
        throw new InvalidDataException("An IPK01 header string runs past the file.");
      pos += length;
    }

    var indexAt = checked(pos + 72);
    if (indexAt + 12 > data.Length)
      throw new InvalidDataException("The IPK01 image index is truncated.");

    var firstImageAt = BinaryPrimitives.ReadUInt32LittleEndian(data[indexAt..]);
    var indexLength = (long)firstImageAt - indexAt;
    if (indexLength is < 72 or > 288 || indexLength % 36 != 0)
      throw new InvalidDataException($"The IPK01 image index has implausible length {indexLength}.");

    var itemCount = checked((int)(indexLength / 12));
    var indexEnd = checked(indexAt + itemCount * 12);
    if (indexEnd > data.Length)
      throw new InvalidDataException("The IPK01 image index runs past the file.");

    var images = new List<RawImage>();
    pos = indexAt;
    for (var i = 0; i < itemCount; ++i) {
      var imageAt = BinaryPrimitives.ReadUInt32LittleEndian(data[pos..]);
      var imageLength = BinaryPrimitives.ReadUInt32LittleEndian(data[(pos + 4)..]);
      var imageType = BinaryPrimitives.ReadUInt32LittleEndian(data[(pos + 8)..]);
      pos += 12;

      if (imageAt == 0 || imageLength == 0 || imageType == uint.MaxValue)
        continue;

      images.Add(_Decode(data, imageAt, imageLength, imageType, indexEnd));
    }

    if (images.Count == 0)
      throw new InvalidDataException("The IPK01 tileset has no embedded image.");
    return images;
  }

  private static List<RawImage> _ReadV3(ReadOnlySpan<byte> data) {
    var pos = 30;
    for (var i = 0; i < 4; ++i) {
      if (pos + 4 > data.Length)
        throw new InvalidDataException("The IPK03 string header is truncated.");
      var characters = BinaryPrimitives.ReadUInt32LittleEndian(data[pos..]);
      pos += 4;
      var bytes = checked((long)characters * 2);
      if (bytes > data.Length - pos)
        throw new InvalidDataException("An IPK03 UTF-16 header string runs past the file.");
      pos = checked(pos + (int)bytes);
    }

    var fixedHeaderAt = pos;
    var imageDataFloor = checked(fixedHeaderAt + 140);
    if (imageDataFloor > data.Length)
      throw new InvalidDataException("The IPK03 fixed header is truncated.");

    int[] recordOffsets = [40, 68, 80];
    var images = new List<RawImage>(recordOffsets.Length);
    foreach (var relative in recordOffsets) {
      var recordAt = fixedHeaderAt + relative;
      if (recordAt + 12 > data.Length)
        throw new InvalidDataException("An IPK03 image record is truncated.");

      var imageAt = BinaryPrimitives.ReadUInt32LittleEndian(data[recordAt..]);
      var imageType = BinaryPrimitives.ReadUInt32LittleEndian(data[(recordAt + 4)..]);
      var imageLength = BinaryPrimitives.ReadUInt32LittleEndian(data[(recordAt + 8)..]);
      if (imageAt == 0 || imageLength == 0 || imageType == uint.MaxValue)
        continue;

      images.Add(_Decode(data, imageAt, imageLength, imageType, imageDataFloor));
    }

    if (images.Count == 0)
      throw new InvalidDataException("The IPK03 tileset has no embedded image.");
    return images;
  }

  private static RawImage _Decode(ReadOnlySpan<byte> data, uint imageAt, uint imageLength, uint imageType, int minimumOffset) {
    var start = checked((int)imageAt);
    var length = checked((int)imageLength);
    if (start < minimumOffset || length > data.Length - start)
      throw new InvalidDataException("An IPG image entry points outside its image-data area.");

    var payload = data.Slice(start, length);
    return imageType switch {
      0 => BmpFile.ToRawImage(BmpReader.FromSpan(payload)),
      1 => GifFile.ToRawImage(GifReader.FromSpan(payload)),
      2 => JpegFile.ToRawImage(JpegReader.FromSpan(payload)),
      3 => TgaFile.ToRawImage(TgaReader.FromSpan(payload)),
      _ => throw new InvalidDataException($"Unsupported Mindjongg embedded image type {imageType}."),
    };
  }
}
