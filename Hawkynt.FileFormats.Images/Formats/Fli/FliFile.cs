using System;
using System.Collections.Generic;
using FileFormat.Core;

namespace FileFormat.Fli;

/// <summary>In-memory representation of a FLI/FLC animation file.</summary>
[FormatMimeType("video/x-fli", "video/x-flc")]
public sealed class FliFile
  : IImageFormatReader<FliFile>, IImageToRawImage<FliFile>,
    IImageFromRawImage<FliFile>, IImageFormatWriter<FliFile>, IMultiImageFileFormat<FliFile> {

  static string IImageFormatMetadata<FliFile>.PrimaryExtension => ".fli";
  static string[] IImageFormatMetadata<FliFile>.FileExtensions => [".fli", ".flc"];
  static FliFile IImageFormatReader<FliFile>.FromSpan(ReadOnlySpan<byte> data) => FliReader.FromSpan(data);

  static bool? IImageFormatMetadata<FliFile>.MatchesSignature(ReadOnlySpan<byte> header) {
    if (header.Length < 6)
      return null;
    if (header[4] == 0x11 && header[5] == 0xAF)
      return true;
    if (header[4] == 0x12 && header[5] == 0xAF)
      return true;
    return null;
  }

  static FormatCapability IImageFormatMetadata<FliFile>.Capabilities => FormatCapability.MultiImage;
  static byte[] IImageFormatWriter<FliFile>.ToBytes(FliFile file) => FliWriter.ToBytes(file);
  public short Width { get; init; }
  public short Height { get; init; }
  public short FrameCount { get; init; }
  public int Speed { get; init; }
  public FliFrameType FrameType { get; init; }
  public byte[]? Palette { get; init; }
  public IReadOnlyList<FliFrame> Frames { get; init; } = [];

  /// <summary>Returns the number of frames in this FLI/FLC file.</summary>
  public static int ImageCount(FliFile file) => file.Frames.Count;

  /// <summary>Converts the frame at the given index to a <see cref="RawImage"/> by applying frames 0..index.</summary>
  public static RawImage ToRawImage(FliFile file, int index) {
    ArgumentNullException.ThrowIfNull(file);
    if ((uint)index >= (uint)file.Frames.Count)
      throw new ArgumentOutOfRangeException(nameof(index));

    var w = (int)file.Width;
    var h = (int)file.Height;
    var canvas = new byte[w * h];
    var palette = file.Palette != null ? file.Palette[..] : _BuildDefaultPalette();

    for (var f = 0; f <= index; ++f)
      _ApplyFrame(file.Frames[f], canvas, palette, w, h);

    return new RawImage {
      Width = w,
      Height = h,
      Format = PixelFormat.Indexed8,
      PixelData = canvas,
      Palette = palette,
      PaletteCount = 256
    };
  }

  /// <summary>Converts the first frame of a FLI/FLC file to a <see cref="RawImage"/>.</summary>
  public static RawImage ToRawImage(FliFile file) {
    ArgumentNullException.ThrowIfNull(file);
    if (file.Frames.Count == 0)
      throw new ArgumentException("FLI file contains no frames.", nameof(file));

    return ToRawImage(file, 0);
  }

  /// <summary>Encodes a picture as a one-frame FLI animation.</summary>
  /// <remarks>
  /// A still picture is an animation of one frame, and that frame needs both of the chunks a first
  /// frame always carries: the palette, because 256 indices mean nothing without one, and the
  /// pixels. They go out uncompressed — a Literal chunk rather than a ByteRun — since a single
  /// frame's size is not what an animation format is judged on and the run coder would only obscure
  /// what is being written.
  /// <para/>
  /// The header's dimensions are sixteen-bit, so a picture larger than that has to be brought inside
  /// it; nothing else here constrains the picture.
  /// </remarks>
  public static FliFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var width = Math.Clamp(image.Width, 1, short.MaxValue);
    var height = Math.Clamp(image.Height, 1, short.MaxValue);
    var bgra = PixelConverter.Convert(image.SampleTo(width, height), PixelFormat.Bgra32);
    var quantised = ColorQuantizer.Quantize(bgra.PixelData, width * height, 256);

    var palette = new byte[768];
    quantised.Palette.AsSpan(0, Math.Min(quantised.Palette.Length, palette.Length)).CopyTo(palette);

    var canvas = new byte[width * height];
    for (var i = 0; i < canvas.Length; ++i)
      canvas[i] = (byte)quantised.Indices[i];

    // One packet, skipping nothing, holding all 256 entries — which the count states as zero.
    var colors = new byte[4 + palette.Length];
    colors[0] = 1;
    palette.CopyTo(colors.AsSpan(4));

    var frame = new FliFrame {
      Chunks = [
        new FliFrameChunk { ChunkType = FliChunkType.Color256, Data = colors },
        new FliFrameChunk { ChunkType = FliChunkType.Literal, Data = canvas },
      ],
    };

    return new() {
      Width = (short)width,
      Height = (short)height,
      FrameCount = 1,
      Speed = _DEFAULT_SPEED,
      FrameType = FliFrameType.Fli,
      Palette = palette,
      Frames = [frame],
    };
  }

  /// <summary>Milliseconds a frame is held for, which for a still picture only has to be sane.</summary>
  private const int _DEFAULT_SPEED = 70;

  private static void _ApplyFrame(FliFrame frame, byte[] canvas, byte[] palette, int w, int h) {
    foreach (var chunk in frame.Chunks)
      switch (chunk.ChunkType) {
        case FliChunkType.Color256:
          _DecodeColor256(chunk.Data, palette);
          break;
        case FliChunkType.Color64:
          _DecodeColor64(chunk.Data, palette);
          break;
        case FliChunkType.ByteRun:
          _DecodeByteRun(chunk.Data, canvas, w, h);
          break;
        case FliChunkType.Literal:
          chunk.Data.AsSpan(0, Math.Min(chunk.Data.Length, canvas.Length)).CopyTo(canvas.AsSpan(0));
          break;
        case FliChunkType.Black:
          Array.Clear(canvas);
          break;
      }
  }

  private static byte[] _BuildDefaultPalette() {
    var palette = new byte[768];
    for (var i = 0; i < 256; ++i) {
      var v = (byte)i;
      palette[i * 3] = v;
      palette[i * 3 + 1] = v;
      palette[i * 3 + 2] = v;
    }

    return palette;
  }

  private static void _DecodeColor256(byte[] data, byte[] palette) {
    var pos = 0;
    if (data.Length < 2)
      return;

    var packetCount = data[pos] | (data[pos + 1] << 8);
    pos += 2;
    var colorIndex = 0;

    for (var p = 0; p < packetCount && pos < data.Length; ++p) {
      colorIndex += data[pos++];
      if (pos >= data.Length)
        break;

      int count = data[pos++];
      if (count == 0)
        count = 256;

      for (var c = 0; c < count && pos + 2 < data.Length && colorIndex < 256; ++c, ++colorIndex) {
        palette[colorIndex * 3] = data[pos++];
        palette[colorIndex * 3 + 1] = data[pos++];
        palette[colorIndex * 3 + 2] = data[pos++];
      }
    }
  }

  private static void _DecodeColor64(byte[] data, byte[] palette) {
    var pos = 0;
    if (data.Length < 2)
      return;

    var packetCount = data[pos] | (data[pos + 1] << 8);
    pos += 2;
    var colorIndex = 0;

    for (var p = 0; p < packetCount && pos < data.Length; ++p) {
      colorIndex += data[pos++];
      if (pos >= data.Length)
        break;

      int count = data[pos++];
      if (count == 0)
        count = 256;

      for (var c = 0; c < count && pos + 2 < data.Length && colorIndex < 256; ++c, ++colorIndex) {
        palette[colorIndex * 3] = (byte)(data[pos++] << 2);
        palette[colorIndex * 3 + 1] = (byte)(data[pos++] << 2);
        palette[colorIndex * 3 + 2] = (byte)(data[pos++] << 2);
      }
    }
  }

  private static void _DecodeByteRun(byte[] data, byte[] canvas, int width, int height) {
    var pos = 0;
    for (var y = 0; y < height && pos < data.Length; ++y) {
      ++pos; // skip packet count byte
      var x = 0;
      while (x < width && pos < data.Length) {
        var count = (sbyte)data[pos++];
        if (count < 0) {
          // run of |count| copies of next byte
          var len = -count;
          if (pos >= data.Length)
            break;

          var value = data[pos++];
          for (var i = 0; i < len && x < width; ++i, ++x)
            canvas[y * width + x] = value;
        } else {
          // copy count literal bytes
          var len = count;
          for (var i = 0; i < len && x < width && pos < data.Length; ++i, ++x)
            canvas[y * width + x] = data[pos++];
        }
      }
    }
  }
}
