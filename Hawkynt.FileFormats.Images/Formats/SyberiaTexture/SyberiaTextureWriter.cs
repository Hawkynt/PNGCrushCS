using System;
using System.IO;

namespace FileFormat.SyberiaTexture;

/// <summary>Assembles a Syberia texture, which is a JPEG with its first ten bytes cut off.</summary>
/// <remarks>
/// The ten are the start-of-image marker, the APP0 marker, its length and four of the five bytes of
/// "JFIF" — always the same ten, which is why the format can leave them out and why a reader can put
/// them back. A picture that does not begin with them is not one this can shorten, so it is refused
/// rather than truncated into something no reader would recognise.
/// </remarks>
public static class SyberiaTextureWriter {

  public static byte[] ToBytes(SyberiaTextureFile file) {
    var restored = file.Restored ?? [];
    var head = SyberiaTextureFile.MissingHead;

    if (restored.Length < head.Length || !restored.AsSpan(0, head.Length).SequenceEqual(head))
      throw new InvalidDataException("A Syberia texture is a JFIF with its first ten bytes removed, and this picture does not begin with them.");

    return restored[head.Length..];
  }
}
