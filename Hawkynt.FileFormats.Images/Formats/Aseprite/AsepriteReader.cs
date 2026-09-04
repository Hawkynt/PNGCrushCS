using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;

namespace FileFormat.Aseprite;

/// <summary>Reads an Aseprite sprite's first frame.</summary>
/// <remarks>
/// Layout per the format's own specification in <c>docs/ase-file-specs.md</c>: a 128-byte file
/// header, then one frame header per frame, then that frame's chunks. Only the chunks that carry a
/// picture are read — layers, palettes and cels; the rest (tags, slices, tilesets, user data) is
/// stepped over by its stated size.
///
/// <para>A sprite is a stack of layers, and the picture is what they compose to. Cels are composited
/// in layer order onto the canvas, each at the offset its cel chunk states, which is why a cel
/// carries its own width and height rather than the sprite's.</para>
/// </remarks>
public static class AsepriteReader {

  private const int _HeaderSize = 128;
  private const ushort _FileMagic = 0xA5E0;
  private const ushort _FrameMagic = 0xF1FA;

  private const ushort _ChunkOldPalette4 = 0x0004;
  private const ushort _ChunkOldPalette11 = 0x0011;
  private const ushort _ChunkLayer = 0x2004;
  private const ushort _ChunkCel = 0x2005;
  private const ushort _ChunkPalette = 0x2019;

  private const ushort _CelRaw = 0;
  private const ushort _CelLinked = 1;
  private const ushort _CelCompressed = 2;
  private const ushort _CelCompressedTilemap = 3;

  private const ushort _LayerTypeImage = 0;
  private const ushort _LayerVisible = 1;
  private const ushort _LayerBackground = 8;
  private const ushort _BlendNormal = 0;

  public static AsepriteFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Aseprite file not found.", file.FullName);

