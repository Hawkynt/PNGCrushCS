using System;
using System.IO;
using FileFormat.Core;
using FileFormat.Iff;
using FileFormat.Ilbm;

namespace FileFormat.IffAnim;

/// <summary>Assembles IFF ANIM file bytes from an <see cref="IffAnimFile"/>.</summary>
public static class IffAnimWriter {

  public static byte[] ToBytes(IffAnimFile file) {
    ArgumentNullException.ThrowIfNull(file);

    // Create an IlbmFile from the RGB24 pixel data
    var rawImage = new RawImage {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = file.PixelData,
    };
    var ilbmFile = IlbmFile.FromRawImage(rawImage);
    var ilbmBytes = IlbmWriter.ToBytes(ilbmFile);

    // Wrap in FORM ANIM: "FORM" + uint32 BE (4 + ilbmBytes.Length) + "ANIM" + ilbmBytes
    var formDataSize = 4 + ilbmBytes.Length; // "ANIM" + embedded ILBM
    var totalSize = 8 + formDataSize;        // "FORM" + size + data

    using var ms = new MemoryStream(totalSize);

    _WriteChunkHeader(ms, "FORM", formDataSize);
    ms.Write("ANIM"u8);
    ms.Write(ilbmBytes);

    return ms.ToArray();
  }

  private static void _WriteChunkHeader(Stream stream, string chunkId, int size) {
    Span<byte> buffer = stackalloc byte[IffChunkHeader.StructSize];
    new Riff.FourCC(chunkId).WriteTo(buffer);
    System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(buffer[4..], size);
    stream.Write(buffer);
  }
}
