using System;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.UifliEditor;

/// <summary>Assembles UIFLI-editor (.uif) file bytes.</summary>
/// <remarks>
/// The run-length encoding runs backwards — the last byte of the file is the first one read and the
/// picture fills from its end towards its start — so the packer walks the picture backwards and
/// writes what it produces in reverse. The escape byte is named in the header rather than found at
/// the end, which is the one thing this format does forwards.
/// </remarks>
public static class UifliEditorWriter {

  /// <summary>Bytes at the front that are never data: the load address and the escape.</summary>
  private const int _HEADER_SIZE = 3;

  /// <summary>The longest run one count can express, a count of zero standing for 256.</summary>
  private const int _MAX_RUN = 256;

  /// <summary>The shortest run worth writing, a run costing three bytes.</summary>
  private const int _MIN_RUN = 4;

  public static byte[] ToBytes(UifliEditorFile file) {
    var data = file.Data ?? [];
    if (data.Length != UifliEditorFile.UnpackedSize)
      throw new InvalidDataException(
        $"A UIFLI picture is written from {UifliEditorFile.UnpackedSize} unpacked bytes, got {data.Length}.");

    var escape = _LeastUsedByte(data);
    var stream = new List<byte>();

    for (var at = data.Length; at > 0;) {
      var value = data[at - 1];
      var run = 1;
      while (run < _MAX_RUN && at - run - 1 >= 0 && data[at - run - 1] == value)
        ++run;

      // Within a command the bytes are in the order the backwards reader meets them: the escape,
      // then the count, then the value.
      if (run >= _MIN_RUN || value == escape) {
        stream.Add(escape);
        stream.Add((byte)(run == _MAX_RUN ? 0 : run));
        stream.Add(value);
      } else
        for (var i = 0; i < run; ++i)
          stream.Add(value);

      at -= run;
    }

    var packed = new byte[_HEADER_SIZE + stream.Count];

    // Where the editor's own loader put the picture, and the escape it named for itself.
    packed[1] = 0x40;
    packed[2] = escape;
    for (var i = 0; i < stream.Count; ++i)
      packed[^(i + 1)] = stream[i];

    return packed;
  }

  /// <summary>The byte value the picture uses least, which costs least to spend as the escape.</summary>
  private static byte _LeastUsedByte(ReadOnlySpan<byte> data) {
    Span<int> counts = stackalloc int[256];
    foreach (var value in data)
      ++counts[value];

    var best = (byte)0;
    for (var candidate = 1; candidate < counts.Length; ++candidate)
      if (counts[candidate] < counts[best])
        best = (byte)candidate;

    return best;
  }
}
