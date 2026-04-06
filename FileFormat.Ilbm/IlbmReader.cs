using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace FileFormat.Ilbm;

/// <summary>Reads IFF ILBM files from bytes, streams, or file paths.</summary>
public static class IlbmReader {

  private const int _MIN_IFF_SIZE = 12; // "FORM" + size + form type

  public static IlbmFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("ILBM file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static IlbmFile FromStream(Stream stream) {
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

  public static IlbmFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static IlbmFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < _MIN_IFF_SIZE)
      throw new InvalidDataException("Data too small for a valid IFF ILBM file.");

    var span = data.AsSpan();

    // Validate FORM magic
    var formId = Encoding.ASCII.GetString(data, 0, 4);
    if (formId != "FORM")
      throw new InvalidDataException($"Invalid IFF magic: expected 'FORM', got '{formId}'.");

    // Validate ILBM form type
    var formType = Encoding.ASCII.GetString(data, 8, 4);
    if (formType != "ILBM")
      throw new InvalidDataException($"Invalid IFF form type: expected 'ILBM', got '{formType}'.");

    var formSize = BinaryPrimitives.ReadInt32BigEndian(span[4..]);

    // Parse chunks
    BmhdChunk? bmhd = null;
    byte[]? cmap = null;
    byte[]? body = null;
    uint camg = 0;

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
          if (chunkSize >= BmhdChunk.StructSize)
            bmhd = BmhdChunk.ReadFrom(span[chunkDataOffset..]);
          break;
        case "CMAP":
          cmap = new byte[chunkSize];
          span.Slice(chunkDataOffset, chunkSize).CopyTo(cmap);
          break;
        case "CAMG":
          if (chunkSize >= 4)
            camg = BinaryPrimitives.ReadUInt32BigEndian(span[chunkDataOffset..]);
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
      throw new InvalidDataException("ILBM file missing required BMHD chunk.");

    if (body == null)
      throw new InvalidDataException("ILBM file missing required BODY chunk.");

    var header = bmhd.Value;
    var width = header.Width;
    var height = header.Height;
    var numPlanes = header.NumPlanes;
    var compression = (IlbmCompression)header.Compression;

    // Decompress BODY if needed
    var bytesPerPlaneRow = ((width + 15) / 16) * 2;
    var bytesPerScanline = bytesPerPlaneRow * numPlanes;
    var expectedPlanarSize = bytesPerScanline * height;

    var planarData = compression == IlbmCompression.ByteRun1
      ? ByteRun1Compressor.Decode(body, expectedPlanarSize)
      : body;

    // Convert planar to chunky
    var pixelData = PlanarConverter.PlanarToChunky(planarData, width, height, numPlanes);

    return new IlbmFile {
      Width = width,
      Height = height,
      NumPlanes = numPlanes,
      Compression = compression,
      Masking = (IlbmMasking)header.Masking,
      TransparentColor = header.TransparentColor,
      XAspect = header.XAspect,
      YAspect = header.YAspect,
      PageWidth = header.PageWidth,
      PageHeight = header.PageHeight,
      PixelData = pixelData,
      Palette = cmap,
      ViewportMode = camg
    };
  }
}
