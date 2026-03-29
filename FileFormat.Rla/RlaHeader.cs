using FileFormat.Core;

namespace FileFormat.Rla;

/// <summary>The 740-byte header at the start of every RLA image file (all fields big-endian).</summary>
[GenerateSerializer]
[HeaderFiller("Space", 700, 36)]
public readonly partial record struct RlaHeader(
  [property: HeaderField(0, 2, Endianness = Endianness.Big)] short WindowLeft,
  [property: HeaderField(2, 2, Endianness = Endianness.Big)] short WindowRight,
  [property: HeaderField(4, 2, Endianness = Endianness.Big)] short WindowBottom,
  [property: HeaderField(6, 2, Endianness = Endianness.Big)] short WindowTop,
  [property: HeaderField(8, 2, Endianness = Endianness.Big)] short ActiveWindowLeft,
  [property: HeaderField(10, 2, Endianness = Endianness.Big)] short ActiveWindowRight,
  [property: HeaderField(12, 2, Endianness = Endianness.Big)] short ActiveWindowBottom,
  [property: HeaderField(14, 2, Endianness = Endianness.Big)] short ActiveWindowTop,
  [property: HeaderField(16, 2, Endianness = Endianness.Big)] short FrameNumber,
  [property: HeaderField(18, 2, Endianness = Endianness.Big)] short StorageType,
  [property: HeaderField(20, 2, Endianness = Endianness.Big)] short NumChannels,
  [property: HeaderField(22, 2, Endianness = Endianness.Big)] short NumMatte,
  [property: HeaderField(24, 2, Endianness = Endianness.Big)] short NumAux,
  [property: HeaderField(26, 2, Endianness = Endianness.Big)] short Revision,
  [property: HeaderField(28, 16)] string Gamma,
  [property: HeaderField(44, 24)] string RedChroma,
  [property: HeaderField(68, 24)] string GreenChroma,
  [property: HeaderField(92, 24)] string BlueChroma,
  [property: HeaderField(116, 24)] string WhitePoint,
  [property: HeaderField(140, 4, Endianness = Endianness.Big)] int JobNumber,
  [property: HeaderField(144, 128)] string FileName,
  [property: HeaderField(272, 128)] string Description,
  [property: HeaderField(400, 64)] string ProgramName,
  [property: HeaderField(464, 32)] string MachineName,
  [property: HeaderField(496, 32)] string User,
  [property: HeaderField(528, 20)] string Date,
  [property: HeaderField(548, 24)] string Aspect,
  [property: HeaderField(572, 8)] string AspectRatio,
  [property: HeaderField(580, 32)] string ColorChannel,
  [property: HeaderField(612, 2, Endianness = Endianness.Big)] short FieldRendered,
  [property: HeaderField(614, 12)] string Time,
  [property: HeaderField(626, 32)] string Filter,
  [property: HeaderField(658, 2, Endianness = Endianness.Big)] short NumBits,
  [property: HeaderField(660, 2, Endianness = Endianness.Big)] short MatteType,
  [property: HeaderField(662, 2, Endianness = Endianness.Big)] short MatteBits,
  [property: HeaderField(664, 2, Endianness = Endianness.Big)] short AuxType,
  [property: HeaderField(666, 2, Endianness = Endianness.Big)] short AuxBits,
  [property: HeaderField(668, 32)] string AuxData,
  [property: HeaderField(736, 4, Endianness = Endianness.Big)] int Next
) {

  public const int StructSize = 740;

  public static HeaderFieldDescriptor[] GetFieldMap()
    => HeaderFieldMapper.GetFieldMap<RlaHeader>();
}
