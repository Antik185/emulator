using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using SpacesBrowser.Models;
using SpacesBrowser.Services;

var testRoot = Path.Combine(Path.GetTempPath(), "SpacesBrowserSmoke", Guid.NewGuid().ToString("N"));

try
{
    Directory.CreateDirectory(testRoot);
    await UpdateSmokeTests.RunAsync(testRoot);
    if (args.Contains("--updates-only")) return 0;
    var paths = new AppPaths(testRoot);
    var store = new ProfileStore(paths);
    var profile = new BrowserProfile
    {
        Name = "Тестовый профиль",
        HomeUrl = "https://ya.ru/",
        BrowserLanguage = "en-US",
        WindowPreset = "Tablet",
        Capability = "Restaurant",
        Region = "Калининград",
        ReadyAfterHours = 36,
        LastPublicIp = "203.0.113.15"
    };

    store.Save(new[] { profile });
    var loaded = store.Load();
    Assert(loaded.Count == 1, "Профиль не загрузился");
    Assert(loaded[0].Id == profile.Id, "ID профиля изменился");
    Assert(loaded[0].Name == profile.Name, "Название профиля изменилось");
    Assert(loaded[0].Capability == "Restaurant", "Тип профиля изменился");
    Assert(loaded[0].Region == "Калининград", "Регион профиля изменился");
    Assert(loaded[0].ReadyAfterHours == 36, "Настраиваемый срок готовности изменился");
    Assert(loaded[0].LastPublicIp == "203.0.113.15", "Последний IP профиля изменился");

    var argsA = BrowserLauncher.BuildArguments(paths.ProfileData(profile.Id), profile, 9229, true);
    var argsB = BrowserLauncher.BuildArguments(paths.ProfileData("other"), profile.HomeUrl);
    Assert(argsA.Contains(profile.Id), "В аргументах нет каталога профиля");
    Assert(argsA != argsB, "Разные профили получили одинаковые аргументы");
    Assert(argsA.Contains("--lang=en-US"), "Язык браузера не применён");
    Assert(argsA.Contains("--window-size=1280,800"), "Пресет окна не применён");
    Assert(argsA.Contains("--remote-debugging-address=127.0.0.1"), "DevTools не ограничен loopback-интерфейсом");
    Assert(argsA.Contains("--remote-debugging-port=9229"), "DevTools-порт не применён");
    Assert(argsA.Contains("about:blank"), "Управляемый запуск не начинается с пустой страницы");
    Assert(BrowserLauncher.NormalizeUrl("ya.ru").StartsWith("https://ya.ru"), "URL не нормализован");
    Assert(BrowserLauncher.NormalizeUrl("file:///windows/system.ini") == "https://ya.ru/", "Опасная схема URL разрешена");
    Assert(BrowserLauncher.NormalizeLanguage("unknown") == "ru-RU", "Неизвестный язык не сброшен");
    var armenia = RegionCatalog.Find("Армения");
    Assert(armenia.TimeZoneId == "Asia/Yerevan" && armenia.UtcOffset == "UTC+4", "Регион не связан с timezone");

    var fixedNow = new DateTime(2026, 8, 31, 12, 0, 0, DateTimeKind.Utc);
    var warmingProfile = new BrowserProfile { CreatedAtUtc = fixedNow.AddDays(-6).AddHours(-2) };
    var warming = ProfileReadiness.Calculate(warmingProfile, fixedNow);
    Assert(!warming.IsReady, "Профиль стал готов раньше семи дней");
    Assert(ProfileReadiness.CountdownLabel(warming).Contains("22ч"), "Таймер готовности рассчитан неверно");
    var readyProfile = new BrowserProfile { CreatedAtUtc = fixedNow.AddDays(-7) };
    Assert(ProfileReadiness.Calculate(readyProfile, fixedNow).IsReady, "Профиль не стал готов через семь дней");
    var customWarmingProfile = new BrowserProfile
    {
        CreatedAtUtc = fixedNow.AddHours(-35),
        ReadyAfterHours = 36
    };
    var customWarming = ProfileReadiness.Calculate(customWarmingProfile, fixedNow);
    Assert(!customWarming.IsReady && ProfileReadiness.CountdownLabel(customWarming).Contains("1ч"),
        "Настраиваемый срок готовности рассчитан неверно");
    customWarmingProfile.CreatedAtUtc = fixedNow.AddHours(-36);
    Assert(ProfileReadiness.Calculate(customWarmingProfile, fixedNow).IsReady,
        "Профиль не стал готов по настраиваемому сроку");

    using var ipClient = new HttpClient(new StubHttpMessageHandler("{\"ip\":\"203.0.113.42\"}"));
    var publicIp = await new PublicIpService(ipClient).GetPublicIpAsync();
    Assert(publicIp == "203.0.113.42", "Публичный IP разобран неверно");
    using var ipv6Client = new HttpClient(new StubHttpMessageHandler("{\"ip\":\"2001:db8::1\"}"));
    await AssertThrowsAsync<InvalidDataException>(
        () => new PublicIpService(ipv6Client).GetPublicIpAsync(),
        "IPv6 не должен отображаться вместо IPv4");

    Assert(BraveInstallerService.OfficialInstallerUri.Scheme == Uri.UriSchemeHttps
           && BraveInstallerService.OfficialInstallerUri.Host == "laptop-updates.brave.com",
        "Установщик Brave загружается не с официального HTTPS-адреса");
    var fakeInstallerBytes = Encoding.ASCII.GetBytes("test-brave-installer");
    using var installerClient = new HttpClient(new BinaryStubHttpMessageHandler(fakeInstallerBytes));
    var installerService = new BraveInstallerService(
        installerClient,
        path => File.ReadAllBytes(path).SequenceEqual(fakeInstallerBytes));
    string? downloadedInstaller = null;
    try
    {
        downloadedInstaller = await installerService.DownloadOfficialInstallerAsync();
        Assert(File.Exists(downloadedInstaller), "Тестовый установщик Brave не сохранён");
        Assert(File.ReadAllBytes(downloadedInstaller).SequenceEqual(fakeInstallerBytes),
            "Скачанный установщик Brave повреждён");
    }
    finally
    {
        BraveInstallerService.Cleanup(downloadedInstaller);
    }

    if (args.Contains("--verify-brave-download", StringComparer.OrdinalIgnoreCase))
    {
        string? officialInstaller = null;
        try
        {
            officialInstaller = await new BraveInstallerService().DownloadOfficialInstallerAsync();
            Assert(BraveInstallerService.IsTrustedBraveBinary(officialInstaller),
                "Официальный установщик Brave не прошёл проверку подписи");
        }
        finally
        {
            BraveInstallerService.Cleanup(officialInstaller);
        }
    }

    File.WriteAllText(paths.ProfilesFile, "[{\"Name\":\"Старый профиль\"}]");
    var migrated = store.Load().Single();
    Assert(migrated.BrowserLanguage == "ru-RU", "Не применён язык по умолчанию для старого профиля");
    Assert(migrated.WindowPreset == "Maximized", "Не применён размер окна по умолчанию для старого профиля");
    Assert(migrated.Capability == "Both", "Не применён тип по умолчанию для старого профиля");
    Assert(migrated.Region == "Калининград", "Не применён регион по умолчанию для старого профиля");
    Assert(migrated.TimeZoneId == "Europe/Kaliningrad", "Не применён timezone по умолчанию");
    Assert(migrated.ReadyAfterHours == ProfileReadiness.DefaultWaitingHours,
        "Не применён срок готовности по умолчанию для старого профиля");

    var detected = new BrowserLocator().FindPreferred();
    Assert(detected is not null, "На тестовом компьютере не найден Chromium-браузер");
    Assert(File.Exists(detected!.ExecutablePath), "Путь найденного браузера не существует");
    if (detected.Kind == BrowserKind.Brave)
    {
        Assert(BraveInstallerService.IsTrustedBraveBinary(detected.ExecutablePath),
            "Windows не подтвердил подпись установленного Brave");
    }

    await VerifyTimeZoneOverrideAsync(detected, testRoot);

    Console.WriteLine($"PASS: store, configurable readiness, last profile IP, Brave installer verification, isolation args, URL validation, region mapping, public IP parsing, timezone override ({detected.DisplayName})");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine("FAIL: " + ex);
    return 1;
}
finally
{
    if (Directory.Exists(testRoot))
    {
        Directory.Delete(testRoot, true);
    }
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static async Task AssertThrowsAsync<TException>(Func<Task> action, string message)
    where TException : Exception
{
    try
    {
        await action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException(message);
}

static async Task VerifyTimeZoneOverrideAsync(BrowserInstall browser, string testRoot)
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
    var report = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
    var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    var webPort = ((IPEndPoint)listener.LocalEndpoint).Port;
    var serverTask = ServeProbeAsync(listener, report, timeout.Token);

    var debuggingListener = new TcpListener(IPAddress.Loopback, 0);
    debuggingListener.Start();
    var debuggingPort = ((IPEndPoint)debuggingListener.LocalEndpoint).Port;
    debuggingListener.Stop();

    var controllerTask = BrowserEnvironmentController.RunAsync(
        debuggingPort,
        "Asia/Yerevan",
        $"http://127.0.0.1:{webPort}/",
        timeout.Token);

    Process? browserProcess = null;
    try
    {
        var browserData = Path.Combine(testRoot, "timezone-browser");
        Directory.CreateDirectory(browserData);
        var startInfo = new ProcessStartInfo
        {
            FileName = browser.ExecutablePath,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("--headless=new");
        startInfo.ArgumentList.Add("--disable-gpu");
        startInfo.ArgumentList.Add("--no-first-run");
        startInfo.ArgumentList.Add("--no-default-browser-check");
        startInfo.ArgumentList.Add("--remote-debugging-address=127.0.0.1");
        startInfo.ArgumentList.Add($"--remote-debugging-port={debuggingPort}");
        startInfo.ArgumentList.Add($"--user-data-dir={browserData}");
        startInfo.ArgumentList.Add("about:blank");
        browserProcess = Process.Start(startInfo)
                         ?? throw new InvalidOperationException("Не удалось запустить браузер для timezone-теста");

        var completed = await Task.WhenAny(report.Task, controllerTask).WaitAsync(timeout.Token);
        if (completed == controllerTask)
        {
            await controllerTask;
            throw new InvalidOperationException("Timezone-контроллер завершился до ответа тестовой страницы");
        }
        var reportedTimeZone = await report.Task;
        Assert(reportedTimeZone == "Asia/Yerevan",
            $"Сайт увидел timezone '{reportedTimeZone}' вместо 'Asia/Yerevan'");
    }
    finally
    {
        timeout.Cancel();
        listener.Stop();
        if (browserProcess is { HasExited: false })
        {
            try
            {
                browserProcess.Kill(true);
                await browserProcess.WaitForExitAsync();
            }
            catch
            {
            }
        }
        browserProcess?.Dispose();
        try
        {
            await controllerTask;
        }
        catch (OperationCanceledException)
        {
        }
        try
        {
            await serverTask;
        }
        catch (OperationCanceledException)
        {
        }
    }
}

static async Task ServeProbeAsync(
    TcpListener listener,
    TaskCompletionSource<string> report,
    CancellationToken cancellationToken)
{
    using var registration = cancellationToken.Register(listener.Stop);
    try
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var client = await listener.AcceptTcpClientAsync();
            _ = HandleProbeRequestAsync(client, report);
        }
    }
    catch (SocketException) when (cancellationToken.IsCancellationRequested)
    {
    }
    catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
    {
    }
}

