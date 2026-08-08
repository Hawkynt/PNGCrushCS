using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;

namespace FileFormat.PalmImageViewer;

/// <summary>Assembles Palm ImageViewer databases from a <see cref="PalmImageViewerFile"/>.</summary>
public static class PalmImageViewerWriter {

  /// <summary>The Palm database header, before the list of records.</summary>
  private const int _DatabaseHeaderSize = 78;

  /// <summary>Each entry in the record list: a four-byte offset then attributes and an identifier.</summary>
  private const int _RecordEntrySize = 8;

  /// <summary>The picture record's own header, ending with the two sizes.</summary>
  private const int _RecordHeaderSize = 58;

  /// <summary>Where the width sits inside the picture record.</summary>
  private const int _WidthOffset = 54;

  /// <summary>Bytes a database or record name may occupy, the last of which is the terminator.</summary>
  private const int _NameLength = 32;

  private static ReadOnlySpan<byte> _Type => "vIMG"u8;
  private static ReadOnlySpan<byte> _Creator => "View"u8;

  public static byte[] ToBytes(PalmImageViewerFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var rows = file.PixelData ?? [];
    var body = Compress(rows);

    // Only one of the two is written, and only when it is the smaller: the flag says which, so a
    // picture the coding would enlarge — a photograph, where no two neighbours agree — costs nothing.
    var compressed = body.Length < rows.Length;
    if (!compressed)
      body = rows;

    var data = new byte[_DatabaseHeaderSize + _RecordEntrySize + _RecordHeaderSize + body.Length];
    var name = Encoding.ASCII.GetBytes(file.Name ?? string.Empty);

    _WriteName(data.AsSpan(0, _NameLength), name);
    _Type.CopyTo(data.AsSpan(60));
    _Creator.CopyTo(data.AsSpan(64));
    BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(76), 1);

    var record = _DatabaseHeaderSize + _RecordEntrySize;
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(_DatabaseHeaderSize), (uint)record);

    _WriteName(data.AsSpan(record, _NameLength), name);
    data[record + 32] = (byte)(compressed ? 1 : 0);
    BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(record + _WidthOffset), (ushort)file.Width);
    BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(record + _WidthOffset + 2), (ushort)file.Height);

    body.CopyTo(data.AsSpan(record + _RecordHeaderSize));

    return data;
  }

  private static void _WriteName(Span<byte> target, ReadOnlySpan<byte> name) {
    // One byte short of the field, so whatever is written is terminated.
    var length = Math.Min(name.Length, target.Length - 1);
    name[..length].CopyTo(target);
  }

  /// <summary>
  /// Applies the record's run-length coding, the exact inverse of the reader's undoing of it.
  /// </summary>
  /// <remarks>
  /// A run pays two bytes and a literal one apiece, so two equal bytes are worth naming only when
  /// they are not already inside a run of literals that would have to be broken to do it — which is
  /// why three is the shortest run coded rather than two. Both counts are stored one less than they
  /// stand for, so 128 is the longest either can be.
  /// </remarks>
  public static byte[] Compress(ReadOnlySpan<byte> rows) {
    var output = new List<byte>(rows.Length);
    var literals = 0;

    for (var at = 0; at < rows.Length;) {
      var run = 1;
      while (run < 128 && at + run < rows.Length && rows[at + run] == rows[at])
        ++run;

      if (run >= 3) {
        _FlushLiterals(output, rows, at, ref literals);
        output.Add((byte)(0x80 + run - 1));
        output.Add(rows[at]);
        at += run;
        continue;
      }

      ++literals;
      ++at;

      if (literals == 128)
        _FlushLiterals(output, rows, at, ref literals);
    }

    _FlushLiterals(output, rows, rows.Length, ref literals);

    return [.. output];
  }

  /// <summary>Emits the literals gathered so far, which end at <paramref name="end"/>.</summary>
  private static void _FlushLiterals(List<byte> output, ReadOnlySpan<byte> rows, int end, ref int literals) {
    if (literals == 0)
      return;

    output.Add((byte)(literals - 1));
    for (var i = end - literals; i < end; ++i)
      output.Add(rows[i]);

    literals = 0;
  }
}
