using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FhirMcp.Core.Auth;

/// <summary>Keeps sign-in tokens between runs, one entry per FHIR server.</summary>
public interface ITokenStore
{
    /// <summary>A short description for messages, such as where the tokens are kept.</summary>
    string Description { get; }

    SmartTokens? Load(string fhirBaseUrl);

    void Save(SmartTokens tokens);

    /// <summary>Removes the tokens for a server. Returns false if there were none.</summary>
    bool Delete(string fhirBaseUrl);
}

/// <summary>Encrypts the token file's contents. Null protection means plain JSON.</summary>
public interface ITokenProtector
{
    byte[] Protect(byte[] plaintext);

    /// <summary>Returns null when the data cannot be decrypted (for example, the key changed).</summary>
    byte[]? Unprotect(byte[] protectedData);
}

/// <summary>
/// Tokens in one file, created with owner-only permissions (0600) on macOS and Linux and
/// replaced atomically on every save. With no protector the file is plain JSON: simple, but
/// readable by anything running as the same user.
/// </summary>
public class FileTokenStore : ITokenStore
{
    private readonly string _path;
    private readonly ITokenProtector? _protector;
    private readonly object _gate = new();

    public FileTokenStore(string path, ITokenProtector? protector = null)
    {
        _path = path;
        _protector = protector;
    }

    public virtual string Description => $"file {_path}";

    public string FilePath => _path;

    public SmartTokens? Load(string fhirBaseUrl)
    {
        lock (_gate)
        {
            return ReadAll().TryGetValue(Key(fhirBaseUrl), out var tokens) ? tokens : null;
        }
    }

    public void Save(SmartTokens tokens)
    {
        lock (_gate)
        {
            var all = ReadAll();
            all[Key(tokens.FhirBaseUrl)] = tokens;
            WriteAll(all);
        }
    }

    public bool Delete(string fhirBaseUrl)
    {
        lock (_gate)
        {
            var all = ReadAll();
            if (!all.Remove(Key(fhirBaseUrl)))
            {
                return false;
            }
            WriteAll(all);
            return true;
        }
    }

    private Dictionary<string, SmartTokens> ReadAll()
    {
        var empty = new Dictionary<string, SmartTokens>(StringComparer.Ordinal);
        if (!File.Exists(_path))
        {
            return empty;
        }

        var bytes = File.ReadAllBytes(_path);
        if (_protector is not null)
        {
            var plaintext = _protector.Unprotect(bytes);
            if (plaintext is null)
            {
                return empty; // Cannot be decrypted: treat as signed out, not as a crash.
            }
            bytes = plaintext;
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, SmartTokens>>(bytes, SmartTokens.JsonOptions) ?? empty;
        }
        catch (JsonException)
        {
            return empty; // A damaged file means signing in again, not a crash.
        }
    }

    private void WriteAll(Dictionary<string, SmartTokens> all)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(_path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var bytes = JsonSerializer.SerializeToUtf8Bytes(all, SmartTokens.JsonOptions);
        if (_protector is not null)
        {
            bytes = _protector.Protect(bytes);
        }

        // Write a temporary file with owner-only permissions, then move it into place, so the
        // tokens are never briefly readable by others and a crash cannot leave half a file.
        var temp = _path + ".tmp";
        var options = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        using (var stream = new FileStream(temp, options))
        {
            stream.Write(bytes);
        }

        File.Move(temp, _path, overwrite: true);
    }

    internal static string Key(string fhirBaseUrl) => fhirBaseUrl.TrimEnd('/') + "/";
}

/// <summary>
/// The macOS option: tokens in a file encrypted with AES-256-GCM, under a random key kept in the
/// login Keychain. The file alone is useless without the Keychain, which is encrypted at rest
/// and unlocked by the user's login. Only the 32-byte key goes into the Keychain, so token size
/// never matters, and it is passed to the <c>security</c> tool on standard input, never as a
/// command-line argument that other processes could read from the process list.
/// </summary>
public sealed class KeychainTokenStore : FileTokenStore
{
    public KeychainTokenStore(string encryptedFilePath, IProcessRunner? runner = null)
        : base(encryptedFilePath, new KeychainKeyProtector(runner ?? new ProcessRunner()))
    {
    }

    public override string Description => $"{FilePath}, encrypted with a key in the macOS Keychain";
}

/// <summary>AES-256-GCM with a key read from, or created in, the macOS Keychain.</summary>
public sealed class KeychainKeyProtector : ITokenProtector
{
    public const string Service = "fhir-mcp";
    public const string Account = "token-encryption-key";
    private const string SecurityTool = "/usr/bin/security";
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly IProcessRunner _runner;
    private byte[]? _key;

    public KeychainKeyProtector(IProcessRunner runner) => _runner = runner;

    public byte[] Protect(byte[] plaintext)
    {
        var key = GetOrCreateKey();
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSize];
        using (var aes = new AesGcm(key, TagSize))
        {
            aes.Encrypt(nonce, plaintext, ciphertext, tag);
        }
        return [.. nonce, .. tag, .. ciphertext];
    }

    public byte[]? Unprotect(byte[] protectedData)
    {
        if (protectedData.Length < NonceSize + TagSize)
        {
            return null;
        }

        var key = ReadKey();
        if (key is null)
        {
            return null;
        }

        var nonce = protectedData.AsSpan(0, NonceSize);
        var tag = protectedData.AsSpan(NonceSize, TagSize);
        var ciphertext = protectedData.AsSpan(NonceSize + TagSize);
        var plaintext = new byte[ciphertext.Length];
        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(nonce, ciphertext, tag, plaintext);
            return plaintext;
        }
        catch (CryptographicException)
        {
            return null; // Wrong key or altered file.
        }
    }

    private byte[]? ReadKey()
    {
        if (_key is not null)
        {
            return _key;
        }

        var (exit, output) = _runner.Run(SecurityTool, ["find-generic-password", "-s", Service, "-a", Account, "-w"]);
        if (exit != 0)
        {
            return null;
        }

        try
        {
            var key = Base64Url.Decode(output.Trim());
            return key.Length == 32 ? _key = key : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private byte[] GetOrCreateKey()
    {
        if (ReadKey() is { } existing)
        {
            return existing;
        }

        var value = Base64Url.Encode(RandomNumberGenerator.GetBytes(32));
        _runner.Run(SecurityTool, ["-i"], $"add-generic-password -U -s {Service} -a {Account} -w {value}\n");

        // Interactive mode does not always report a failed command in its exit code, so read the
        // key back to confirm it was stored.
        _key = null;
        return ReadKey() ?? throw new SmartAuthException("Could not save the token encryption key to the macOS Keychain.");
    }
}

/// <summary>Runs a command and returns its exit code and output. Replaced by a fake in tests.</summary>
public interface IProcessRunner
{
    (int ExitCode, string StandardOutput) Run(string fileName, IReadOnlyList<string> arguments, string? standardInput = null);
}

public sealed class ProcessRunner : IProcessRunner
{
    public (int ExitCode, string StandardOutput) Run(string fileName, IReadOnlyList<string> arguments, string? standardInput = null)
    {
        var info = new ProcessStartInfo(fileName)
        {
            RedirectStandardInput = standardInput is not null,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        using var process = Process.Start(info) ?? throw new InvalidOperationException($"Could not start {fileName}.");
        if (standardInput is not null)
        {
            process.StandardInput.Write(standardInput);
            process.StandardInput.Close();
        }

        var errors = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEnd();
        errors.Wait();
        process.WaitForExit();
        return (process.ExitCode, output);
    }
}
