using FileFormat.Core;

namespace FileFormat.Neochrome;

/// <summary>The 128-byte header at the start of every NEOchrome file. All fields are big-endian.</summary>
[GenerateSerializer, Endian(Endianness.Big)]
[Filler(48, 80)]
public readonly partial record struct NeochromeHeader( short Flag, short Resolution, short Pal0, short Pal1, short Pal2, short Pal3, short Pal4, short Pal5, short Pal6, short Pal7, short Pal8, short Pal9, short Pal10, short Pal11, short Pal12, short Pal13, short Pal14, short Pal15, byte AnimSpeed, byte AnimDirection, short AnimSteps, short AnimXOffset, short AnimYOffset, short AnimWidth, short AnimHeight
) {

 public const int StructSize = 128;

 /// <summary>Extracts the 16-entry palette from individual fields.</summary>
 public short[] GetPalette() => [
 this.Pal0, this.Pal1, this.Pal2, this.Pal3,
 this.Pal4, this.Pal5, this.Pal6, this.Pal7,
 this.Pal8, this.Pal9, this.Pal10, this.Pal11,
 this.Pal12, this.Pal13, this.Pal14, this.Pal15
 ];

 public static HeaderFieldDescriptor[] GetFieldMap()
 => HeaderFieldMapper.GetFieldMap<NeochromeHeader>();
}
