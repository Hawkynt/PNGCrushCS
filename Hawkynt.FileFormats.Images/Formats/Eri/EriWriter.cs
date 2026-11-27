using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FileFormat.Eri;

/// <summary>Writes ERI files in the standard lossless format (level 1), which every ERI decoder reads.</summary>
/// <remarks>
/// Full-colour, grey and opacity pictures are coded in 16×16 blocks with the run-length gamma
/// code, each block given whichever colour operation leaves the smallest differences; paletted
/// pictures use the 256-colour move-to-front code. Rows are stored top row first. Every frame is
/// written whole, so each stands alone.
/// </remarks>
public static class EriWriter {

  private const string _FORMAT_NAME = "Entis Rasterized Image - 1";
  private const int _BLOCKING_DEGREE = 4;

  public static byte[] ToBytes(EriFile file) {
    var frames = file.Frames ?? throw new ArgumentException("An ERI file needs at least one frame.", nameof(file));
    if (frames.Count == 0)
      throw new ArgumentException("An ERI file needs at least one frame.", nameof(file));

    var colorType = file.ColorType;
    var width = file.Width;
    var height = file.Height;
    if (width < 1 || height < 1 || width > EriReader.MaxDimension || height > EriReader.MaxDimension)
      throw new ArgumentException($"ERI pictures are 1 to {EriReader.MaxDimension} pixels a side, not {width}x{height}.", nameof(file));

    var channels = colorType switch { EriColorType.Rgba => 4, EriColorType.Rgb => 3, _ => 1 };
    var (formatType, bits) = colorType switch {
      EriColorType.Rgba => (EriInfoHeader.RgbaImage, 32),
      EriColorType.Rgb => (EriInfoHeader.RgbImage, 24),
      EriColorType.Gray => (EriInfoHeader.GrayImage, 8),
      _ => (EriInfoHeader.RgbImage | EriInfoHeader.WithPalette
            | (file.Info.FormatType & EriInfoHeader.UseClipping), 8),
    };
    if (colorType == EriColorType.Palette && file.Palette is not { Length: >= 256 * 3 })
      throw new ArgumentException("A paletted ERI picture needs a 256-entry palette.", nameof(file));

    var info = new EriInfoHeader(
      EriInfoHeader.StandardVersion, EriInfoHeader.LosslessTransformation, EriInfoHeader.RunLengthGamma,
      formatType, width, -height, bits, colorType == EriColorType.Palette ? file.Info.ClippedPixel : 0,
      0, 0, 0, colorType == EriColorType.Palette ? 0 : _BLOCKING_DEGREE, 0, 0, 0);

    var contains = EriFileHeader.ContainImage
      | (colorType == EriColorType.Palette ? EriFileHeader.ContainPalette : 0)
      | (file.Sequence is { Length: > 0 } ? EriFileHeader.ContainSequence : 0);
    var fileHeader = new EriFileHeader(EriInfoHeader.StandardVersion, contains, 1, (uint)frames.Count, file.FileHeader.TotalTime);

    var header = new MemoryStream();
    var body = new byte[EriInfoHeader.Size];
    var fileBody = new byte[EriFileHeader.Size];
    fileHeader.Write(fileBody);
    _Record(header, "FileHdr ", fileBody);
    info.Write(body);
    _Record(header, "ImageInf", body);
    if (file.Sequence is { Length: > 0 } sequence) {
      var table = new byte[sequence.Length * 8];
      for (var i = 0; i < sequence.Length; ++i) {
        BinaryPrimitives.WriteUInt32LittleEndian(table.AsSpan(i * 8), sequence[i].Frame);
        BinaryPrimitives.WriteUInt32LittleEndian(table.AsSpan(i * 8 + 4), sequence[i].Duration);
      }
      _Record(header, "Sequence", table);
    }
    if (!string.IsNullOrEmpty(file.Copyright))
      _Record(header, "cpyright", _Text(file.Copyright));
    if (!string.IsNullOrEmpty(file.Description))
      _Record(header, "descript", _Text(file.Description));

    var stream = new MemoryStream();
    if (colorType == EriColorType.Palette) {
      var palette = new byte[256 * 4];
      for (var i = 0; i < 256; ++i) {
        palette[i * 4] = file.Palette![i * 3 + 2];
        palette[i * 4 + 1] = file.Palette[i * 3 + 1];
        palette[i * 4 + 2] = file.Palette[i * 3];
      }
      _Record(stream, "Palette ", palette);
    }

    foreach (var frame in frames) {
      if (frame is null || frame.Length < width * height * channels)
        throw new ArgumentException($"Every frame needs {width * height * channels} bytes.", nameof(file));
      var code = colorType == EriColorType.Palette
        ? EriLosslessEncoder.EncodePaletteRows(frame, width, height)
        : EriLosslessEncoder.EncodeBlocks(frame, width, height, channels, _BLOCKING_DEGREE, colorType == EriColorType.Gray);
      _Record(stream, "ImageFrm", code);
    }

    var output = new MemoryStream();
    var emc = new byte[EriReader.RecordsOffset];
    EriReader.Signature.CopyTo(emc);
    BinaryPrimitives.WriteUInt32LittleEndian(emc.AsSpan(8), EriReader.RasterizedImageId);
    Encoding.ASCII.GetBytes(_FORMAT_NAME).CopyTo(emc, 16);
    output.Write(emc);
    _Record(output, "Header  ", header.ToArray());
    _Record(output, "Stream  ", stream.ToArray());
    return output.ToArray();
  }

