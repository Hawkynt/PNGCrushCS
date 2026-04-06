using System;
using System.IO;

namespace FileFormat.AmicaPaint;

/// <summary>Reads Commodore 64 Amica Paint (.ami) files from bytes, streams, or file paths.</summary>
public static class AmicaPaintReader {

  public static AmicaPaintFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Amica Paint file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static AmicaPaintFile FromStream(Stream stream) {
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

  public static AmicaPaintFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static AmicaPaintFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < AmicaPaintFile.ExpectedFileSize)
      throw new InvalidDataException($"Data too small for a valid Amica Paint file (expected {AmicaPaintFile.ExpectedFileSize} bytes, got {data.Length}).");

    if (data.Length != AmicaPaintFile.ExpectedFileSize)
      throw new InvalidDataException($"Invalid Amica Paint file size (expected {AmicaPaintFile.ExpectedFileSize} bytes, got {data.Length}).");

    var offset = 0;

    var loadAddress = (ushort)(data[offset] | (data[offset + 1] << 8));
    offset += AmicaPaintFile.LoadAddressSize;

    var bitmapData = new byte[AmicaPaintFile.BitmapDataSize];
    data.AsSpan(offset, AmicaPaintFile.BitmapDataSize).CopyTo(bitmapData.AsSpan(0));
    offset += AmicaPaintFile.BitmapDataSize;

    var screenRam = new byte[AmicaPaintFile.ScreenRamSize];
    data.AsSpan(offset, AmicaPaintFile.ScreenRamSize).CopyTo(screenRam.AsSpan(0));
    offset += AmicaPaintFile.ScreenRamSize;

    var colorRam = new byte[AmicaPaintFile.ColorRamSize];
    data.AsSpan(offset, AmicaPaintFile.ColorRamSize).CopyTo(colorRam.AsSpan(0));
    offset += AmicaPaintFile.ColorRamSize;

    var backgroundColor = data[offset];

    return new() {
      LoadAddress = loadAddress,
      BitmapData = bitmapData,
      ScreenRam = screenRam,
      ColorRam = colorRam,
      BackgroundColor = backgroundColor,
    };
  }
}
