using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FileFormat.Cgm;

/// <summary>Splits a binary metafile into its commands.</summary>
public static class CgmReader {

  /// <summary>Class 0 element 1: the command every metafile opens with.</summary>
  public const int ClassDelimiter = 0, BeginMetafile = 1, EndMetafile = 2;

  /// <summary>The parameter length that says a second word states the real one.</summary>
  private const int _LongFormEscape = 31;

  /// <summary>More commands than any of these files holds, and it bounds a bad read.</summary>
  private const int _MaxCommands = 1 << 21;

  public static CgmFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Computer Graphics Metafile not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static CgmFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromBytes(data);
    }

    using var memory = new MemoryStream();
    stream.CopyTo(memory);
    return FromBytes(memory.ToArray());
  }

  public static CgmFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static CgmFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < 4)
      throw new InvalidDataException("A Computer Graphics Metafile is too short to hold one command.");

    var first = BinaryPrimitives.ReadUInt16BigEndian(data);
    if ((first >> 12) != ClassDelimiter || ((first >> 5) & 0x7F) != BeginMetafile)
      throw new InvalidDataException("Not a binary Computer Graphics Metafile: it does not open with BEGIN METAFILE.");

    var commands = new List<CgmCommand>();
    var at = 0;
    var ended = false;

    while (at + 2 <= data.Length && commands.Count < _MaxCommands) {
      var header = BinaryPrimitives.ReadUInt16BigEndian(data[at..]);
      at += 2;

      var elementClass = header >> 12;
      var elementId = (header >> 5) & 0x7F;
      var length = header & 0x1F;

      byte[] parameters;
      if (length < _LongFormEscape) {
        if (at + length > data.Length)
          throw new InvalidDataException($"A metafile command of {length} bytes runs past the end of the file.");

        parameters = data.Slice(at, length).ToArray();
        at += length + (length & 1);
      } else
        parameters = _LongForm(data, ref at);

      commands.Add(new(elementClass, elementId, parameters));

      if (elementClass == ClassDelimiter && elementId == EndMetafile) {
        ended = true;
        break;
      }
    }

    // Landing exactly on END METAFILE is what says the lengths have been read as lengths. A stream
    // walked with the wrong idea of where a command ends does not arrive there.
    if (!ended)
      throw new InvalidDataException("A Computer Graphics Metafile ends with END METAFILE and this one does not.");

    return new() { Commands = commands, Name = _NameOf(commands) };
  }

  /// <summary>
  /// Reads a parameter list too long for the header, which the standard splits into partitions.
  /// </summary>
  /// <remarks>
  /// Each partition is a word — the top bit says whether another follows, the other fifteen give
  /// this partition's length — and then that many bytes, padded to a word boundary. The header word
  /// is not repeated for the partitions after the first.
  /// </remarks>
  private static byte[] _LongForm(ReadOnlySpan<byte> data, ref int at) {
    var joined = new List<byte>();
    while (true) {
      if (at + 2 > data.Length)
        throw new InvalidDataException("A metafile command states a long parameter list the file cannot hold.");

      var word = BinaryPrimitives.ReadUInt16BigEndian(data[at..]);
      at += 2;

      var more = (word & 0x8000) != 0;
      var partition = word & 0x7FFF;
      if (at + partition > data.Length)
        throw new InvalidDataException($"A metafile parameter partition of {partition} bytes runs past the end of the file.");

      for (var i = 0; i < partition; ++i)
        joined.Add(data[at + i]);
      at += partition + (partition & 1);

      if (!more)
        return joined.ToArray();
    }
  }

  private static string? _NameOf(List<CgmCommand> commands) {
    foreach (var command in commands) {
      if (command.ElementClass != ClassDelimiter || command.ElementId != BeginMetafile)
        continue;

      var parameters = command.Parameters;
      if (parameters.Length < 1)
        return null;

      var length = Math.Min(parameters[0], parameters.Length - 1);
      return Encoding.Latin1.GetString(parameters, 1, length);
    }

    return null;
  }
}
