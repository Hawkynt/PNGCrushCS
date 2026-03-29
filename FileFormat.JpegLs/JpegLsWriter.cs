using System;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.JpegLs;

/// <summary>Assembles JPEG-LS file bytes from pixel data using the LOCO-I algorithm.</summary>
public static class JpegLsWriter {

  public static byte[] ToBytes(JpegLsFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return _Encode(file.PixelData, file.Width, file.Height, file.ComponentCount, file.BitsPerSample, file.NearLossless);
  }

  private static byte[] _Encode(byte[] pixelData, int width, int height, int componentCount, int bitsPerSample, int near) {
    var maxVal = (1 << bitsPerSample) - 1;
    var bytesPerSample = bitsPerSample > 8 ? 2 : 1;

    using var ms = new MemoryStream();

    _WriteMarker(ms, JpegLsCodec.MarkerSoi);
    _WriteSof55(ms, bitsPerSample, height, width, componentCount);

    for (var c = 0; c < componentCount; ++c) {
      var codec = JpegLsCodec.CreateDefault(maxVal, near);
      _WriteSos(ms, [(byte)(c + 1)], near, JpegLsInterleaveMode.None, 0);

      var component = _ExtractComponent(pixelData, width, height, componentCount, c, bytesPerSample);

      var encoded = _EncodeComponent(component, width, height, codec);
      ms.Write(encoded, 0, encoded.Length);
    }

    _WriteMarker(ms, JpegLsCodec.MarkerEoi);

    return ms.ToArray();
  }

  private static int[] _ExtractComponent(byte[] pixelData, int width, int height, int componentCount, int componentIndex, int bytesPerSample) {
    var size = width * height;
    var result = new int[size];
    if (bytesPerSample == 2)
      for (var i = 0; i < size; ++i) {
        var offset = (i * componentCount + componentIndex) * 2;
        result[i] = (pixelData[offset] << 8) | pixelData[offset + 1];
      }
    else
      for (var i = 0; i < size; ++i)
        result[i] = pixelData[i * componentCount + componentIndex];
    return result;
  }

  private static byte[] _EncodeComponent(int[] samples, int width, int height, JpegLsCodec codec) {
    var writer = new BitWriter();

    for (var y = 0; y < height; ++y) {
      var x = 0;
      while (x < width) {
        JpegLsPredictor.GetNeighbors(samples, width, height, x, y, out var a, out var b, out var c, out var d);
        JpegLsPredictor.ComputeGradients(a, b, c, d, out var d1, out var d2, out var d3);

        var ctx = codec.QuantizeContext(d1, d2, d3, out var negative);

        if (ctx < 0) {
          x = JpegLsRunMode.Encode(writer, samples, width, x, y, a, codec);
          continue;
        }

        var idx = y * width + x;
        codec.EncodeRegular(writer, samples[idx], a, b, c, ctx, negative);
        ++x;
      }
    }

    writer.Flush();
    return _ByteStuff(writer.ToArray());
  }

  private static byte[] _ByteStuff(byte[] data) {
    var count = 0;
    for (var i = 0; i < data.Length; ++i)
      if (data[i] == 0xFF)
        ++count;

    if (count == 0)
      return data;

    var result = new byte[data.Length + count];
    var j = 0;
    for (var i = 0; i < data.Length; ++i) {
      result[j++] = data[i];
      if (data[i] == 0xFF)
        result[j++] = 0x00;
    }

    return result;
  }

  private static void _WriteMarker(Stream stream, ushort marker) {
    stream.WriteByte((byte)(marker >> 8));
    stream.WriteByte((byte)(marker & 0xFF));
  }

  internal static void WriteSof55(Stream stream, int bitsPerSample, int height, int width, int componentCount) =>
    _WriteSof55(stream, bitsPerSample, height, width, componentCount);

  private static void _WriteSof55(Stream stream, int bitsPerSample, int height, int width, int componentCount) {
    _WriteMarker(stream, JpegLsCodec.MarkerSof55);
    var length = 8 + componentCount * 3;
    stream.WriteByte((byte)(length >> 8));
    stream.WriteByte((byte)(length & 0xFF));
    stream.WriteByte((byte)bitsPerSample);
    stream.WriteByte((byte)(height >> 8));
    stream.WriteByte((byte)(height & 0xFF));
    stream.WriteByte((byte)(width >> 8));
    stream.WriteByte((byte)(width & 0xFF));
    stream.WriteByte((byte)componentCount);

    for (var i = 0; i < componentCount; ++i) {
      stream.WriteByte((byte)(i + 1));
      stream.WriteByte(0x11);
      stream.WriteByte(0);
    }
  }

  internal static void WriteSos(Stream stream, byte[] componentIds, int near, JpegLsInterleaveMode interleave, int pointTransform) =>
    _WriteSos(stream, componentIds, near, interleave, pointTransform);

  private static void _WriteSos(Stream stream, byte[] componentIds, int near, JpegLsInterleaveMode interleave, int pointTransform) {
    _WriteMarker(stream, JpegLsCodec.MarkerSos);
    var length = 6 + componentIds.Length * 2;
    stream.WriteByte((byte)(length >> 8));
    stream.WriteByte((byte)(length & 0xFF));
    stream.WriteByte((byte)componentIds.Length);

    foreach (var id in componentIds) {
      stream.WriteByte(id);
      stream.WriteByte(0);
    }

    stream.WriteByte((byte)near);
    stream.WriteByte((byte)interleave);
    stream.WriteByte((byte)pointTransform);
  }
}

/// <summary>Bit-level writer for Golomb-Rice codes, writing MSB-first.</summary>
internal sealed class BitWriter {

  private readonly List<byte> _bytes = new();
  private int _currentByte;
  private int _bitsInCurrentByte;

  internal void WriteBit(int bit) {
    _currentByte = (_currentByte << 1) | (bit & 1);
    ++_bitsInCurrentByte;
    if (_bitsInCurrentByte == 8) {
      _bytes.Add((byte)_currentByte);
      _currentByte = 0;
      _bitsInCurrentByte = 0;
    }
  }

  internal void WriteBits(int value, int count) {
    for (var i = count - 1; i >= 0; --i)
      WriteBit((value >> i) & 1);
  }

  internal void Flush() {
    if (_bitsInCurrentByte > 0) {
      _currentByte <<= 8 - _bitsInCurrentByte;
      _bytes.Add((byte)_currentByte);
      _currentByte = 0;
      _bitsInCurrentByte = 0;
    }
  }

  internal byte[] ToArray() => _bytes.ToArray();
}
