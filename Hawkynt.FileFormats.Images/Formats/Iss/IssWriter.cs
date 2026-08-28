using System;
using System.Buffers.Binary;

namespace FileFormat.Iss;

/// <summary>Writes the documented/XnView-compatible ISS grayscale raster layout.</summary>
public static class IssWriter {

  public static byte[] ToBytes(IssFile file) {
    if (file.Width <= 0 || file.Height <= 0)
      throw new ArgumentException("ISS dimensions must be positive.", nameof(file));
    if (file.Kind is not (IssFile.MonochromeKind or IssFile.GrayscaleKind))
      throw new ArgumentException($"Unsupported ISS kind {file.Kind}.", nameof(file));

    var stride = IssFile.RowStride(file.Kind, file.Width);
    var expected = checked(stride * file.Height);
    if (file.PixelData == null || file.PixelData.Length < expected)
      throw new ArgumentException($"ISS pixel payload needs {expected} bytes.", nameof(file));

    var output = new byte[checked(IssFile.PixelsOffset + expected)];
    IssFile.Magic.CopyTo(output);
    // The reader intentionally ignores the unknown words at 8, 12 and 14. Write zero for those
    // reserved/unknown fields and only populate the fields that have been verified from samples.
    BinaryPrimitives.WriteUInt16BigEndian(output.AsSpan(10, 2), checked((ushort)file.Kind));
    BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(18, 4), checked((uint)file.Height));
    BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(22, 4), checked((uint)file.Width));
    file.PixelData.AsSpan(0, expected).CopyTo(output.AsSpan(IssFile.PixelsOffset));
    return output;
  }
}
