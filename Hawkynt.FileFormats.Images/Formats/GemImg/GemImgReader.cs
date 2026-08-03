using System;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.GemImg;

/// <summary>Reads GEM IMG files from bytes, streams, or file paths.</summary>
public static class GemImgReader {

  public static GemImgFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("GEM IMG file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static GemImgFile FromStream(Stream stream) {
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

  public static GemImgFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < GemImgHeader.StructSize)
      throw new InvalidDataException("Data too small for a valid GEM IMG file.");

    var span = data;
    var header = GemImgHeader.ReadFrom(span);

    // Nothing was checked before the size was used to allocate, so a file of some other format
    // named .img — one here begins 0x3FFF and states 65535 planes of 65535 rows — came out of the
    // reader as an arithmetic overflow rather than as "this is not one of these".
    if (header.Version is < 1 or > 3)
      throw new InvalidDataException($"A GEM IMG states version 1 to 3; this file states {header.Version}.");
    if (header.NumPlanes is < 1 or > 8)
      throw new InvalidDataException($"A GEM IMG holds one to eight planes; this file states {header.NumPlanes}.");
    if (header.ScanWidth <= 0 || header.ScanLines <= 0 || header.ScanWidth > 32767 || header.ScanLines > 32767)
      throw new InvalidDataException($"A GEM IMG of {header.ScanWidth}x{header.ScanLines} is no size.");

    var dataOffset = header.HeaderLength * 2;
    var width = header.ScanWidth;
    var height = header.ScanLines;
    var numPlanes = header.NumPlanes;
    var patternLength = header.PatternLength;
    var bytesPerRow = (width + 7) / 8;
    var pixelData = new byte[(long)numPlanes * bytesPerRow * height];

    if (dataOffset >= data.Length)
      return new GemImgFile {
        Version = header.Version,
        Width = width,
        Height = height,
        NumPlanes = numPlanes,
        PatternLength = patternLength,
        PixelWidth = header.PixelWidth,
        PixelHeight = header.PixelHeight,
        PixelData = pixelData
      };

    // A scanline is coded as however many items it takes to fill it, and the file holds one coded
    // scanline per plane per row, taken row by row rather than plane by plane. Both were wrong here:
    // each item ended a row, so a row written as two runs was read as two rows, and all of plane
    // nought was read before any of plane one. Worse, the commonest item of the four was not decoded
    // at all — it fell through to "unknown opcode, skip" and advanced a single byte.
    //
    // The four items:
    //   00 00 FF nn   the scanline that follows stands for nn of them
    //   00 nn         the pattern of PatternLength bytes that follows, nn times over
    //   80 nn         nn bytes taken as they stand
    //   nn            a run: the low seven bits count the bytes, the top bit is what they hold
    var scanlines = new List<byte[]>(numPlanes * height);
    var pos = dataOffset;

    while (scanlines.Count < numPlanes * height && pos < data.Length) {
      var repeat = 1;
      if (pos + 3 < data.Length && data[pos] == 0x00 && data[pos + 1] == 0x00 && data[pos + 2] == 0xFF) {
        repeat = Math.Max(1, (int)data[pos + 3]);
        pos += 4;
      }

      var line = new byte[bytesPerRow];
      var at = 0;
      while (at < bytesPerRow && pos < data.Length) {
        var opcode = data[pos++];

        if (opcode == 0x00) {
          if (pos >= data.Length)
            break;

          var count = data[pos++];
          var patternBytes = Math.Min(patternLength, data.Length - pos);
          if (patternBytes <= 0)
            break;

          for (var r = 0; r < count && at < bytesPerRow; ++r)
            for (var p = 0; p < patternBytes && at < bytesPerRow; ++p)
              line[at++] = data[pos + p];

          pos += patternBytes;
          continue;
        }

        if (opcode == 0x80) {
          if (pos >= data.Length)
            break;

          var count = data[pos++];
          var toCopy = Math.Min(count, Math.Min(data.Length - pos, bytesPerRow - at));
          data.Slice(pos, toCopy).CopyTo(line.AsSpan(at));
          at += toCopy;
          pos += count;
          continue;
        }

        var run = Math.Min(opcode & 0x7F, bytesPerRow - at);
        line.AsSpan(at, run).Fill((opcode & 0x80) != 0 ? (byte)0xFF : (byte)0x00);
        at += run;
      }

      for (var r = 0; r < repeat && scanlines.Count < numPlanes * height; ++r)
        scanlines.Add(line);
    }

    // Coded scanline k is row k/planes of plane k%planes; the picture is held one whole plane after
    // another, so that is where each one lands.
    for (var k = 0; k < scanlines.Count; ++k) {
      var plane = k % numPlanes;
      var row = k / numPlanes;
      scanlines[k].CopyTo(pixelData.AsSpan((plane * height + row) * bytesPerRow));
    }

    return new GemImgFile {
      Version = header.Version,
      Width = width,
      Height = height,
      NumPlanes = numPlanes,
      PatternLength = patternLength,
      PixelWidth = header.PixelWidth,
      PixelHeight = header.PixelHeight,
      PixelData = pixelData
    };
    }

  public static GemImgFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
