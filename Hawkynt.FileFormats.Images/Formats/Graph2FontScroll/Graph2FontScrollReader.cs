using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Graph2Font;

namespace FileFormat.Graph2FontScroll;

/// <summary>Reads Graph2Font vertical scrolls from bytes, streams, or file paths.</summary>
public static class Graph2FontScrollReader {

  /// <summary>Reads a scroll and the projects it names, which must sit beside it.</summary>
  public static Graph2FontScrollFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Scroll not found.", file.FullName);

    var directory = file.DirectoryName
      ?? throw new InvalidDataException("A scroll names files beside it, so it needs a directory to be beside.");

    var frames = new List<byte[]>();
    foreach (var name in _ReadNames(File.ReadAllBytes(file.FullName))) {
      var named = new FileInfo(Path.Combine(directory, name));
      if (!named.Exists)
        throw new FileNotFoundException($"The scroll names {name}, which is not beside it.", named.FullName);

      var project = Graph2FontReader.Unwrap(File.ReadAllBytes(named.FullName));
      Graph2FontReader.Describe(project);
      frames.Add(project);
    }

    if (frames.Count == 0)
      throw new InvalidDataException("A scroll naming no projects is not a picture.");

    return new() { Frames = frames };
  }

  public static Graph2FontScrollFile FromStream(Stream stream) {
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

  /// <summary>
  /// Reads the list, which is all a scroll contains — so from bytes alone there is nothing to
  /// resolve the names against and the result carries no frames.
  /// </summary>
  public static Graph2FontScrollFile FromSpan(ReadOnlySpan<byte> data) {
    _ReadNames(data);

    return new() { Frames = [] };
  }

  /// <summary>
  /// Reads the file names, rejecting anything a name cannot contain.
  /// </summary>
  /// <remarks>
  /// Every byte must be printable and every line must end with a carriage return and a newline
  /// together, which is most of what identifies the format — but the path separators are refused
  /// specifically. A scroll names files beside it, and a name that could climb out of that
  /// directory is not one this program wrote.
  /// </remarks>
  private static List<string> _ReadNames(ReadOnlySpan<byte> data) {
    var names = new List<string>();
    var start = 0;

    for (var at = 0; at < data.Length; ++at) {
      var b = data[at];

      switch (b) {
        case (byte)'\r':
          if (at + 1 >= data.Length || data[at + 1] != '\n')
            throw new InvalidDataException("A scroll's line is not closed.");

          names.Add(System.Text.Encoding.ASCII.GetString(data[start..at]));
          ++at;
          start = at + 1;
          break;

        case (byte)'/':
        case (byte)':':
        case (byte)'\\':
          throw new InvalidDataException("A scroll names a file outside its own directory.");

        default:
          if (b < ' ' || b > '~')
            throw new InvalidDataException("Not a Graph2Font scroll: it is not a list of names.");

          break;
      }
    }

    if (start != data.Length)
      throw new InvalidDataException("A scroll's last line is not closed.");

    return names;
  }

  public static Graph2FontScrollFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
