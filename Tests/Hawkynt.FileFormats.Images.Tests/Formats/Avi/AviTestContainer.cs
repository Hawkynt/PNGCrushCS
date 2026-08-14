using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.Avi.Tests;

/// <summary>
/// Builds AVI containers byte by byte so the reader can be tested without a sample file in the tree.
/// </summary>
/// <remarks>
/// The layout copied here is the one ffmpeg writes — <c>RIFF/AVI </c> holding <c>LIST hdrl</c> with
/// an <c>avih</c> and a <c>LIST strl</c> of <c>strh</c>+<c>strf</c>, then <c>LIST movi</c> with one
/// <c>00dc</c> per frame. The field offsets were read off a hexdump of ffmpeg's own output rather
/// than off the documentation, so a container built here is the shape the reader will meet.
/// </remarks>
internal static class AviTestContainer {

  public const int FRAME_WIDTH = 8;
  public const int FRAME_HEIGHT = 4;

  /// <summary>Assembles a video-only AVI around the given frame payloads.</summary>
  /// <param name="compression">The <c>biCompression</c> four-character code; 0 is BI_RGB.</param>
  /// <param name="width">Picture width in pixels.</param>
  /// <param name="height">The <c>biHeight</c> field verbatim — negative means the rows run top-down.</param>
  /// <param name="bitsPerPixel">The <c>biBitCount</c> field.</param>
  /// <param name="frames">One payload per <c>00dc</c> chunk; an empty one is written as an empty chunk.</param>
  /// <param name="palette">Palette entries as BGRx quads appended to the <c>strf</c>, or null for none.</param>
  /// <param name="streamType">The <c>strh.fccType</c>; anything but <c>vids</c> makes the stream a non-video one.</param>
  public static byte[] Build(
    string compression,
    int width,
    int height,
    short bitsPerPixel,
    IReadOnlyList<byte[]> frames,
    byte[]? palette = null,
    string streamType = "vids") {

    var strf = _BuildStreamFormat(compression, width, height, bitsPerPixel, palette);
    var strh = _BuildStreamHeader(compression, width, Math.Abs(height), frames.Count, streamType);
    var avih = _BuildMainHeader(width, Math.Abs(height), frames.Count);

    var strl = _List("strl", [_Chunk("strh", strh), _Chunk("strf", strf)]);
    var hdrl = _List("hdrl", [_Chunk("avih", avih), strl]);

    var movieParts = new List<byte[]>(frames.Count);
    foreach (var frame in frames)
      movieParts.Add(_Chunk("00dc", frame));
    var movi = _List("movi", movieParts);

    var body = new MemoryStream();
    body.Write("AVI "u8);
    body.Write(hdrl);
    body.Write(movi);

    var payload = body.ToArray();
    var file = new MemoryStream();
    file.Write("RIFF"u8);
    _WriteUInt32(file, (uint)payload.Length);
    file.Write(payload);
    return file.ToArray();
  }

  /// <summary>A bottom-up 24-bit DIB raster whose rows each carry one solid colour.</summary>
  /// <remarks>
  /// The rows differ so that a reader flipping the picture the wrong way is visible in the result
  /// rather than merely possible. Rows are padded to a four-byte boundary, which is what a DIB does
  /// and what ffmpeg's <c>rawvideo</c> chunks are.
  /// </remarks>
  public static byte[] BuildBgr24Raster(int width, int height, IReadOnlyList<(byte B, byte G, byte R)> rowColoursTopDown, bool bottomUp) {
    var stride = (width * 3 + 3) & ~3;
    var raster = new byte[stride * height];

    for (var row = 0; row < height; ++row) {
      var colour = rowColoursTopDown[row];
      var target = bottomUp ? height - 1 - row : row;
      var offset = target * stride;
      for (var x = 0; x < width; ++x) {
        raster[offset + x * 3] = colour.B;
        raster[offset + x * 3 + 1] = colour.G;
        raster[offset + x * 3 + 2] = colour.R;
      }
    }

    return raster;
  }

