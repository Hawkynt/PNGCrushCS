using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace FileFormat.IffAcbm;

/// <summary>Reads IFF ACBM files from bytes, streams, or file paths.</summary>
public static class IffAcbmReader {

  private const int _MIN_IFF_SIZE = 12; // "FORM" + size + form type
  private const int _BMHD_SIZE = 20;

  public static IffAcbmFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("ACBM file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static IffAcbmFile FromStream(Stream stream) {
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

  public static IffAcbmFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static IffAcbmFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < _MIN_IFF_SIZE)
      throw new InvalidDataException("Data too small for a valid IFF ACBM file.");

    var formId = Encoding.ASCII.GetString(data.Slice(0, 4));
    if (formId != "FORM")
      throw new InvalidDataException($"Invalid IFF magic: expected 'FORM', got '{formId}'.");

    var formType = Encoding.ASCII.GetString(data.Slice(8, 4));
    if (formType != "ACBM")
      throw new InvalidDataException($"Invalid IFF form type: expected 'ACBM', got '{formType}'.");

    var formSize = BinaryPrimitives.ReadInt32BigEndian(data[4..]);

    // Parse chunks
    ushort width = 0, height = 0;
    byte numPlanes = 0;
    byte xAspect = 0, yAspect = 0;
    short pageWidth = 0, pageHeight = 0;
    ushort transparentColor = 0;
    var hasBmhd = false;
    byte[]? cmap = null;
    byte[]? abit = null;

    var offset = 12;
    var endOffset = Math.Min(8 + formSize, data.Length);

    while (offset + 8 <= endOffset) {
      var chunkId = Encoding.ASCII.GetString(data.Slice(offset, 4));
      var chunkSize = BinaryPrimitives.ReadInt32BigEndian(data[(offset + 4)..]);
      var chunkDataOffset = offset + 8;

      if (chunkDataOffset + chunkSize > data.Length)
        break;

      switch (chunkId) {
        case "BMHD":
          if (chunkSize >= _BMHD_SIZE) {
            var bmhd = data[chunkDataOffset..];
            width = BinaryPrimitives.ReadUInt16BigEndian(bmhd);
            height = BinaryPrimitives.ReadUInt16BigEndian(bmhd[2..]);
            // skip xPos (4), yPos (6)
            numPlanes = bmhd[8];
            // skip masking (9), compression (10), padding (11)
            transparentColor = BinaryPrimitives.ReadUInt16BigEndian(bmhd[12..]);
            xAspect = bmhd[14];
            yAspect = bmhd[15];
            pageWidth = BinaryPrimitives.ReadInt16BigEndian(bmhd[16..]);
            pageHeight = BinaryPrimitives.ReadInt16BigEndian(bmhd[18..]);
            hasBmhd = true;
          }
          break;
        case "CMAP":
          cmap = new byte[chunkSize];
          data.Slice(chunkDataOffset, chunkSize).CopyTo(cmap);
          break;
        case "ABIT":
          abit = new byte[chunkSize];
          data.Slice(chunkDataOffset, chunkSize).CopyTo(abit);
          break;
      }

      // Advance to next chunk (2-byte aligned)
      offset = chunkDataOffset + chunkSize + (chunkSize & 1);
    }

    if (!hasBmhd)
      throw new InvalidDataException("ACBM file missing required BMHD chunk.");

    if (abit == null)
      throw new InvalidDataException("ACBM file missing required ABIT chunk.");

    // Convert contiguous bitplane data to chunky indexed pixels
    var pixelData = _ContiguousPlanarToChunky(abit, width, height, numPlanes);

    return new IffAcbmFile {
      Width = width,
      Height = height,
      NumPlanes = numPlanes,
      PixelData = pixelData,
      Palette = cmap ?? [],
      XAspect = xAspect,
      YAspect = yAspect,
      PageWidth = pageWidth,
      PageHeight = pageHeight,
      TransparentColor = transparentColor,
    };
  }

  /// <summary>
  ///   Converts contiguous (non-interleaved) word-aligned bitplane data to chunky indexed pixels.
  ///   Layout: all rows of plane 0, then all rows of plane 1, etc.
  ///   Each plane row is <c>(width+15)/16*2</c> bytes (word-aligned).
  /// </summary>
  internal static byte[] _ContiguousPlanarToChunky(ReadOnlySpan<byte> planarData, int width, int height, int numPlanes) {
    var bytesPerPlaneRow = ((width + 15) / 16) * 2;
    var bytesPerPlane = bytesPerPlaneRow * height;
    var result = new byte[width * height];

    for (var plane = 0; plane < numPlanes; ++plane) {
      var planeOffset = plane * bytesPerPlane;

      for (var y = 0; y < height; ++y) {
        var rowOffset = planeOffset + y * bytesPerPlaneRow;

        for (var x = 0; x < width; ++x) {
          var byteIndex = x / 8;
          var bitIndex = 7 - (x % 8);
          var dataOffset = rowOffset + byteIndex;

          if (dataOffset < planarData.Length && (planarData[dataOffset] & (1 << bitIndex)) != 0)
            result[y * width + x] |= (byte)(1 << plane);
        }
      }
    }

    return result;
  }
}
