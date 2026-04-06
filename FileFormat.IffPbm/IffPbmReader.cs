using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace FileFormat.IffPbm;

/// <summary>Reads IFF PBM files from bytes, streams, or file paths.</summary>
public static class IffPbmReader {

  private const int _MIN_IFF_SIZE = 12; // "FORM" + size + form type

  public static IffPbmFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("IFF PBM file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static IffPbmFile FromStream(Stream stream) {
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

  public static IffPbmFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static IffPbmFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < _MIN_IFF_SIZE)
      throw new InvalidDataException("Data too small for a valid IFF PBM file.");

    var span = data.AsSpan();

    // Validate FORM magic
    var formId = Encoding.ASCII.GetString(data, 0, 4);
    if (formId != "FORM")
      throw new InvalidDataException($"Invalid IFF magic: expected 'FORM', got '{formId}'.");

    // Validate PBM form type (note: "PBM " with trailing space)
    var formType = Encoding.ASCII.GetString(data, 8, 4);
    if (formType != "PBM ")
      throw new InvalidDataException($"Invalid IFF form type: expected 'PBM ', got '{formType}'.");

    var formSize = BinaryPrimitives.ReadInt32BigEndian(span[4..]);

    // Parse chunks
    IffPbmBmhd? bmhd = null;
    byte[]? cmap = null;
    byte[]? body = null;

    var offset = 12; // skip FORM header + form type
    var endOffset = Math.Min(8 + formSize, data.Length);

    while (offset + 8 <= endOffset) {
      var chunkId = Encoding.ASCII.GetString(data, offset, 4);
      var chunkSize = BinaryPrimitives.ReadInt32BigEndian(span[(offset + 4)..]);
      var chunkDataOffset = offset + 8;

      if (chunkDataOffset + chunkSize > data.Length)
        break;

      switch (chunkId) {
        case "BMHD":
          if (chunkSize >= IffPbmBmhd.StructSize)
            bmhd = IffPbmBmhd.ReadFrom(span[chunkDataOffset..]);
          break;
        case "CMAP":
          cmap = new byte[chunkSize];
          span.Slice(chunkDataOffset, chunkSize).CopyTo(cmap);
          break;
        case "BODY":
          body = new byte[chunkSize];
          span.Slice(chunkDataOffset, chunkSize).CopyTo(body);
          break;
      }

      // Advance to next chunk (2-byte aligned)
      offset = chunkDataOffset + chunkSize + (chunkSize & 1);
    }

    if (bmhd == null)
      throw new InvalidDataException("IFF PBM file missing required BMHD chunk.");

    if (body == null)
      throw new InvalidDataException("IFF PBM file missing required BODY chunk.");

    var header = bmhd.Value;
    var width = (int)header.Width;
    var height = (int)header.Height;
    var compression = (IffPbmCompression)header.Compression;

    // PBM stores chunky pixels: one byte per pixel, rows padded to even length
    var rowBytes = width + (width & 1); // pad each row to even byte count
    var expectedSize = rowBytes * height;

    byte[] rawPixels;
    if (compression == IffPbmCompression.ByteRun1)
      rawPixels = ByteRun1Compressor.Decode(body, expectedSize);
    else
      rawPixels = body.Length >= expectedSize ? body[..expectedSize] : body;

    // Strip row padding to get clean pixel data
    byte[] pixelData;
    if ((width & 1) != 0) {
      pixelData = new byte[width * height];
      for (var y = 0; y < height; ++y)
        rawPixels.AsSpan(y * rowBytes, width).CopyTo(pixelData.AsSpan(y * width));
    } else {
      pixelData = rawPixels.Length == width * height ? rawPixels : rawPixels[..(width * height)];
    }

    return new IffPbmFile {
      Width = width,
      Height = height,
      Compression = compression,
      TransparentColor = header.TransparentColor,
      XAspect = header.XAspect,
      YAspect = header.YAspect,
      PageWidth = header.PageWidth,
      PageHeight = header.PageHeight,
      PixelData = pixelData,
      Palette = cmap,
    };
  }
}
