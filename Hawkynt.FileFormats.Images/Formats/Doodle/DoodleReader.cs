using System;
using System.IO;

namespace FileFormat.Doodle;

/// <summary>Reads Commodore 64 Doodle hires files from bytes, streams, or file paths.</summary>
public static class DoodleReader {

  public static DoodleFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Doodle file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static DoodleFile FromStream(Stream stream) => FromBytes(StreamBytes.ReadAll(stream));

  public static DoodleFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < DoodleFile.ExpectedFileSize)
      throw new InvalidDataException($"Data too small for a valid Doodle file (expected {DoodleFile.ExpectedFileSize} bytes, got {data.Length}).");

    if (data.Length != DoodleFile.ExpectedFileSize)
      throw new InvalidDataException($"Invalid Doodle file size (expected {DoodleFile.ExpectedFileSize} bytes, got {data.Length}).");

    var loadAddress = (ushort)(data[0] | (data[1] << 8));

    var screenRam = new byte[DoodleFile.ScreenRamSize];
    data.Slice(DoodleFile.ScreenRamOffset, DoodleFile.ScreenRamSize).CopyTo(screenRam.AsSpan(0));

    var bitmapData = new byte[DoodleFile.BitmapDataSize];
    data.Slice(DoodleFile.BitmapOffset, DoodleFile.BitmapDataSize).CopyTo(bitmapData.AsSpan(0));

    return new() {
      LoadAddress = loadAddress,
      BitmapData = bitmapData,
      ScreenRam = screenRam,
    };
    }

  public static DoodleFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
