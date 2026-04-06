using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.Psp;

/// <summary>Reads Paint Shop Pro files from bytes, streams, or file paths.</summary>
public static class PspReader {

  private const int _MAGIC_SIZE = 32;
  private const int _FILE_HEADER_SIZE = _MAGIC_SIZE + 4; // magic + major(2) + minor(2)
  private const int _BLOCK_HEADER_SIZE_V5 = 10; // id(2) + initial length(4) + total length(4)
  private const int _GENERAL_ATTRIBUTES_MIN_SIZE = 27; // width(4)+height(4)+resolution(8)+metric(1)+compression(2)+bitDepth(2)+planeCount(2)+colorCount(4)

  public static PspFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("PSP file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static PspFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromBytes(data);
    }
    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return FromBytes(ms.ToArray());
  }

  public static PspFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static PspFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < _FILE_HEADER_SIZE)
      throw new InvalidDataException("Data too small for a valid PSP file.");

    _ValidateMagic(data);

    var majorVersion = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(_MAGIC_SIZE));
    var minorVersion = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(_MAGIC_SIZE + 2));

    var width = 0;
    var height = 0;
    var bitDepth = 24;
    byte[]? compositePixels = null;

    var offset = _FILE_HEADER_SIZE;
    while (offset + 6 <= data.Length) {
      var blockId = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset));
      var initialLength = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset + 2));

      uint totalLength;
      int dataOffset;
      if (majorVersion >= 5) {
        if (offset + _BLOCK_HEADER_SIZE_V5 > data.Length)
          break;

        totalLength = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset + 6));
        dataOffset = offset + _BLOCK_HEADER_SIZE_V5;
      } else {
        totalLength = initialLength;
        dataOffset = offset + 6;
      }

      if (blockId == PspFile.BlockIdGeneralAttributes)
        _ParseGeneralAttributes(data, dataOffset, (int)initialLength, out width, out height, out bitDepth);
      else if (blockId == PspFile.BlockIdCompositeImage)
        compositePixels = _ParseCompositeImage(data, dataOffset, (int)totalLength - (dataOffset - offset));

      offset += (int)totalLength;
      if (offset <= dataOffset)
        break;
    }

    if (width <= 0 || height <= 0)
      throw new InvalidDataException("PSP file missing General Image Attributes block or invalid dimensions.");

    var expectedPixelBytes = width * height * 3;
    if (compositePixels == null)
      compositePixels = new byte[expectedPixelBytes];
    else if (compositePixels.Length != expectedPixelBytes) {
      var adjusted = new byte[expectedPixelBytes];
      compositePixels.AsSpan(0, Math.Min(compositePixels.Length, expectedPixelBytes)).CopyTo(adjusted.AsSpan(0));
      compositePixels = adjusted;
    }

    return new PspFile {
      Width = width,
      Height = height,
      BitDepth = bitDepth,
      MajorVersion = majorVersion,
      MinorVersion = minorVersion,
      PixelData = compositePixels,
    };
  }

  private static void _ValidateMagic(byte[] data) {
    for (var i = 0; i < PspFile.Magic.Length; ++i)
      if (data[i] != PspFile.Magic[i])
        throw new InvalidDataException("Invalid PSP magic bytes.");
  }

  private static void _ParseGeneralAttributes(byte[] data, int offset, int length, out int width, out int height, out int bitDepth) {
    if (length < _GENERAL_ATTRIBUTES_MIN_SIZE || offset + _GENERAL_ATTRIBUTES_MIN_SIZE > data.Length)
      throw new InvalidDataException("General Image Attributes block too small.");

    var span = data.AsSpan(offset);
    width = BinaryPrimitives.ReadInt32LittleEndian(span);
    height = BinaryPrimitives.ReadInt32LittleEndian(span[4..]);
    // resolution(8 bytes) at offset 8, metric(1) at offset 16, compression(2) at offset 17
    bitDepth = BinaryPrimitives.ReadUInt16LittleEndian(span[19..]);
  }

  private static byte[]? _ParseCompositeImage(byte[] data, int offset, int availableLength) {
    if (availableLength <= 0 || offset >= data.Length)
      return null;

    var copyLen = Math.Min(availableLength, data.Length - offset);
    var result = new byte[copyLen];
    data.AsSpan(offset, copyLen).CopyTo(result.AsSpan(0));
    return result;
  }
}
