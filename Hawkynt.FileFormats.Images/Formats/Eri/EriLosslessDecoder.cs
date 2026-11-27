using System;
using System.IO;

namespace FileFormat.Eri;

/// <summary>Decodes one lossless ERI image record into rows of samples, in the order the file stores them.</summary>
/// <remarks>
/// The picture is cut into square blocks of 2^n pixels and each block's channels are coded one
/// after the other, as signed byte differences: every sample has the one to its left and the one
/// above subtracted (the left from the block to the left, the above from the block above), and one
/// colour channel may have another subtracted as well — the "colour operation", which a code in
/// front of each block selects. The extended format adds an "arrangement" to that code: the
/// samples of a block may be sent column by column, or with the channels interleaved.
/// <para/>
/// The 256-colour format is not blocked at all: each row is a run of palette indices coded by
/// move-to-front with a table per preceding colour, or by the ERINA code over the same context.
/// </remarks>
internal static class EriLosslessDecoder {

  /// <summary>How many channels a picture of this type is coded in.</summary>
  public static int Channels(in EriInfoHeader info)
    => (info.FormatType & EriInfoHeader.TypeMask) switch {
      EriInfoHeader.GrayImage => 1,
      EriInfoHeader.RgbImage when info.BitsPerPixel <= 8 => 1,
      EriInfoHeader.RgbImage when (info.FormatType & EriInfoHeader.WithAlpha) != 0 => 4,
      EriInfoHeader.RgbImage => 3,
      _ => throw new NotSupportedException($"ERI colour type {info.FormatType:X8} is not one the format defines."),
    };

  /// <summary>Decodes <paramref name="record"/> into <paramref name="samples"/> (rows × width × channels).</summary>
  /// <param name="record">The image record's body: the four-byte ERI header and the code.</param>
  /// <param name="info">The picture's information header.</param>
  /// <param name="samples">Where the samples go; for a difference frame, holding the frame before.</param>
  /// <param name="difference">Whether the record codes the difference from the frame before.</param>
  public static void Decode(byte[] record, in EriInfoHeader info, byte[] samples, bool difference) {
    if (record.Length < 4)
      throw new InvalidDataException("ERI image record is too short to hold its header.");

    var version = record[0];
    var operationTable = record[1];
    var encodeType = record[2];
    var bitCount = record[3];

    if (version == 2) {
      if (operationTable != 0 || encodeType != 0 || bitCount != 0)
        throw new InvalidDataException("ERI 256-colour header has its reserved bytes set.");
      _DecodePaletteRows(record, info, samples);
      return;
    }

    // Nothing published writes the arithmetic code any more -- neither Entis library's encoder
    // accepts it -- so there is nothing to hold a decoder of it to.
    if (info.Architecture == EriInfoHeader.ArithmeticCode)
      throw new NotSupportedException("ERI arithmetic code (architecture 32) is not read: no encoder exists to verify a decoder against, and the specification withdraws it for full colour.");

    if (operationTable != 0 || (encodeType & 0xFE) != 0)
      throw new InvalidDataException($"ERI image header {version:X2} {operationTable:X2} {encodeType:X2} names an operation table or encoding this format does not define.");

    switch (version) {
      case 1 when bitCount != 0:
        throw new InvalidDataException("ERI standard image header states a symbol width.");
      case 1:
        break;
      case 8 when bitCount != 8:
        throw new InvalidDataException("ERINA image header does not state 8-bit symbols.");
      case 8:
        break;
      case 16 when bitCount != 8 || encodeType != 0:
        throw new InvalidDataException("ERISA image header must state 8-bit symbols and block-independent coding.");
      case 16:
        break;
      case 4:
        throw new NotSupportedException("ERI extended full-colour format (version 4) is withdrawn by its specification and not read here.");
      default:
        throw new NotSupportedException($"ERI image coding version {version} is not one the format defines for a lossless picture.");
    }

    var architecture = info.Architecture;
    if (version == 8 && architecture == EriInfoHeader.RunLengthGamma)
      throw new NotSupportedException("ERI version-8 blocks with run-length gamma symbols are not read: the ERINA library writes them but cannot read them back, and nothing else reads them as written.");
    if (version == 8 && architecture != EriInfoHeader.RunLengthHuffman)
      throw new InvalidDataException("ERINA image record under an information header that does not name the ERINA code.");
    if (version == 16 && architecture != EriInfoHeader.Nemesis)
      throw new InvalidDataException("ERISA image record under an information header that does not name the ERISA code.");
    if (version == 1 && architecture != EriInfoHeader.RunLengthGamma)
      throw new InvalidDataException($"ERI standard image record under architecture {architecture:X8}.");

    _DecodeBlocks(record, info, samples, difference, encodeType & 1, architecture);
  }

