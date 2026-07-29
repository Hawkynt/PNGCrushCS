using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Iff;

namespace FileFormat.IffAcbm;

/// <summary>Assembles IFF ACBM file bytes from an <see cref="IffAcbmFile"/>.</summary>
public static class IffAcbmWriter {

  private const int _BMHD_SIZE = 20;

  public static byte[] ToBytes(IffAcbmFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var width = file.Width;
    var height = file.Height;
    var numPlanes = file.NumPlanes;

    // Convert chunky indexed pixels to contiguous bitplane data
    var abitData = _ChunkyToContiguousPlanar(file.PixelData, width, height, numPlanes);

    // Calculate chunk sizes (each chunk: 4-byte ID + 4-byte size + data + optional pad byte)
    var bmhdChunkSize = 8 + _BMHD_SIZE; // BMHD is always 20 bytes, no pad needed
    var cmapDataSize = file.Palette.Length;
    var cmapChunkSize = cmapDataSize > 0 ? 8 + cmapDataSize + (cmapDataSize & 1) : 0;
    var abitDataSize = abitData.Length;
    var abitChunkSize = 8 + abitDataSize + (abitDataSize & 1);

    var formDataSize = 4 + bmhdChunkSize + cmapChunkSize + abitChunkSize; // "ACBM" + chunks
    var totalSize = 8 + formDataSize; // "FORM" + size + data

    using var ms = new MemoryStream(totalSize);

    // FORM header
    _WriteChunkHeader(ms, "FORM", formDataSize);

    // Form type
    ms.Write("ACBM"u8);

    // BMHD chunk
    _WriteChunkHeader(ms, "BMHD", _BMHD_SIZE);
    Span<byte> bmhdBuffer = stackalloc byte[_BMHD_SIZE];
    BinaryPrimitives.WriteUInt16BigEndian(bmhdBuffer, (ushort)width);
    BinaryPrimitives.WriteUInt16BigEndian(bmhdBuffer[2..], (ushort)height);
    BinaryPrimitives.WriteInt16BigEndian(bmhdBuffer[4..], 0); // xPos
    BinaryPrimitives.WriteInt16BigEndian(bmhdBuffer[6..], 0); // yPos
    bmhdBuffer[8] = numPlanes;
    bmhdBuffer[9] = 0; // masking
    bmhdBuffer[10] = 0; // compression (ACBM is always uncompressed)
    bmhdBuffer[11] = 0; // padding
    BinaryPrimitives.WriteUInt16BigEndian(bmhdBuffer[12..], (ushort)file.TransparentColor);
    bmhdBuffer[14] = file.XAspect;
    bmhdBuffer[15] = file.YAspect;
    BinaryPrimitives.WriteInt16BigEndian(bmhdBuffer[16..], (short)file.PageWidth);
    BinaryPrimitives.WriteInt16BigEndian(bmhdBuffer[18..], (short)file.PageHeight);
    ms.Write(bmhdBuffer);

    // CMAP chunk (optional)
    if (cmapDataSize > 0) {
      _WriteChunkHeader(ms, "CMAP", cmapDataSize);
      ms.Write(file.Palette);
      if ((cmapDataSize & 1) != 0)
        ms.WriteByte(0); // pad to 2-byte alignment
    }

    // ABIT chunk
    _WriteChunkHeader(ms, "ABIT", abitDataSize);
    ms.Write(abitData);
    if ((abitDataSize & 1) != 0)
      ms.WriteByte(0); // pad to 2-byte alignment

    return ms.ToArray();
  }

  /// <summary>
  ///   Converts chunky indexed pixel data to contiguous (non-interleaved) word-aligned bitplane data.
  ///   Layout: all rows of plane 0, then all rows of plane 1, etc.
  ///   Each plane row is <c>(width+15)/16*2</c> bytes (word-aligned).
  /// </summary>
  internal static byte[] _ChunkyToContiguousPlanar(ReadOnlySpan<byte> chunkyData, int width, int height, int numPlanes) {
    var bytesPerPlaneRow = ((width + 15) / 16) * 2;
    var bytesPerPlane = bytesPerPlaneRow * height;
    var result = new byte[numPlanes * bytesPerPlane];

    for (var plane = 0; plane < numPlanes; ++plane) {
      var planeOffset = plane * bytesPerPlane;

      for (var y = 0; y < height; ++y) {
        var rowOffset = planeOffset + y * bytesPerPlaneRow;

        for (var x = 0; x < width; ++x) {
          var pixel = chunkyData[y * width + x];
          if ((pixel & (1 << plane)) != 0) {
            var byteIndex = x / 8;
            var bitIndex = 7 - (x % 8);
            result[rowOffset + byteIndex] |= (byte)(1 << bitIndex);
          }
        }
      }
    }

    return result;
  }

  private static void _WriteChunkHeader(Stream stream, string chunkId, int size) {
    Span<byte> buffer = stackalloc byte[IffChunkHeader.StructSize];
    new Riff.FourCC(chunkId).WriteTo(buffer);
    BinaryPrimitives.WriteInt32BigEndian(buffer[4..], size);
    stream.Write(buffer);
  }
}