  private static byte[] _Text(string text) {
    var unicode = Encoding.Unicode.GetBytes(text);
    var result = new byte[2 + unicode.Length];
    result[0] = 0xFF;
    result[1] = 0xFE;
    unicode.CopyTo(result, 2);
    return result;
  }

  private static void _Record(Stream output, string id, ReadOnlySpan<byte> body) {
    Span<byte> head = stackalloc byte[16];
    Encoding.ASCII.GetBytes(id, head);
    BinaryPrimitives.WriteUInt64LittleEndian(head[8..], (ulong)body.Length);
    output.Write(head);
    output.Write(body);
  }
}

/// <summary>Codes pictures in the standard lossless ERI format.</summary>
internal static class EriLosslessEncoder {

  private static readonly int[] _ColorOperationCandidates = [0, 5, 6, 7, 9, 10, 11, 13, 14, 15];

  /// <summary>Codes a full-colour or grey picture in blocks, block-dependent, with the run-length gamma code.</summary>
  /// <param name="pixels">Top row first, <paramref name="channels"/> bytes a pixel in blue, green, red, alpha order.</param>
  public static byte[] EncodeBlocks(ReadOnlySpan<byte> pixels, int width, int rows, int channels, int degree, bool gray) {
    var size = 1 << degree;
    var area = size * size;
    var widthBlocks = (width + size - 1) >> degree;
    var heightBlocks = (rows + size - 1) >> degree;
    var blockSamples = area * channels;

    var operations = new List<int>(widthBlocks * heightBlocks);
    var stream = new List<sbyte>(widthBlocks * heightBlocks * blockSamples);
    var line = new byte[channels * widthBlocks * size];
    var column = new sbyte[size * channels];
    var block = new byte[blockSamples];
    var vertical = new sbyte[blockSamples];
    var horizontal = new sbyte[blockSamples];
    var trial = new sbyte[blockSamples];
    var best = new sbyte[blockSamples];

    for (var by = 0; by < heightBlocks; ++by) {
      Array.Clear(column);
      for (var bx = 0; bx < widthBlocks; ++bx) {
        // Sample the block, repeating the picture's last column and row into whatever lies outside it.
        for (var c = 0; c < channels; ++c)
          for (var y = 0; y < size; ++y) {
            var py = Math.Min(by * size + y, rows - 1);
            for (var x = 0; x < size; ++x) {
              var px = Math.Min(bx * size + x, width - 1);
              block[c * area + y * size + x] = pixels[(py * width + px) * channels + c];
            }
          }

        // Down first, against the row above; then across, against the block to the left.
        var lineSegment = line.AsSpan(bx * size * channels, size * channels);
        for (var c = 0; c < channels; ++c) {
          for (var y = 0; y < size; ++y)
            for (var x = 0; x < size; ++x) {
              var at = c * area + y * size + x;
              var above = y == 0 ? lineSegment[c * size + x] : block[at - size];
              vertical[at] = (sbyte)(block[at] - above);
            }
          block.AsSpan(c * area + (size - 1) * size, size).CopyTo(lineSegment[(c * size)..]);
        }

        for (var r = 0; r < size * channels; ++r) {
          var previous = column[r];
          for (var x = 0; x < size; ++x) {
            var at = r * size + x;
            horizontal[at] = (sbyte)(vertical[at] - previous);
            previous = vertical[at];
          }
          column[r] = previous;
        }

        if (channels < 3) {
          stream.AddRange(horizontal);
          continue;
        }

        var bestCost = long.MaxValue;
        var bestOperation = 0;
        foreach (var operation in _ColorOperationCandidates) {
          horizontal.CopyTo(trial, 0);
          var (source, first, second) = EriBlockOperation.ColorOperations[operation];
          if (source >= 0)
            for (var i = 0; i < area; ++i) {
              var value = horizontal[source * area + i];
              trial[first * area + i] -= value;
              if (second >= 0)
                trial[second * area + i] -= value;
            }

          long cost = 0;
          foreach (var value in trial)
            cost += value == 0 ? 1 : 1 + EriBitWriter.GammaLength(Math.Abs((int)value));
          if (cost < bestCost) {
            bestCost = cost;
            bestOperation = operation;
            trial.CopyTo(best, 0);
          }
        }

        operations.Add(bestOperation);
        stream.AddRange(best);
      }
    }

    var bits = new EriBitWriter();
    bits.WriteBits(1, 8);
    bits.WriteBits(0, 8);
    bits.WriteBits(1, 8);
    bits.WriteBits(0, 8);
    if (channels >= 3)
      foreach (var operation in operations)
        bits.WriteBits(operation, 4);
    bits.WriteBit(0);
    _WriteRuns(bits, stream);
    return bits.ToArray();
  }

