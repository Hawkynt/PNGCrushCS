using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using FileFormat.Bmp;
using FileFormat.Codecs.MsMpeg4;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Encodes Go2Meeting screen video as <c>G2M4</c> with Kempf tiles: a flat colour, or a palette of up
/// to 256 colours with the indices deflated.
/// </summary>
/// <remarks>
/// The package's own encoder — FFmpeg has none — writing what FFmpeg's <c>kempf_decode_tile</c>
/// reads. The first picture states the display information and every 64x64 tile; each picture after
/// it sends only the tiles that changed, and inside those marks the rows that did not change so the
/// decoder keeps them. A tile of 256 colours or fewer comes back exactly; one of more is mapped to a
/// 6x7x6 colour cube, which is the only loss. FFmpeg's decoder is the oracle that reads what it writes.
/// </remarks>
[VerifiedBy(ConformanceOracle.FFmpeg)]
public sealed class G2mVideoEncoder : IVideoCodecEncoder<G2mVideoEncoder> {

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("G2M4");

  private const int _TILE = 64;
  private const int _KEY_FRAME_INTERVAL = 120;

  private readonly MediaStreamInfo _requested;
  private readonly int _width;
  private readonly int _height;
  private byte[]? _last;
  private int _sinceKeyFrame;
  private MediaStreamInfo? _stream;

  private G2mVideoEncoder(MediaStreamInfo stream) {
    this._requested = stream;
    this._width = stream.Width;
    this._height = stream.Height;
  }

  public static string CodecName => G2mVideoDecoder.CodecName;

  public static CodecTag Codec => _Tag;

  public static bool Accepts(MediaStreamInfo stream) => stream.Codec.EqualsIgnoringCase(_Tag);

