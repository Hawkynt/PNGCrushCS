using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace FileFormat.Ilbm;

/// <summary>Assembles IFF ILBM file bytes from an <see cref="IlbmFile"/>.</summary>
public static class IlbmWriter {

  public static byte[] ToBytes(IlbmFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var width = file.Width;
    var height = file.Height;
    var numPlanes = file.NumPlanes;

    // Convert chunky to planar
    var planarData = PlanarConverter.ChunkyToPlanar(file.PixelData, width, height, numPlanes);

    // Optionally compress
    byte[] bodyData;
    if (file.Compression == IlbmCompression.ByteRun1)
      bodyData = ByteRun1Compressor.Encode(planarData);
    else
      bodyData = planarData;

    // Calculate sizes
    var bmhdChunkSize = 8 + BmhdChunk.StructSize; // ID(4) + size(4) + data(20)
    var camgChunkSize = file.ViewportMode != 0 ? 8 + 4 : 0; // ID(4) + size(4) + uint32(4)
    var cmapChunkSize = file.Palette != null ? 8 + file.Palette.Length + (file.Palette.Length & 1) : 0;
    var bodyChunkSize = 8 + bodyData.Length + (bodyData.Length & 1);
    var formDataSize = 4 + bmhdChunkSize + camgChunkSize + cmapChunkSize + bodyChunkSize; // "ILBM" + chunks
    var totalSize = 8 + formDataSize; // "FORM" + size + data

    using var ms = new MemoryStream(totalSize);

    // FORM header
    ms.Write(Encoding.ASCII.GetBytes("FORM"));
    _WriteInt32BigEndian(ms, formDataSize);

    // Form type
    ms.Write(Encoding.ASCII.GetBytes("ILBM"));

    // BMHD chunk
    ms.Write(Encoding.ASCII.GetBytes("BMHD"));
    _WriteInt32BigEndian(ms, BmhdChunk.StructSize);
    var bmhdBuffer = new byte[BmhdChunk.StructSize];
    var bmhd = new BmhdChunk(
      (ushort)width,
      (ushort)height,
      0,
      0,
      (byte)numPlanes,
      (byte)file.Masking,
      (byte)file.Compression,
      0,
      (ushort)file.TransparentColor,
      file.XAspect,
      file.YAspect,
      (short)file.PageWidth,
      (short)file.PageHeight
    );
    bmhd.WriteTo(bmhdBuffer);
    ms.Write(bmhdBuffer);

    // CAMG chunk (optional)
    if (file.ViewportMode != 0) {
      ms.Write(Encoding.ASCII.GetBytes("CAMG"));
      _WriteInt32BigEndian(ms, 4);
      _WriteUInt32BigEndian(ms, file.ViewportMode);
    }

    // CMAP chunk (optional)
    if (file.Palette != null) {
      ms.Write(Encoding.ASCII.GetBytes("CMAP"));
      _WriteInt32BigEndian(ms, file.Palette.Length);
      ms.Write(file.Palette);
      if ((file.Palette.Length & 1) != 0)
        ms.WriteByte(0); // pad to 2-byte alignment
    }

    // BODY chunk
    ms.Write(Encoding.ASCII.GetBytes("BODY"));
    _WriteInt32BigEndian(ms, bodyData.Length);
    ms.Write(bodyData);
    if ((bodyData.Length & 1) != 0)
      ms.WriteByte(0); // pad to 2-byte alignment

    return ms.ToArray();
  }

  private static void _WriteInt32BigEndian(Stream stream, int value) {
    Span<byte> buffer = stackalloc byte[4];
    BinaryPrimitives.WriteInt32BigEndian(buffer, value);
    stream.Write(buffer);
  }

  private static void _WriteUInt32BigEndian(Stream stream, uint value) {
    Span<byte> buffer = stackalloc byte[4];
    BinaryPrimitives.WriteUInt32BigEndian(buffer, value);
    stream.Write(buffer);
  }
}