  private static void _DecodeBlocks(byte[] record, in EriInfoHeader info, byte[] samples, bool difference, int blockDependent, uint architecture) {
    var channels = Channels(info);
    if (info.BlockingDegree is < 1 or > 8)
      throw new InvalidDataException($"ERI block size 2^{info.BlockingDegree} is not one a lossless picture can use.");

    var degree = info.BlockingDegree;
    var size = 1 << degree;
    var area = size * size;
    var blockSamples = area * channels;
    var width = info.Width;
    var rows = info.Rows;
    var widthBlocks = (width + size - 1) >> degree;
    var heightBlocks = (rows + size - 1) >> degree;

    var bits = new EriBitReader(record, 4);
    var operationTree = architecture == EriInfoHeader.RunLengthHuffman ? new EriHuffmanTree() : null;
    var operationModel = architecture == EriInfoHeader.Nemesis ? new EriArithmeticModel() : null;

    byte[]? operations = null;
    if (blockDependent != 0 && channels >= 3) {
      operations = new byte[widthBlocks * heightBlocks];
      for (var i = 0; i < operations.Length; ++i)
        operations[i] = architecture == EriInfoHeader.RunLengthGamma
          ? (byte)(bits.ReadBits(4) | 0xC0)
          : (byte)_HuffmanSymbol(bits, operationTree!);
    }

    if (bits.ReadBit() != 0)
      throw new InvalidDataException("ERI reserved bit before the image code is set.");

    EriGammaDecoder? gamma = null;
    EriArithmeticDecoder? arithmetic = null;
    IEriSymbolDecoder symbols;
    switch (architecture) {
      case EriInfoHeader.RunLengthGamma:
        gamma = new EriGammaDecoder(bits);
        if (blockDependent != 0)
          gamma.Reset();
        symbols = gamma;
        break;
      case EriInfoHeader.RunLengthHuffman:
        symbols = new EriHuffmanDecoder(bits);
        break;
      default:
        arithmetic = new EriArithmeticDecoder(bits);
        symbols = new EriNemesisDecoder(arithmetic);
        break;
    }

    var arrangements = EriArrangement.Tables(size, channels);
    var line = new sbyte[channels * widthBlocks * size];
    var column = new sbyte[size * channels];
    var arranged = new sbyte[blockSamples];
    var block = new sbyte[blockSamples];
    var stride = width * channels;
    var additive = difference && channels >= 3 && info.BitsPerPixel >= 24;
    var gray = info.FormatType == EriInfoHeader.GrayImage;
    var next = 0;

    for (var by = 0; by < heightBlocks; ++by) {
      Array.Clear(column);
      var blockRows = Math.Min(size, rows - by * size);

      for (var bx = 0; bx < widthBlocks; ++bx) {
        int operation;
        if (channels >= 3) {
          if (operations != null)
            operation = operations[next++];
          else if (architecture == EriInfoHeader.Nemesis) {
            operation = arithmetic!.Decode(operationModel!);
            if (operation < 0)
              throw new InvalidDataException("ERISA block operation decoded to the escape symbol.");
          } else if (architecture == EriInfoHeader.RunLengthHuffman)
            operation = _HuffmanSymbol(bits, operationTree!);
          else {
            operation = bits.ReadBits(4) | 0xC0;
            gamma!.Reset();
          }
        } else {
          // A grey picture is differenced like any channel; a blocked paletted one is not at all.
          operation = gray ? 0xC0 : 0x00;
          if (blockDependent == 0 && gamma != null)
            gamma.Reset();
        }

        symbols.Decode(arranged);
        EriBlockOperation.Undo(operation, arranged, block, arrangements, column, line.AsSpan(bx * size * channels, size * channels), size, channels);

        var blockColumns = Math.Min(size, width - bx * size);
        for (var c = 0; c < channels; ++c)
          for (var y = 0; y < blockRows; ++y) {
            var source = c * area + y * size;
            var target = (by * size + y) * stride + bx * size * channels + c;
            for (var x = 0; x < blockColumns; ++x, target += channels)
              samples[target] = additive ? (byte)(samples[target] + block[source + x]) : (byte)block[source + x];
          }
      }
    }
  }

