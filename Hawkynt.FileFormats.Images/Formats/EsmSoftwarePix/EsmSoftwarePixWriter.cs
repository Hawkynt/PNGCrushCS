using FileFormat.Wrappers;

namespace FileFormat.EsmSoftwarePix;

/// <summary>Assembles the wrapper: the name it opens with, then the picture.</summary>
public static class EsmSoftwarePixWriter {

  public static byte[] ToBytes(EsmSoftwarePixFile file) => WrappedPicture.Assemble(EsmSoftwarePixFile.Magic, file.Embedded ?? []);
}
