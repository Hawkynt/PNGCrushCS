using System;
using System.IO;

namespace FileFormat.LogoSys;

/// <summary>Reads Windows 95/98 boot logo (logo.sys) raw pixel dumps from bytes, streams, or file paths.</summary>
public static class LogoSysReader {

  public static LogoSysFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Logo.sys file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static LogoSysFile FromStream(Stream stream) {
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

  public static LogoSysFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length != LogoSysFile.FileSize)
      throw new InvalidDataException($"Invalid logo.sys data size: expected exactly {LogoSysFile.FileSize} bytes, got {data.Length}.");

    var palette = new byte[LogoSysFile.PaletteSize];
    data.Slice(0, LogoSysFile.PaletteSize).CopyTo(palette);

    var pixelData = new byte[LogoSysFile.PixelDataSize];
    data.Slice(LogoSysFile.PaletteSize, LogoSysFile.PixelDataSize).CopyTo(pixelData);

    return new LogoSysFile {
      Palette = palette,
      PixelData = pixelData,
    };
    }

  public static LogoSysFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length != LogoSysFile.FileSize)
      throw new InvalidDataException($"Invalid logo.sys data size: expected exactly {LogoSysFile.FileSize} bytes, got {data.Length}.");

    var palette = new byte[LogoSysFile.PaletteSize];
    data.AsSpan(0, LogoSysFile.PaletteSize).CopyTo(palette);

    var pixelData = new byte[LogoSysFile.PixelDataSize];
    data.AsSpan(LogoSysFile.PaletteSize, LogoSysFile.PixelDataSize).CopyTo(pixelData);

    return new LogoSysFile {
      Palette = palette,
      PixelData = pixelData,
    };
  }
}
