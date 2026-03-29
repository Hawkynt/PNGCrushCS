using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.DbwRender;

/// <summary>In-memory representation of a DBW Render image.</summary>
public sealed class DbwRenderFile : IImageFileFormat<DbwRenderFile> {

  /// <summary>Header size in bytes (2 width + 2 height + 6 reserved).</summary>
  public const int HeaderSize = 10;

  static string IImageFileFormat<DbwRenderFile>.PrimaryExtension => ".dbw";
  static string[] IImageFileFormat<DbwRenderFile>.FileExtensions => [".dbw"];
  static DbwRenderFile IImageFileFormat<DbwRenderFile>.FromFile(FileInfo file) => DbwRenderReader.FromFile(file);
  static DbwRenderFile IImageFileFormat<DbwRenderFile>.FromBytes(byte[] data) => DbwRenderReader.FromBytes(data);
  static DbwRenderFile IImageFileFormat<DbwRenderFile>.FromStream(Stream stream) => DbwRenderReader.FromStream(stream);
  static RawImage IImageFileFormat<DbwRenderFile>.ToRawImage(DbwRenderFile file) => ToRawImage(file);
  static byte[] IImageFileFormat<DbwRenderFile>.ToBytes(DbwRenderFile file) => DbwRenderWriter.ToBytes(file);

  public int Width { get; init; }
  public int Height { get; init; }

  /// <summary>Raw RGB pixel data (3 bytes per pixel).</summary>
  public byte[] PixelData { get; init; } = [];

  public static RawImage ToRawImage(DbwRenderFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = file.PixelData[..],
    };
  }

  public static DbwRenderFile FromRawImage(RawImage image) => throw new NotSupportedException("DBW writing from raw image is not supported.");
}
