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

  // LibreOffice's ZCOMPRESS = ('S' | ('D' << 8)) | 0x01000000, stored little-endian.
  private const uint _ZCompress = 0x01004453;
  private const int _ObjectHeaderSize = 11;
  private const int _BmpFileHeaderSize = 14;
  private const int _BmpInfoHeaderSize = 40;
  private const int _CompressedInfoSize = 12;
  private const uint _MaxUncompressedThumbnailBytes = 512 * 1024 * 1024;

  public static SdgFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < _ObjectHeaderSize + _BmpFileHeaderSize + _BmpInfoHeaderSize)
      throw new InvalidDataException("Data is too small to contain an SGA3 gallery object with a bitmap thumbnail.");

    var images = new List<RawImage>();
    for (var at = 0; at + _ObjectHeaderSize + _BmpFileHeaderSize <= data.Length; ++at) {
      if (!_IsSga3(data, at)
          || BinaryPrimitives.ReadUInt16LittleEndian(data[(at + 4)..]) != 4
          || data[at + 10] == 0
          || data[at + 11] != (byte)'B'
          || data[at + 12] != (byte)'M')
        continue;

      try {
        var image = _DecodeBitmap(data[(at + _ObjectHeaderSize)..], out var encodedLength);
        images.Add(image);
        at += Math.Max(0, encodedLength + _ObjectHeaderSize - 1);
      } catch (Exception exception) when (exception is InvalidDataException or ArgumentException or NotSupportedException) {
        // SGA3 may occur inside unrelated payload bytes. Only a structurally valid bitmap counts.
      }
    }

    if (images.Count == 0)
      throw new InvalidDataException("No decodable SGA3 bitmap thumbnail was found in the SDG gallery file.");

    return new SdgFile { Images = images };
  }

  private static RawImage _DecodeBitmap(ReadOnlySpan<byte> data, out int encodedLength) {
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
    if (originalCompression is not (0 or 1 or 2))
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
    if (paletteBytes > uncoded.Length)
      throw new InvalidDataException("The SGA3 thumbnail palette is larger than the decompressed payload.");

    var normalized = new byte[checked(_BmpFileHeaderSize + _BmpInfoHeaderSize + uncoded.Length)];
    data[..(_BmpFileHeaderSize + _BmpInfoHeaderSize)].CopyTo(normalized);
    BinaryPrimitives.WriteUInt32LittleEndian(normalized.AsSpan(2), checked((uint)normalized.Length));
    BinaryPrimitives.WriteUInt32LittleEndian(normalized.AsSpan(10), checked((uint)(_BmpFileHeaderSize + _BmpInfoHeaderSize + paletteBytes)));
    BinaryPrimitives.WriteUInt32LittleEndian(normalized.AsSpan(30), originalCompression);
    BinaryPrimitives.WriteUInt32LittleEndian(normalized.AsSpan(34), checked((uint)(uncoded.Length - paletteBytes)));
    uncoded.CopyTo(normalized.AsSpan(_BmpFileHeaderSize + _BmpInfoHeaderSize));

    encodedLength = checked(codedAt + (int)codedSize);
    return BmpFile.ToRawImage(BmpReader.FromSpan(normalized));
  }

  private static bool _IsSga3(ReadOnlySpan<byte> data, int at)
    => data.Length >= at + 4
       && data[at] == (byte)'S' && data[at + 1] == (byte)'G'
       && data[at + 2] == (byte)'A' && data[at + 3] == (byte)'3';
}