    return FromSpan(File.ReadAllBytes(file.FullName));
  }

  public static AsepriteFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    using var buffer = new MemoryStream();
    stream.CopyTo(buffer);
    return FromSpan(buffer.ToArray());
  }

  public static AsepriteFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static AsepriteFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < _HeaderSize)
      throw new InvalidDataException("Data too small for an Aseprite header.");
    if (BinaryPrimitives.ReadUInt16LittleEndian(data[4..]) != _FileMagic)
      throw new InvalidDataException("Not an Aseprite sprite: the header does not state 0xA5E0.");

    var frames = BinaryPrimitives.ReadUInt16LittleEndian(data[6..]);
    var width = BinaryPrimitives.ReadUInt16LittleEndian(data[8..]);
    var height = BinaryPrimitives.ReadUInt16LittleEndian(data[10..]);
    var depth = BinaryPrimitives.ReadUInt16LittleEndian(data[12..]);
    var transparentIndex = data[28];

    if (width == 0 || height == 0)
      throw new InvalidDataException($"Aseprite sprite states an empty canvas of {width}x{height}.");
    if (frames == 0)
      throw new InvalidDataException("Aseprite sprite states no frames.");
    if (depth is not (8 or 16 or 32))
      throw new NotSupportedException($"Aseprite colour depth {depth} is not one the format defines.");

    var colorDepth = (AsepriteColorDepth)depth;
    var bytesPerPixel = depth / 8;

    // The canvas the cels compose onto. Indexed sprites start at the transparent index, which is the
    // one the header nominates and not necessarily zero; the others start fully transparent.
    var canvas = new byte[checked(width * height * bytesPerPixel)];
    if (colorDepth == AsepriteColorDepth.Indexed && transparentIndex != 0)
      canvas.AsSpan().Fill(transparentIndex);

    byte[]? palette = null;
    var paletteCount = 0;
    var celsComposited = 0;

    // Layer order is the order the layer chunks appear in, so the visibility and blend mode of each
    // is collected as they are met and consulted when a cel names its layer.
    var layerVisible = new bool[64];
    var layerIsBackground = new bool[64];
    var layerCount = 0;

    var at = _HeaderSize;
    for (var frame = 0; frame < frames; ++frame) {
      if (at + 16 > data.Length)
        throw new InvalidDataException($"Aseprite frame {frame} runs past the end of the file.");

      var frameSize = BinaryPrimitives.ReadUInt32LittleEndian(data[at..]);
      if (BinaryPrimitives.ReadUInt16LittleEndian(data[(at + 4)..]) != _FrameMagic)
        throw new InvalidDataException($"Aseprite frame {frame} does not state 0xF1FA.");
      if (frameSize < 16 || at + frameSize > (uint)data.Length)
        throw new InvalidDataException($"Aseprite frame {frame} states a size of {frameSize} that does not fit the file.");

      // The old 16-bit chunk count is superseded by the 32-bit one when that is non-zero.
      var oldChunks = BinaryPrimitives.ReadUInt16LittleEndian(data[(at + 6)..]);
      var newChunks = BinaryPrimitives.ReadUInt32LittleEndian(data[(at + 12)..]);
      var chunks = newChunks != 0 ? newChunks : oldChunks;

      var chunkAt = at + 16;
      var frameEnd = at + (int)frameSize;
      for (var chunk = 0u; chunk < chunks; ++chunk) {
        if (chunkAt + 6 > frameEnd)
          break;

        var chunkSize = BinaryPrimitives.ReadUInt32LittleEndian(data[chunkAt..]);
        var chunkType = BinaryPrimitives.ReadUInt16LittleEndian(data[(chunkAt + 4)..]);
        if (chunkSize < 6 || chunkAt + chunkSize > (uint)frameEnd)
          throw new InvalidDataException($"Aseprite chunk states a size of {chunkSize} that does not fit its frame.");

        var body = data.Slice(chunkAt + 6, (int)chunkSize - 6);
        switch (chunkType) {
          case _ChunkLayer:
            _ReadLayer(body, ref layerVisible, ref layerIsBackground, ref layerCount);
            break;
          case _ChunkPalette:
            _ReadPalette(body, ref palette, ref paletteCount);
            break;
          case _ChunkOldPalette4:
          case _ChunkOldPalette11:
            // Superseded by the 0x2019 chunk, which Aseprite writes alongside it. Only read when the
            // new one has not been met, so a sprite from a version that wrote both is unaffected.
            if (palette == null)
              _ReadOldPalette(body, chunkType, ref palette, ref paletteCount);
            break;
          case _ChunkCel:
            // Only the first frame is a picture here; later frames are the animation.
            if (frame == 0 && _ReadCel(body, canvas, width, height, colorDepth, bytesPerPixel, transparentIndex, layerVisible, layerIsBackground, layerCount))
              ++celsComposited;
            break;
        }

        chunkAt += (int)chunkSize;
      }

      at += (int)frameSize;
    }

    if (celsComposited == 0)
      throw new InvalidDataException("Aseprite sprite's first frame carries no cel this reader could compose.");
    if (colorDepth == AsepriteColorDepth.Indexed && palette == null)
      throw new InvalidDataException("Aseprite indexed sprite carries no palette.");

    return new AsepriteFile {
      Width = width,
      Height = height,
      ColorDepth = colorDepth,
      PixelData = canvas,
      Palette = palette,
      PaletteColorCount = paletteCount,
      TransparentIndex = transparentIndex,
      FrameCount = frames,
    };
  }

  private static void _ReadLayer(ReadOnlySpan<byte> body, ref bool[] visible, ref bool[] background, ref int count) {
    if (body.Length < 16)
      throw new InvalidDataException("Aseprite layer chunk is too short.");

    var flags = BinaryPrimitives.ReadUInt16LittleEndian(body);
    var type = BinaryPrimitives.ReadUInt16LittleEndian(body[2..]);
    var blend = BinaryPrimitives.ReadUInt16LittleEndian(body[10..]);

    // A group layer holds no pixels of its own and a tilemap layer's cels are tile indices rather
    // than pixels; neither contributes to the composite here, but both still occupy a layer index,
    // so they are counted and marked invisible rather than skipped.
    var contributes = type == _LayerTypeImage && (flags & _LayerVisible) != 0;
    if (contributes && blend != _BlendNormal)
      throw new NotSupportedException(
        $"Aseprite layer uses blend mode {blend}; only normal blending is composed here rather than approximated.");

    if (count == visible.Length) {
      Array.Resize(ref visible, count * 2);
      Array.Resize(ref background, count * 2);
    }

    background[count] = (flags & _LayerBackground) != 0;
    visible[count++] = contributes;
  }

  private static void _ReadPalette(ReadOnlySpan<byte> body, ref byte[]? palette, ref int count) {
    if (body.Length < 20)
      throw new InvalidDataException("Aseprite palette chunk is too short.");

    var newSize = BinaryPrimitives.ReadUInt32LittleEndian(body);
    var first = BinaryPrimitives.ReadUInt32LittleEndian(body[4..]);
    var last = BinaryPrimitives.ReadUInt32LittleEndian(body[8..]);
    if (newSize > 256 || first > 255 || last > 255 || last < first)
      throw new InvalidDataException($"Aseprite palette states entries {first}..{last} of {newSize}.");

    palette ??= new byte[256 * 3];
    count = Math.Max(count, (int)newSize);

    var at = 20;
    for (var entry = first; entry <= last; ++entry) {
      if (at + 6 > body.Length)
        throw new InvalidDataException("Aseprite palette chunk ends inside an entry.");

      var flags = BinaryPrimitives.ReadUInt16LittleEndian(body[at..]);
      palette[entry * 3] = body[at + 2];
      palette[entry * 3 + 1] = body[at + 3];
      palette[entry * 3 + 2] = body[at + 4];
      at += 6;

      // A named entry carries a length-prefixed name after its colour.
      if ((flags & 1) == 0)
        continue;

      if (at + 2 > body.Length)
        throw new InvalidDataException("Aseprite palette entry states a name that does not fit.");
      at += 2 + BinaryPrimitives.ReadUInt16LittleEndian(body[at..]);
    }
  }

  private static void _ReadOldPalette(ReadOnlySpan<byte> body, ushort chunkType, ref byte[]? palette, ref int count) {
    if (body.Length < 2)
      throw new InvalidDataException("Aseprite old palette chunk is too short.");

    // The 0x0004 chunk states colours in six bits per primary, the 0x0011 one in eight.
    var scale = chunkType == _ChunkOldPalette4 ? 1 : 4;
    var packets = BinaryPrimitives.ReadUInt16LittleEndian(body);
    palette ??= new byte[256 * 3];

    var at = 2;
    var entry = 0;
    for (var packet = 0; packet < packets; ++packet) {
      if (at + 2 > body.Length)
        throw new InvalidDataException("Aseprite old palette chunk ends inside a packet.");

      entry += body[at];
      var packetCount = body[at + 1] == 0 ? 256 : body[at + 1];
      at += 2;

      for (var i = 0; i < packetCount; ++i, ++entry) {
        if (at + 3 > body.Length || entry > 255)
          throw new InvalidDataException("Aseprite old palette packet states more entries than fit.");

        palette[entry * 3] = (byte)Math.Min(255, body[at] * scale);
        palette[entry * 3 + 1] = (byte)Math.Min(255, body[at + 1] * scale);
        palette[entry * 3 + 2] = (byte)Math.Min(255, body[at + 2] * scale);
        at += 3;
      }

      count = Math.Max(count, entry);
    }
  }

  private static bool _ReadCel(
    ReadOnlySpan<byte> body,
    byte[] canvas,
    int width,
    int height,
    AsepriteColorDepth depth,
    int bytesPerPixel,
    byte transparentIndex,
    bool[] layerVisible,
    bool[] layerIsBackground,
    int layerCount
  ) {
    if (body.Length < 16)
      throw new InvalidDataException("Aseprite cel chunk is too short.");

    var layer = BinaryPrimitives.ReadUInt16LittleEndian(body);
    var x = BinaryPrimitives.ReadInt16LittleEndian(body[2..]);
    var y = BinaryPrimitives.ReadInt16LittleEndian(body[4..]);
    var celType = BinaryPrimitives.ReadUInt16LittleEndian(body[7..]);

    if (celType is _CelLinked)
      // A linked cel names a frame to take its pixels from. Only the first frame is composed here,
      // and it cannot link backwards, so there is nothing for this to contribute.
      return false;
    if (celType is _CelCompressedTilemap)
      throw new NotSupportedException("Aseprite tilemap cels reference a tileset rather than carrying pixels.");
    if (celType is not (_CelRaw or _CelCompressed))
      throw new NotSupportedException($"Aseprite cel type {celType} is not one the format defines.");

    if (layer >= layerCount || !layerVisible[layer])
      return false;

    if (body.Length < 20)
      throw new InvalidDataException("Aseprite cel chunk states no size.");

    var celWidth = BinaryPrimitives.ReadUInt16LittleEndian(body[16..]);
    var celHeight = BinaryPrimitives.ReadUInt16LittleEndian(body[18..]);
    if (celWidth == 0 || celHeight == 0)
      return false;

    var expected = checked(celWidth * celHeight * bytesPerPixel);
    var payload = body[20..];
    var pixels = celType == _CelRaw ? payload.ToArray() : _Inflate(payload, expected);
    if (pixels.Length < expected)
      throw new InvalidDataException($"Aseprite cel carries {pixels.Length} bytes for a {celWidth}x{celHeight} area needing {expected}.");

    // The nominated index only means transparency on a layer that is not the background. Aseprite
    // marks an opaque picture's layer as background precisely so index zero stays a colour there.
    _Composite(
      canvas, width, height, pixels, celWidth, celHeight, x, y, depth, bytesPerPixel,
      layerIsBackground[layer] ? -1 : transparentIndex);
    return true;
  }

  private static byte[] _Inflate(ReadOnlySpan<byte> payload, int expected) {
    using var input = new MemoryStream(payload.ToArray());
    using var zlib = new ZLibStream(input, CompressionMode.Decompress);
    var result = new byte[expected];
    var read = 0;
    while (read < expected) {
      var got = zlib.Read(result, read, expected - read);
      if (got <= 0)
        break;
      read += got;
    }

    if (read < expected)
      throw new InvalidDataException($"Aseprite cel inflated to {read} bytes where {expected} were stated.");
    return result;
  }

  private static void _Composite(
    byte[] canvas,
    int width,
    int height,
    byte[] cel,
    int celWidth,
    int celHeight,
    int originX,
    int originY,
    AsepriteColorDepth depth,
    int bytesPerPixel,
    int transparentIndex
  ) {
    for (var row = 0; row < celHeight; ++row) {
      var targetY = originY + row;
      if (targetY < 0 || targetY >= height)
        continue;

      for (var column = 0; column < celWidth; ++column) {
        var targetX = originX + column;
        if (targetX < 0 || targetX >= width)
          continue;

        var source = (row * celWidth + column) * bytesPerPixel;
        var target = (targetY * width + targetX) * bytesPerPixel;

        switch (depth) {
          case AsepriteColorDepth.Indexed: {
            // An indexed sprite has no partial coverage: a pixel is either the transparent index or
            // it replaces what is under it.
            var index = cel[source];
            if (index != transparentIndex)
              canvas[target] = index;
            break;
          }

          case AsepriteColorDepth.Grayscale:
            _Over(canvas, target, cel[source], cel[source], cel[source], cel[source + 1], grayscale: true);
            break;

          default:
            _Over(canvas, target, cel[source], cel[source + 1], cel[source + 2], cel[source + 3], grayscale: false);
            break;
        }
      }
    }
  }

  /// <summary>Source-over composition of one pixel, which is what a normal-blend layer does.</summary>
  private static void _Over(byte[] canvas, int target, byte r, byte g, byte b, byte alpha, bool grayscale) {
    if (alpha == 0)
      return;

    var backdropAlpha = canvas[target + (grayscale ? 1 : 3)];
    if (alpha == 255 || backdropAlpha == 0) {
      if (grayscale) {
        canvas[target] = r;
        canvas[target + 1] = alpha;
      } else {
        canvas[target] = r;
        canvas[target + 1] = g;
        canvas[target + 2] = b;
        canvas[target + 3] = alpha;
      }

      return;
    }

    // out = src + dst * (1 - srcAlpha), with the result un-premultiplied again so the canvas stays
    // in the straight-alpha form the file states.
    var outAlpha = alpha + backdropAlpha * (255 - alpha) / 255;
    if (outAlpha == 0)
      return;

    byte Mix(byte source, byte backdrop)
      => (byte)((source * alpha * 255 + backdrop * backdropAlpha * (255 - alpha)) / (255 * outAlpha));

    if (grayscale) {
      canvas[target] = Mix(r, canvas[target]);
      canvas[target + 1] = (byte)outAlpha;
      return;
    }

    canvas[target] = Mix(r, canvas[target]);
    canvas[target + 1] = Mix(g, canvas[target + 1]);
    canvas[target + 2] = Mix(b, canvas[target + 2]);
    canvas[target + 3] = (byte)outAlpha;
  }
}