  public static G2mVideoEncoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException("Go2Meeting can only encode a video stream.");
    if (stream.Width < 16 || stream.Height < 16 || stream.Width > 255 * _TILE || stream.Height > 255 * _TILE)
      throw new NotSupportedException($"Go2Meeting pictures are 16 to {255 * _TILE} samples a side; {stream.Width}x{stream.Height} was asked for.");
    return new(stream);
  }

  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    ArgumentNullException.ThrowIfNull(frame);
    if (frame.Width != this._width || frame.Height != this._height)
      throw new InvalidDataException($"This Go2Meeting stream is {this._width}x{this._height}; a picture of {frame.Width}x{frame.Height} arrived.");

    var rgb = frame.ToRgb24();
    var key = this._last == null || this._sinceKeyFrame >= _KEY_FRAME_INTERVAL;
    using var output = new MemoryStream();
    output.Write("G2M4"u8);

    if (key) {
      var info = new byte[37];
      BinaryPrimitives.WriteUInt32BigEndian(info, (uint)this._width);
      BinaryPrimitives.WriteUInt32BigEndian(info.AsSpan(4), (uint)this._height);
      BinaryPrimitives.WriteUInt32BigEndian(info.AsSpan(8), 3); // Kempf
      BinaryPrimitives.WriteUInt32BigEndian(info.AsSpan(12), _TILE);
      BinaryPrimitives.WriteUInt32BigEndian(info.AsSpan(16), _TILE);
      info[20] = 32;
      BinaryPrimitives.WriteUInt32BigEndian(info.AsSpan(21), 0xFF0000);
      BinaryPrimitives.WriteUInt32BigEndian(info.AsSpan(25), 0xFF00);
      BinaryPrimitives.WriteUInt32BigEndian(info.AsSpan(29), 0xFF);
      _Chunk(output, 0xC8, info);
    }

    var tilesX = (this._width + _TILE - 1) / _TILE;
    var tilesY = (this._height + _TILE - 1) / _TILE;
    var any = false;
    for (var ty = 0; ty < tilesY; ++ty)
      for (var tx = 0; tx < tilesX; ++tx) {
        var tile = this._Tile(rgb, tx, ty, key);
        if (tile == null)
          continue;
        any = true;
        var body = new byte[tile.Length + 2];
        body[0] = (byte)tx;
        body[1] = (byte)ty;
        tile.CopyTo(body, 2);
        _Chunk(output, 0xC9, body);
      }

    if (!any)
      _Chunk(output, 0xCC, new byte[3]); // a packet is at least twelve bytes; this chunk means nothing

    this._last = rgb;
    this._sinceKeyFrame = key ? 1 : this._sinceKeyFrame + 1;
    packet = new(this._requested.Index, output.ToArray(), PresentationTimestamp: presentationTimestamp,
      DecodeTimestamp: presentationTimestamp, IsKeyFrame: key);
    return true;
  }

  public IEnumerable<CodedPacket> Flush() => [];

  public MediaStreamInfo DescribeStream() {
    if (this._stream != null)
      return this._stream;
    var header = new BitmapInfoHeader(
      HeaderSize: BitmapInfoHeader.StructSize, Width: this._width, Height: this._height, Planes: 1, BitsPerPixel: 24,
      Compression: unchecked((int)_Tag.Value), ImageSize: 0, XPixelsPerMeter: 0, YPixelsPerMeter: 0, ColorsUsed: 0, ImportantColors: 0);
    var format = new byte[BitmapInfoHeader.StructSize];
    header.WriteTo(format);
    return this._stream = new() {
      Index = this._requested.Index, Kind = MediaStreamKind.Video, Codec = _Tag, Handler = _Tag,
      CodecId = "V_MS/VFW/FOURCC", TimeBase = this._requested.TimeBase, FrameRate = this._requested.FrameRate,
      Width = this._width, Height = this._height, BitsPerPixel = 24, CodecPrivateData = format,
    };
  }

  private static void _Chunk(MemoryStream output, byte type, byte[] body) {
    Span<byte> head = stackalloc byte[5];
    BinaryPrimitives.WriteUInt32LittleEndian(head, (uint)body.Length + 1);
    head[4] = type;
    output.Write(head);
    output.Write(body);
  }

  /// <summary>One tile's Kempf data, or <c>null</c> where nothing in it changed.</summary>
  private byte[]? _Tile(byte[] rgb, int tx, int ty, bool key) {
    var x0 = tx * _TILE;
    var y0 = ty * _TILE;
    var width = Math.Min(this._width - x0, _TILE);
    var height = Math.Min(this._height - y0, _TILE);

    int Colour(byte[] picture, int x, int y) {
      var at = ((y0 + y) * this._width + x0 + x) * 3;
      return picture[at] << 16 | picture[at + 1] << 8 | picture[at + 2];
    }

    var changedRows = new bool[height];
    var changed = false;
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width && !changedRows[y]; ++x)
        if (key || Colour(rgb, x, y) != Colour(this._last!, x, y))
          changed = changedRows[y] = true;
    if (!changed)
      return null;

    var colours = new List<int>();
    var index = new Dictionary<int, int>();
    for (var y = 0; y < height && colours.Count <= 256; ++y)
      for (var x = 0; x < width && colours.Count <= 256; ++x)
        if (index.TryAdd(Colour(rgb, x, y), colours.Count))
          colours.Add(Colour(rgb, x, y));

    if (colours.Count == 1) {
      var c = colours[0];
      return [0 << 5, (byte)(c >> 16), (byte)(c >> 8), (byte)c];
    }

    if (colours.Count > 256) {
      colours.Clear();
      index.Clear();
      for (var r = 0; r < 6; ++r)
        for (var g = 0; g < 7; ++g)
          for (var b = 0; b < 6; ++b)
            colours.Add(r * 51 << 16 | (g * 255 / 6) << 8 | b * 51);
    }

    int IndexOf(int colour) {
      if (index.TryGetValue(colour, out var found))
        return found;
      var best = 0;
      var distance = int.MaxValue;
      for (var i = 0; i < colours.Count; ++i) {
        int dr = (colours[i] >> 16) - (colour >> 16), dg = ((colours[i] >> 8) & 0xFF) - ((colour >> 8) & 0xFF), db = (colours[i] & 0xFF) - (colour & 0xFF);
        var d = dr * dr + dg * dg + db * db;
        if (d < distance) {
          distance = d;
          best = i;
        }
      }

      index[colour] = best;
      return best;
    }

    var bits = colours.Count <= 2 ? 1 : colours.Count <= 4 ? 2 : colours.Count <= 16 ? 4 : 8;
    var alignedWidth = (width + 15) & ~15;
    var writer = new MsMpeg4BitWriter();
    for (var y = 0; y < height; ++y) {
      if (!changedRows[y]) {
        writer.Write(1, 8); // keep this row as it is
        continue;
      }

      writer.Write(0, 8);
      for (var x = 0; x < width; ++x)
        writer.Write(IndexOf(Colour(rgb, x, y)), bits);
      for (var x = width; x < alignedWidth; ++x)
        writer.Write(0, bits);
    }

    byte[] compressed;
    using (var deflated = new MemoryStream()) {
      using (var zlib = new ZLibStream(deflated, CompressionLevel.SmallestSize, leaveOpen: true))
        zlib.Write(writer.ToArray());
      compressed = deflated.ToArray();
    }

    if (compressed.Length > 0xFFFF)
      throw new InvalidOperationException("A Go2Meeting tile deflated past the sixteen bits its size is stated in.");

    using var tile = new MemoryStream();
    tile.WriteByte(2 << 5);
    tile.WriteByte((byte)(colours.Count - 1));
    foreach (var colour in colours) {
      tile.WriteByte((byte)(colour >> 16));
      tile.WriteByte((byte)(colour >> 8));
      tile.WriteByte((byte)colour);
    }

    tile.WriteByte((byte)(compressed.Length >> 8));
    tile.WriteByte((byte)compressed.Length);
    tile.Write(compressed);
    return tile.ToArray();
  }
}
