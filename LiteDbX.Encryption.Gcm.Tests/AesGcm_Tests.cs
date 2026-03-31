using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using LiteDbX.Engine;
using Xunit;

namespace LiteDbX.Encryption.Gcm.Tests;

public class AesGcm_Tests
{
    public AesGcm_Tests()
    {
        GcmEncryptionRegistration.Register();
    }

    [Fact]
    public void Encrypt_Decrypt_Stream()
    {
        using (var media = new MemoryStream())
        {
            using (var crypto = new AesGcmStream("abc", media))
            {
                var input0 = new byte[8192];
                var input1 = new byte[8192];
                var input2 = new byte[8192];

                var output0 = new byte[8192];
                var output1 = new byte[8192];
                var output2 = new byte[8192];

                Array.Fill(input0, (byte)100);
                Array.Fill(input1, (byte)101);
                Array.Fill(input2, (byte)102);

                crypto.Position = 0 * 8192;
                crypto.Write(input0, 0, 8192);

                crypto.Position = 2 * 8192;
                crypto.Write(input2, 0, 8192);

                crypto.Position = 1 * 8192;
                crypto.Write(input1, 0, 8192);

                media.Position = 0;
                media.ReadExactly(output0, 0, 8192);
                media.ReadExactly(output1, 0, 8192);
                media.ReadExactly(output2, 0, 8192);

                output0.All(x => x == 100).Should().BeFalse();
                output1.All(x => x == 101).Should().BeFalse();
                output2.All(x => x == 102).Should().BeFalse();

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
    public void Invalid_Password()
    {
        byte[] persisted;

        using (var memoryStream = new MemoryStream())
        {
            using (var encrypted = new AesGcmStream("password", memoryStream))
            {
                _ = encrypted.Length;
                persisted = memoryStream.ToArray();
            }
        }

        System.Action act = () => new AesGcmStream("wrong-password", new MemoryStream(persisted)).Dispose();

        act.Should().Throw<LiteException>();
    }

    [Fact]
    public void Tampered_Page_Fails_On_Read()
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

        System.Action act = () =>
        {
            var encrypted = new AesGcmStream("password", new MemoryStream(persisted));
            using (encrypted)
            {
                var output = new byte[8192];
                encrypted.ReadExactly(output, 0, output.Length);
            }
        };

        act.Should().Throw<LiteException>();
    }
}

