using System;

namespace FileFormat.HighresMedium;

/// <summary>Assembles Highres Medium (.hrm) file bytes from an in-memory representation.</summary>
public static class HighresMediumWriter {

  public static byte[] ToBytes(HighresMediumFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[HighresMediumFile.FileSize];
    var span = result.AsSpan();

    // Frame 1
    var header1 = HighresMediumHeader.FromPalette(file.Palette1);
    header1.WriteTo(span);
    file.PixelData1.AsSpan(0, Math.Min(32000, file.PixelData1.Length)).CopyTo(span.Slice(HighresMediumHeader.StructSize));

    // Frame 2
    var frame2Offset = HighresMediumHeader.FrameSize;
    var header2 = HighresMediumHeader.FromPalette(file.Palette2);
    header2.WriteTo(span.Slice(frame2Offset));
    file.PixelData2.AsSpan(0, Math.Min(32000, file.PixelData2.Length)).CopyTo(span.Slice(frame2Offset + HighresMediumHeader.StructSize));

    return result;
  }
}
