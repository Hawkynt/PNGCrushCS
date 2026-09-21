using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileFormat.Codecs.Vqa;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Encodes Westwood VQA (<c>WSVQ</c>) in the defined version-1/version-2 palettised forms or the
/// version-2/version-3 15-bit HiColor form.
/// </summary>
/// <remarks>
/// Palettised input is lossless with respect to indices and every palette colour that VQA's six-bit
/// VGA palette can represent. Each packet carries a full palette and full codebook, and its VPT table
/// is wrapped in a literal-only format80 stream. That is intentionally conservative: it produces true
/// independently decodable pictures instead of coupling an encoder's correctness to codebook rate
/// control heuristics.
/// <para/>
/// HiColor input is reduced to the format's RGB555 precision. It does use VQA's inter-frame syntax:
/// unchanged blocks become VPTR skip commands, while changed blocks reference a freshly supplied full
/// codebook. Overlay streams additionally use the alpha-skip vector command for transparent source
/// pixels. A packet using either block skips or alpha skips is marked non-key because it needs the
/// previous framebuffer; there are no forward references or B-pictures in VQA.
/// <para/>
/// Written directly from Gordan Ugarkovic's VQA_INFO/HC_VQA bitstream descriptions and checked against
/// FFmpeg's LGPL decoder. No third-party encoder code or dependency is used.
/// </remarks>
[VerifiedBy(ConformanceOracle.FFmpeg)]
public sealed class VqaVideoEncoder : IVideoCodecEncoder<VqaVideoEncoder> {

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("WSVQ");
  private const int _HEADER_LENGTH = 42;
  private const int _PALETTE_ENTRIES = 256;
  private const int _PALETTE_BYTES = _PALETTE_ENTRIES * 3;
  private const int _MAX_HICOLOR_VECTORS = 0x2000;
  private const ushort _OVERLAY_FLAG = 1 << 2;
  private const ushort _HICOLOR_FLAG = 1 << 4;

  private readonly MediaStreamInfo _requested;
  private readonly byte[] _header;
  private readonly int _version;
  private readonly bool _highColour;
  private readonly bool _overlay;
  private readonly int _width;
  private readonly int _height;
  private readonly int _blockWidth;
  private readonly int _blockHeight;
  private readonly int _blocksWide;
  private readonly int _blocksHigh;
  private ushort[]? _previousHighColour;

  private readonly record struct VectorKey(UInt128 Low, UInt128 High);

  public static string CodecName => "Westwood VQA Video";
  public static CodecTag Codec => _Tag;

  public static VqaVideoEncoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException("Westwood VQA can only encode video streams.");
    if (stream.Width is <= 0 or > ushort.MaxValue || stream.Height is <= 0 or > ushort.MaxValue)
      throw new NotSupportedException($"VQA dimensions must fit unsigned 16-bit header fields; {stream.Width}x{stream.Height} was supplied.");

    byte[] header;
    int version;
    bool highColour;
    int blockWidth;
    int blockHeight;

    if (stream.CodecPrivateData.Length >= _HEADER_LENGTH) {
      header = stream.CodecPrivateData[.._HEADER_LENGTH].ToArray();
      version = BinaryPrimitives.ReadUInt16LittleEndian(header);
      highColour = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(14)) == 0;
      blockWidth = header[10];
      blockHeight = header[11];

