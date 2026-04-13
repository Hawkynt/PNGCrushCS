using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FileFormat.Pcx;

namespace FileFormat.Dcx;

/// <summary>Reads DCX (multi-page PCX) files from bytes, streams, or file paths.</summary>
public static class DcxReader {

  private const uint _MAGIC = 0x3ADE68B1;
  private const int _MIN_SIZE = 8; // magic (4) + at least one zero-terminator offset (4)
  private const int _MAX_PAGES = 1023;

  public static DcxFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("DCX file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static DcxFile FromStream(Stream stream) {
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

  public static DcxFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < _MIN_SIZE)
      throw new InvalidDataException("Data too small for a valid DCX file.");

    var magic = BinaryPrimitives.ReadUInt32LittleEndian(data);
    if (magic != _MAGIC)
      throw new InvalidDataException("Invalid DCX magic bytes.");

    // Read page offsets until zero or max reached
    var offsets = new List<uint>();
    var pos = 4;
    while (pos + 4 <= data.Length && offsets.Count < _MAX_PAGES) {
      var offset = BinaryPrimitives.ReadUInt32LittleEndian(data[pos..]);
      pos += 4;
      if (offset == 0)
        break;
      offsets.Add(offset);
    }

    // Parse each embedded PCX page
    var pages = new List<PcxFile>(offsets.Count);
    for (var i = 0; i < offsets.Count; ++i) {
      var start = (int)offsets[i];
      var end = i + 1 < offsets.Count ? (int)offsets[i + 1] : data.Length;
      if (start >= data.Length)
        break;
      if (end > data.Length)
        end = data.Length;

      var pageData = new byte[end - start];
      data.Slice(start, pageData.Length).CopyTo(pageData);
      pages.Add(PcxReader.FromBytes(pageData));
    }

    return new DcxFile { Pages = pages };
  }

  public static DcxFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
