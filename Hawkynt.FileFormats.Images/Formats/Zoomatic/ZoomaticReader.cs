using System;
using System.IO;

namespace FileFormat.Zoomatic;

/// <summary>Reads Zoomatic (.zom) files from bytes, streams, or file paths.</summary>
public static class ZoomaticReader {

  public static ZoomaticFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Zoomatic file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static ZoomaticFile FromStream(Stream stream) => FromBytes(StreamBytes.ReadAll(stream));

  public static ZoomaticFile FromSpan(ReadOnlySpan<byte> data) {
    var unpacked = ZoomaticCompression.Unpack(data, ZoomaticFile.UnpackedSize);

    // Little-endian, and read from the file rather than from the stream: the depacker stops before
    // these two bytes, which is exactly why they can hold the address.
    var loadAddress = (ushort)(data[0] | (data[1] << 8));

    var bitmapData = new byte[ZoomaticFile.BitmapDataSize];
    Array.Copy(unpacked, ZoomaticFile.BitmapOffset, bitmapData, 0, ZoomaticFile.BitmapDataSize);

    var screenData = new byte[ZoomaticFile.ScreenDataSize];
    Array.Copy(unpacked, ZoomaticFile.ScreenOffset, screenData, 0, ZoomaticFile.ScreenDataSize);

    var colorData = new byte[ZoomaticFile.ColorDataSize];
    Array.Copy(unpacked, ZoomaticFile.ColorOffset, colorData, 0, ZoomaticFile.ColorDataSize);

    return new() {
      LoadAddress = loadAddress,
      BitmapData = bitmapData,
      ScreenData = screenData,
      ColorData = colorData,
      BackgroundColor = unpacked[ZoomaticFile.BackgroundOffset],
    };
  }

  public static ZoomaticFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
