using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FileFormat.Fsh;

/// <summary>Reads FSH files from bytes, streams, or file paths.</summary>
public static class FshReader {

  private const int _FILE_HEADER_SIZE = 16;
  private const int _DIRECTORY_ENTRY_SIZE = 8;
  private const int _RECORD_HEADER_SIZE = 16;
  private const int _PALETTE_SIZE = 1024;

  public static FshFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("FSH file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static FshFile FromStream(Stream stream) {
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

  public static FshFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < _FILE_HEADER_SIZE)
      throw new InvalidDataException("Data too small for a valid FSH file.");

    var span = data.AsSpan();

    // Validate magic "SHPI"
    if (span[0] != (byte)'S' || span[1] != (byte)'H' || span[2] != (byte)'P' || span[3] != (byte)'I')
      throw new InvalidDataException("Invalid FSH signature: expected 'SHPI'.");

    var fileSize = BinaryPrimitives.ReadInt32LittleEndian(span[4..]);
    var entryCount = BinaryPrimitives.ReadInt32LittleEndian(span[8..]);
    var directoryId = Encoding.ASCII.GetString(span.Slice(12, 4));

    if (entryCount < 0)
      throw new InvalidDataException($"Invalid FSH entry count: {entryCount}.");

    var directoryEnd = _FILE_HEADER_SIZE + (long)entryCount * _DIRECTORY_ENTRY_SIZE;
    if (data.Length < directoryEnd)
      throw new InvalidDataException("Data too small for the declared FSH directory.");

    var entries = new List<FshEntry>(entryCount);
    for (var i = 0; i < entryCount; ++i) {
      var dirOffset = _FILE_HEADER_SIZE + i * _DIRECTORY_ENTRY_SIZE;
      var tag = Encoding.ASCII.GetString(span.Slice(dirOffset, 4));
      var entryOffset = BinaryPrimitives.ReadInt32LittleEndian(span[(dirOffset + 4)..]);

      if (entryOffset < 0 || entryOffset + _RECORD_HEADER_SIZE > data.Length)
        continue;

      var entry = _ReadEntry(data, entryOffset, tag);
      entries.Add(entry);
    }

    return new FshFile {
      DirectoryId = directoryId,
      Entries = entries,
    };
  }

  private static FshEntry _ReadEntry(byte[] data, int offset, string tag) {
    var span = data.AsSpan();

    var recordCode = span[offset];
    // Bytes 1-3: data size in 16-byte units (24-bit LE)
    var width = BinaryPrimitives.ReadUInt16LittleEndian(span[(offset + 4)..]);
    var height = BinaryPrimitives.ReadUInt16LittleEndian(span[(offset + 6)..]);
    var centerX = BinaryPrimitives.ReadUInt16LittleEndian(span[(offset + 8)..]);
    var centerY = BinaryPrimitives.ReadUInt16LittleEndian(span[(offset + 10)..]);

    var pixelStart = offset + _RECORD_HEADER_SIZE;
    byte[]? palette = null;

    if ((FshRecordCode)recordCode == FshRecordCode.Indexed8) {
      // Palette (1024 bytes BGRA) comes before pixel data
      if (pixelStart + _PALETTE_SIZE > data.Length)
        throw new InvalidDataException("Data too small for FSH indexed palette.");

      palette = new byte[_PALETTE_SIZE];
      data.AsSpan(pixelStart, _PALETTE_SIZE).CopyTo(palette.AsSpan(0));
      pixelStart += _PALETTE_SIZE;
    }

    var pixelDataSize = _CalculatePixelDataSize((FshRecordCode)recordCode, width, height);
    if (pixelStart + pixelDataSize > data.Length)
      pixelDataSize = data.Length - pixelStart;

    if (pixelDataSize < 0)
      pixelDataSize = 0;

    var pixelData = new byte[pixelDataSize];
    if (pixelDataSize > 0)
      data.AsSpan(pixelStart, pixelDataSize).CopyTo(pixelData.AsSpan(0));

    return new FshEntry {
      Tag = tag,
      RecordCode = (FshRecordCode)recordCode,
      Width = width,
      Height = height,
      PixelData = pixelData,
      Palette = palette,
      CenterX = centerX,
      CenterY = centerY,
    };
  }

  internal static int _CalculatePixelDataSize(FshRecordCode code, int width, int height) => code switch {
    FshRecordCode.Argb8888 or FshRecordCode.Argb8888_78 => width * height * 4,
    FshRecordCode.Rgb888 => width * height * 3,
    FshRecordCode.Rgb565 or FshRecordCode.Argb4444 or FshRecordCode.Argb1555 => width * height * 2,
    FshRecordCode.Indexed8 => width * height,
    FshRecordCode.Dxt1 => Math.Max(1, (width + 3) / 4) * Math.Max(1, (height + 3) / 4) * 8,
    FshRecordCode.Dxt3 => Math.Max(1, (width + 3) / 4) * Math.Max(1, (height + 3) / 4) * 16,
    _ => 0,
  };
}
