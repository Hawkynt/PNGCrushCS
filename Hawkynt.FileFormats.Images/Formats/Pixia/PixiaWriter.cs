using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;
using FileFormat.Core;
using FileFormat.Jpeg;

namespace FileFormat.Pixia;

/// <summary>Assembles a Pixia picture: the header, a preview, then the picture as one layer.</summary>
/// <remarks>
/// One layer, fully opaque, at version 3 — which is the shape the reader here understands and the
/// least that is honest. What the real files put in the second table, and what the unknown fields of
/// the property records mean, is not established, so those bytes are left as they are found: zero.
/// <para/>
/// The preview these carry is the canvas rescaled so its shorter side is 256, and rescaling is not
/// something a file format needs decided for it here — so the picture itself is written as the
/// preview. It is what the field is for and it costs nothing to be right about the length.
/// </remarks>
public static class PixiaWriter {

  /// <summary>The longest run a count can express.</summary>
  private const int _MAXIMUM_RUN = 200;

  public static byte[] ToBytes(PixiaFile file) {
    var width = file.Width;
    var height = file.Height;
    if (width < 1 || height < 1)
      throw new ArgumentException($"Invalid Pixia picture size: {width}x{height}.", nameof(file));

    var pixels = file.PixelData ?? new byte[width * height * 3];

    var preview = file.Preview is { Length: > 3 } carried && carried[0] == 0xFF && carried[1] == 0xD8
      ? carried
      : JpegWriter.ToBytes(JpegFile.FromRawImage(new RawImage {
        Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels,
      }));

    var body = new List<byte>();

    // The record ahead of the runs: how many 8-bit planes follow, then two ones, then nothing.
    var record = new byte[PixiaFile.LayerRecordSize];
    record[0] = 1;
    BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(4), 1);
    BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(8), 1);
    body.AddRange(record);

    // Every plane holds one row more than the layer is tall, the extra a copy of the last.
    var stored = new byte[width * (height + 1) * 3];
    pixels.AsSpan(0, width * height * 3).CopyTo(stored);
    pixels.AsSpan((height - 1) * width * 3, width * 3).CopyTo(stored.AsSpan(height * width * 3));

    _WriteColourRuns(body, stored);
    _WriteOpaquePlane(body, width * (height + 1));

    var result = new byte[PixiaFile.PreviewAt + preview.Length + body.Count];

    Encoding.ASCII.GetBytes(PixiaFile.Signature).CopyTo(result, 0);
    BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(PixiaFile.VersionAt), PixiaFile.WrittenVersion);
    BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(PixiaFile.LayerCountAt), 1);
    BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(PixiaFile.GeometryAt), width);
    BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(PixiaFile.GeometryAt + 4), height);
    BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(PixiaFile.GeometryAt + 8), 24);
    BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(PixiaFile.PropertiesAt + PixiaFile.PropertyVisibleAt), 1);
    BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(PixiaFile.HeaderSize), preview.Length);
    preview.CopyTo(result.AsSpan(PixiaFile.PreviewAt));
    body.CopyTo(result, PixiaFile.PreviewAt + preview.Length);

    return result;
  }

  /// <summary>Runs of a count and one blue, green and red.</summary>
  private static void _WriteColourRuns(List<byte> body, byte[] stored) {
    var count = stored.Length / 3;
    var at = 0;

    while (at < count) {
      var run = 1;
      while (run < _MAXIMUM_RUN && at + run < count
             && stored[(at + run) * 3] == stored[at * 3]
             && stored[(at + run) * 3 + 1] == stored[at * 3 + 1]
             && stored[(at + run) * 3 + 2] == stored[at * 3 + 2])
        ++run;

      body.Add((byte)run);
      body.Add(stored[at * 3 + 2]);
      body.Add(stored[at * 3 + 1]);
      body.Add(stored[at * 3]);
      at += run;
    }

    body.AddRange([PixiaFile.RunTerminator, 0, 0, 0]);
  }

  /// <summary>The opacity plane, opaque everywhere.</summary>
  private static void _WriteOpaquePlane(List<byte> body, int count) {
    for (var at = 0; at < count;) {
      var run = Math.Min(_MAXIMUM_RUN, count - at);
      body.Add((byte)run);
      body.Add(0xFF);
      at += run;
    }

    body.AddRange([PixiaFile.RunTerminator, 0]);
  }
}
