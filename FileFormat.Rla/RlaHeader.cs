using FileFormat.Core;

namespace FileFormat.Rla;

/// <summary>The 740-byte header at the start of every RLA image file (all fields big-endian).</summary>
[GenerateSerializer, Endian(Endianness.Big)]
[Filler(700, 36)]
public readonly partial record struct RlaHeader(
 short WindowLeft,
 short WindowRight,
 short WindowBottom,
 short WindowTop,
 short ActiveWindowLeft,
 short ActiveWindowRight,
 short ActiveWindowBottom,
 short ActiveWindowTop,
 short FrameNumber,
 short StorageType,
 short NumChannels,
 short NumMatte,
 short NumAux,
 short Revision,
 [property: SeqField(Size = 16)] string Gamma,
 [property: SeqField(Size = 24)] string RedChroma,
 [property: SeqField(Size = 24)] string GreenChroma,
 [property: SeqField(Size = 24)] string BlueChroma,
 [property: SeqField(Size = 24)] string WhitePoint,
 int JobNumber,
 [property: SeqField(Size = 128)] string FileName,
 [property: SeqField(Size = 128)] string Description,
 [property: SeqField(Size = 64)] string ProgramName,
 [property: SeqField(Size = 32)] string MachineName,
 [property: SeqField(Size = 32)] string User,
 [property: SeqField(Size = 20)] string Date,
 [property: SeqField(Size = 24)] string Aspect,
 [property: SeqField(Size = 8)] string AspectRatio,
 [property: SeqField(Size = 32)] string ColorChannel,
 short FieldRendered,
 [property: SeqField(Size = 12)] string Time,
 [property: SeqField(Size = 32)] string Filter,
 short NumBits,
 short MatteType,
 short MatteBits,
 short AuxType,
 short AuxBits,
 [property: SeqField(Size = 32)] string AuxData,
 [property: FieldOffset(736)] int Next
) {

 public const int StructSize = 740;

 public static HeaderFieldDescriptor[] GetFieldMap()
 => HeaderFieldMapper.GetFieldMap<RlaHeader>();
}
