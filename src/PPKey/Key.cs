using System.Security.Cryptography;
using System.Text;

namespace RizzziGit.TCP.Messaging.PPKey;

public class Key
{
  public static Key Generate()
  {
    RSACryptoServiceProvider provider = new(2048);

    return new(provider);
  }

  public string ToPEM(Key key, bool exportPrivate)
  {
    return exportPrivate ? key.Provider.ExportRSAPrivateKeyPem() : key.Provider.ExportRSAPublicKeyPem();
  }

  public static Key FromPEM(string keyPem)
  {
    RSACryptoServiceProvider provider = new(2048);
    provider.ImportFromPem(keyPem);

    return new(provider);
  }

  private Key(RSACryptoServiceProvider provider)
  {
    Provider = provider;
  }

  private RSACryptoServiceProvider Provider;
  public bool PublicOnly { get => Provider.PublicOnly; }

  public byte[] Encrypt(byte[] data)
  {
    return Provider.Encrypt(data, RSAEncryptionPadding.Pkcs1);
  }

  public Buffer Encrypt(Buffer data)
  {
    return Buffer.FromByteArray(Encrypt(data.ToByteArray()));
  }

  public byte[] Decrypt(byte[] data)
  {
    return Provider.Decrypt(data, RSAEncryptionPadding.Pkcs1);
  }

  public Buffer Decrypt(Buffer data)
  {
    return Buffer.FromByteArray(Decrypt(data.ToByteArray()));
  }

  public byte[] Sign(byte[] data)
  {
    return Provider.SignData(data, 0, data.Length, HashAlgorithmName.SHA512, RSASignaturePadding.Pkcs1);
  }

  public Buffer Sign(Buffer data)
  {
    return Buffer.FromByteArray(Sign(data.ToByteArray()));
  }

  public bool Verify(byte[] data, byte[] signature)
  {
    return Provider.VerifyData(data, 0, data.Length, signature, HashAlgorithmName.SHA512, RSASignaturePadding.Pkcs1);
  }

  public bool Verify(Buffer data, Buffer signature)
  {
    return Verify(data.ToByteArray(), signature.ToByteArray());
  }
}
