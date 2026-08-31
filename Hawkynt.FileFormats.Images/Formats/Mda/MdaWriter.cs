using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace FileFormat.Mda;

/// <summary>Writes MicroDesign Area (.MDA) monochrome bitmap files.</summary>
public static class MdaWriter {

  private const int _PrefixSize = MdaFile.StampSize + 4;

  public static byte[] ToBytes(MdaFile file) {
    MdaFile.Validate(file, nameof(file));

    using var output = new MemoryStream();
    output.Write(_CreateHeader(file));

    if (file.Version == MdaVersion.Area2)
      _WriteArea2(file.RasterData, output);
    else
      _WriteArea3(file, output);

    return output.ToArray();
  }

  public static void ToStream(MdaFile file, Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    stream.Write(ToBytes(file));
  }

  public static void ToFile(MdaFile file, FileInfo destination) {
    ArgumentNullException.ThrowIfNull(destination);
    File.WriteAllBytes(destination.FullName, ToBytes(file));
  }

  private static byte[] _CreateHeader(MdaFile file) {
    var header = new byte[_PrefixSize];
    ".MDA"u8.CopyTo(header);
    "MicroDesignPCW"u8.CopyTo(header.AsSpan(4));
    if (file.Version == MdaVersion.Area2)
      "v1.00"u8.CopyTo(header.AsSpan(18));
    else
      "v1.30"u8.CopyTo(header.AsSpan(18));

    header[23] = 13;
    header[24] = 10;
    Encoding.ASCII.GetBytes(file.SerialNumber.AsSpan(), header.AsSpan(25, MdaFile.SerialNumberLength));
    header[32] = 13;
    header[33] = 10;

    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(128, 2), checked((ushort)file.Height));
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(130, 2), checked((ushort)MdaFile.GetRowStride(file.Width)));
    return header;
  }

  private static void _WriteArea2(ReadOnlySpan<byte> raster, Stream output) {
    var index = 0;
    while (index < raster.Length) {
      var value = raster[index];
      if (value is not (0x00 or 0xFF)) {
        output.WriteByte(value);
        ++index;
        continue;
      }

      var count = 1;
      while (count < 256 && index + count < raster.Length && raster[index + count] == value)
        ++count;

      output.WriteByte(value);
      output.WriteByte(count == 256 ? (byte)0 : (byte)count);
      index += count;
    }
  }

  private static void _WriteArea3(MdaFile file, Stream output) {
    var stride = MdaFile.GetRowStride(file.Width);
    for (var y = 0; y < file.Height; ++y) {
      var row = file.RasterData.AsSpan(y * stride, stride);
      if (_IsAllSame(row)) {
        output.WriteByte(0x00);
        output.WriteByte(row[0]);
        continue;
      }

      var data = _EncodeBlocks(row);
      if (y == 0) {
        output.WriteByte(0x01);
        output.Write(data);
        continue;
      }

      var previous = file.RasterData.AsSpan((y - 1) * stride, stride);
      var difference = new byte[stride];
      for (var x = 0; x < stride; ++x)
        difference[x] = (byte)(row[x] ^ previous[x]);

      var differenceData = _EncodeBlocks(difference);
      if (differenceData.Length < data.Length) {
        output.WriteByte(0x02);
        output.Write(differenceData);
      } else {
        output.WriteByte(0x01);
        output.Write(data);
      }
    }
  }

  private static byte[] _EncodeBlocks(ReadOnlySpan<byte> data) {
    using var output = new MemoryStream();
    var index = 0;

    while (index < data.Length) {
      var runLength = _CountRun(data, index);
      if (runLength >= 3) {
        output.WriteByte(unchecked((byte)(1 - runLength)));
        output.WriteByte(data[index]);
        index += runLength;
        continue;
      }

      var literalStart = index;
      index += runLength;
      while (index < data.Length && index - literalStart < 128) {
        runLength = _CountRun(data, index);
        if (runLength >= 3)
          break;

        if (index - literalStart + runLength > 128) {
          index = literalStart + 128;
          break;
        }

        index += runLength;
      }

      var literalLength = index - literalStart;
      output.WriteByte((byte)(literalLength - 1));
      output.Write(data.Slice(literalStart, literalLength));
    }

    return output.ToArray();
  }

  private static int _CountRun(ReadOnlySpan<byte> data, int start) {
    var maximum = Math.Min(128, data.Length - start);
    var count = 1;
    while (count < maximum && data[start + count] == data[start])
      ++count;

    return count;
  }

  private static bool _IsAllSame(ReadOnlySpan<byte> data) {
    var first = data[0];
    for (var i = 1; i < data.Length; ++i)
      if (data[i] != first)
        return false;

    return true;
  }
}