  /// <summary>An 8-bit indexed raster where every pixel of a row holds that row's palette index.</summary>
  public static byte[] BuildIndexed8Raster(int width, int height, bool bottomUp) {
    var stride = (width + 3) & ~3;
    var raster = new byte[stride * height];

    for (var row = 0; row < height; ++row) {
      var target = bottomUp ? height - 1 - row : row;
      var offset = target * stride;
      for (var x = 0; x < width; ++x)
        raster[offset + x] = (byte)row;
    }

    return raster;
  }

  private static byte[] _BuildMainHeader(int width, int height, int frameCount) {
    var data = new byte[56];
    var span = data.AsSpan();
    BinaryPrimitives.WriteUInt32LittleEndian(span, 100000);        // dwMicroSecPerFrame
    BinaryPrimitives.WriteUInt32LittleEndian(span[16..], (uint)frameCount); // dwTotalFrames
    BinaryPrimitives.WriteUInt32LittleEndian(span[24..], 1);       // dwStreams
    BinaryPrimitives.WriteUInt32LittleEndian(span[32..], (uint)width);
    BinaryPrimitives.WriteUInt32LittleEndian(span[36..], (uint)height);
    return data;
  }

  private static byte[] _BuildStreamHeader(string compression, int width, int height, int frameCount, string streamType) {
    var data = new byte[56];
    var span = data.AsSpan();
    _WriteFourCC(span, streamType);
    _WriteFourCC(span[4..], compression.Length == 4 ? compression : "\0\0\0\0");
    BinaryPrimitives.WriteUInt32LittleEndian(span[20..], 1);   // dwScale
    BinaryPrimitives.WriteUInt32LittleEndian(span[24..], 10);  // dwRate
    BinaryPrimitives.WriteUInt32LittleEndian(span[32..], (uint)frameCount); // dwLength
    BinaryPrimitives.WriteInt16LittleEndian(span[52..], (short)width);
    BinaryPrimitives.WriteInt16LittleEndian(span[54..], (short)height);
    return data;
  }

  private static byte[] _BuildStreamFormat(string compression, int width, int height, short bitsPerPixel, byte[]? palette) {
    var data = new byte[40 + (palette?.Length ?? 0)];
    var span = data.AsSpan();
    BinaryPrimitives.WriteInt32LittleEndian(span, 40);              // biSize
    BinaryPrimitives.WriteInt32LittleEndian(span[4..], width);
    BinaryPrimitives.WriteInt32LittleEndian(span[8..], height);
    BinaryPrimitives.WriteInt16LittleEndian(span[12..], 1);         // biPlanes
    BinaryPrimitives.WriteInt16LittleEndian(span[14..], bitsPerPixel);
    if (compression.Length == 4)
      _WriteFourCC(span[16..], compression);
    if (palette != null) {
      BinaryPrimitives.WriteInt32LittleEndian(span[32..], palette.Length / 4); // biClrUsed
      palette.CopyTo(data, 40);
    }

    return data;
  }

  private static void _WriteFourCC(Span<byte> destination, string value) {
    for (var i = 0; i < 4; ++i)
      destination[i] = (byte)value[i];
  }

  private static byte[] _Chunk(string id, byte[] payload) {
    var stream = new MemoryStream();
    stream.Write(_Ascii(id));
    _WriteUInt32(stream, (uint)payload.Length);
    stream.Write(payload);
    if ((payload.Length & 1) != 0)
      stream.WriteByte(0);
    return stream.ToArray();
  }

  private static byte[] _List(string type, IReadOnlyList<byte[]> elements) {
    var body = new MemoryStream();
    body.Write(_Ascii(type));
    foreach (var element in elements)
      body.Write(element);

    var payload = body.ToArray();
    var stream = new MemoryStream();
    stream.Write("LIST"u8);
    _WriteUInt32(stream, (uint)payload.Length);
    stream.Write(payload);
    return stream.ToArray();
  }

  private static byte[] _Ascii(string value) {
    var result = new byte[value.Length];
    for (var i = 0; i < value.Length; ++i)
      result[i] = (byte)value[i];
    return result;
  }

  private static void _WriteUInt32(Stream stream, uint value) {
    Span<byte> buffer = stackalloc byte[4];
    BinaryPrimitives.WriteUInt32LittleEndian(buffer, value);
    stream.Write(buffer);
  }
}