      var headerWidth = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(6));
      var headerHeight = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(8));
      if (headerWidth != stream.Width || headerHeight != stream.Height)
        throw new InvalidDataException(
          $"VQA CodecPrivateData describes {headerWidth}x{headerHeight}, but the requested stream is {stream.Width}x{stream.Height}.");
    } else {
      highColour = stream.BitsPerPixel != 8;
      version = highColour ? 3 : 2;
      blockWidth = 4;
      blockHeight = 2;
      header = _NewHeader(stream, version, highColour, blockWidth, blockHeight);
    }

    if (version is < 1 or > 3)
      throw new NotSupportedException($"VQA version {version} is not defined; versions 1 through 3 are supported.");
    if (highColour && version == 1)
      throw new NotSupportedException("Version-1 VQA is the original palettised form; no version-1 HiColor syntax is defined.");
    if (!highColour && version == 3)
      throw new NotSupportedException("Version-3 VQA is the HiColor form and cannot carry an eight-bit palette stream.");
    if (blockWidth != 4 || blockHeight is not (2 or 4))
      throw new NotSupportedException($"VQA encoding is defined for 4x2 or 4x4 vectors, not {blockWidth}x{blockHeight}.");
    if (stream.Width % blockWidth != 0 || stream.Height % blockHeight != 0)
      throw new NotSupportedException($"{stream.Width}x{stream.Height} is not an exact grid of {blockWidth}x{blockHeight} VQA vectors.");
    if (stream.BitsPerPixel != 0 && highColour && stream.BitsPerPixel is not (15 or 16 or 24 or 32))
      throw new NotSupportedException($"HiColor VQA produces 15-bit RGB; {stream.BitsPerPixel} requested bits per pixel is incompatible.");
    if (stream.BitsPerPixel != 0 && !highColour && stream.BitsPerPixel != 8)
      throw new NotSupportedException($"Palettised VQA produces 8-bit indices; {stream.BitsPerPixel} requested bits per pixel is incompatible.");

    return new(stream, header, version, highColour, blockWidth, blockHeight);
  }

  private VqaVideoEncoder(MediaStreamInfo requested, byte[] header, int version, bool highColour, int blockWidth, int blockHeight) {
    this._requested = requested;
    this._header = header;
    this._version = version;
    this._highColour = highColour;
    this._overlay = (BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(2)) & _OVERLAY_FLAG) != 0;
    this._width = requested.Width;
    this._height = requested.Height;
    this._blockWidth = blockWidth;
    this._blockHeight = blockHeight;
    this._blocksWide = this._width / blockWidth;
    this._blocksHigh = this._height / blockHeight;
  }

  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    ArgumentNullException.ThrowIfNull(frame);
    if (frame.Width != this._width || frame.Height != this._height)
      throw new InvalidDataException($"VQA geometry is fixed at {this._width}x{this._height}; received {frame.Width}x{frame.Height}.");
    if (!frame.HasEnoughPixelData)
      throw new InvalidDataException("The source RawImage does not contain enough pixel data for its declared format and dimensions.");

    var (data, keyFrame) = this._highColour ? this._EncodeHighColour(frame) : this._EncodePaletted(frame);
    packet = new(
      StreamIndex: this._requested.Index,
      Data: data,
      PresentationTimestamp: presentationTimestamp,
      DecodeTimestamp: presentationTimestamp,
      Duration: 1,
      IsKeyFrame: keyFrame);
    return true;
  }

  public MediaStreamInfo DescribeStream() => new() {
    Index = this._requested.Index,
    Kind = MediaStreamKind.Video,
    Codec = _Tag,
    Handler = _Tag,
    TimeBase = this._requested.TimeBase,
    FrameRate = this._requested.FrameRate,
    DeclaredFrameCount = this._requested.DeclaredFrameCount,
    Width = this._width,
    Height = this._height,
    BitsPerPixel = this._highColour ? 15 : 8,
    CodecPrivateData = this._header,
    Language = this._requested.Language,
    Name = this._requested.Name,
  };

  private static byte[] _NewHeader(MediaStreamInfo stream, int version, bool highColour, int blockWidth, int blockHeight) {
    var header = new byte[_HEADER_LENGTH];
    BinaryPrimitives.WriteUInt16LittleEndian(header, checked((ushort)version));
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(2), highColour ? _HICOLOR_FLAG : (ushort)0);
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(6), checked((ushort)stream.Width));
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(8), checked((ushort)stream.Height));
    header[10] = checked((byte)blockWidth);
    header[11] = checked((byte)blockHeight);

    var rate = 15;
    if (stream.FrameRate.IsKnown && stream.FrameRate.Denominator > 0 && stream.FrameRate.Numerator % stream.FrameRate.Denominator == 0) {
      var integerRate = stream.FrameRate.Numerator / stream.FrameRate.Denominator;
      if (integerRate is > 0 and <= byte.MaxValue)
        rate = (int)integerRate;
    }
    header[12] = (byte)rate;
    header[13] = highColour ? (byte)0 : (byte)8;
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(14), highColour ? (ushort)0 : (ushort)_PALETTE_ENTRIES);
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(16), checked((ushort)Math.Min(ushort.MaxValue, stream.Width / blockWidth * (long)(stream.Height / blockHeight))));
    return header;
  }

  // ============================================================================================
  // Palettised VQA
  // ============================================================================================

  private (byte[] Data, bool KeyFrame) _EncodePaletted(RawImage frame) {
    if (frame.Format != PixelFormat.Indexed8)
      throw new NotSupportedException(
        $"Palettised VQA takes Indexed8 pictures; {frame.Format} would require choosing a 256-colour quantisation outside the codec syntax.");

    var palette = this._EncodePalette(frame);
    var pixels = frame.PixelData.AsSpan(0, checked(this._width * this._height));
    this._ValidateIndices(pixels, frame.PaletteCount);

    var blockCount = this._blocksWide * this._blocksHigh;
    var blockArea = this._blockWidth * this._blockHeight;
    var codebook = new List<byte>(Math.Min(blockCount, 4096) * blockArea);
    var entries = new Dictionary<VectorKey, int>();
    var table = new byte[blockCount * 2];
    var maxEntries = this._version == 1 || this._blockHeight == 2 ? 0x0f00 : 0xff00;
    var solidSentinel = this._blockHeight == 4 ? 0xff : 0x0f;

    for (var block = 0; block < blockCount; ++block) {
      var bx = block % this._blocksWide;
      var by = block / this._blocksWide;
      var first = pixels[(by * this._blockHeight) * this._width + bx * this._blockWidth];
      var solid = true;
      for (var yy = 0; yy < this._blockHeight && solid; ++yy)
        for (var xx = 0; xx < this._blockWidth; ++xx)
          if (pixels[(by * this._blockHeight + yy) * this._width + bx * this._blockWidth + xx] != first) {
            solid = false;
            break;
          }

      if (solid) {
        if (this._version == 1) {
          table[block * 2] = (byte)(255 - first);
          table[block * 2 + 1] = 0xff;
        } else {
          table[block] = first;
          table[blockCount + block] = (byte)solidSentinel;
        }
        continue;
      }

      var key = this._PalettedVectorKey(pixels, bx, by);
      if (!entries.TryGetValue(key, out var entry)) {
        entry = entries.Count;
        if (entry >= maxEntries)
          throw new NotSupportedException(
            $"This frame needs more than {maxEntries} distinct non-solid VQA vectors, which cannot be indexed by this VQA form.");
        entries.Add(key, entry);
        this._AppendPalettedVector(codebook, pixels, bx, by);
      }

      if (this._version == 1) {
        var pointer = checked(entry << 3);
        table[block * 2] = (byte)pointer;
        table[block * 2 + 1] = (byte)(pointer >> 8);
      } else {
        table[block] = (byte)entry;
        table[blockCount + block] = (byte)(entry >> 8);
      }
    }

    using var output = new MemoryStream();
    _WriteChunk(output, "CBF0", codebook.ToArray());
    _WriteChunk(output, "CPL0", palette);
    _WriteChunk(output, "VPTZ", VqaFormat80.CompressLiterals(table));
    return (output.ToArray(), true);
  }

  private byte[] _EncodePalette(RawImage frame) {
    if (frame.Palette == null || frame.PaletteCount <= 0)
      throw new InvalidDataException("An Indexed8 VQA picture needs a palette.");
    if (frame.PaletteCount > _PALETTE_ENTRIES)
      throw new InvalidDataException($"VQA's palettised form holds at most 256 colours, not {frame.PaletteCount}.");
    if (frame.Palette.Length < frame.PaletteCount * 3)
      throw new InvalidDataException($"The picture declares {frame.PaletteCount} palette entries but carries fewer RGB triples.");

    var result = new byte[_PALETTE_BYTES];
    for (var i = 0; i < frame.PaletteCount * 3; ++i) {
      var value = frame.Palette[i];
      var six = value >> 2;
      if (ChannelScaling.Expand6(six) != value)
        throw new NotSupportedException(
          $"Palette byte {i} has value {value}, which VQA's six-bit VGA palette cannot represent exactly. Quantise the palette explicitly before encoding.");
      result[i] = (byte)six;
    }
    return result;
  }

  private void _ValidateIndices(ReadOnlySpan<byte> pixels, int paletteCount) {
    for (var i = 0; i < pixels.Length; ++i)
      if (pixels[i] >= paletteCount)
        throw new InvalidDataException(
          $"Pixel {i % this._width},{i / this._width} uses palette index {pixels[i]}, but the picture declares only {paletteCount} colours.");
  }

  private VectorKey _PalettedVectorKey(ReadOnlySpan<byte> pixels, int bx, int by) {
    UInt128 low = 0;
    UInt128 high = 0;
    var n = 0;
    for (var yy = 0; yy < this._blockHeight; ++yy)
      for (var xx = 0; xx < this._blockWidth; ++xx, ++n) {
        var value = pixels[(by * this._blockHeight + yy) * this._width + bx * this._blockWidth + xx];
        if (n < 16)
          low |= (UInt128)value << (n * 8);
        else
          high |= (UInt128)value << ((n - 16) * 8);
      }
    return new(low, high);
  }

  private void _AppendPalettedVector(List<byte> codebook, ReadOnlySpan<byte> pixels, int bx, int by) {
    for (var yy = 0; yy < this._blockHeight; ++yy)
      for (var xx = 0; xx < this._blockWidth; ++xx)
        codebook.Add(pixels[(by * this._blockHeight + yy) * this._width + bx * this._blockWidth + xx]);
  }

  // ============================================================================================
  // HiColor VQA
  // ============================================================================================

  private (byte[] Data, bool KeyFrame) _EncodeHighColour(RawImage frame) {
    var target = this._PackHighColour(frame);
    var next = this._previousHighColour?.ToArray() ?? new ushort[target.Length];
    var codebook = new List<byte>();
    var entries = new Dictionary<VectorKey, int>();
    using var pointers = new MemoryStream();
    var usesReference = false;

    for (var by = 0; by < this._blocksHigh; ++by) {
      var skipCount = 0;
      for (var bx = 0; bx < this._blocksWide; ++bx) {
        var state = this._ClassifyHighColourBlock(target, next, bx, by);
        if (state.Skip) {
          ++skipCount;
          usesReference |= this._previousHighColour != null || state.HasTransparency;
          continue;
        }

        _WriteSkip(pointers, ref skipCount);
        var key = this._HighColourVectorKey(target, bx, by);
        if (!entries.TryGetValue(key, out var entry)) {
          entry = entries.Count;
          if (entry >= _MAX_HICOLOR_VECTORS)
            throw new NotSupportedException("A HiColor VQA frame needs more than 8192 distinct changed vectors.");
          entries.Add(key, entry);
          this._AppendHighColourVector(codebook, target, bx, by);
        }

        var type = state.HasTransparency ? 4 : 3;
        usesReference |= state.HasTransparency;
        Span<byte> command = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(command, (ushort)((type << 13) | entry));
        pointers.Write(command);
      }
      _WriteSkip(pointers, ref skipCount);
    }

    using var output = new MemoryStream();
    if (codebook.Count > 0)
      _WriteChunk(output, "CBF0", codebook.ToArray());
    _WriteChunk(output, "VPTR", pointers.ToArray());

    this._previousHighColour = next;
    return (output.ToArray(), !usesReference);
  }

  private ushort[] _PackHighColour(RawImage frame) {
    var result = new ushort[checked(this._width * this._height)];
    if (this._overlay) {
      var rgba = frame.ToRgba32();
      for (var i = 0; i < result.Length; ++i) {
        var alpha = rgba[i * 4 + 3];
        if (alpha is not (0 or 255))
          throw new NotSupportedException(
            $"VQA overlay transparency is one bit; pixel {i % this._width},{i / this._width} has alpha {alpha}. Quantise alpha explicitly to 0 or 255.");
        result[i] = (ushort)(_PackRgb555(rgba[i * 4], rgba[i * 4 + 1], rgba[i * 4 + 2]) | (alpha == 0 ? 0x8000 : 0));
      }
    } else {
      var rgb = frame.ToRgb24();
      for (var i = 0; i < result.Length; ++i)
        result[i] = _PackRgb555(rgb[i * 3], rgb[i * 3 + 1], rgb[i * 3 + 2]);
    }
    return result;
  }

  private readonly record struct BlockState(bool Skip, bool HasTransparency);

  private BlockState _ClassifyHighColourBlock(ReadOnlySpan<ushort> target, Span<ushort> next, int bx, int by) {
    var unchanged = this._previousHighColour != null;
    var anyOpaque = false;
    var hasTransparency = false;

    for (var yy = 0; yy < this._blockHeight; ++yy) {
      for (var xx = 0; xx < this._blockWidth; ++xx) {
        var pixel = (by * this._blockHeight + yy) * this._width + bx * this._blockWidth + xx;
        var source = target[pixel];
        if ((source & 0x8000) != 0) {
          hasTransparency = true;
          continue;
        }

        anyOpaque = true;
        var visible = (ushort)(source & 0x7fff);
        if (this._previousHighColour == null || this._previousHighColour[pixel] != visible)
          unchanged = false;
        next[pixel] = visible;
      }
    }

    return new(!anyOpaque || unchanged && !hasTransparency, hasTransparency);
  }

  private VectorKey _HighColourVectorKey(ReadOnlySpan<ushort> target, int bx, int by) {
    UInt128 low = 0;
    UInt128 high = 0;
    var n = 0;
    for (var yy = 0; yy < this._blockHeight; ++yy)
      for (var xx = 0; xx < this._blockWidth; ++xx, ++n) {
        var value = target[(by * this._blockHeight + yy) * this._width + bx * this._blockWidth + xx];
        if (n < 8)
          low |= (UInt128)value << (n * 16);
        else
          high |= (UInt128)value << ((n - 8) * 16);
      }
    return new(low, high);
  }

  private void _AppendHighColourVector(List<byte> codebook, ReadOnlySpan<ushort> target, int bx, int by) {
    Span<byte> word = stackalloc byte[2];
    for (var yy = 0; yy < this._blockHeight; ++yy)
      for (var xx = 0; xx < this._blockWidth; ++xx) {
        var value = target[(by * this._blockHeight + yy) * this._width + bx * this._blockWidth + xx];
        BinaryPrimitives.WriteUInt16LittleEndian(word, value);
        codebook.Add(word[0]);
        codebook.Add(word[1]);
      }
  }

  private static void _WriteSkip(Stream output, ref int count) {
    while (count > 0) {
      var part = Math.Min(0x1fff, count);
      Span<byte> command = stackalloc byte[2];
      BinaryPrimitives.WriteUInt16LittleEndian(command, checked((ushort)part));
      output.Write(command);
      count -= part;
    }
  }

  private static ushort _PackRgb555(byte r, byte g, byte b) {
    var r5 = (r * 31 + 127) / 255;
    var g5 = (g * 31 + 127) / 255;
    var b5 = (b * 31 + 127) / 255;
    return (ushort)((r5 << 10) | (g5 << 5) | b5);
  }

  // ============================================================================================
  // VQFR sub-chunks
  // ============================================================================================

  private static void _WriteChunk(Stream output, string id, ReadOnlySpan<byte> payload) {
    if (id.Length != 4)
      throw new ArgumentException("A VQA chunk id is exactly four ASCII characters.", nameof(id));
    output.WriteByte((byte)id[0]);
    output.WriteByte((byte)id[1]);
    output.WriteByte((byte)id[2]);
    output.WriteByte((byte)id[3]);
    Span<byte> size = stackalloc byte[4];
    BinaryPrimitives.WriteUInt32BigEndian(size, checked((uint)payload.Length));
    output.Write(size);
    output.Write(payload);
    if ((payload.Length & 1) != 0)
      output.WriteByte(0);
  }
}
