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
 string Gamma,
 string RedChroma,
 string GreenChroma,
 string BlueChroma,
 string WhitePoint,
 int JobNumber,
 string FileName,
 string Description,
 string ProgramName,
 string MachineName,
 string User,
 string Date,
 string Aspect,
 string AspectRatio,
 string ColorChannel,
 short FieldRendered,
 string Time,
 string Filter,
 short NumBits,
 short MatteType,
 short MatteBits,
 short AuxType,
 short AuxBits,
 string AuxData,
 [property: FieldOffset(736)] int Next
) {

 public const int StructSize = 740;

 public static HeaderFieldDescriptor[] GetFieldMap()
 => HeaderFieldMapper.GetFieldMap<RlaHeader>();
}
