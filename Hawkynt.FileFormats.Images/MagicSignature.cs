namespace Hawkynt.FileFormats.Images;

/// <summary>
/// A magic-byte signature that identifies a file format from its raw bytes.
/// Emitted at compile time by <c>FileFormat.Registry.Generator</c> from
/// <c>[FormatMagicBytes]</c> attributes on <c>FileFormat.&lt;Name&gt;.&lt;Name&gt;File</c> types.
/// </summary>
/// <param name="Signature">The bytes that must appear at <paramref name="Offset"/>.</param>
/// <param name="Offset">Byte offset within the header where the signature must appear.</param>
/// <param name="MinHeaderLength">Minimum header length required to evaluate this signature
/// (always <c>Offset + Signature.Length</c>).</param>
public readonly record struct MagicSignature(byte[] Signature, int Offset, int MinHeaderLength);
