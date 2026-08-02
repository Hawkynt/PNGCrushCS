using System;
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

    // KNOWN WRONG for a multi-plane file, with two things visibly amiss in the loop below.
    //
    // A sixteen-colour sample decodes here to fifteen colours where XnView and RECOIL both show
    // seven, and those two agree with each other — so indices are being invented, which is what
    // happens when planes are combined out of step.
    //
    // First: this reads plane by plane, taking all of plane nought's rows before any of plane one's.
    // A GEM IMG interleaves them by scanline — row nought of every plane, then row one of every
    // plane — so for anything with more than one plane the wrong bits are being put together.
    //
    // Second: each opcode here ends a scanline. A scanline is built from as many items as it takes
    // to fill it, and only then does the row advance, so a row coded as two runs is read as two rows.
    //
    // Both need the opcode meanings settled against the samples before they can be fixed — the
    // encoding of a pattern run against a vertical replication is not what this code assumes, and
    // guessing at it would trade one wrong picture for another.
    var pos = dataOffset;
    for (var plane = 0; plane < numPlanes; ++plane) {
      var planeOffset = plane * bytesPerRow * height;
      var row = 0;
      while (row < height && pos < data.Length) {
        var opcode = data[pos];

        if (opcode == 0x00 && pos + 1 < data.Length) {
          // Vertical replication: repeat previous scan line 'count' times
          ++pos;
          var count = data[pos];
          ++pos;
          var srcRowOffset = planeOffset + (row > 0 ? (row - 1) * bytesPerRow : 0);
          for (var r = 0; r < count && row < height; ++r) {
            var dstRowOffset = planeOffset + row * bytesPerRow;
            pixelData.AsSpan(srcRowOffset, bytesPerRow).CopyTo(pixelData.AsSpan(dstRowOffset));
            ++row;
          }
        } else if (opcode == 0x80 && pos + 1 < data.Length) {
          // Bit string: literal data
          ++pos;
          var count = data[pos];
          ++pos;
          var dstRowOffset = planeOffset + row * bytesPerRow;
          var toCopy = Math.Min(count, Math.Min(data.Length - pos, bytesPerRow));
          data.Slice(pos, toCopy).CopyTo(pixelData.AsSpan(dstRowOffset));
          pos += count;
          ++row;
        } else if (opcode == 0xFF && pos + 1 < data.Length) {
          // Pattern run: repeat pattern 'count' times
          ++pos;
          var count = data[pos];
          ++pos;
          var patLen = Math.Min(patternLength, data.Length - pos);
          var dstRowOffset = planeOffset + row * bytesPerRow;
          var dstPos = 0;
          for (var r = 0; r < count && dstPos < bytesPerRow; ++r)
            for (var p = 0; p < patLen && dstPos < bytesPerRow; ++p)
              pixelData[dstRowOffset + dstPos++] = data[pos + p];
          pos += patLen;
          ++row;
        } else {
          // Unknown opcode, skip
          ++pos;
        }
      }
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
