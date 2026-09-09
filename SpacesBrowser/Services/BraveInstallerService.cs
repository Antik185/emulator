using System.Diagnostics;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace SpacesBrowser.Services;

public sealed class BraveInstallerService
{
    private const long MaximumInstallerBytes = 64L * 1024 * 1024;
    private static readonly HttpClient SharedClient = CreateClient();
    private static readonly Guid VerifyAction = new("00AAC56B-CD44-11D0-8CC2-00C04FC295EE");
    private readonly HttpClient _httpClient;
    private readonly Func<string, bool> _signatureVerifier;

    public BraveInstallerService()
        : this(SharedClient, IsTrustedBraveBinary)
    {
    }

    public BraveInstallerService(HttpClient httpClient, Func<string, bool> signatureVerifier)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _signatureVerifier = signatureVerifier ?? throw new ArgumentNullException(nameof(signatureVerifier));
    }

    public static Uri OfficialInstallerUri { get; } = new("https://laptop-updates.brave.com/latest/winx64");

    public async Task<string> DownloadOfficialInstallerAsync(
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var downloadDirectory = Path.Combine(
            Path.GetTempPath(),
            "SpacesBrowser",
            "BraveInstaller",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(downloadDirectory);
        var installerPath = Path.Combine(downloadDirectory, "BraveBrowserSetup.exe");

        try
        {
            using var response = await _httpClient.GetAsync(
                OfficialInstallerUri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();

            var expectedLength = response.Content.Headers.ContentLength;
            if (expectedLength > MaximumInstallerBytes)
            {
                throw new InvalidDataException("Установщик Brave оказался неожиданно большим.");
            }

            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            var buffer = new byte[81920];
            long totalBytes = 0;
            await using (var destination = new FileStream(
                             installerPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             81920,
                             useAsync: true))
            {
                int bytesRead;
                while ((bytesRead = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
                {
                    totalBytes += bytesRead;
                    if (totalBytes > MaximumInstallerBytes)
                    {
                        throw new InvalidDataException("Установщик Brave превысил допустимый размер.");
                    }

                    await destination.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                    if (expectedLength is > 0)
                    {
                        progress?.Report((int)Math.Min(100, totalBytes * 100 / expectedLength.Value));
                    }
                }

                await destination.FlushAsync(cancellationToken);
            }

            if (totalBytes == 0)
            {
                throw new InvalidDataException("Скачан пустой установщик Brave.");
            }

            if (!_signatureVerifier(installerPath))
            {
                throw new InvalidDataException("Цифровая подпись установщика Brave не прошла проверку.");
            }

            progress?.Report(100);
            return installerPath;
        }
        catch
        {
            Cleanup(installerPath);
            throw;
        }
    }

    public static async Task<int> RunInstallerAsync(
        string installerPath,
        CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = installerPath,
            Arguments = "/silent /install",
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(installerPath) ?? Path.GetTempPath()
        };
        using var process = Process.Start(startInfo)
                            ?? throw new InvalidOperationException("Не удалось запустить установщик Brave.");
        await process.WaitForExitAsync(cancellationToken);
        return process.ExitCode;
    }

    public static bool IsTrustedBraveBinary(string filePath)
    {
        if (!OperatingSystem.IsWindows() || !File.Exists(filePath) || !HasValidAuthenticodeSignature(filePath))
        {
            return false;
        }

        try
        {
            using var certificate = X509Certificate.CreateFromSignedFile(filePath);
            using var certificate2 = new X509Certificate2(certificate);
            var publisher = certificate2.GetNameInfo(X509NameType.SimpleName, forIssuer: false);
            return publisher.Contains("Brave Software", StringComparison.OrdinalIgnoreCase);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    public static void Cleanup(string? installerPath)
    {
        if (string.IsNullOrWhiteSpace(installerPath))
        {
            return;
        }

        try
        {
            if (File.Exists(installerPath))
            {
                File.Delete(installerPath);
            }

            var directory = Path.GetDirectoryName(installerPath);
            if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: false);
            }
        }
        catch
        {
        }
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(3)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("SpacesBrowser/1.0");
        return client;
    }

    private static bool HasValidAuthenticodeSignature(string filePath)
    {
        var fileInfo = new WinTrustFileInfo
        {
            StructureSize = (uint)Marshal.SizeOf<WinTrustFileInfo>(),
            FilePath = filePath
        };
        var fileInfoPointer = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustFileInfo>());

        try
        {
            Marshal.StructureToPtr(fileInfo, fileInfoPointer, fDeleteOld: false);
            var trustData = new WinTrustData
            {
                StructureSize = (uint)Marshal.SizeOf<WinTrustData>(),
                UiChoice = 2,
                RevocationChecks = 0,
                UnionChoice = 1,
                FileInfoPointer = fileInfoPointer,
                StateAction = 0,
                ProviderFlags = 0x00000010,
                UiContext = 0
            };
            return WinVerifyTrust(IntPtr.Zero, VerifyAction, ref trustData) == 0;
        }
        finally
        {
            Marshal.DestroyStructure<WinTrustFileInfo>(fileInfoPointer);
            Marshal.FreeHGlobal(fileInfoPointer);
        }
    }

    [DllImport("wintrust.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern uint WinVerifyTrust(
        IntPtr windowHandle,
        [MarshalAs(UnmanagedType.LPStruct)] Guid actionId,
        ref WinTrustData trustData);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustFileInfo
    {
        public uint StructureSize;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string FilePath;

        public IntPtr FileHandle;
        public IntPtr KnownSubjectPointer;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustData
    {
        public uint StructureSize;
        public IntPtr PolicyCallbackData;
        public IntPtr SipClientData;
        public uint UiChoice;
        public uint RevocationChecks;
        public uint UnionChoice;
        public IntPtr FileInfoPointer;
        public uint StateAction;
        public IntPtr StateData;
        public IntPtr UrlReference;
        public uint ProviderFlags;
        public uint UiContext;
    }
}
