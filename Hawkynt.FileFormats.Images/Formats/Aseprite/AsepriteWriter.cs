using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;

namespace FileFormat.Aseprite;

/// <summary>Assembles an Aseprite sprite of one frame and one layer.</summary>
/// <remarks>
/// A sprite this writes is the plainest one the format allows: a single visible, normal-blend image
/// layer holding a single compressed cel that covers the canvas. That is what a picture converted to
/// a sprite is; the layer stack, animation and tilesets Aseprite files can carry have no counterpart
/// in a single raster and are not invented here.
/// </remarks>
public static class AsepriteWriter {

  private const int _HeaderSize = 128;
  private const ushort _FileMagic = 0xA5E0;
  private const ushort _FrameMagic = 0xF1FA;
  private const ushort _ChunkLayer = 0x2004;
  private const ushort _ChunkCel = 0x2005;
  private const ushort _ChunkPalette = 0x2019;
  private const ushort _CelCompressed = 2;

  /// <summary>Aseprite reads a sprite whose header states this version or later.</summary>
  private const uint _FileFlagsLayerOpacityValid = 1;

  public static byte[] ToBytes(AsepriteFile file) {
    ArgumentNullException.ThrowIfNull(file);
    if (file.Width is <= 0 or > ushort.MaxValue || file.Height is <= 0 or > ushort.MaxValue)
      throw new ArgumentException($"An Aseprite canvas of {file.Width}x{file.Height} is outside what the header can state.", nameof(file));

    var bytesPerPixel = (int)file.ColorDepth / 8;
    var expected = checked(file.Width * file.Height * bytesPerPixel);
    if (file.PixelData is null || file.PixelData.Length < expected)
      throw new ArgumentException($"Aseprite pixel data holds {file.PixelData?.Length ?? 0} bytes where {expected} are needed.", nameof(file));

    var indexed = file.ColorDepth == AsepriteColorDepth.Indexed;
    if (indexed && file.Palette is null)
      throw new ArgumentException("An indexed Aseprite sprite needs a palette.", nameof(file));

    using var buffer = new MemoryStream();

    // The header is written last, once the frame's size is known; reserve its bytes for now.
    buffer.Write(new byte[_HeaderSize]);

    var frameStart = (int)buffer.Position;
    buffer.Write(new byte[16]);

    var chunks = 0;
    _WriteLayerChunk(buffer);
    ++chunks;

    var paletteCount = 0;
    if (indexed) {
      paletteCount = Math.Clamp(file.PaletteColorCount > 0 ? file.PaletteColorCount : file.Palette!.Length / 3, 1, 256);
      _WritePaletteChunk(buffer, file.Palette!, paletteCount);
      ++chunks;
    }

    _WriteCelChunk(buffer, file.PixelData, file.Width, file.Height, bytesPerPixel);
    ++chunks;

    var result = buffer.ToArray();
    var frameSize = result.Length - frameStart;

    // Frame header.
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(frameStart), (uint)frameSize);
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(frameStart + 4), _FrameMagic);
    // The 16-bit count is kept in step with the 32-bit one; a reader that only knows the old field
    // still sees the right number as long as it fits.
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(frameStart + 6), (ushort)chunks);
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(frameStart + 8), 100);
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(frameStart + 12), (uint)chunks);

    // File header.
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(0), (uint)result.Length);
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(4), _FileMagic);
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(6), 1);
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(8), (ushort)file.Width);
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(10), (ushort)file.Height);
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(12), (ushort)file.ColorDepth);
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(14), _FileFlagsLayerOpacityValid);
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(18), 100);
    result[28] = indexed ? file.TransparentIndex : (byte)0;
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(32), (ushort)paletteCount);
    result[34] = 1;
    result[35] = 1;

    return result;
  }

  private static void _WriteLayerChunk(MemoryStream buffer) {
    var name = "Background"u8;

    // flags(2) type(2) childLevel(2) defaultWidth(2) defaultHeight(2) blendMode(2) opacity(1)
    // reserved(3) name(2 + bytes)
    // Visible and background. The background flag is what says the nominated transparent index is an
    // ordinary colour on this layer, which is what an opaque picture needs: without it, every pixel
    // holding index zero would read back as nothing.
    Span<byte> body = stackalloc byte[18 + 10];
    BinaryPrimitives.WriteUInt16LittleEndian(body, 1 | 8);
    BinaryPrimitives.WriteUInt16LittleEndian(body[2..], 0); // image layer
    BinaryPrimitives.WriteUInt16LittleEndian(body[10..], 0); // normal blend
    body[12] = 255; // opacity
    BinaryPrimitives.WriteUInt16LittleEndian(body[16..], (ushort)name.Length);
    name.CopyTo(body[18..]);

    _WriteChunk(buffer, _ChunkLayer, body);
  }

  private static void _WritePaletteChunk(MemoryStream buffer, byte[] palette, int count) {
    var body = new byte[20 + count * 6];
    BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(0), (uint)count);
    BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(4), 0);
    BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(8), (uint)(count - 1));

    for (var entry = 0; entry < count; ++entry) {
      var at = 20 + entry * 6;
      var source = entry * 3;
      // flags stay zero: no entry is named.
      body[at + 2] = source + 2 < palette.Length ? palette[source] : (byte)0;
      body[at + 3] = source + 2 < palette.Length ? palette[source + 1] : (byte)0;
      body[at + 4] = source + 2 < palette.Length ? palette[source + 2] : (byte)0;
      body[at + 5] = 255;
    }

    _WriteChunk(buffer, _ChunkPalette, body);
  }

  private static void _WriteCelChunk(MemoryStream buffer, byte[] pixels, int width, int height, int bytesPerPixel) {
    var deflated = _Deflate(pixels, checked(width * height * bytesPerPixel));

    // layer(2) x(2) y(2) opacity(1) type(2) zIndex(2) reserved(5) width(2) height(2) data
    var body = new byte[20 + deflated.Length];
    BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(0), 0);
    BinaryPrimitives.WriteInt16LittleEndian(body.AsSpan(2), 0);
    BinaryPrimitives.WriteInt16LittleEndian(body.AsSpan(4), 0);
    body[6] = 255;
    BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(7), _CelCompressed);
    BinaryPrimitives.WriteInt16LittleEndian(body.AsSpan(9), 0);
    BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(16), (ushort)width);
    BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(18), (ushort)height);
    deflated.CopyTo(body.AsSpan(20));

    _WriteChunk(buffer, _ChunkCel, body);
  }

  private static byte[] _Deflate(byte[] pixels, int length) {
    using var compressed = new MemoryStream();
    using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
      zlib.Write(pixels, 0, length);

    return compressed.ToArray();
  }

  private static void _WriteChunk(MemoryStream buffer, ushort type, ReadOnlySpan<byte> body) {
    Span<byte> header = stackalloc byte[6];
    BinaryPrimitives.WriteUInt32LittleEndian(header, (uint)(6 + body.Length));
    BinaryPrimitives.WriteUInt16LittleEndian(header[4..], type);
    buffer.Write(header);
    buffer.Write(body);
  }
}