  /// <summary>Writes the alternating runs of zeros and non-zero values, led by the bit that says which comes first.</summary>
  private static void _WriteRuns(EriBitWriter bits, List<sbyte> symbols) {
    if (symbols.Count == 0)
      return;

    bits.WriteBit(symbols[0] == 0 ? 0 : 1);
    var i = 0;
    while (i < symbols.Count) {
      var zeros = symbols[i] == 0;
      var end = i;
      while (end < symbols.Count && (symbols[end] == 0) == zeros)
        ++end;

      bits.WriteGamma(end - i);
      if (!zeros)
        for (var j = i; j < end; ++j) {
          var value = (int)symbols[j];
          // -128 has no positive counterpart in a byte; its magnitude 128 wraps back to it unsigned.
          if (value < 0 && value != -128) {
            bits.WriteBit(1);
            bits.WriteGamma(-value);
          } else {
            bits.WriteBit(0);
            bits.WriteGamma(value == -128 ? 128 : value);
          }
        }

      i = end;
    }
  }

  /// <summary>Codes a paletted picture row by row with the RL-MTF-G code.</summary>
  public static byte[] EncodePaletteRows(ReadOnlySpan<byte> pixels, int width, int rows) {
    var bits = new EriBitWriter();
    bits.WriteBits(2, 8);
    bits.WriteBits(0, 24);

    var tables = new byte[0x101][];
    for (var t = 0; t <= 0x100; ++t) {
      tables[t] = new byte[256];
      for (var i = 0; i < 256; ++i)
        tables[t][i] = (byte)i;
    }

    for (var y = 0; y < rows; ++y) {
      var row = pixels.Slice(y * width, width);
      var table = tables[0x100];
      var last = -1;
      var x = 0;
      while (x < width) {
        var value = row[x++];
        var index = Array.IndexOf(table, value);
        bits.WriteGamma(index + 1);
        Array.Copy(table, 0, table, 1, index);
        table[0] = value;

        if (last == value) {
          var repeat = 0;
          while (x + repeat < width && row[x + repeat] == value)
            ++repeat;
          bits.WriteGamma(repeat + 1);
          x += repeat;
        } else {
          last = value;
          table = tables[value];
        }
      }
    }

    return bits.ToArray();
  }
}
