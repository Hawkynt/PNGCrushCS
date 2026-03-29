using System;
using System.Buffers.Binary;
using System.Text;

namespace FileFormat.Fsh;

/// <summary>Assembles FSH file bytes from entries.</summary>
public static class FshWriter {

  private const int _FILE_HEADER_SIZE = 16;
  private const int _DIRECTORY_ENTRY_SIZE = 8;
  private const int _RECORD_HEADER_SIZE = 16;
  private const int _PALETTE_SIZE = 1024;

  public static byte[] ToBytes(FshFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var entryCount = file.Entries.Count;
    var directorySize = entryCount * _DIRECTORY_ENTRY_SIZE;

    // Calculate total size
    var dataOffset = _FILE_HEADER_SIZE + directorySize;
    var totalSize = dataOffset;

    for (var i = 0; i < entryCount; ++i) {
      totalSize += _RECORD_HEADER_SIZE;
      if (file.Entries[i].RecordCode == FshRecordCode.Indexed8)
        totalSize += _PALETTE_SIZE;

      totalSize += file.Entries[i].PixelData.Length;
    }

    var result = new byte[totalSize];
    var span = result.AsSpan();

    // File header
    result[0] = (byte)'S';
    result[1] = (byte)'H';
    result[2] = (byte)'P';
    result[3] = (byte)'I';
    BinaryPrimitives.WriteInt32LittleEndian(span[4..], totalSize);
    BinaryPrimitives.WriteInt32LittleEndian(span[8..], entryCount);

    var dirId = file.DirectoryId ?? "GIMX";
    var dirIdBytes = Encoding.ASCII.GetBytes(dirId.Length >= 4 ? dirId[..4] : dirId.PadRight(4, '\0'));
    dirIdBytes.AsSpan(0, 4).CopyTo(span[12..]);

    // Write directory entries and data
    var currentDataOffset = dataOffset;
    for (var i = 0; i < entryCount; ++i) {
      var entry = file.Entries[i];
      var dirEntryOffset = _FILE_HEADER_SIZE + i * _DIRECTORY_ENTRY_SIZE;

      // Directory entry: 4-char tag + offset
      var tagStr = entry.Tag ?? "\0\0\0\0";
      var tagBytes = Encoding.ASCII.GetBytes(tagStr.Length >= 4 ? tagStr[..4] : tagStr.PadRight(4, '\0'));
      tagBytes.AsSpan(0, 4).CopyTo(span[dirEntryOffset..]);
      BinaryPrimitives.WriteInt32LittleEndian(span[(dirEntryOffset + 4)..], currentDataOffset);

      // Record header
      var entryDataSize = _RECORD_HEADER_SIZE;
      if (entry.RecordCode == FshRecordCode.Indexed8)
        entryDataSize += _PALETTE_SIZE;

      entryDataSize += entry.PixelData.Length;

      var dataSizeIn16 = (entryDataSize + 15) / 16;

      result[currentDataOffset] = (byte)entry.RecordCode;
      result[currentDataOffset + 1] = (byte)(dataSizeIn16 & 0xFF);
      result[currentDataOffset + 2] = (byte)((dataSizeIn16 >> 8) & 0xFF);
      result[currentDataOffset + 3] = (byte)((dataSizeIn16 >> 16) & 0xFF);
      BinaryPrimitives.WriteUInt16LittleEndian(span[(currentDataOffset + 4)..], (ushort)entry.Width);
      BinaryPrimitives.WriteUInt16LittleEndian(span[(currentDataOffset + 6)..], (ushort)entry.Height);
      BinaryPrimitives.WriteUInt16LittleEndian(span[(currentDataOffset + 8)..], (ushort)entry.CenterX);
      BinaryPrimitives.WriteUInt16LittleEndian(span[(currentDataOffset + 10)..], (ushort)entry.CenterY);
      // Bytes 12-15: position/flags (leave as zeros)

      var pixelStart = currentDataOffset + _RECORD_HEADER_SIZE;

      if (entry.RecordCode == FshRecordCode.Indexed8 && entry.Palette != null) {
        var palLen = Math.Min(_PALETTE_SIZE, entry.Palette.Length);
        entry.Palette.AsSpan(0, palLen).CopyTo(result.AsSpan(pixelStart));
        pixelStart += _PALETTE_SIZE;
      }

      entry.PixelData.AsSpan(0, entry.PixelData.Length).CopyTo(result.AsSpan(pixelStart));
      currentDataOffset += entryDataSize;
    }

    return result;
  }
}
