using System;
using System.IO;

namespace FileFormat.RawGreyscale;

/// <summary>Reads raw greyscale dumps, whose whole content is their pixels.</summary>
public static class RawGreyscaleReader {

  /// <summary>
  /// Reads a dump by name, which is the only thing that says how many channels are in it.
  /// </summary>
  /// <remarks>
  /// <c>.gry</c> and <c>.grey</c> say greyscale, so the length only has to place the shape.
  /// <c>.raw</c> says nothing — the converter writes colour dumps under it too — so a length that a
  /// colour reading would also explain is refused instead of guessed at. See
  /// <see cref="RawGreyscaleFile.SizeOfBareDump"/>.
  /// </remarks>
  public static RawGreyscaleFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Dump not found.", file.FullName);

    var data = File.ReadAllBytes(file.FullName);

    return string.Equals(file.Extension, ".raw", StringComparison.OrdinalIgnoreCase)
      ? _At(RawGreyscaleFile.SizeOfBareDump(data.Length), data)
      : _At(RawGreyscaleFile.SizeOf(data.Length), data);
  }

  private static RawGreyscaleFile _At((int Width, int Height) size, byte[] data)
    => new() { Width = size.Width, Height = size.Height, PixelData = data };

  public static RawGreyscaleFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromSpan(data);
    }

    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return FromSpan(ms.ToArray());
  }

  public static RawGreyscaleFile FromSpan(ReadOnlySpan<byte> data)
    => _At(RawGreyscaleFile.SizeOf(data.Length), data.ToArray());

  public static RawGreyscaleFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
