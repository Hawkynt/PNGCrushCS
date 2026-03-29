using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using FileFormat.Core;
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

    ms.Write(Encoding.ASCII.GetBytes("FORM"));
    _WriteInt32BigEndian(ms, formDataSize);
    ms.Write(Encoding.ASCII.GetBytes("ANIM"));
    ms.Write(ilbmBytes);

    return ms.ToArray();
  }

  private static void _WriteInt32BigEndian(Stream stream, int value) {
    Span<byte> buffer = stackalloc byte[4];
    BinaryPrimitives.WriteInt32BigEndian(buffer, value);
    stream.Write(buffer);
  }
}
