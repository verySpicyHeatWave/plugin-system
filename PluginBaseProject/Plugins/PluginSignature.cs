using System;
using System.IO;
using System.Security.Cryptography;

namespace PluginBaseProject.Plugins
{
    public static class PluginSignature
    {
        private static readonly Lazy<string> PublicKeyPem = new(() =>
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "publisher.public.pem")));

        public static bool Verify(byte[] data, byte[]? signature)
        {
            if (data == null) return false;
            if (signature == null) return false;
            try
            {
                using var rsa = RSA.Create();
                rsa.ImportFromPem(PublicKeyPem.Value);
                return rsa.VerifyData(data, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

            }
            catch
            {
                return false;
            }
        }
    }
}
