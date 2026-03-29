using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FileFormat.Icns;

/// <summary>Reads ICNS files from bytes, streams, or file paths.</summary>
public static class IcnsReader {

  /// <summary>The ICNS magic bytes: "icns" (0x69636E73).</summary>
  private static readonly byte[] _Magic = [(byte)'i', (byte)'c', (byte)'n', (byte)'s'];

  /// <summary>Minimum valid ICNS file size: 8-byte header.</summary>
  private const int MinFileSize = 8;

  public static IcnsFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("ICNS file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static IcnsFile FromStream(Stream stream) {
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

  public static IcnsFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < MinFileSize)
      throw new InvalidDataException("Data too small for a valid ICNS file.");

    if (data[0] != _Magic[0] || data[1] != _Magic[1] || data[2] != _Magic[2] || data[3] != _Magic[3])
      throw new InvalidDataException("Invalid ICNS magic signature.");

    var fileLength = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(4));
    if (fileLength < MinFileSize || fileLength > data.Length)
      throw new InvalidDataException("Invalid ICNS file length field.");

    var entries = new List<IcnsEntry>();
    var offset = 8;
    var end = (int)fileLength;

    while (offset + IcnsEntry.HeaderSize <= end) {
      var osType = Encoding.ASCII.GetString(data, offset, 4);
      var entryLength = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset + 4));

      if (entryLength < IcnsEntry.HeaderSize)
        throw new InvalidDataException($"Invalid entry length {entryLength} for entry '{osType}' at offset {offset}.");

      var dataLength = entryLength - IcnsEntry.HeaderSize;
      if (offset + entryLength > end)
        break;

      var entryData = new byte[dataLength];
      data.AsSpan(offset + IcnsEntry.HeaderSize, dataLength).CopyTo(entryData.AsSpan(0));

      var (width, height) = _GetDimensions(osType, entryData);
      entries.Add(new(osType, entryData, width, height));

      offset += entryLength;
    }

    return new() { Entries = entries };
  }

  /// <summary>Determines the pixel dimensions of an icon entry based on its OSType and optional embedded data.</summary>
  private static (int Width, int Height) _GetDimensions(string osType, byte[] data) => osType switch {
    // Modern PNG-based types
    "ic07" => (128, 128),
    "ic08" => (256, 256),
    "ic09" => (512, 512),
    "ic10" => (1024, 1024),
    "ic11" => (32, 32),
    "ic12" => (64, 64),
    "ic13" => (256, 256),
    "ic14" => (512, 512),

    // Legacy 24-bit RGB types
    "is32" => (16, 16),
    "il32" => (32, 32),
    "ih32" => (48, 48),
    "it32" => (128, 128),

    // 8-bit alpha mask types
    "s8mk" => (16, 16),
    "l8mk" => (32, 32),
    "h8mk" => (48, 48),
    "t8mk" => (128, 128),

    // 1-bit types
    "ICN#" => (32, 32),
    "icm#" => (16, 12),
    "ics#" => (16, 16),

    _ => (0, 0),
  };
}
