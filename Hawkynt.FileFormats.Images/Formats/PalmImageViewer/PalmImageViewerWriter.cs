using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;

namespace FileFormat.PalmImageViewer;

/// <summary>Assembles Palm ImageViewer databases from a <see cref="PalmImageViewerFile"/>.</summary>
/// <remarks>
/// One thing here is deliberately not written the way the other tool would like. Its reader refuses
/// any file shorter than a row of the picture plus 257 bytes, which compares an unpacked row against
/// a packed file and so turns down anything that compressed well. It turns down its own writer's
/// output for it: a flat 320 by 200 picture written by that tool is 270 bytes, its size check wants
/// 297, and it reports insufficient image data for a file it made itself. Padding ours out to suit
/// that would cost every picture the compression the format exists to give it.
/// </remarks>
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

  /// <summary>Where the depth is stated inside the picture record.</summary>
  private const int _DepthOffset = 33;

  /// <summary>Where the two anchors sit, the pair a picture that is not anchored fills with ones.</summary>
  private const int _AnchorOffset = 50;

  /// <summary>
  /// The three bytes that follow a record's attributes: the identifier every ImageViewer record
  /// carries, counting up from the first.
  /// </summary>
  /// <remarks>
  /// A Palm record's identifier is nominally the database's own business, and this reader has never
  /// looked at it. It is not free, though: the reader every other tool uses compares it against this
  /// exact value and calls the file corrupt when it differs, so a record identified any other way is
  /// one only we can open.
  /// </remarks>
  private static ReadOnlySpan<byte> _RecordIdentifier => [0x6F, 0x80, 0x00];

  /// <summary>The attributes byte an ImageViewer record carries.</summary>
  private const byte _RecordAttributes = 0x40;

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
    data[_DatabaseHeaderSize + 4] = _RecordAttributes;
    _RecordIdentifier.CopyTo(data.AsSpan(_DatabaseHeaderSize + 5));

    _WriteName(data.AsSpan(record, _NameLength), name);
    data[record + 32] = (byte)(compressed ? 1 : 0);
    data[record + _DepthOffset] = DepthByte(file.BitsPerPixel);

    // A picture with no anchor says so with ones rather than with zeroes, which are a corner.
    BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(record + _AnchorOffset), ushort.MaxValue);
    BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(record + _AnchorOffset + 2), ushort.MaxValue);

    BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(record + _WidthOffset), (ushort)file.Width);
    BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(record + _WidthOffset + 2), (ushort)file.Height);

    body.CopyTo(data.AsSpan(record + _RecordHeaderSize));

    return data;
  }

  /// <summary>How a depth is named in the record, which is not by the number of bits.</summary>
  /// <remarks>
  /// Two bits is written as nought and one bit as 255, so the value is a name rather than a count
  /// and the obvious reading of it — a zero meaning "unset" — is the two-bit form.
  /// </remarks>
  public static byte DepthByte(int bitsPerPixel) => bitsPerPixel switch {
    4 => 0x02,
    2 => 0x00,
    _ => 0xFF,
  };

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
