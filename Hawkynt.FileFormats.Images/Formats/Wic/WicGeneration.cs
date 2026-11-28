namespace FileFormat.Wic;

/// <summary>Which of the two JWIC header generations a file carries.</summary>
/// <remarks>
/// The generations share the header layout and the coding, and differ in two things that decide how
/// a file reads: the magic number, and which colour the first channel of a colour file holds. The
/// 1.0 translator took red into its first channel; 1.01 took the bytes in BeOS's B, G, R memory
/// order instead, so its first channel is blue. Reading a 1.01 file as 1.0 swaps red and blue.
/// </remarks>
public enum WicGeneration {

  /// <summary>JWICTranslator 1.0 (January 2000): magic <c>1B 7A FB 30</c>, version 1, channels R, G, B.</summary>
  Version100,

  /// <summary>JWICTranslator 1.01 and the Java applet (2000): magic <c>FA DE BA BE</c>, version 0x0101, channels B, G, R.</summary>
  Version101,
}
