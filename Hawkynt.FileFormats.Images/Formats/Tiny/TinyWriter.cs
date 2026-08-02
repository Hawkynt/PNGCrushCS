using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.Tiny;

/// <summary>Assembles Tiny (compressed DEGAS) file bytes from a TinyFile.</summary>
public static class TinyWriter {

  public static byte[] ToBytes(TinyFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var (control, data) = TinyCompressor.Compress(file.PixelData);

    using var ms = new MemoryStream();
    ms.WriteByte((byte)file.Resolution);

    Span<byte> buffer = stackalloc byte[2];
    for (var i = 0; i < 16; ++i) {
      BinaryPrimitives.WriteInt16BigEndian(buffer, i < file.Palette.Length ? file.Palette[i] : (short)0);
      ms.Write(buffer);
    }

    // The two lengths the reader needs before it can tell the blocks apart: how many control bytes,
    // then how many words of data — not how many bytes.
    BinaryPrimitives.WriteUInt16BigEndian(buffer, (ushort)control.Length);
    ms.Write(buffer);
    BinaryPrimitives.WriteUInt16BigEndian(buffer, (ushort)(data.Length / 2));
    ms.Write(buffer);

    ms.Write(control);
    ms.Write(data);

    return ms.ToArray();
  }

  public static void ToStream(TinyFile file, Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    var bytes = ToBytes(file);
    stream.Write(bytes, 0, bytes.Length);
  }

  public static void ToFile(TinyFile file, FileInfo target) {
    ArgumentNullException.ThrowIfNull(target);
    File.WriteAllBytes(target.FullName, ToBytes(file));
  }
}
