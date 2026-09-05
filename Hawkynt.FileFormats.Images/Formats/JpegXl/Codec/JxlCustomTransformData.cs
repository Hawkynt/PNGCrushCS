using System;

namespace FileFormat.JpegXl.Codec;

/// <summary>
/// The bundle that follows <see cref="JxlImageMetadata"/> in the codestream:
/// the inverse opsin matrix a XYB image is brought back through, and the
/// weights the upsamplers use (libjxl <c>CustomTransformData</c> in
/// <c>lib/jxl/image_metadata.cc</c>).
/// </summary>
/// <remarks>
/// Nothing here is optional. The bundle is always present, and when its fields
/// are left at their defaults it is one bit long — which is why leaving it
/// unread stayed invisible for so long: the byte alignment that follows
/// normally swallows that one bit. It only shows itself when the metadata
/// happens to end on a byte boundary, and then every bit of the frame that
/// follows is off by one.
/// </remarks>
internal sealed class JxlCustomTransformData {

  public bool AllDefault { get; init; }

  /// <summary>False when the file states its own inverse opsin matrix, biases
  /// or quantization biases rather than the ones the format defines.</summary>
  public bool OpsinAllDefault { get; init; } = true;

  /// <summary>The nine inverse-opsin coefficients, then three opsin biases,
  /// then four quantization biases, in the order the file states them. Empty
  /// when the file leaves them at their defaults.</summary>
  public float[] OpsinValues { get; init; } = [];

  /// <summary>Which upsampling weight sets the file states for itself:
  /// bit 0 for 2x, bit 1 for 4x, bit 2 for 8x.</summary>
  public uint CustomWeightsMask { get; init; }

  public static JxlCustomTransformData Decode(JxlBitReader r, bool xybEncoded) {
    ArgumentNullException.ThrowIfNull(r);

    if (r.ReadBool())
      return new() { AllDefault = true };

    var opsinAllDefault = true;
    var opsinValues = Array.Empty<float>();
    if (xybEncoded) {
      opsinAllDefault = r.ReadBool();
      if (!opsinAllDefault) {
        // Nine matrix coefficients, three opsin biases, four quantization biases.
        opsinValues = new float[16];
        for (var i = 0; i < opsinValues.Length; ++i)
          opsinValues[i] = _ReadF16(r);
      }
    }

    var mask = r.ReadBits(3);
    if ((mask & 1) != 0)
      _SkipWeights(r, 15);
    if ((mask & 2) != 0)
      _SkipWeights(r, 55);
    if ((mask & 4) != 0)
      _SkipWeights(r, 210);

    return new() {
      AllDefault = false,
      OpsinAllDefault = opsinAllDefault,
      OpsinValues = opsinValues,
      CustomWeightsMask = mask,
    };
  }

  private static void _SkipWeights(JxlBitReader r, int count) {
    for (var i = 0; i < count; ++i)
      _ReadF16(r);
  }

  /// <summary>Read a half-precision float the way the format states it: sign,
  /// five bits of exponent, ten of mantissa, least significant bit first.</summary>
  private static float _ReadF16(JxlBitReader r) => (float)BitConverter.UInt16BitsToHalf((ushort)r.ReadBits(16));
}