  private static int _HuffmanSymbol(EriBitReader bits, EriHuffmanTree tree) {
    var code = tree.Decode(bits, true);
    if (code is { } known)
      return known;
    var plain = bits.ReadBits(8);
    tree.Add(plain);
    return plain;
  }

  private static void _DecodePaletteRows(byte[] record, in EriInfoHeader info, byte[] samples) {
    if (info.BitsPerPixel != 8)
      throw new InvalidDataException("ERI 256-colour image record under a picture that is not 8 bits deep.");

    var bits = new EriBitReader(record, 4);
    var width = info.Width;
    var rows = info.Rows;

    switch (info.Architecture) {
      case EriInfoHeader.RunLengthGamma: {
        var tables = new byte[0x101][];
        for (var t = 0; t <= 0x100; ++t) {
          tables[t] = new byte[256];
          for (var i = 0; i < 256; ++i)
            tables[t][i] = (byte)i;
        }

        for (var y = 0; y < rows; ++y) {
          var row = samples.AsSpan(y * width, width);
          var table = tables[0x100];
          var last = -1;
          var x = 0;
          while (x < width) {
            var index = bits.ReadGamma() - 1;
            if (index > 0xFF)
              throw new InvalidDataException("RL-MTF-G code names a move-to-front position past the table.");

            var value = table[index];
            Array.Copy(table, 0, table, 1, index);
            table[0] = value;
            row[x++] = value;

            if (last == value) {
              var repeat = bits.ReadGamma() - 1;
              repeat = Math.Min(repeat, width - x);
              row.Slice(x, repeat).Fill(value);
              x += repeat;
            } else {
              last = value;
              table = tables[value];
            }
          }
        }

        return;
      }

      case EriInfoHeader.RunLengthHuffman: {
        var trees = new EriHuffmanTree[0x101];
        for (var t = 0; t <= 0x100; ++t)
          trees[t] = new();
        var tree = trees[0];

        for (var y = 0; y < rows; ++y) {
          var row = samples.AsSpan(y * width, width);
          var last = -1;
          var x = 0;
          while (x < width) {
            var value = _HuffmanSymbol(bits, tree);
            row[x++] = (byte)value;
            if (last == value) {
              var code = trees[0x100].Decode(bits, true);
              int length;
              if (code is { } known)
                length = known;
              else {
                length = bits.ReadGamma();
                trees[0x100].Add(length);
              }

              var repeat = length - 1;
              if (repeat > width - x)
                throw new InvalidDataException("ERINA 256-colour run continues past the end of its row.");
              row.Slice(x, repeat).Fill((byte)value);
              x += repeat;
            }

            last = value;
            tree = trees[value & 0xFF];
          }
        }

        return;
      }


      default:
        throw new InvalidDataException($"ERI 256-colour image record under architecture {info.Architecture:X8}.");
    }
  }
}
