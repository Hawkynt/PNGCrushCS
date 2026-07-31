using System;
using System.IO;

namespace FileFormat.Picasso;

/// <summary>Reads Picasso pictures from bytes, streams, or file paths.</summary>
public static class PicassoReader {

  /// <summary>Reads a picture, taking its per-cell colours from the .pic1 file beside it.</summary>
  public static PicassoFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Picture not found.", file.FullName);

    var colors = _ReadCompanion(file)
      ?? throw new InvalidDataException($"No colours beside {file.Name}; a Picasso bitmap holds only two of them.");

    var picture = FromSpan(File.ReadAllBytes(file.FullName)) with { Colors = colors };

    // The multicolour bit has to be set in every cell, or this is a picture of another program.
    for (var cell = 0; cell < PicassoFile.Columns * (PicassoFile.Size / PicassoFile.CellHeight); ++cell)
      if ((colors[PicassoFile.ColorsOffset + cell] & 8) == 0)
        throw new InvalidDataException($"Cell {cell} is not multicoloured, so this is not a Picasso picture.");

    return picture;
  }

  private static byte[]? _ReadCompanion(FileInfo file) {
    var directory = file.DirectoryName;
    if (directory == null)
      return null;

    var stem = Path.GetFileNameWithoutExtension(file.Name);
    foreach (var extension in (string[])[".pic1", ".PIC1"]) {
      var candidate = new FileInfo(Path.Combine(directory, stem + extension));
      if (candidate.Exists && candidate.Length == PicassoFile.ColorFileSize)
        return File.ReadAllBytes(candidate.FullName);
    }

    return null;
  }

  public static PicassoFile FromStream(Stream stream) {
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

  public static PicassoFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length != PicassoFile.FileSize || data[0] != 0 || data[1] != 13
        || data[3876] != 150 || data[3877] != 23 || data[3879] != 140)
      throw new InvalidDataException("Not a Picasso picture.");

    return new() { Data = data.ToArray(), Colors = [] };
  }

  public static PicassoFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
