using System;
using System.Text;

namespace FileFormat.Arn;

/// <summary>Writes the XnView-compatible ARN PDS-style label, palette planes and indexed rows.</summary>
public static class ArnWriter {

  private const int _RecordBytes = 256;

  public static byte[] ToBytes(ArnFile file) {
    if (file.Width <= 0 || file.Height <= 0)
      throw new ArgumentException("ARN dimensions must be positive.", nameof(file));
    if (file.Palette == null || file.Palette.Length < ArnFile.PaletteEntries * 3)
      throw new ArgumentException("ARN requires a 256-entry RGB palette.", nameof(file));
    var pixelCount = checked(file.Width * file.Height);
    if (file.PixelData == null || file.PixelData.Length < pixelCount)
      throw new ArgumentException($"ARN needs {pixelCount} indexed pixels.", nameof(file));

    var labelRecords = 1;
    byte[] label;
    while (true) {
      var text =
        $"SIMPLE = {ArnFile.SimpleValuePrefix}\r\n" +
        $"RECORD_BYTES = {_RecordBytes}\r\n" +
        $"LABEL_RECORDS = {labelRecords}\r\n" +
        "OBJECT = IMAGE\r\n" +
        $"LINES = {file.Height}\r\n" +
        $"LINE_SAMPLES = {file.Width}\r\n" +
        $"SAMPLE_BITS = {ArnFile.SupportedSampleBits}\r\n" +
        "END_OBJECT = IMAGE\r\n" +
        "END\r\n";
      var raw = Encoding.ASCII.GetBytes(text);
      var wantedRecords = Math.Max(1, (raw.Length + _RecordBytes - 1) / _RecordBytes);
      if (wantedRecords != labelRecords) {
        labelRecords = wantedRecords;
        continue;
      }
      label = new byte[labelRecords * _RecordBytes];
      raw.CopyTo(label, 0);
      break;
    }

    var gapBytes = ((ArnFile.GapBeforePalette + _RecordBytes - 1) / _RecordBytes) * _RecordBytes;
    var palettePlaneStride = ((ArnFile.PaletteEntries + _RecordBytes - 1) / _RecordBytes) * _RecordBytes;
    var paletteStart = checked(label.Length + gapBytes);
    var pixelsStart = checked(paletteStart + palettePlaneStride * 3);
    var output = new byte[checked(pixelsStart + pixelCount)];
    label.CopyTo(output, 0);

    for (var plane = 0; plane < 3; ++plane)
      for (var i = 0; i < ArnFile.PaletteEntries; ++i)
        output[paletteStart + plane * palettePlaneStride + i] = file.Palette[i * 3 + plane];

    file.PixelData.AsSpan(0, pixelCount).CopyTo(output.AsSpan(pixelsStart));
    return output;
  }
}
