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

  public static DoodleFile FromStream(Stream stream) {
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

  public static DoodleFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static DoodleFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < DoodleFile.ExpectedFileSize)
      throw new InvalidDataException($"Data too small for a valid Doodle file (expected {DoodleFile.ExpectedFileSize} bytes, got {data.Length}).");

    if (data.Length != DoodleFile.ExpectedFileSize)
      throw new InvalidDataException($"Invalid Doodle file size (expected {DoodleFile.ExpectedFileSize} bytes, got {data.Length}).");

    var offset = 0;

    var loadAddress = (ushort)(data[offset] | (data[offset + 1] << 8));
    offset += DoodleFile.LoadAddressSize;

    var bitmapData = new byte[DoodleFile.BitmapDataSize];
    data.AsSpan(offset, DoodleFile.BitmapDataSize).CopyTo(bitmapData.AsSpan(0));
    offset += DoodleFile.BitmapDataSize;

    var screenRam = new byte[DoodleFile.ScreenRamSize];
    data.AsSpan(offset, DoodleFile.ScreenRamSize).CopyTo(screenRam.AsSpan(0));

    return new() {
      LoadAddress = loadAddress,
      BitmapData = bitmapData,
      ScreenRam = screenRam,
    };
  }
}
