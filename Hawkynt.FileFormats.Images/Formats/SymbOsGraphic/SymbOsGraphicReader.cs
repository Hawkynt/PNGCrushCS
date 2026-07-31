using System;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.SymbOsGraphic;

/// <summary>Reads SymbOS graphics from bytes, streams, or file paths.</summary>
public static class SymbOsGraphicReader {

  public static SymbOsGraphicFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Graphic not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static SymbOsGraphicFile FromStream(Stream stream) {
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

  public static SymbOsGraphicFile FromSpan(ReadOnlySpan<byte> data) {
    var chunks = new List<SymbOsChunk>();
    int width = 0, left = 0, top = 0, rowHeight = 0;

    for (var offset = 0; offset + 3 < data.Length;) {
      int stride = data[offset];
      if (stride == 0)
        break;

      if (stride == SymbOsGraphicFile.RowMarker) {
        // A row of chunks ends here, and every row must come to the same width.
        if (width == 0)
          width = left;
        else if (left != width)
          throw new InvalidDataException($"A row of chunks is {left} pixels wide, not {width}.");

        left = 0;
        top += rowHeight;
        rowHeight = 0;
        offset += 3;
        continue;
      }

      int chunkWidth, chunkHeight, header;
      var wide = stride == SymbOsGraphicFile.WideHeader;

      if (!wide) {
        if (stride > SymbOsGraphicFile.MaxNarrowStride)
          throw new InvalidDataException($"Not a SymbOS graphic: a chunk header of {stride}.");

        chunkWidth = data[offset + 1];
        if ((chunkWidth + 3) >> 2 != stride)
          throw new InvalidDataException($"A four-colour chunk {chunkWidth} pixels wide is not {stride} bytes.");

        chunkHeight = data[offset + 2];
        header = 3;
      } else {
        if (offset + 8 >= data.Length || data[offset + 1] != 5)
          throw new InvalidDataException("Not a SymbOS graphic: a malformed sixteen-colour chunk.");

        stride = data[offset + 2] | (data[offset + 3] << 8);
        chunkWidth = data[offset + 4] | (data[offset + 5] << 8);
        if ((chunkWidth + 1) >> 1 != stride)
          throw new InvalidDataException($"A sixteen-colour chunk {chunkWidth} pixels wide is not {stride} bytes.");

        chunkHeight = data[offset + 6] | (data[offset + 7] << 8);
        header = 8;
      }

      // Every chunk in a row is the same height, since the row is one band of the picture.
      if (left == 0)
        rowHeight = chunkHeight;
      else if (chunkHeight != rowHeight)
        throw new InvalidDataException($"A chunk is {chunkHeight} rows deep where its row is {rowHeight}.");

      offset += header;
      if (offset + chunkHeight * stride > data.Length)
        throw new InvalidDataException("A SymbOS chunk runs past the end of the file.");

      chunks.Add(new() {
        DataOffset = offset,
        Stride = stride,
        Width = chunkWidth,
        Height = chunkHeight,
        Left = left,
        Top = top,
        IsWide = wide,
      });

      left += chunkWidth;
      offset += chunkHeight * stride;
    }

    // The last row is not followed by a marker, so its width is only checked here — and a file that
    // does end with one leaves nothing to check against, which is what makes it malformed.
    if (width == 0)
      width = left;
    else if (left != width)
      throw new InvalidDataException($"The last row of chunks is {left} pixels wide, not {width}.");

    var height = top + rowHeight;
    if (width == 0 || height == 0)
      throw new InvalidDataException($"Not a SymbOS graphic: {width}x{height}.");

    return new() { Data = data.ToArray(), Width = width, Height = height, Chunks = chunks };
  }

  public static SymbOsGraphicFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