static async Task HandleProbeRequestAsync(TcpClient client, TaskCompletionSource<string> report)
{
    using (client)
    using (var stream = client.GetStream())
    using (var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true))
    {
        var requestLine = await reader.ReadLineAsync() ?? string.Empty;
        string? header;
        do
        {
            header = await reader.ReadLineAsync();
        } while (!string.IsNullOrEmpty(header));

        var path = requestLine.Split(' ', StringSplitOptions.RemoveEmptyEntries).ElementAtOrDefault(1) ?? "/";
        if (path.StartsWith("/report?tz=", StringComparison.Ordinal))
        {
            report.TrySetResult(Uri.UnescapeDataString(path[11..]));
            await WriteHttpResponseAsync(stream, "text/plain", "ok");
            return;
        }

        const string html = "<script>fetch('/report?tz='+encodeURIComponent(Intl.DateTimeFormat().resolvedOptions().timeZone))</script>";
        await WriteHttpResponseAsync(stream, "text/html; charset=utf-8", html);
    }
}

static async Task WriteHttpResponseAsync(NetworkStream stream, string contentType, string body)
{
    var bodyBytes = Encoding.UTF8.GetBytes(body);
    var headers = Encoding.ASCII.GetBytes(
        $"HTTP/1.1 200 OK\r\nContent-Type: {contentType}\r\nContent-Length: {bodyBytes.Length}\r\nConnection: close\r\n\r\n");
    await stream.WriteAsync(headers);
    await stream.WriteAsync(bodyBytes);
}

sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly string _responseBody;

    public StubHttpMessageHandler(string responseBody)
    {
        _responseBody = responseBody;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(_responseBody, Encoding.UTF8, "application/json")
        });
    }
}

sealed class BinaryStubHttpMessageHandler : HttpMessageHandler
{
    private readonly byte[] _responseBody;

    public BinaryStubHttpMessageHandler(byte[] responseBody)
    {
        _responseBody = responseBody;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(_responseBody)
        });
    }
}
