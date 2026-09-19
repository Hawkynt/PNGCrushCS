using System;
using System.IO;

namespace FileFormat.Codecs.UtVideo;

internal readonly record struct UtVideoT2Streams(byte[] Packed, byte[] Control);

/// <summary>The independent eight-sample packing used by Ut Video T2 bands.</summary>
/// <remarks>
/// This is a clean-room implementation from the format's observable behaviour. Each group chooses
/// the smallest signed width from two through eight bits; an all-zero group consumes no packed
/// bytes. Intra frames use three control bits and always carry gradient residuals. Delta frames use
/// four: the high bit selects a residual against the immediately preceding frame, otherwise the
/// same spatial gradient is used. There is no forward or bidirectional reference in T2.
/// </remarks>
internal static class UtVideoT2Packing {

  private const int _GROUP_SIZE = 8;

  internal static int ControlLength(int sliceBytes, bool delta) {
    if ((sliceBytes & 63) != 0)
      throw new ArgumentOutOfRangeException(nameof(sliceBytes), sliceBytes, "A T2 band contains whole 64-byte packing units.");

    return checked(sliceBytes / 64 * (delta ? 4 : 3));
  }

  internal static UtVideoT2Streams Encode(
    ReadOnlySpan<byte> plane,
    ReadOnlySpan<byte> previous,
    int stride,
    int firstRow,
    int lastRow,
    bool delta) {
    if (stride <= 0 || (stride & 63) != 0)
      throw new ArgumentOutOfRangeException(nameof(stride));
    if (firstRow < 0 || lastRow < firstRow || lastRow > plane.Length / stride)
      throw new ArgumentOutOfRangeException(nameof(firstRow));
    if (delta && previous.Length < plane.Length)
      throw new InvalidOperationException("A Ut Video T2 delta frame has no complete preceding frame to reference.");

    var sliceBytes = checked((lastRow - firstRow) * stride);
    var control = new byte[ControlLength(sliceBytes, delta)];
    using var packed = new MemoryStream(sliceBytes);
    var controlBit = 0;

    Span<byte> spatial = stackalloc byte[_GROUP_SIZE];
    Span<byte> temporal = stackalloc byte[_GROUP_SIZE];

    for (var row = firstRow; row < lastRow; ++row) {
      var rowAt = row * stride;
      var firstBandRow = row == firstRow;
      byte left = firstBandRow ? (byte)128 : (byte)0;
      byte topLeft = 0;

      for (var column = 0; column < stride; column += _GROUP_SIZE) {
        for (var i = 0; i < _GROUP_SIZE; ++i) {
          var at = rowAt + column + i;
          var sample = plane[at];
          var above = firstBandRow ? (byte)0 : plane[at - stride];
          var predicted = firstBandRow ? left : (byte)(left + above - topLeft);

          spatial[i] = (byte)(sample - predicted);
          if (delta)
            temporal[i] = (byte)(sample - previous[at]);

          left = sample;
          if (!firstBandRow)
            topLeft = above;
        }

        var spatialBits = _RequiredBits(spatial);
        var mode = spatialBits == 0 ? 0 : spatialBits - 1;
        ReadOnlySpan<byte> selected = spatial;
        var selectedBits = spatialBits;

        if (delta) {
          var temporalBits = _RequiredBits(temporal);
          if (temporalBits == 0 || spatialBits != 0 && temporalBits <= spatialBits) {
            mode = 8 | (temporalBits == 0 ? 0 : temporalBits - 1);
            selected = temporal;
            selectedBits = temporalBits;
          }
        }

        _WriteBits(control, controlBit, mode, delta ? 4 : 3);
        controlBit += delta ? 4 : 3;
        _WriteResidualGroup(packed, selected, selectedBits);
      }
    }

    return new(packed.ToArray(), control);
  }

