using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using FileFormat.Core;

namespace FileFormat.PalmImageViewer;

/// <summary>Reads Palm ImageViewer pictures from bytes, streams, or file paths.</summary>
public static class PalmImageViewerReader {

  /// <summary>The Palm database header, before the list of records.</summary>
  private const int _DatabaseHeaderSize = 78;

  /// <summary>Each entry in the record list: a four-byte offset then attributes and an identifier.</summary>
  private const int _RecordEntrySize = 8;

  /// <summary>The picture record's own header, ending with the two sizes.</summary>
  private const int _RecordHeaderSize = 58;

  /// <summary>Where the width sits inside the picture record.</summary>
  private const int _WidthOffset = 54;

  /// <summary>Where the depth is stated inside the picture record.</summary>
  private const int _DepthOffset = 33;

  private static ReadOnlySpan<byte> _Type => "vIMG"u8;
  private static ReadOnlySpan<byte> _Creator => "View"u8;

  public static PalmImageViewerFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Palm ImageViewer file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static PalmImageViewerFile FromStream(Stream stream) {
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

  public static PalmImageViewerFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < _DatabaseHeaderSize + _RecordEntrySize)
      throw new InvalidDataException("Data too small for a valid Palm database.");

    if (!data.Slice(60, 4).SequenceEqual(_Type) || !data.Slice(64, 4).SequenceEqual(_Creator))
      throw new InvalidDataException(
        $"Not an ImageViewer database: type '{Encoding.ASCII.GetString(data.Slice(60, 4))}' "
        + $"creator '{Encoding.ASCII.GetString(data.Slice(64, 4))}'.");

    if (BinaryPrimitives.ReadUInt16BigEndian(data[76..]) < 1)
      throw new InvalidDataException("Palm database contains no records.");

    var recordOffset = (int)BinaryPrimitives.ReadUInt32BigEndian(data[_DatabaseHeaderSize..]);
    if (recordOffset < 0 || recordOffset + _RecordHeaderSize > data.Length)
      throw new InvalidDataException("Record offset points beyond the file.");

    var record = data[recordOffset..];

    var nameEnd = 0;
    while (nameEnd < 32 && record[nameEnd] != 0)
      ++nameEnd;

    // The low bit says whether the rows are run-length coded; the byte after it names the depth,
    // and names it rather than counting it — two bits is nought and one bit is 255.
    var isCompressed = (record[32] & 1) != 0;
    var width = BinaryPrimitives.ReadUInt16BigEndian(record[_WidthOffset..]);
    var height = BinaryPrimitives.ReadUInt16BigEndian(record[(_WidthOffset + 2)..]);

    if (width <= 0 || height <= 0)
      throw new InvalidDataException($"Invalid ImageViewer size {width}x{height}.");

    var body = record[_RecordHeaderSize..];
    var rows = isCompressed ? _Decompress(body) : body.ToArray();

    var bitsPerPixel = record[_DepthOffset] switch { 0 => 2, 2 => 4, _ => 1 };
    if (PackedRows.Stride(width, bitsPerPixel) * height > rows.Length)
      throw new InvalidDataException(
        $"An ImageViewer {width}x{height} of {bitsPerPixel} bits needs more than the {rows.Length} bytes "
        + "its record decompressed to.");

    return new() {
      Width = width,
      Height = height,
      BitsPerPixel = bitsPerPixel,
      Name = Encoding.ASCII.GetString(record[..nameEnd]),
      PixelData = rows,
    };
  }

  /// <summary>Undoes the record's run-length coding.</summary>
  /// <remarks>
  /// A byte below 128 counts literal bytes that follow it; from 128 up it counts how many times the
  /// single byte after it repeats. Both counts are one less than the number they stand for, so a
  /// run of one is expressible and a wasted code is not.
  /// </remarks>
  private static byte[] _Decompress(ReadOnlySpan<byte> source) {
    using var output = new MemoryStream();

    for (var at = 0; at < source.Length;) {
      var control = source[at++];

      if (control < 0x80) {
        var count = Math.Min(control + 1, source.Length - at);
        if (count <= 0)
          break;

        output.Write(source.Slice(at, count));
        at += count;
        continue;
      }

      if (at >= source.Length)
        break;

      var repeat = control - 0x80 + 1;
      var value = source[at++];
      for (var i = 0; i < repeat; ++i)
        output.WriteByte(value);
    }

    return output.ToArray();
  }

  public static PalmImageViewerFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
