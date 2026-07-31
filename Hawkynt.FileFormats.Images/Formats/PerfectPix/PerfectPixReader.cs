using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.PerfectPix;

/// <summary>Reads Perfect Pix pictures from bytes, streams, or file paths.</summary>
public static class PerfectPixReader {

  /// <summary>
  /// Reads a picture, taking its two fields from the .odd and .eve files beside it.
  /// </summary>
  /// <remarks>
  /// Neither companion is optional: the head file holds no picture at all, only its size, its mode
  /// and its colours.
  /// </remarks>
  public static PerfectPixFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Picture not found.", file.FullName);

    var head = FromSpan(File.ReadAllBytes(file.FullName));
    var length = head.Height * (head.Width >> 2);

    var odd = _ReadCompanion(file, "odd", length)
      ?? throw new InvalidDataException($"No odd field beside {file.Name}.");
    var even = _ReadCompanion(file, "eve", length)
      ?? throw new InvalidDataException($"No even field beside {file.Name}.");

    return head with { OddField = odd, EvenField = even };
  }

  private static byte[]? _ReadCompanion(FileInfo file, string extension, int length) {
    var directory = file.DirectoryName;
    if (directory == null)
      return null;

    var stem = Path.GetFileNameWithoutExtension(file.Name);
    foreach (var name in (string[])[stem + "." + extension, stem + "." + extension.ToUpperInvariant()]) {
      var candidate = new FileInfo(Path.Combine(directory, name));
      if (candidate.Exists && candidate.Length == length)
        return File.ReadAllBytes(candidate.FullName);
    }

    return null;
  }

  public static PerfectPixFile FromStream(Stream stream) {
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

  public static PerfectPixFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < 10)
      throw new InvalidDataException($"Not a Perfect Pix picture: {data.Length} bytes.");

    var mode = data[0];

    // The two sixteen-colour forms are a fixed size; the striped one grows by its palette count.
    switch (mode) {
      case PerfectPixFile.OffsetMode:
      case PerfectPixFile.WideMode:
        if (data.Length != 22 || data[5] != 1)
          throw new InvalidDataException($"A mode {mode} Perfect Pix picture is 22 bytes, got {data.Length}.");

        for (var i = 0; i < PerfectPixFile.WideColorCount; ++i)
          if (data[6 + i] > 26)
            throw new InvalidDataException($"Colour {i} is not one the firmware names.");

        break;

      case PerfectPixFile.StripedMode:
        if (data.Length != (1 + data[5]) * 5)
          throw new InvalidDataException($"A mode 5 Perfect Pix picture of {data[5]} palettes is not {data.Length} bytes.");

        break;

      default:
        throw new InvalidDataException($"A Perfect Pix picture is mode 3, 4 or 5, not {mode}.");
    }

    var width = data[1] | (data[2] << 8);
    if (width == 0 || width > 384 || (width & 3) != 0)
      throw new InvalidDataException($"A Perfect Pix picture is not {width} pixels across.");

    var height = data[3] | (data[4] << 8);
    if (height == 0 || height > 272)
      throw new InvalidDataException($"A Perfect Pix picture is not {height} rows.");

    return new() { Head = data.ToArray(), OddField = [], EvenField = [], Width = width, Height = height, Mode = mode };
  }

  public static PerfectPixFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
