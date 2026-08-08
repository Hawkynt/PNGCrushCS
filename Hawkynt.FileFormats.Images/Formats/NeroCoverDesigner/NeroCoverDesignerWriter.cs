using FileFormat.Wrappers;

namespace FileFormat.NeroCoverDesigner;

/// <summary>Assembles the wrapper: the name it opens with, then the picture.</summary>
public static class NeroCoverDesignerWriter {

  public static byte[] ToBytes(NeroCoverDesignerFile file) => WrappedPicture.Assemble(NeroCoverDesignerFile.Magic, file.Embedded ?? []);
}
