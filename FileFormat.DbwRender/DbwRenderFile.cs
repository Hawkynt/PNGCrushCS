using System;
using FileFormat.Core;

namespace FileFormat.DbwRender;

/// <summary>In-memory representation of a DBW Render image.</summary>
public readonly record struct DbwRenderFile : IImageFormatReader<DbwRenderFile>, IImageToRawImage<DbwRenderFile>, IImageFormatWriter<DbwRenderFile> {

  /// <summary>Header size in bytes (2 width + 2 height + 6 reserved).</summary>
  public const int HeaderSize = 10;

  static string IImageFormatMetadata<DbwRenderFile>.PrimaryExtension => ".dbw";
  static string[] IImageFormatMetadata<DbwRenderFile>.FileExtensions => [".dbw"];
  static DbwRenderFile IImageFormatReader<DbwRenderFile>.FromSpan(ReadOnlySpan<byte> data) => DbwRenderReader.FromSpan(data);
  static byte[] IImageFormatWriter<DbwRenderFile>.ToBytes(DbwRenderFile file) => DbwRenderWriter.ToBytes(file);

  public int Width { get; init; }
  public int Height { get; init; }

  /// <summary>Raw RGB pixel data (3 bytes per pixel).</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(DbwRenderFile file) {
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = file.PixelData[..],
    };
  }

}
