using System;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.Anime4Ever;

/// <summary>Assembles an Anime 4ever picture, coding every byte as a literal.</summary>
/// <remarks>
/// The format's references save space and are not needed for a file to be correct: a stream of
/// literals decodes to exactly the same picture. What it does need is the flag machinery around
/// them, which is packed two levels deep — a flag says literal or reference, the flags come eight
/// to a byte, and whether the next eight need a byte at all is itself a flag from a second stream
/// packed the same way.
/// <para/>
/// So a run of literals costs one bit per sixty-four rather than one per eight, and the writer
/// spends nothing on flags it does not need. The flag bytes have to be emitted where the reader
/// asks for them, which is why the commands are laid out first and turned into bytes second.
/// </remarks>
public static class Anime4EverWriter {

  /// <summary>Flags to a byte, in each of the two streams.</summary>
  private const int _FlagsPerByte = 8;

  /// <summary>Flags one byte of the outer stream covers.</summary>
  private const int _FlagsPerOuterByte = _FlagsPerByte * _FlagsPerByte;

  /// <summary>What the address in a destination command is offset by.</summary>
  private const int _AddressBias = 19984 - 128;

  public static byte[] ToBytes(Anime4EverFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var unpacked = file.Unpacked ?? new byte[Anime4EverFile.UnpackedSize];
    var commands = new List<(int Flag, byte[] Bytes)>(unpacked.Length + 2);

    // The stream starts with nowhere to write, so the first command has to name a destination. The
    // address is where the picture sat in the machine's memory, not an offset in the file.
    commands.Add((1, [0, (byte)(_AddressBias & 0xFF), (byte)((_AddressBias >> 8) & 0xFF), unpacked.Length > 0 ? unpacked[0] : (byte)0]));

    for (var i = 1; i < unpacked.Length; ++i)
      commands.Add((0, [unpacked[i]]));

    // A reference of length zero is what ends the stream.
    commands.Add((1, [1, 0]));

    return _Emit(commands);
  }

  /// <summary>Lays the commands out with their flag bytes where the reader looks for them.</summary>
  private static byte[] _Emit(List<(int Flag, byte[] Bytes)> commands) {
    using var output = new MemoryStream();

    for (var at = 0; at < commands.Count; at += _FlagsPerOuterByte) {
      var outer = 0;
      var inner = new int[_FlagsPerByte];

      for (var group = 0; group < _FlagsPerByte; ++group) {
        var bits = 0;
        for (var bit = 0; bit < _FlagsPerByte; ++bit) {
          var index = at + group * _FlagsPerByte + bit;
          bits = (bits << 1) | (index < commands.Count ? commands[index].Flag : 0);
        }

        inner[group] = bits;
        outer = (outer << 1) | (bits != 0 ? 1 : 0);
      }

      output.WriteByte((byte)outer);

      for (var group = 0; group < _FlagsPerByte; ++group) {
        // A group of nothing but literals needs no byte of its own; the outer flag already said so.
        if (inner[group] != 0)
          output.WriteByte((byte)inner[group]);

        for (var bit = 0; bit < _FlagsPerByte; ++bit) {
          var index = at + group * _FlagsPerByte + bit;
          if (index < commands.Count)
            output.Write(commands[index].Bytes);
        }
      }
    }

    return output.ToArray();
  }

  public static void ToFile(Anime4EverFile file, FileInfo target) {
    ArgumentNullException.ThrowIfNull(target);
    File.WriteAllBytes(target.FullName, ToBytes(file));
  }
}
