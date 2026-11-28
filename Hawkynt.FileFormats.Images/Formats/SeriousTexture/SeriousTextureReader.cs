using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;
using FileFormat.Core.BlockDecoders;

namespace FileFormat.SeriousTexture;

/// <summary>Reads Serious Engine textures from bytes, streams, or file paths.</summary>
public static class SeriousTextureReader {

  public static SeriousTextureFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Serious Engine texture not found.", file.FullName);
    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static SeriousTextureFile FromStream(Stream stream) => FromBytes(StreamBytes.ReadAll(stream));

  public static SeriousTextureFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static SeriousTextureFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < 8 || !data.StartsWith("TVER"u8))
      throw new InvalidDataException("Not a Serious Engine texture: it does not open with TVER.");
    var version = BinaryPrimitives.ReadInt32LittleEndian(data[4..]);
    if (version is not (3 or 4))
      throw new NotSupportedException($"This Serious Engine texture is version {version}; only 3 and 4 exist.");

    var at = 8;

    uint flags = 0;
    int mexWidth = 0, mexHeight = 0, fine = 0, first = 0, frameSize = 0, frameCount = 0, width = 0, height = 0;
    var header = false;
    var compressed = false;
    List<byte[]>? frames = null;
    var animations = new List<SeriousTextureAnimation>();

    while (at + 4 <= data.Length) {
      var id = Encoding.ASCII.GetString(data.Slice(at, 4));
      at += 4;
      switch (id) {
        case "TDAT":
          flags = (uint)_Int(data, ref at);
          mexWidth = _Int(data, ref at);
          mexHeight = _Int(data, ref at);
          fine = _Int(data, ref at);
          if (version == 3)
            _Int(data, ref at); // the mip-map count, which the frame size already accounts for
          first = _Int(data, ref at);
          if (version == 3)
            frameSize = _Int(data, ref at);
          frameCount = _Int(data, ref at);
          if (first is < 0 or > 30 || mexWidth <= 0 || mexHeight <= 0)
            throw new InvalidDataException($"This Serious Engine texture states {mexWidth}x{mexHeight} at mip level {first}.");
          width = mexWidth >> first;
          height = mexHeight >> first;
          if (width is <= 0 or > 16384 || height is <= 0 or > 16384 || frameCount is <= 0 or > 4096)
            throw new InvalidDataException($"This Serious Engine texture states {frameCount} frames of {width}x{height}.");
          header = true;
          break;

        case "FRMS":
          _RequireHeader(header, id);
          frames = version == 4
            ? _RawFrames(data, ref at, width, height, frameCount, (flags & SeriousTextureFile.AlphaChannelFlag) != 0)
            : _OldFrames(data, ref at, width, height, frameCount, frameSize, (flags & SeriousTextureFile.AlphaChannelFlag) != 0);
          break;

        case "FRMC":
          _RequireHeader(header, id);
          if (frameCount != 1)
            throw new NotSupportedException($"This Serious Engine texture compresses {frameCount} frames; only one-frame compressed textures have been seen.");
          frames = [_CompressedFrame(data, ref at, width, height)];
          compressed = true;
          break;

        case "ANIM":
          if (at + 8 > data.Length || !data[at..].StartsWith("ADAT"u8))
            throw new InvalidDataException("A Serious Engine texture's ANIM chunk does not open with ADAT.");
          at += 4;
          var count = _Int(data, ref at);
          if (count is < 0 or > 4096)
            throw new InvalidDataException($"A Serious Engine texture states {count} animations.");
          for (var i = 0; i < count; ++i) {
            if (at + 40 > data.Length)
              throw new InvalidDataException("A Serious Engine texture ends inside an animation.");
            var nameField = data.Slice(at, 32);
            var end = nameField.IndexOf((byte)0);
            var name = Encoding.ASCII.GetString(end < 0 ? nameField : nameField[..end]);
            at += 32;
            var seconds = BinaryPrimitives.ReadSingleLittleEndian(data[at..]);
            at += 4;
            var n = _Int(data, ref at);
            if (n is < 0 or > 65536 || at + 4L * n > data.Length)
              throw new InvalidDataException($"A Serious Engine animation states {n} frames.");
            var indices = new int[n];
            for (var f = 0; f < n; ++f)
              indices[f] = _Int(data, ref at);
            animations.Add(new(name, seconds, indices));
          }
          break;

        case "FXDT" or "FXBF" or "FXB2" or "BAST":
          throw new NotSupportedException($"This is a Serious Engine effect texture ({id}), whose pixels the engine generates as it runs; it holds no picture to read.");

        default:
          throw new InvalidDataException($"A Serious Engine texture holds a chunk '{id}' the engine does not know.");
      }
    }

    if (frames == null)
      throw new InvalidDataException("This Serious Engine texture holds no frames.");

    return new() {
      Version = version,
      Flags = flags,
      MexWidth = mexWidth,
      MexHeight = mexHeight,
      FineMipLevels = fine,
      FirstMipLevel = first,
      Width = width,
      Height = height,
      Compressed = compressed,
      Frames = frames,
      Animations = animations,
    };
  }

  private static int _Int(ReadOnlySpan<byte> data, ref int at) {
    if (at + 4 > data.Length)
      throw new InvalidDataException("A Serious Engine texture ends inside a chunk.");
    var value = BinaryPrimitives.ReadInt32LittleEndian(data[at..]);
    at += 4;
    return value;
  }

  private static void _RequireHeader(bool header, string id) {
    if (!header)
      throw new InvalidDataException($"A Serious Engine texture's {id} chunk comes before its TDAT.");
  }

  private static List<byte[]> _RawFrames(ReadOnlySpan<byte> data, ref int at, int width, int height, int count, bool alpha) {
    var pixels = width * height;
    var stride = alpha ? 4 : 3;
    if (at + (long)pixels * stride * count > data.Length)
      throw new InvalidDataException($"A Serious Engine texture's {count} frames of {width}x{height} run past its end.");
    var frames = new List<byte[]>(count);
    for (var f = 0; f < count; ++f) {
      var rgba = new byte[pixels * 4];
      for (var p = 0; p < pixels; ++p, at += stride) {
        rgba[4 * p] = data[at];
        rgba[4 * p + 1] = data[at + 1];
        rgba[4 * p + 2] = data[at + 2];
        rgba[4 * p + 3] = alpha ? data[at + 3] : (byte)255;
      }

      frames.Add(rgba);
    }

    return frames;
  }

  /// <summary>Version 3: each frame a chain of 16-bit mip-maps, of which the first is the picture.</summary>
  private static List<byte[]> _OldFrames(ReadOnlySpan<byte> data, ref int at, int width, int height, int count, int frameSize, bool alpha) {
    var pixels = width * height;
    if (frameSize < pixels * 2 || at + (long)frameSize * count > data.Length)
      throw new InvalidDataException($"A version 3 Serious Engine texture states frames of {frameSize} bytes for {width}x{height}.");
    var frames = new List<byte[]>(count);
    for (var f = 0; f < count; ++f) {
      var rgba = new byte[pixels * 4];
      for (var p = 0; p < pixels; ++p) {
        int v = BinaryPrimitives.ReadUInt16LittleEndian(data[(at + 2 * p)..]);
        int r, g, b, a;
        if (alpha) {
          r = (v >> 12) & 15;
          g = (v >> 8) & 15;
          b = (v >> 4) & 15;
          a = v & 15;
          (r, g, b, a) = (r * 17, g * 17, b * 17, a * 17);
        } else {
          r = (v >> 11) & 31;
          g = (v >> 6) & 31;
          b = (v >> 1) & 31;
          (r, g, b, a) = ((r << 3) | (r >> 2), (g << 3) | (g >> 2), (b << 3) | (b >> 2), 255);
        }

        rgba[4 * p] = (byte)r;
        rgba[4 * p + 1] = (byte)g;
        rgba[4 * p + 2] = (byte)b;
        rgba[4 * p + 3] = (byte)a;
      }

      at += frameSize;
      frames.Add(rgba);
    }

    return frames;
  }

  /// <summary><c>FRMC</c>: a total length, then each mip-map as a length and its DXT5 blocks; the first is the picture.</summary>
  private static byte[] _CompressedFrame(ReadOnlySpan<byte> data, ref int at, int width, int height) {
    if (at + 8 > data.Length)
      throw new InvalidDataException("A Serious Engine texture ends inside its FRMC chunk.");
    var total = BinaryPrimitives.ReadUInt32LittleEndian(data[at..]);
    var start = at + 4;
    if (start + (long)total > data.Length)
      throw new InvalidDataException($"A Serious Engine texture's compressed frames state {total} bytes, which it does not hold.");
    var expected = ((width + 3) / 4) * ((height + 3) / 4) * 16;
    var first = (int)BinaryPrimitives.ReadUInt32LittleEndian(data[start..]);
    if (first != expected)
      throw new InvalidDataException($"A Serious Engine texture's first compressed mip-map is {first} bytes; DXT5 at {width}x{height} is {expected}.");

    // Every mip-map's length is checked so that the chunk is known to be read as written.
    var mip = start;
    while (mip < start + total) {
      var length = BinaryPrimitives.ReadUInt32LittleEndian(data[mip..]);
      mip += 4 + (int)length;
      if (mip > start + total)
        throw new InvalidDataException("A Serious Engine texture's compressed mip-maps overrun their total.");
    }

    var rgba = new byte[width * height * 4];
    Bc3Decoder.DecodeImage(data.Slice(start + 4, first), width, height, rgba);
    at = start + (int)total;
    return rgba;
  }
}
