using FileFormat.Core;

namespace FileFormat.Ani;

/// <summary>ANI header data from the 'anih' chunk (36 bytes).</summary>
[GenerateSerializer]
public readonly partial record struct AniHeader(
  int CbSize,
  int NumFrames,
  int NumSteps,
  int Width,
  int Height,
  int BitCount,
  int NumPlanes,
  int DisplayRate,
  int Flags
) {

 public const int StructSize = 36;

 /// <summary>The frames are whole icon or cursor files rather than bare bitmaps (AF_ICON).</summary>
 /// <remarks>
 /// Microsoft's own <c>ANICUR.H</c> defines <c>AF_ICON 0x0001L</c> and
 /// <c>AF_SEQUENCE 0x0002L</c>, and Wine's <c>cursoricon.c</c> agrees. When this bit is set the
 /// header's width, height and depth describe nothing — each frame carries its own.
 /// </remarks>
 public bool HasIconFrames => (this.Flags & 1) != 0;

 /// <summary>Whether the ANI states a sequence (AF_SEQUENCE, flag bit 1).</summary>
 /// <remarks>
 /// This read bit 0 until it was checked against the format: bit 0 is AF_ICON, which every
 /// real-world animated cursor sets, so every file looked as though it had a sequence and none
 /// that actually had one was distinguishable from the rest. The <c>seq </c> chunk itself is
 /// still read whenever it is present rather than only when this says so, because Windows reads
 /// it unconditionally and a file that carries one means it.
 /// </remarks>
 public bool HasSequence => (this.Flags & 2) != 0;

 public static HeaderFieldDescriptor[] GetFieldMap()
 => HeaderFieldMapper.GetFieldMap<AniHeader>();
}