  internal static void Decode(
    ReadOnlySpan<byte> packed,
    ReadOnlySpan<byte> control,
    Span<byte> plane,
    ReadOnlySpan<byte> previous,
    int stride,
    int firstRow,
    int lastRow,
    bool delta,
    int planeIndex,
    int sliceIndex) {
    if (stride <= 0 || (stride & 63) != 0)
      throw new InvalidDataException($"Plane {planeIndex} has a T2 stride of {stride}, which is not a whole 64-byte packing unit.");
    if (firstRow < 0 || lastRow < firstRow || lastRow > plane.Length / stride)
      throw new InvalidDataException($"Plane {planeIndex} slice {sliceIndex} addresses rows outside its allocated picture.");
    if (delta && previous.Length < plane.Length)
      throw new InvalidDataException("A Ut Video T2 delta frame was received before a complete reference frame.");

    var expectedControl = ControlLength(checked((lastRow - firstRow) * stride), delta);
    if (control.Length != expectedControl)
      throw new InvalidDataException(
        $"Plane {planeIndex} slice {sliceIndex} carries {control.Length} control bytes where {expectedControl} are required.");

    var packedAt = 0;
    var controlBit = 0;
    Span<byte> residuals = stackalloc byte[_GROUP_SIZE];

    for (var row = firstRow; row < lastRow; ++row) {
      var rowAt = row * stride;
      var firstBandRow = row == firstRow;
      byte left = firstBandRow ? (byte)128 : (byte)0;
      byte topLeft = 0;

      for (var column = 0; column < stride; column += _GROUP_SIZE) {
        var mode = _ReadBits(control, controlBit, delta ? 4 : 3);
        controlBit += delta ? 4 : 3;
        var temporal = delta && (mode & 8) != 0;
        var widthCode = mode & 7;
        var bits = widthCode == 0 ? 0 : widthCode + 1;
        _ReadResidualGroup(packed, ref packedAt, residuals, bits, planeIndex, sliceIndex);

        for (var i = 0; i < _GROUP_SIZE; ++i) {
          var at = rowAt + column + i;
          var above = firstBandRow ? (byte)0 : plane[at - stride];
          byte sample;
          if (temporal) {
            sample = (byte)(previous[at] + residuals[i]);
          } else {
            var predicted = firstBandRow ? left : (byte)(left + above - topLeft);
            sample = (byte)(predicted + residuals[i]);
          }

          plane[at] = sample;
          left = sample;
          if (!firstBandRow)
            topLeft = above;
        }
      }
    }

    if (packedAt != packed.Length)
      throw new InvalidDataException(
        $"Plane {planeIndex} slice {sliceIndex} consumes {packedAt} of its {packed.Length} packed bytes.");
  }

  private static int _RequiredBits(ReadOnlySpan<byte> residuals) {
    var required = 0;
    foreach (var residual in residuals) {
      var value = residual < 128 ? residual : residual - 256;
      if (value == 0)
        continue;

      var bits = 2;
      while (bits < 8) {
        var limit = 1 << (bits - 1);
        if (value >= -limit && value < limit)
          break;
        ++bits;
      }

      required = Math.Max(required, bits);
    }

    return required;
  }

  private static void _WriteResidualGroup(Stream output, ReadOnlySpan<byte> residuals, int bits) {
    if (bits == 0)
      return;

    var midpoint = 1 << (bits - 1);
    ulong word = 0;
    for (var i = 0; i < _GROUP_SIZE; ++i) {
      var signed = residuals[i] < 128 ? residuals[i] : residuals[i] - 256;
      var code = signed + midpoint;
      word |= (ulong)code << (i * bits);
    }

    for (var i = 0; i < bits; ++i)
      output.WriteByte((byte)(word >> (i * 8)));
  }

  private static void _ReadResidualGroup(
    ReadOnlySpan<byte> packed,
    ref int at,
    Span<byte> residuals,
    int bits,
    int planeIndex,
    int sliceIndex) {
    residuals.Clear();
    if (bits == 0)
      return;

    if (at > packed.Length - bits)
      throw new InvalidDataException(
        $"Plane {planeIndex} slice {sliceIndex} ends inside an {bits}-byte T2 symbol group.");

    ulong word = 0;
    for (var i = 0; i < bits; ++i)
      word |= (ulong)packed[at + i] << (i * 8);
    at += bits;

    var mask = (1u << bits) - 1;
    var midpoint = 1 << (bits - 1);
    for (var i = 0; i < _GROUP_SIZE; ++i) {
      var code = (int)(word >> (i * bits) & mask);
      residuals[i] = (byte)(code - midpoint);
    }
  }

  private static void _WriteBits(Span<byte> target, int bitOffset, int value, int width) {
    for (var bit = 0; bit < width; ++bit)
      if ((value & 1 << bit) != 0)
        target[(bitOffset + bit) >> 3] |= (byte)(1 << ((bitOffset + bit) & 7));
  }

  private static int _ReadBits(ReadOnlySpan<byte> source, int bitOffset, int width) {
    var value = 0;
    for (var bit = 0; bit < width; ++bit)
      value |= ((source[(bitOffset + bit) >> 3] >> ((bitOffset + bit) & 7)) & 1) << bit;
    return value;
  }
}
