namespace FileFormat.Core;

/// <summary>Widens the narrow colour channels vintage hardware stores to eight bits.</summary>
/// <remarks>
/// The obvious arithmetic — multiply by 255 and divide by the maximum — is not what these machines
/// do, and it is off by one for most inputs: three bits of value 2 becomes 72 that way and 73 by
/// replication. Hardware repeats the value's own bits into the low end instead, which reaches both
/// 0 and 255 exactly and lands on the values every emulator and reference decoder produces.
/// <para/>
/// One byte per format quietly disagreeing with every other tool is exactly the kind of defect that
/// survives a test suite written against itself, so the conversion lives here rather than being
/// rewritten at each call site.
/// </remarks>
public static class ChannelScaling {

  /// <summary>Widens a two-bit channel, as the blue of a G3R3B2 byte stores it.</summary>
  public static byte Expand2(int value) => (byte)((value << 6) | (value << 4) | (value << 2) | value);

  /// <summary>Widens a three-bit channel, as the Atari ST and MSX V9938 store it.</summary>
  public static byte Expand3(int value) => (byte)((value << 5) | (value << 2) | (value >> 1));

  /// <summary>Widens a four-bit channel, as the Atari STE and TT store it.</summary>
  public static byte Expand4(int value) => (byte)((value << 4) | value);

  /// <summary>Widens a five-bit channel, as 15- and 16-bit truecolour formats store it.</summary>
  public static byte Expand5(int value) => (byte)((value << 3) | (value >> 2));

  /// <summary>Widens a six-bit channel, as the green of a 16-bit truecolour pixel stores it.</summary>
  public static byte Expand6(int value) => (byte)((value << 2) | (value >> 4));
}
