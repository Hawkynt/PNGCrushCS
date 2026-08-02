using System;
using FileFormat.Core;

namespace FileFormat.IffSham;

/// <summary>In-memory representation of an IFF SHAM (Sliced HAM) image.</summary>
public readonly record struct IffShamFile : IImageFormatReader<IffShamFile>, IImageToRawImage<IffShamFile>, IImageFormatWriter<IffShamFile> {

  /// <summary>Minimum valid file size (FORM header = 12 bytes).</summary>
  internal const int MinFileSize = 12;

  /// <summary>Default width for SHAM images.</summary>
  internal const int DefaultWidth = 320;

  /// <summary>Default height for SHAM images.</summary>
  internal const int DefaultHeight = 200;

  static string IImageFormatMetadata<IffShamFile>.PrimaryExtension => ".sham";
  static string[] IImageFormatMetadata<IffShamFile>.FileExtensions => [".sham"];
  static IffShamFile IImageFormatReader<IffShamFile>.FromSpan(ReadOnlySpan<byte> data) => IffShamReader.FromSpan(data);
  static byte[] IImageFormatWriter<IffShamFile>.ToBytes(IffShamFile file) => IffShamWriter.ToBytes(file);

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Raw file data.</summary>
  public byte[] RawData { get; init; }

  /// <summary>
  /// Refuses the picture, SHAM's per-scanline palettes not being decoded here.
  /// </summary>
  /// <remarks>
  /// What this used to return was a black picture of the right size, and nothing marked it as
  /// anything else — so a file that had not been decoded at all counted as a decode. The whole point
  /// of SHAM is that the palette changes from one scanline to the next, so a decode that ignores
  /// that has not read the picture in any sense.
  /// </remarks>
  public static RawImage ToRawImage(IffShamFile file)
    => throw new NotSupportedException("A SHAM picture is not decoded here; only the file itself is recognised.");

}
