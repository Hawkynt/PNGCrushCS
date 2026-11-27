using System;
using System.Collections.Generic;
using FileFormat.Core;

namespace FileFormat.SeriousTexture;

/// <summary>One animation of a Serious Engine texture: a name, the seconds a frame is shown, and the frames in order.</summary>
public sealed record SeriousTextureAnimation(string Name, float SecondsPerFrame, int[] Frames);

/// <summary>A Croteam Serious Engine texture (<c>.tex</c>, and the <c>.tbn</c> thumbnails saved games carry).</summary>
/// <remarks>
/// Written from a description derived from the engine's released source (GPL-2.0, read and not
/// converted: <c>Engine/Graphics/Texture.cpp</c> and <c>Engine/Base/Anim.cpp</c>) and checked against
/// twelve files, every one of which it accounts for to the byte. <c>TVER</c> and a version, 3 or 4,
/// then chunks: <c>TDAT</c> states the flags, the size in world units ("mex"), the mip-map counts,
/// the first mip level — the picture's size in pixels is the mex size shifted right by it — and the
/// frame count; <c>FRMS</c> holds the frames, in version 4 the top mip-map only, RGB, or RGBA where
/// the alpha flag is set, rows from the top; in version 3 each frame is a whole chain of 16-bit
/// mip-maps, RGB565 with the low bit unused, or RGBA4444 with alpha; <c>ANIM</c> holds the
/// animations, each a 32-byte name, a float of seconds a frame and the frame indices.
/// <para/>
/// <c>FRMC</c>, compressed frames, appears in none of the released source. Measured on the one file
/// that has it: a total length, then each mip-map as a length and that many bytes of DXT5 blocks,
/// down to 1x1 — the lengths add up to the total and the file to the byte — and the blocks are DXT5
/// rather than DXT3, since 332 of its blocks are the constant-alpha DXT5 block <c>FF FF</c> and six
/// noughts while not one repeats a byte eight times as DXT3's opaque block would. Effect textures
/// (<c>FXDT</c>, <c>FXBF</c>, <c>FXB2</c>, <c>BAST</c>), whose pixels the engine generates as it runs,
/// are refused by name, as is a compressed texture of more than one frame, of which there is no
/// sample.
/// </remarks>
[FormatMagicBytes([0x54, 0x56, 0x45, 0x52])]
public readonly record struct SeriousTextureFile
  : IImageFormatReader<SeriousTextureFile>, IImageToRawImage<SeriousTextureFile>, IImageFromRawImage<SeriousTextureFile>,
    IImageFormatWriter<SeriousTextureFile>, IMultiImageFileFormat<SeriousTextureFile> {

  /// <summary>The <c>TDAT</c> flag marking an alpha channel.</summary>
  public const uint AlphaChannelFlag = 1;

  static string IImageFormatMetadata<SeriousTextureFile>.PrimaryExtension => ".tex";
  static string[] IImageFormatMetadata<SeriousTextureFile>.FileExtensions => [".tex", ".tbn"];
  static SeriousTextureFile IImageFormatReader<SeriousTextureFile>.FromSpan(ReadOnlySpan<byte> data) => SeriousTextureReader.FromSpan(data);
  static byte[] IImageFormatWriter<SeriousTextureFile>.ToBytes(SeriousTextureFile file) => SeriousTextureWriter.ToBytes(file);

  /// <summary>3 or 4.</summary>
  public int Version { get; init; }

  /// <summary>The <c>TDAT</c> flags as stored.</summary>
  public uint Flags { get; init; }

  /// <summary>The size in world units; the size in pixels is this shifted right by <see cref="FirstMipLevel"/>.</summary>
  public int MexWidth { get; init; }

  /// <inheritdoc cref="MexWidth"/>
  public int MexHeight { get; init; }

  /// <summary>How many mip-maps the engine filters finely.</summary>
  public int FineMipLevels { get; init; }

  /// <summary>The mip level the stored picture is.</summary>
  public int FirstMipLevel { get; init; }

  public int Width { get; init; }

  public int Height { get; init; }

  /// <summary>Whether the frames came as DXT5 (<c>FRMC</c>).</summary>
  public bool Compressed { get; init; }

  /// <summary>Each frame as RGBA, four bytes a pixel, rows from the top.</summary>
  public IReadOnlyList<byte[]> Frames { get; init; }

  /// <summary>The animations over the frames.</summary>
  public IReadOnlyList<SeriousTextureAnimation> Animations { get; init; }

  public bool HasAlpha => (this.Flags & AlphaChannelFlag) != 0;

  public static int ImageCount(SeriousTextureFile file) => file.Frames.Count;

  public static RawImage ToRawImage(SeriousTextureFile file) => ToRawImage(file, 0);

  public static RawImage ToRawImage(SeriousTextureFile file, int index) => new() {
    Width = file.Width,
    Height = file.Height,
    Format = PixelFormat.Rgba32,
    PixelData = file.Frames[index][..],
  };

  /// <summary>
  /// A one-frame version-4 texture of the picture, sampled down to powers of two where it is not one,
  /// since the engine's textures are; an alpha channel where any pixel is not opaque.
  /// </summary>
  public static SeriousTextureFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    static int Floor2(int v) => 1 << (31 - int.LeadingZeroCount(v));
    var width = Floor2(image.Width);
    var height = Floor2(image.Height);
    var sized = width == image.Width && height == image.Height ? image : image.SampleTo(width, height);
    var rgba = sized.EnsureFormat(PixelFormat.Rgba32).PixelData;
    var alpha = false;
    for (var i = 3; i < rgba.Length && !alpha; i += 4)
      alpha = rgba[i] != 255;
    return new() {
      Version = 4,
      Flags = alpha ? AlphaChannelFlag : 0,
      MexWidth = width,
      MexHeight = height,
      FineMipLevels = 31 - int.LeadingZeroCount(Math.Min(width, height)) + 1,
      FirstMipLevel = 0,
      Width = width,
      Height = height,
      Frames = [rgba[..(width * height * 4)]],
      Animations = [new("OnlyAnim", 0.02f, [0])],
    };
  }
}
