using System;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.VerticalHiresInterlace;

/// <summary>Assembles Vertical Hires Interlace (.vhi) file bytes.</summary>
/// <remarks>
/// Written packed. The encoding alternates a command byte and a count: nought introduces that many
/// literal bytes and one a repeat of the byte after it, with a count of nought meaning 256 in both
/// cases. A literal run therefore costs two bytes of overhead however long it is, which is why
/// literals are gathered rather than emitted one at a time.
/// </remarks>
public static class VerticalHiresInterlaceWriter {

  /// <summary>The longest either command can express, a count of zero standing for 256.</summary>
  private const int _MAX_RUN = 256;

  /// <summary>The shortest repeat worth breaking a run of literals for.</summary>
  private const int _MIN_RUN = 4;

  public static byte[] ToBytes(VerticalHiresInterlaceFile file) {
    var data = file.Data ?? [];
    if (data.Length != VerticalHiresInterlaceFile.UnpackedSize)
      throw new InvalidDataException(
        $"A Vertical Hires Interlace picture is written from {VerticalHiresInterlaceFile.UnpackedSize} unpacked bytes, got {data.Length}.");

    // Where the editor's own loader put the picture.
    var packed = new List<byte> { 0x00, 0x40 };
    var literals = new List<byte>();

    for (var at = 0; at < data.Length;) {
      var value = data[at];
      var run = 1;
      while (run < _MAX_RUN && at + run < data.Length && data[at + run] == value)
        ++run;

      if (run < _MIN_RUN) {
        for (var i = 0; i < run; ++i) {
          literals.Add(value);
          if (literals.Count == _MAX_RUN)
            _FlushLiterals(packed, literals);
        }

        at += run;
        continue;
      }

      _FlushLiterals(packed, literals);
      packed.Add(1);
      packed.Add((byte)(run == _MAX_RUN ? 0 : run));
      packed.Add(value);
      at += run;
    }

    _FlushLiterals(packed, literals);

    // An unpacked file is recognised by its length alone, so a packed one must not happen to have
    // it — and the reader unpacks until the picture is full, so a byte past the end is ignored.
    if (packed.Count == VerticalHiresInterlaceFile.PlainFileSize)
      packed.Add(0);

    return [.. packed];
  }

  private static void _FlushLiterals(List<byte> packed, List<byte> literals) {
    if (literals.Count == 0)
      return;

    packed.Add(0);
    packed.Add((byte)(literals.Count == _MAX_RUN ? 0 : literals.Count));
    packed.AddRange(literals);
    literals.Clear();
  }
}
