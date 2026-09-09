using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;
using FileFormat.WebP.Vp8;
using FileFormat.WebP.Vp8L;

namespace FileFormat.WebP;

/// <summary>In-memory representation of a WebP file with full VP8/VP8L pixel codec support.</summary>
[FormatMimeType("image/webp")]
public sealed class WebPFile :
  IImageFormatReader<WebPFile>, IImageToRawImage<WebPFile>, IImageFromRawImage<WebPFile>, IImageFormatWriter<WebPFile>,
  IMultiImageFileFormat<WebPFile>,
  IFormatChunkLayout<WebPFile>, IFormatChunkRewriter<WebPFile>, IFormatChunkPlanRewriter<WebPFile> {

  public required WebPFeatures Features { get; init; }
  public byte[] ImageData { get; init; } = [];
  public bool IsLossless { get; init; }
  public List<(string ChunkId, byte[] Data)> MetadataChunks { get; init; } = [];

  /// <summary>The animation's frames in the order they are shown, or empty for a still picture.</summary>
  public IReadOnlyList<WebPFrame> Frames { get; init; } = [];

  /// <summary>What the ANIM chunk stated, or <c>null</c> for a still picture.</summary>
  public WebPAnimationInfo? Animation { get; init; }

  /// <summary>VP8 lossy alpha plane bytes (one byte per pixel, top-left origin, no padding).
  /// Only meaningful when <see cref="IsLossless"/> is false and <see cref="WebPFeatures.HasAlpha"/>
  /// is true. Lossless format already carries alpha inline. Stored in the ALPH chunk on emit.</summary>
  public byte[]? AlphaData { get; init; }

  public static string PrimaryExtension => ".webp";
  public static string[] FileExtensions => [".webp", ".wep"];
  static FormatCapability IImageFormatMetadata<WebPFile>.Capabilities => FormatCapability.MultiImage | FormatCapability.HasDedicatedOptimizer;
  static WebPFile IImageFormatReader<WebPFile>.FromSpan(ReadOnlySpan<byte> data) => WebPReader.FromSpan(data);

  public static bool? MatchesSignature(ReadOnlySpan<byte> header)
    => header.Length >= 12
       && header[0] == 0x52 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x46
       && header[8] == 0x57 && header[9] == 0x45 && header[10] == 0x42 && header[11] == 0x50
       ? true
       : null;

  public static WebPFile FromFile(FileInfo file) => WebPReader.FromFile(file);
  public static WebPFile FromBytes(byte[] data) => WebPReader.FromBytes(data);
  public static WebPFile FromStream(Stream stream) => WebPReader.FromStream(stream);
  public static byte[] ToBytes(WebPFile file) => WebPWriter.ToBytes(file);

  static IEnumerable<ChunkSpan> IFormatChunkLayout<WebPFile>.EnumerateChunks(ReadOnlySpan<byte> data)
    => WebPChunkLayout.Enumerate(data);

  static byte[] IFormatChunkRewriter<WebPFile>.Rewrite(ReadOnlySpan<byte> data, IReadOnlyList<ChunkRewriteRule> rules)
    => WebPChunkLayout.Rewrite(data, rules);

  static ChunkRewriteResult IFormatChunkPlanRewriter<WebPFile>.ApplyPlan(ReadOnlySpan<byte> data, ChunkRewritePlan plan)
    => WebPChunkLayout.ApplyPlan(data, plan);

  /// <summary>How many frames this file holds — one for a still picture, one per ANMF chunk for an
  /// animation.</summary>
  public static int ImageCount(WebPFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return file.Frames.Count == 0 ? 1 : file.Frames.Count;
  }

  /// <summary>The canvas as it stands when frame <paramref name="index"/> is shown.</summary>
  /// <remarks>
  /// Not the frame's own rectangle. A frame carries only what changed, and what it looks like on
  /// screen is that rectangle drawn over the canvas the frames before it left behind — so frame
  /// <paramref name="index"/> costs the decoding of every frame up to it, and always comes back at
  /// the canvas size the VP8X chunk states.
  /// </remarks>
  public static RawImage ToRawImage(WebPFile file, int index) {
    ArgumentNullException.ThrowIfNull(file);
    if ((uint)index >= (uint)ImageCount(file))
      throw new ArgumentOutOfRangeException(nameof(index), index, $"The file holds {ImageCount(file)} frame(s).");

    if (file.Frames.Count == 0)
      return ToRawImage(file);

    return new() {
      Width = file.Features.Width,
      Height = file.Features.Height,
      Format = PixelFormat.Rgba32,
      PixelData = WebPAnimationCompositor.Compose(file, index),
      Metadata = WebPMetadataCodec.Read(file.MetadataChunks),
    };
  }

  /// <summary>Every frame, composited in one pass.</summary>
  /// <remarks>
  /// Overridden rather than left to the interface's default, which asks for each frame in turn and
  /// so replays the animation from the beginning every time — a hundred-frame animation would cost
  /// five thousand frame decodes to read once.
  /// </remarks>
  public static IReadOnlyList<RawImage> ToRawImages(WebPFile file) {
    ArgumentNullException.ThrowIfNull(file);
    if (file.Frames.Count == 0)
      return [ToRawImage(file)];

    var metadata = WebPMetadataCodec.Read(file.MetadataChunks);
    var canvases = WebPAnimationCompositor.ComposeAll(file);
    var images = new RawImage[canvases.Count];
    for (var i = 0; i < images.Length; ++i)
      images[i] = new() {
        Width = file.Features.Width,
        Height = file.Features.Height,
        Format = PixelFormat.Rgba32,
        PixelData = canvases[i],
        Metadata = metadata,
      };

    return images;
  }

  public static RawImage ToRawImage(WebPFile file) {
    ArgumentNullException.ThrowIfNull(file);

    // An animation's first frame is a rectangle on a canvas like every other, and reading it as a
    // picture of canvas size gets both wrong whenever it does not happen to cover the whole canvas.
    if (file.Frames.Count > 0)
      return ToRawImage(file, 0);

    var w = file.Features.Width;
    var h = file.Features.Height;

    if (file.IsLossless) {
      var rgba = Vp8LDecoder.Decode(file.ImageData, w, h, file.Features.HasAlpha);
      return new() {
        Width = w,
        Height = h,
        Format = file.Features.HasAlpha ? PixelFormat.Rgba32 : PixelFormat.Rgb24,
        PixelData = file.Features.HasAlpha ? rgba : _StripAlpha(rgba, w * h),
        Metadata = WebPMetadataCodec.Read(file.MetadataChunks),
      };
    }

    // VP8 lossy: ported from golang.org/x/image/vp8 (Nigel Tao's clean reference implementation).
    var rgb = Vp8Decoder.Decode(file.ImageData, w, h);

    // If an ALPH chunk accompanied the VP8 lossy data, splice in the alpha plane and
    // upgrade the output to Rgba32. Without ALPH, VP8 lossy is RGB-only.
    if (file.Features.HasAlpha && file.AlphaData != null && file.AlphaData.Length == w * h) {
      var rgba = new byte[w * h * 4];
      for (var i = 0; i < w * h; ++i) {
        rgba[i * 4 + 0] = rgb[i * 3 + 0];
        rgba[i * 4 + 1] = rgb[i * 3 + 1];
        rgba[i * 4 + 2] = rgb[i * 3 + 2];
        rgba[i * 4 + 3] = file.AlphaData[i];
      }
      return new() {
        Width = w, Height = h, Format = PixelFormat.Rgba32, PixelData = rgba,
        Metadata = WebPMetadataCodec.Read(file.MetadataChunks),
      };
    }

    return new() {
      Width = w,
      Height = h,
      Format = PixelFormat.Rgb24,
      PixelData = rgb,
      Metadata = WebPMetadataCodec.Read(file.MetadataChunks),
    };
  }

  public static WebPFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    // Grey and indexed sources used to be refused because the VP8L encoder emitted a stream its own
    // reader choked on — the cause was single-symbol Huffman trees being written as one bit when a
    // decoder consumes none. With that fixed, any layout converts up to colour safely.
    image = image.EnsureAnyFormat(PixelFormat.Rgba32, PixelFormat.Rgb24);

    var hasAlpha = image.Format is PixelFormat.Rgba32;
    var w = image.Width;
    var h = image.Height;

    // Encode as VP8L (lossless) for pixel-perfect round-trip
    var argb = _ToArgb(image);
    var vp8lData = Vp8LEncoder.Encode(argb, w, h, hasAlpha);

    return new() {
      Features = new WebPFeatures(w, h, hasAlpha, IsLossless: true, IsAnimated: false),
      ImageData = vp8lData,
      IsLossless = true,
      MetadataChunks = WebPMetadataCodec.Write(image.Metadata),
    };
  }

  /// <summary>Encode as VP8 lossy at the given quality (0-100). Alpha is preserved losslessly
  /// in an accompanying ALPH chunk (uncompressed method 0); the RGB plane goes through the
  /// usual lossy VP8 path. Pixel-perfect decoding requires <see cref="FromRawImage(RawImage)"/>
  /// (VP8L lossless), but RGBA-with-alpha now round-trips alpha bit-exactly even via the
  /// lossy path.</summary>
  public static WebPFile FromRawImageLossy(RawImage image, int quality = 75) {
    ArgumentNullException.ThrowIfNull(image);
    var vp8Data = Vp8Encoder.Encode(image, quality);

    byte[]? alphaData = null;
    var hasAlpha = false;
    if (image.Format == PixelFormat.Rgba32) {
      alphaData = _ExtractAlphaPlane(image);
      // Skip the ALPH chunk if every pixel is fully opaque — saves bytes and avoids
      // marking the file as having alpha when it effectively doesn't.
      hasAlpha = _AlphaHasTransparency(alphaData);
      if (!hasAlpha) alphaData = null;
    }

    return new() {
      Features = new WebPFeatures(image.Width, image.Height, hasAlpha, IsLossless: false, IsAnimated: false),
      MetadataChunks = WebPMetadataCodec.Write(image.Metadata),
      ImageData = vp8Data,
      IsLossless = false,
      AlphaData = alphaData,
    };
  }

  private static byte[] _ExtractAlphaPlane(RawImage image) {
    var pixelCount = image.Width * image.Height;
    var alpha = new byte[pixelCount];
    for (var i = 0; i < pixelCount; ++i)
      alpha[i] = image.PixelData[i * 4 + 3];
    return alpha;
  }

  private static bool _AlphaHasTransparency(byte[] alpha) {
    foreach (var a in alpha)
      if (a != 0xFF) return true;
    return false;
  }

  /// <summary>Convert RGBA byte array to ARGB uint array for VP8L encoder.</summary>
  private static uint[] _ToArgb(RawImage image) {
    var count = image.Width * image.Height;
    var argb = new uint[count];

    if (image.Format == PixelFormat.Rgba32) {
      for (var i = 0; i < count; ++i) {
        var off = i * 4;
        argb[i] = ((uint)image.PixelData[off + 3] << 24)
                   | ((uint)image.PixelData[off] << 16)
                   | ((uint)image.PixelData[off + 1] << 8)
                   | image.PixelData[off + 2];
      }
    } else if (image.Format == PixelFormat.Rgb24) {
      for (var i = 0; i < count; ++i) {
        var off = i * 3;
        argb[i] = 0xFF000000
                   | ((uint)image.PixelData[off] << 16)
                   | ((uint)image.PixelData[off + 1] << 8)
                   | image.PixelData[off + 2];
      }
    } else if (image.Format == PixelFormat.Gray8) {
      for (var i = 0; i < count; ++i) {
        var v = image.PixelData[i];
        argb[i] = 0xFF000000 | ((uint)v << 16) | ((uint)v << 8) | v;
      }
    } else {
      throw new ArgumentException($"Unsupported pixel format for WebP: {image.Format}.", nameof(image));
    }

    return argb;
  }

  /// <summary>Strip alpha channel from RGBA to produce RGB24 byte array.</summary>
  private static byte[] _StripAlpha(byte[] rgba, int pixelCount) {
    var rgb = new byte[pixelCount * 3];
    for (var i = 0; i < pixelCount; ++i) {
      rgb[i * 3] = rgba[i * 4];
      rgb[i * 3 + 1] = rgba[i * 4 + 1];
      rgb[i * 3 + 2] = rgba[i * 4 + 2];
    }
    return rgb;
  }
}
