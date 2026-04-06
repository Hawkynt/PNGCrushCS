using System;
using System.IO;

namespace FileFormat.HiEddi;

/// <summary>Reads HiEddi C64 hires files from bytes, streams, or file paths.</summary>
public static class HiEddiReader {

  public static HiEddiFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("HiEddi file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static HiEddiFile FromStream(Stream stream) {
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

  public static HiEddiFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static HiEddiFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < HiEddiFile.ExpectedFileSize)
      throw new InvalidDataException($"Data too small for a valid HiEddi file (expected {HiEddiFile.ExpectedFileSize} bytes, got {data.Length}).");

    if (data.Length != HiEddiFile.ExpectedFileSize)
      throw new InvalidDataException($"Invalid HiEddi file size (expected {HiEddiFile.ExpectedFileSize} bytes, got {data.Length}).");

    var offset = 0;

    var loadAddress = (ushort)(data[offset] | (data[offset + 1] << 8));
    offset += HiEddiFile.LoadAddressSize;

    var bitmapData = new byte[HiEddiFile.BitmapDataSize];
    data.AsSpan(offset, HiEddiFile.BitmapDataSize).CopyTo(bitmapData.AsSpan(0));
    offset += HiEddiFile.BitmapDataSize;

    var screenRam = new byte[HiEddiFile.ScreenRamSize];
    data.AsSpan(offset, HiEddiFile.ScreenRamSize).CopyTo(screenRam.AsSpan(0));

    return new() {
      LoadAddress = loadAddress,
      BitmapData = bitmapData,
      ScreenRam = screenRam,
    };
  }
}
