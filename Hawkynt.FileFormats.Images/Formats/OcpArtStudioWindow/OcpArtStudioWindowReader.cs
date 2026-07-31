using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.OcpArtStudioWindow;

/// <summary>Reads Advanced OCP Art Studio windows from bytes, streams, or file paths.</summary>
public static class OcpArtStudioWindowReader {

  /// <summary>Bytes a companion palette holds past any header.</summary>
  private const int _PALETTE_LENGTH = 239;

  /// <summary>Bytes between one colour of a companion palette and the next.</summary>
  private const int _PALETTE_STRIDE = 12;

  /// <summary>
  /// Reads a window, taking its colours from the .pal file beside it.
  /// </summary>
  /// <remarks>
  /// The palette is not optional here as it is for some formats with companions: a window stores
  /// no colours of its own and none can be guessed, so without the companion there is no picture.
  /// </remarks>
  public static OcpArtStudioWindowFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Window not found.", file.FullName);

    var palette = _ReadCompanion(file)
      ?? throw new InvalidDataException($"No usable palette beside {file.Name}; a window stores none of its own.");

    return _Read(File.ReadAllBytes(file.FullName), palette);
  }

  private static byte[]? _ReadCompanion(FileInfo file) {
    var directory = file.DirectoryName;
    if (directory == null)
      return null;

    var stem = Path.GetFileNameWithoutExtension(file.Name);
    foreach (var extension in (string[])[".pal", ".PAL"]) {
      var candidate = new FileInfo(Path.Combine(directory, stem + extension));
      if (!candidate.Exists)
        continue;

      var data = File.ReadAllBytes(candidate.FullName);
      var offset = AmstradGraphics.HeaderLength(data);
      if (data.Length != offset + _PALETTE_LENGTH || data[offset] != OcpArtStudioWindowFile.RequiredMode)
        continue;

      var palette = new byte[OcpArtStudioWindowFile.ColorCount * 3];
      for (var i = 0; i < OcpArtStudioWindowFile.ColorCount; ++i) {
        var c = data[offset + 3 + i * _PALETTE_STRIDE];
        if (c < AmstradGraphics.ColorBias || c >= AmstradGraphics.ColorBias + AmstradGraphics.ColorCount)
          return null;

        AmstradGraphics.Palette.Slice((c - AmstradGraphics.ColorBias) * 3, 3).CopyTo(palette.AsSpan(i * 3));
      }

      return palette;
    }

    return null;
  }

  public static OcpArtStudioWindowFile FromStream(Stream stream) {
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

  /// <summary>Reads a window without its palette, which leaves it a shape and no colours.</summary>
  public static OcpArtStudioWindowFile FromSpan(ReadOnlySpan<byte> data) => _Read(data, new byte[48]);

  private static OcpArtStudioWindowFile _Read(ReadOnlySpan<byte> data, byte[] palette) {
    if (data.Length < 6)
      throw new InvalidDataException($"Not an OCP Art Studio window: {data.Length} bytes.");

    // The size is at the end because the program appended it after writing the picture.
    var width = data[^4] | (data[^3] << 8);
    var height = data[^2];
    if (width == 0 || width > 640 || height == 0 || height > 200)
      throw new InvalidDataException($"An OCP window is not {width}x{height}.");

    var stride = (width + 7) >> 3;
    var offset = AmstradGraphics.HeaderLength(data);
    var bitmap = data.Length == offset + stride * height + OcpArtStudioWindowFile.TrailerLength
      ? data.Slice(offset, stride * height).ToArray()
      : _Unpack(data, offset, stride * height);

    // A mode 0 pixel spans two of the screen positions the stored width counts.
    return new() { Bitmap = bitmap, Palette = palette, Width = width >> 1, Height = height, Stride = stride };
  }

  /// <summary>
  /// Unpacks a window that was packed, which is stored in named blocks rather than one stream.
  /// </summary>
  /// <remarks>
  /// Each block opens with the three letters "MJH" and its own length, and the run-length coding
  /// runs on across the boundary between them. Blocks exist because the program wrote a picture in
  /// pieces as it compressed, not because the encoding needs them — which is why a run may be
  /// counted against one block and finish inside the next.
  /// </remarks>
  private static byte[] _Unpack(ReadOnlySpan<byte> data, int offset, int length) {
    var unpacked = new byte[length];
    var at = offset;
    var blockLeft = 0;

    for (var target = 0; target < length;) {
      while (blockLeft <= 0) {
        if (at + 4 >= data.Length || data[at] != 'M' || data[at + 1] != 'J' || data[at + 2] != 'H')
          throw new InvalidDataException("A packed OCP window's blocks do not follow one another.");

        blockLeft = data[at + 3] | (data[at + 4] << 8);
        at += 5;
      }

      if (at >= data.Length)
        throw new InvalidDataException("A packed OCP window ends before its picture does.");

      var command = data[at++];
      byte value;
      int count;

      if (command == 1) {
        if (at + 1 >= data.Length)
          throw new InvalidDataException("A packed OCP window's run has no count or no value.");

        count = data[at++];
        if (count == 0)
          count = 256;

        value = data[at++];
      } else {
        count = 1;
        value = command;
      }

      blockLeft -= count;
      while (count-- > 0 && target < length)
        unpacked[target++] = value;
    }

    return unpacked;
  }

  public static OcpArtStudioWindowFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
