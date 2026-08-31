using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.Tiny;

/// <summary>Serializes Tiny Stuff compressed Atari ST pictures.</summary>
public static class TinyWriter {

  /// <summary>Serializes one complete Tiny Stuff file.</summary>
  public static byte[] ToBytes(TinyFile file) {
    TinyFile.Validate(file, nameof(file));
    var (control, data) = TinyCompressor.Compress(file.PixelData);

    using var ms = new MemoryStream(1 + 4 + 32 + 4 + control.Length + data.Length);
    ms.WriteByte((byte)((byte)file.Resolution + (file.HasColorAnimation ? 3 : 0)));

    Span<byte> buffer = stackalloc byte[2];
    if (file.HasColorAnimation) {
      ms.WriteByte(file.AnimationLimits);
      ms.WriteByte(unchecked((byte)file.AnimationSpeedDirection));
      BinaryPrimitives.WriteUInt16BigEndian(buffer, file.AnimationDuration);
      ms.Write(buffer);
    }

    foreach (var entry in file.Palette) {
      BinaryPrimitives.WriteInt16BigEndian(buffer, entry);
      ms.Write(buffer);
    }

    BinaryPrimitives.WriteUInt16BigEndian(buffer, checked((ushort)control.Length));
    ms.Write(buffer);
    BinaryPrimitives.WriteUInt16BigEndian(buffer, checked((ushort)(data.Length / 2)));
    ms.Write(buffer);
    ms.Write(control);
    ms.Write(data);
    return ms.ToArray();
  }

  /// <summary>Writes one complete Tiny Stuff file to a stream.</summary>
  public static void ToStream(TinyFile file, Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    stream.Write(ToBytes(file));
  }

  /// <summary>Writes one complete Tiny Stuff file to disk.</summary>
  public static void ToFile(TinyFile file, FileInfo target) {
    ArgumentNullException.ThrowIfNull(target);
    File.WriteAllBytes(target.FullName, ToBytes(file));
  }
}
