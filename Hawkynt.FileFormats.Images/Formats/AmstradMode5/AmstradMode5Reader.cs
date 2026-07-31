using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.AmstradMode5;

/// <summary>Reads Mode 5 pictures from bytes, streams, or file paths.</summary>
public static class AmstradMode5Reader {

  /// <summary>
  /// Reads a picture, taking its bitmap from the .gfx file beside it.
  /// </summary>
  /// <remarks>
  /// The companion is not optional: the .cm5 holds only colours, so without the bitmap there is
  /// nothing to colour.
  /// </remarks>
  public static AmstradMode5File FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Picture not found.", file.FullName);

    var bitmap = _ReadCompanion(file)
      ?? throw new InvalidDataException($"No bitmap beside {file.Name}; a Mode 5 file holds only colours.");

    return FromSpan(File.ReadAllBytes(file.FullName)) with { Bitmap = bitmap };
  }

  private static byte[]? _ReadCompanion(FileInfo file) {
    var directory = file.DirectoryName;
    if (directory == null)
      return null;

    var stem = Path.GetFileNameWithoutExtension(file.Name);
    foreach (var extension in (string[])[".gfx", ".GFX"]) {
      var candidate = new FileInfo(Path.Combine(directory, stem + extension));
      if (candidate.Exists && candidate.Length == AmstradMode5File.BitmapFileSize)
        return File.ReadAllBytes(candidate.FullName);
    }

    return null;
  }

  public static AmstradMode5File FromStream(Stream stream) {
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

  public static AmstradMode5File FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length != AmstradMode5File.FileSize)
      throw new InvalidDataException($"A Mode 5 file is {AmstradMode5File.FileSize} bytes, got {data.Length}.");

    // Every stored colour has to be one the hardware can make, which is the whole of the check.
    foreach (var c in data)
      if (c < AmstradGraphics.ColorBias || c >= AmstradGraphics.ColorBias + AmstradGraphics.ColorCount)
        throw new InvalidDataException("Not a Mode 5 picture: a colour the Gate Array cannot make.");

    return new() { Colors = data.ToArray(), Bitmap = [] };
  }

  public static AmstradMode5File FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
