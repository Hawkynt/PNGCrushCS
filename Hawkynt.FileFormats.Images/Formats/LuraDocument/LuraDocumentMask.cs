namespace FileFormat.LuraDocument;

/// <summary>The byte scrambling LuraTech lays over the JBIG mask of every page.</summary>
/// <remarks>
/// A 32-bit state starts at <c>resolution + width + 13 + height</c>, where the resolution is the larger of the page's
/// two whole-number dots per inch and the size is the page's. Before each byte the state steps
/// <c>s ^= ((~s &gt;&gt; 1) + 1) ^ (s &lt;&lt; 1)</c>, and the byte is XORed with the state's low eight bits. The step does
/// not depend on the data, so the same operation scrambles and unscrambles.
/// </remarks>
internal static class LuraDocumentMask {

  /// <summary>The starting state for a page.</summary>
  public static uint Seed(int resolution, int width, int height) => unchecked((uint)resolution + (uint)width + 13u + (uint)height);

  /// <summary>Scrambles or unscrambles in place.</summary>
  public static void Apply(byte[] data, uint state) {
    for (var i = 0; i < data.Length; ++i) {
      state ^= unchecked(((~state >> 1) + 1) ^ (state << 1));
      data[i] ^= (byte)state;
    }
  }
}
