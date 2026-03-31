using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using FluentAssertions;
using LiteDbX.Engine;
using Xunit;

namespace LiteDbX.Internals;

public class Aes_Tests
{
    [Theory]
    [InlineData(AESEncryptionType.ECB)]
    [InlineData(AESEncryptionType.GCM)]
    public void Encrypt_Decrypt_Stream(AESEncryptionType encryptionType)
    {
        using (var media = new MemoryStream())
        {
            using (var crypto = CreateEncryptedStream("abc", media, encryptionType))
            {
                var input0 = new byte[8192];
                var input1 = new byte[8192];
                var input2 = new byte[8192];

                var output0 = new byte[8192];
                var output1 = new byte[8192];
                var output2 = new byte[8192];

                input0.Fill(100, 0, 8192);
                input1.Fill(101, 0, 8192);
                input2.Fill(102, 0, 8192);

                // write 0, 2, 1 but in order 0, 1, 2
                crypto.Position = 0 * 8192;
                crypto.Write(input0, 0, 8192);

                crypto.Position = 2 * 8192;
                crypto.Write(input2, 0, 8192);

                crypto.Position = 1 * 8192;
                crypto.Write(input1, 0, 8192);

                // read encrypted data
                media.Position = 0;
                media.ReadExactly(output0, 0, 8192);
                media.ReadExactly(output1, 0, 8192);
                media.ReadExactly(output2, 0, 8192);

                output0.All(x => x == 100).Should().BeFalse();
                output1.All(x => x == 101).Should().BeFalse();
                output2.All(x => x == 102).Should().BeFalse();

                // read decrypted data
                crypto.Position = 0 * 8192;
                crypto.ReadExactly(output0, 0, 8192);

                crypto.Position = 2 * 8192;
                crypto.ReadExactly(output2, 0, 8192);

                crypto.Position = 1 * 8192;
                crypto.ReadExactly(output1, 0, 8192);

                output0.All(x => x == 100).Should().BeTrue();
                output1.All(x => x == 101).Should().BeTrue();
                output2.All(x => x == 102).Should().BeTrue();
            }
        }
    }

    [Fact]
    public void Encrypted_Stream_Factory_Prefers_Stored_Mode_On_Reopen()
    {
        byte[] persisted;
        var input = Enumerable.Repeat((byte)123, 8192).ToArray();

        using (var media = new MemoryStream())
        {
            using (var crypto = EncryptedStreamFactory.Open("abc", media, AESEncryptionType.GCM))
            {
                crypto.Write(input, 0, input.Length);
                crypto.Flush();
                persisted = media.ToArray();
            }
        }

        using var reopenedMedia = new MemoryStream(persisted);
        using var reopened = EncryptedStreamFactory.Open("abc", reopenedMedia, AESEncryptionType.ECB);

        var output = new byte[8192];
        reopened.ReadExactly(output, 0, output.Length);

        output.Should().Equal(input);
        reopened.Should().BeAssignableTo<IEncryptedStream>();
        ((IEncryptedStream)reopened).EncryptionType.Should().Be(AESEncryptionType.GCM);
    }

    /// <summary>
    /// Test whether AesStream can handle stream that has invalid page size.
    /// </summary>
    [Fact]
    public void AesStream_Invalid_Page_Size()
    {
        var fakeContent = new byte[]
        {
            1, 22, 222, 184, 3, 227, 126, 129, 205, 182, 182, 143, 201, 181, 242, 107, 36,
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            20, 88, 18, 70, 65, 77, 202, 50, 184, 177, 167, 59, 80, 255, 67, 66, 20,
            88, 18, 70, 65, 77, 202, 50, 184, 177, 167, 59, 80, 255, 67, 66
        };

        // stream of 64bytes (invalid page size)
        using (var memoryStream = new MemoryStream())
        {
            memoryStream.Write(fakeContent, 0, fakeContent.Length);
            memoryStream.Position = 0;

            using (var crypto = new AesStream("password", memoryStream))
            {
                // 1st page is hidden, so AesStream.Length returns (stream.Length - PAGE_SIZE)
                Assert.Equal(0, crypto.Length);

                // AesStream should add padding to the underlying stream to make its size equivalent to PAGE_SIZE
                Assert.Equal(8192, memoryStream.Length);
            }
        }
    }

    /// <summary>
    /// Test whether AesStream can handle stream where bytes 32-64 are empty.
    /// </summary>
    [Fact]
    public void AesStream_Invalid_Password()
    {
        // stream of 8192 bytes where bytes 32 to 64 is empty. 
        using (var memoryStream = new MemoryStream())
        {
            // 1st byte to indicate the stream is encrypted
            memoryStream.WriteByte(1);

            // next 16 bytes contain salt
            var salt = new byte[16];

            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(salt);
            }

            memoryStream.Write(salt, 0, salt.Length);

            // remaining (8192 - 17) bytes are empty
            var emptyContent = new byte[8175];
            memoryStream.Write(emptyContent, 0, emptyContent.Length);

            // reset the stream position to 0
            memoryStream.Position = 0;

            using (var crypto = new AesStream("password", memoryStream))
            {
                // 1st page is hidden, so AesStream.Length returns (stream.Length - PAGE_SIZE)
                Assert.Equal(0, crypto.Length);

                // AesStream should add padding to the underlying stream to make its size equivalent to PAGE_SIZE
                Assert.Equal(8192, memoryStream.Length);

                // AesStream should fill bytes 32-64 with encrypted 1s
                var checkBytes = new byte[32];
                var cryptoReaderField = typeof(AesStream)
                                        .GetField("_reader", BindingFlags.Instance | BindingFlags.NonPublic);
                var cryptoReader = cryptoReaderField?.GetValue(crypto) as CryptoStream;

                cryptoReader.Should().NotBeNull();

                memoryStream.Position = 32;
                cryptoReader!.ReadExactly(checkBytes, 0, checkBytes.Length);
                Assert.All(checkBytes, b => Assert.Equal(1, b));
            }
        }
    }

    [Fact]
    public void AesGcmStream_Invalid_Password()
    {
        byte[] persisted;

        using (var memoryStream = new MemoryStream())
        {
            using (new AesGcmStream("password", memoryStream))
            {
                persisted = memoryStream.ToArray();
            }
        }

        Action act = () =>
        {
            using var encrypted = new AesGcmStream("wrong-password", new MemoryStream(persisted));
        };

        act.Should().Throw<LiteException>();
    }

    [Fact]
    public void AesGcmStream_Tampered_Page_Fails_On_Read()
    {
        byte[] persisted;
        var input = Enumerable.Repeat((byte)7, 8192).ToArray();

        using (var memoryStream = new MemoryStream())
        {
            using (var crypto = new AesGcmStream("password", memoryStream))
            {
                crypto.Write(input, 0, input.Length);
                crypto.Flush();
                persisted = memoryStream.ToArray();
            }
        }

        persisted[8192 + 24] ^= 0x5A;

        Action act = () =>
        {
            using var encrypted = new AesGcmStream("password", new MemoryStream(persisted));
            var output = new byte[8192];
            encrypted.ReadExactly(output, 0, output.Length);
        };

        act.Should().Throw<LiteException>();
    }

    private static Stream CreateEncryptedStream(string password, Stream stream, AESEncryptionType encryptionType)
    {
        return encryptionType == AESEncryptionType.GCM
            ? new AesGcmStream(password, stream)
            : new AesStream(password, stream);
    }
}