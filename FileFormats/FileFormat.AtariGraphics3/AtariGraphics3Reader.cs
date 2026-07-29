using System;
using System.IO;

namespace FileFormat.AtariGraphics3;

/// <summary>Reads Atari 8-bit Graphics 3 screens from bytes, streams, or file paths.</summary>
public static class AtariGraphics3Reader {

  public static AtariGraphics3File FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Graphics 3 screen not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static AtariGraphics3File FromStream(Stream stream) {
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

  public static AtariGraphics3File FromSpan(ReadOnlySpan<byte> data) {
    // The two variants differ only by whether four colour bytes follow the screen.
    var stored = data.Length switch {
      AtariGraphics3File.ColoredFileSize => true,
      AtariGraphics3File.PlainFileSize => false,
      _ => throw new InvalidDataException(
        $"A Graphics 3 screen is {AtariGraphics3File.PlainFileSize} or {AtariGraphics3File.ColoredFileSize} bytes, got {data.Length}.")
    };

    var screen = new byte[AtariGraphics3File.ScreenDataSize];
    data[..AtariGraphics3File.ScreenDataSize].CopyTo(screen);

    var colors = new byte[AtariGraphics3File.ColorCount];
    if (stored)
      data.Slice(AtariGraphics3File.ScreenDataSize, AtariGraphics3File.ColorCount).CopyTo(colors);
    else
      AtariGraphics3File.DefaultColors.CopyTo(colors);

    return new() { ScreenData = screen, Colors = colors, HasStoredColors = stored };
  }

  public static AtariGraphics3File FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
