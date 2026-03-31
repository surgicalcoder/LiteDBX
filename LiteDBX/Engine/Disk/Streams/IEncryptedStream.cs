using System.IO;

namespace LiteDbX.Engine;

internal interface IEncryptedStream
{
    AESEncryptionType EncryptionType { get; }
}

