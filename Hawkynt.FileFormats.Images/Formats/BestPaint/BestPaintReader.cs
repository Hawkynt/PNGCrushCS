using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.BestPaint;

/// <summary>Reads Best Paint pictures from bytes, streams, or file paths.</summary>
public static class BestPaintReader {

  public static BestPaintFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static BestPaintFile FromStream(Stream stream) {
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

  public static BestPaintFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length != BestPaintFile.FileSize || data[0] != 0 || data[1] != 17)
      throw new InvalidDataException("Not a Best Paint picture.");

    // Only the lower half of the palette can be a foreground, so a higher ink is not this format.
    for (var cell = 0; cell < BestPaintFile.Rows * 20; ++cell)
      if ((data[BestPaintFile.ColorsOffset + cell] & 15) >= Vic20Graphics.ForegroundColorCount)
        throw new InvalidDataException($"Cell {cell} names an ink the VIC-I cannot draw.");

    return new() { Data = data.ToArray() };
  }

  public static BestPaintFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
