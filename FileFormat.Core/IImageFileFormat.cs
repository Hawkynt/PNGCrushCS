using System;
using System.IO;

namespace FileFormat.Core;

/// <summary>Defines a type that can be read from a file, converted to/from <see cref="RawImage"/>, and written to bytes.</summary>
public interface IImageFileFormat<TSelf> where TSelf : IImageFileFormat<TSelf> {

  /// <summary>The canonical file extension for this format (e.g. ".png").</summary>
  static abstract string PrimaryExtension { get; }

  /// <summary>All recognized file extensions for this format (e.g. [".png"]).</summary>
  static abstract string[] FileExtensions { get; }

  /// <summary>Capability flags for this format (e.g. MonochromeOnly, IndexedOnly). Default: <see cref="FormatCapability.VariableResolution"/>.</summary>
  static virtual FormatCapability Capabilities => FormatCapability.VariableResolution;

  /// <summary>Tests whether the given file header matches this format's signature. Returns <c>true</c> (match), <c>false</c> (explicitly not this format), or <c>null</c> (no opinion — fall back to attribute-based matching).</summary>
  static virtual bool? MatchesSignature(ReadOnlySpan<byte> header) => null;

  /// <summary>Reads a file from disk into the in-memory representation.</summary>
  static abstract TSelf FromFile(FileInfo file);

  /// <summary>Reads from a byte array. Default: writes to temp file then calls <see cref="FromFile"/>.</summary>
  static virtual TSelf FromBytes(byte[] data) {
    var tmp = Path.GetTempFileName();
    try {
      File.WriteAllBytes(tmp, data);
      return TSelf.FromFile(new FileInfo(tmp));
    } finally {
      try { File.Delete(tmp); } catch { /* best effort */ }
    }
  }

  /// <summary>Reads from a stream. Default: reads all bytes then calls <see cref="FromBytes"/>.</summary>
  static virtual TSelf FromStream(Stream stream) {
    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return TSelf.FromBytes(ms.ToArray());
  }

  /// <summary>Converts the in-memory representation to a platform-independent <see cref="RawImage"/>.</summary>
  static abstract RawImage ToRawImage(TSelf file);

  /// <summary>Creates the in-memory representation from a platform-independent <see cref="RawImage"/>.</summary>
  static abstract TSelf FromRawImage(RawImage image);

  /// <summary>Serializes the in-memory representation to a byte array.</summary>
  static abstract byte[] ToBytes(TSelf file);
}
