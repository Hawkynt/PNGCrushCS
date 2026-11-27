using System;
using System.IO;
using System.Text;

namespace FileFormat.SeriousTexture;

/// <summary>Writes version-4 Serious Engine textures: <c>TDAT</c>, the frames uncompressed in <c>FRMS</c>, and <c>ANIM</c>.</summary>
/// <remarks>
/// The layout of every uncompressed version-4 sample, chunk for chunk: RGB frames where the alpha
/// flag is clear and RGBA where it is set, and the animations as stated — the engine's own one-frame
/// <c>OnlyAnim</c> at a fiftieth of a second where none are.
/// </remarks>
public static class SeriousTextureWriter {

  public static byte[] ToBytes(SeriousTextureFile file) {
    var frames = file.Frames ?? throw new ArgumentException("A Serious Engine texture needs its frames.", nameof(file));
    if (frames.Count == 0)
      throw new ArgumentException("A Serious Engine texture needs at least one frame.", nameof(file));
    var first = file.FirstMipLevel;
    if (file.Width <= 0 || file.Height <= 0 || file.MexWidth >> first != file.Width || file.MexHeight >> first != file.Height)
      throw new ArgumentException($"A Serious Engine texture of {file.Width}x{file.Height} cannot be {file.MexWidth}x{file.MexHeight} world units at mip level {first}.", nameof(file));

    var alpha = file.HasAlpha;
    using var output = new MemoryStream();
    using var writer = new BinaryWriter(output, Encoding.ASCII, leaveOpen: true);
    writer.Write("TVER"u8);
    writer.Write(4);
    writer.Write("TDAT"u8);
    writer.Write(file.Flags);
    writer.Write(file.MexWidth);
    writer.Write(file.MexHeight);
    writer.Write(file.FineMipLevels);
    writer.Write(first);
    writer.Write(frames.Count);
    writer.Write("FRMS"u8);
    var pixels = file.Width * file.Height;
    foreach (var frame in frames) {
      if (frame.Length < pixels * 4)
        throw new ArgumentException($"A frame of {file.Width}x{file.Height} needs {pixels * 4} bytes of RGBA.", nameof(file));
      for (var p = 0; p < pixels; ++p)
        writer.Write(frame.AsSpan(4 * p, alpha ? 4 : 3));
    }

    var animations = file.Animations is { Count: > 0 } ? file.Animations : [new("OnlyAnim", 0.02f, [0])];
    writer.Write("ANIM"u8);
    writer.Write("ADAT"u8);
    writer.Write(animations.Count);
    foreach (var animation in animations) {
      var name = new byte[32];
      var bytes = Encoding.ASCII.GetBytes(animation.Name ?? string.Empty);
      if (bytes.Length > 31)
        throw new ArgumentException($"A Serious Engine animation name is at most 31 characters; '{animation.Name}' is not.", nameof(file));
      bytes.CopyTo(name, 0);
      writer.Write(name);
      writer.Write(animation.SecondsPerFrame);
      writer.Write(animation.Frames.Length);
      foreach (var index in animation.Frames)
        writer.Write(index);
    }

    writer.Flush();
    return output.ToArray();
  }
}
