using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace FileFormat.IffPbm;

/// <summary>Assembles IFF PBM file bytes from an <see cref="IffPbmFile"/>.</summary>
public static class IffPbmWriter {

  public static byte[] ToBytes(IffPbmFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var width = file.Width;
    var height = file.Height;

    // PBM stores chunky pixels with rows padded to even byte count
    var rowBytes = width + (width & 1);
    byte[] paddedPixels;
    if ((width & 1) != 0) {
      paddedPixels = new byte[rowBytes * height];
      for (var y = 0; y < height; ++y)
        file.PixelData.AsSpan(y * width, width).CopyTo(paddedPixels.AsSpan(y * rowBytes));
    } else {
      paddedPixels = file.PixelData;
    }

    // Optionally compress
    var bodyData = file.Compression == IffPbmCompression.ByteRun1
      ? ByteRun1Compressor.Encode(paddedPixels)
      : paddedPixels;

    // Calculate sizes
    var bmhdChunkSize = 8 + IffPbmBmhd.StructSize; // ID(4) + size(4) + data(20)
    var cmapChunkSize = file.Palette != null ? 8 + file.Palette.Length + (file.Palette.Length & 1) : 0;
    var bodyChunkSize = 8 + bodyData.Length + (bodyData.Length & 1);
    var formDataSize = 4 + bmhdChunkSize + cmapChunkSize + bodyChunkSize; // "PBM " + chunks
    var totalSize = 8 + formDataSize; // "FORM" + size + data

    using var ms = new MemoryStream(totalSize);

    // FORM header
    ms.Write(Encoding.ASCII.GetBytes("FORM"));
    _WriteInt32BigEndian(ms, formDataSize);

    // Form type (note: "PBM " with trailing space)
    ms.Write(Encoding.ASCII.GetBytes("PBM "));

    // BMHD chunk
    ms.Write(Encoding.ASCII.GetBytes("BMHD"));
    _WriteInt32BigEndian(ms, IffPbmBmhd.StructSize);
    var bmhdBuffer = new byte[IffPbmBmhd.StructSize];
    var bmhd = new IffPbmBmhd(
      (ushort)width,
      (ushort)height,
      0,
      0,
      8, // PBM is always 8 planes (chunky 8-bit)
      0, // no masking
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
}
