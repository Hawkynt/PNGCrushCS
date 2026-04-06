using System;

namespace FileFormat.Core;

/// <summary>Parses a file format from a byte span. Use <see cref="FormatIO"/> for byte[], Stream, and FileInfo overloads.</summary>
public interface IImageFormatReader<TSelf> : IImageFormatMetadata<TSelf> where TSelf : IImageFormatReader<TSelf> {

  /// <summary>Parses the format from raw bytes.</summary>
  static abstract TSelf FromSpan(ReadOnlySpan<byte> data);
}
