using System.Buffers.Binary;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.Security;
using System.Net.NetworkInformation;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

namespace GuiFileLAN;

public sealed class NetworkService : IAsyncDisposable
{
    public const int DiscoveryPort = 45831;
    public const int TransferPort = 45832;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private readonly string _deviceId;
    private readonly string _deviceName = Environment.MachineName;
    private readonly X509Certificate2 _certificate;
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _shutdown = new();
    private UdpClient? _udp;
    private readonly object _peerLock = new();
    private readonly Dictionary<string, PeerInfo> _peers = new();

    public event Action<PeerInfo>? PeerChanged;
    public event Func<TransferOffer, Task<bool>>? OfferReceived;
    public event Action<TransferOffer>? ReceiveStarted;
    public event Action<string, long, long>? ReceiveProgress;
    public event Action<TransferOffer>? ReceiveCompleted;
    public event Action<TransferOffer, string>? ReceiveFailed;

    public NetworkService()
    {
        _deviceId = LoadDeviceId();
        _certificate = CreateServerCertificate(_deviceId);
        _listener = new TcpListener(IPAddress.Any, TransferPort);
    }

    private static X509Certificate2 CreateServerCertificate(string deviceId)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest($"CN={deviceId}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        using var generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(2));

        // Windows Schannel rejects ephemeral private keys for server certificates.
        // Import through the user's key store so SslStream can acquire the TLS credentials.
        var pfx = generated.Export(X509ContentType.Pfx);
        try
        {
            return X509CertificateLoader.LoadPkcs12(pfx, (string?)null, X509KeyStorageFlags.UserKeySet, Pkcs12LoaderLimits.Defaults);
        }
        finally { CryptographicOperations.ZeroMemory(pfx); }
    }

    public void Start()
    {
        _listener.Start();
        _ = Task.Run(() => AcceptLoop(_shutdown.Token));
        _ = Task.Run(() => DiscoveryLoop(_shutdown.Token));
    }

    public IReadOnlyList<PeerInfo> GetPeers()
    {
        lock (_peerLock) return _peers.Values.Where(p => DateTime.UtcNow - p.LastSeen < TimeSpan.FromSeconds(12)).ToList();
    }

    private static string LoadDeviceId()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GuiFileLAN");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "device-id.txt");
        if (File.Exists(path)) return File.ReadAllText(path).Trim();
        var id = Guid.NewGuid().ToString("N");
        File.WriteAllText(path, id);
        return id;
    }

    private async Task DiscoveryLoop(CancellationToken token)
    {
        try
        {
            _udp = new UdpClient(AddressFamily.InterNetwork);
            _udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _udp.EnableBroadcast = true;
            _udp.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));
            var certBytes = _certificate.Export(X509ContentType.Cert);
            var announce = JsonSerializer.SerializeToUtf8Bytes(new DiscoveryMessage(_deviceId, _deviceName, TransferPort, Convert.ToBase64String(certBytes)), JsonOptions);
            _ = Task.Run(() => ReceiveAnnouncements(token));
            var endpoints = GetBroadcastEndpoints().ToList();
            var refreshEndpointsAt = DateTime.UtcNow.AddMinutes(1);
            while (!token.IsCancellationRequested)
            {
                if (DateTime.UtcNow >= refreshEndpointsAt)
                {
                    endpoints = GetBroadcastEndpoints().ToList();
                    refreshEndpointsAt = DateTime.UtcNow.AddMinutes(1);
                }
                foreach (var endpoint in endpoints)
                {
                    try { await _udp.SendAsync(announce, endpoint, token); }
                    catch (SocketException) { }
                }
                await Task.Delay(TimeSpan.FromSeconds(3), token);
            }
        }
        catch (OperationCanceledException) { }
        catch (SocketException) { }
    }

    private async Task ReceiveAnnouncements(CancellationToken token)
    {
        if (_udp is null) return;
        while (!token.IsCancellationRequested)
        {
            try
            {
                var packet = await _udp.ReceiveAsync(token);
                if (packet.Buffer.Length > 4096) continue;
                var message = JsonSerializer.Deserialize<DiscoveryMessage>(packet.Buffer, JsonOptions);
                if (message is null || message.Id == _deviceId || message.Port != TransferPort || string.IsNullOrWhiteSpace(message.CertificateDer)) continue;
                var info = new PeerInfo(message.Id, message.Name, packet.RemoteEndPoint.Address.ToString(), message.Port, message.CertificateDer, DateTime.UtcNow);
                lock (_peerLock) _peers[info.Id] = info;
                PeerChanged?.Invoke(info);
            }
            catch (OperationCanceledException) { break; }
            catch (SocketException) when (!token.IsCancellationRequested) { await Task.Delay(250, token); }
            catch (JsonException) { }
            catch (ObjectDisposedException) { break; }
        }
    }

    private static IEnumerable<IPEndPoint> GetBroadcastEndpoints()
    {
        yield return new IPEndPoint(IPAddress.Broadcast, DiscoveryPort);
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
            {
                if (unicast.Address.AddressFamily != AddressFamily.InterNetwork || unicast.IPv4Mask is null) continue;
                var ip = unicast.Address.GetAddressBytes();
                var mask = unicast.IPv4Mask.GetAddressBytes();
                var broadcast = new byte[4];
                for (var i = 0; i < 4; i++) broadcast[i] = (byte)(ip[i] | (mask[i] ^ 255));
                yield return new IPEndPoint(new IPAddress(broadcast), DiscoveryPort);
            }
        }
    }

    private async Task AcceptLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                var client = await _listener.AcceptTcpClientAsync(token);
                _ = Task.Run(() => HandleClient(client, token), token);
            }
            catch (OperationCanceledException) { break; }
            catch (SocketException) when (!token.IsCancellationRequested) { await Task.Delay(250, token); }
        }
    }

    private async Task HandleClient(TcpClient client, CancellationToken token)
    {
        TransferOffer? activeOffer = null;
        using (client)
        using (var ssl = new SslStream(client.GetStream(), false))
        {
            try
            {
                await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
                {
                    ServerCertificate = _certificate,
                    EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                    ClientCertificateRequired = false
                }, token);
                var offerMessage = await ReadMessage(ssl, token);
                if (offerMessage?.Type != "offer" || offerMessage.Offer is not { } offer || !IsValidOffer(offer)) return;
                activeOffer = offer;
                var handlers = OfferReceived;
                var accepted = handlers is not null && await handlers.Invoke(offer);
                await WriteMessage(ssl, new WireMessage("decision", Accepted: accepted), token);
                if (!accepted) return;
                ReceiveStarted?.Invoke(offer);
                var root = Path.Combine(GetReceiveFolder(), "Gửi File LAN", SafeSegment(offer.SenderName));
                Directory.CreateDirectory(root);
                var collisionMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var total = offer.TotalBytes;
                long received = 0;
                var buffer = new byte[131072];
                foreach (var file in offer.Files)
                {
                    token.ThrowIfCancellationRequested();
                    var relative = SanitizeRelativePath(file.Path);
                    relative = MakeUniquePath(relative, root, collisionMap);
                    var target = Path.GetFullPath(Path.Combine(root, relative));
                    var rootFull = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
                    if (!target.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Đường dẫn file không hợp lệ.");
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    var temp = target + ".part-" + Guid.NewGuid().ToString("N");
                    try
                    {
                        await using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 131072, useAsync: true))
                        {
                            var remaining = file.Length;
                            while (remaining > 0)
                            {
                                var read = await ssl.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), token);
                                if (read == 0) throw new EndOfStreamException("Kết nối đã ngắt trước khi nhận đủ file.");
                                await output.WriteAsync(buffer.AsMemory(0, read), token);
                                remaining -= read;
                                received += read;
                                ReceiveProgress?.Invoke(offer.Id, received, total);
                            }
                            await output.FlushAsync(token);
                        }
                        File.Move(temp, target);
                    }
                    finally { if (File.Exists(temp)) File.Delete(temp); }
                }
                await WriteMessage(ssl, new WireMessage("complete"), token);
                ReceiveCompleted?.Invoke(offer);
            }
            catch (Exception ex) when (ex is OperationCanceledException or IOException or AuthenticationException or InvalidDataException or UnauthorizedAccessException or JsonException)
            {
                // Do not surface remote exception details; report a concise local failure to the UI.
                if (activeOffer is not null) ReceiveFailed?.Invoke(activeOffer, ex is OperationCanceledException ? "Đã hủy hoặc kết nối bị ngắt." : "Không thể nhận đủ file. Hãy kiểm tra dung lượng và quyền ghi thư mục.");
            }
        }
    }

    public async Task SendAsync(PeerInfo peer, IReadOnlyList<TransferFile> files, IProgress<long> progress, CancellationToken token)
    {
        foreach (var file in files)
            if (!File.Exists(file.FullPath) || new FileInfo(file.FullPath).Length != file.Length)
                throw new IOException($"File đã thay đổi sau khi chọn: {Path.GetFileName(file.FullPath)}");
        using var client = new TcpClient(AddressFamily.InterNetwork);
        await client.ConnectAsync(IPAddress.Parse(peer.Address), peer.Port, token);
        using var ssl = new SslStream(client.GetStream(), false, (_, certificate, _, _) =>
        {
            if (certificate is null) return false;
            try
            {
                using var cert = X509CertificateLoader.LoadCertificate(certificate.GetRawCertData());
                using var advertised = X509CertificateLoader.LoadCertificate(Convert.FromBase64String(peer.CertificateDer));
                return CryptographicOperations.FixedTimeEquals(cert.GetCertHash(HashAlgorithmName.SHA256), advertised.GetCertHash(HashAlgorithmName.SHA256));
            }
            catch { return false; }
        });
        await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
        {
            TargetHost = peer.Name,
            EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
        }, token);
        var offer = new TransferOffer(Guid.NewGuid().ToString("N"), _deviceId, _deviceName,
            files.Select(f => new OfferedFile(f.RelativePath, f.Length)).ToList(), files.Sum(f => f.Length));
        await WriteMessage(ssl, new WireMessage("offer", Offer: offer), token);
        var decision = await ReadMessage(ssl, token);
        if (decision is null || decision.Type != "decision") throw new IOException("Máy nhận đóng kết nối.");
        if (!decision.Accepted) throw new OperationCanceledException("Máy nhận đã từ chối lượt gửi.");
        long sent = 0;
        var buffer = new byte[131072];
        foreach (var file in files)
        {
            await using var input = new FileStream(file.FullPath, FileMode.Open, FileAccess.Read, FileShare.Read, buffer.Length, useAsync: true);
            if (input.Length != file.Length) throw new IOException($"File đã thay đổi sau khi chọn: {Path.GetFileName(file.FullPath)}");
            var remaining = file.Length;
            while (remaining > 0)
            {
                var read = await input.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), token);
                if (read == 0) throw new EndOfStreamException($"File thay đổi trong khi gửi: {Path.GetFileName(file.FullPath)}");
                await ssl.WriteAsync(buffer.AsMemory(0, read), token);
                remaining -= read;
                sent += read;
                progress.Report(sent);
            }
        }
        await ssl.FlushAsync(token);
        var done = await ReadMessage(ssl, token);
        if (done?.Type != "complete") throw new IOException("Máy nhận không xác nhận hoàn tất.");
    }

    private static bool IsValidOffer(TransferOffer offer)
    {
        if (offer.Files.Count is < 1 or > 5000) return false;
        long total = 0;
        foreach (var file in offer.Files)
        {
            if (file.Length < 0 || file.Length > 1024L * 1024 * 1024 * 1024 || string.IsNullOrWhiteSpace(file.Path)) return false;
            try { _ = SanitizeRelativePath(file.Path); }
            catch (InvalidDataException) { return false; }
            if (long.MaxValue - total < file.Length) return false;
            total += file.Length;
        }
        return offer.TotalBytes == total;
    }
    private static string SafeSegment(string value) { var invalid = Path.GetInvalidFileNameChars(); var clean = new string(value.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim('.',' '); return string.IsNullOrWhiteSpace(clean) ? "Thiết bị" : clean; }
    private static string SanitizeRelativePath(string value)
    {
        var normalized = value.Replace('\\', '/');
        var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || parts.Any(p => p is "." or ".." || p.Contains(':') || p.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)) throw new InvalidDataException("Đường dẫn file không hợp lệ.");
        return Path.Combine(parts.Select(SafeSegment).ToArray());
    }
    private static string MakeUniquePath(string relative, string root, Dictionary<string, string> seen)
    {
        var candidate = relative;
        var full = Path.Combine(root, candidate);
        var index = 2;
        while (seen.ContainsKey(candidate) || File.Exists(full) || Directory.Exists(full))
        {
            var dir = Path.GetDirectoryName(relative);
            var name = Path.GetFileNameWithoutExtension(relative);
            var ext = Path.GetExtension(relative);
            candidate = Path.Combine(dir ?? "", $"{name} ({index++}){ext}");
            full = Path.Combine(root, candidate);
        }
        seen[candidate] = candidate;
        return candidate;
    }
    private static string GetReceiveFolder()
    {
        try
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GuiFileLAN", "settings.json");
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), JsonOptions)?.ReceiveFolder ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        }
        catch { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"); }
    }
    private static async Task WriteMessage(Stream stream, WireMessage message, CancellationToken token)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(message, JsonOptions);
        var prefix = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(prefix, bytes.Length);
        await stream.WriteAsync(prefix, token); await stream.WriteAsync(bytes, token); await stream.FlushAsync(token);
    }
    private static async Task<WireMessage?> ReadMessage(Stream stream, CancellationToken token)
    {
        var prefix = new byte[4]; await ReadExactly(stream, prefix, token);
        var length = BinaryPrimitives.ReadInt32LittleEndian(prefix);
        if (length is < 1 or > 4 * 1024 * 1024) throw new InvalidDataException("Thông điệp mạng không hợp lệ.");
        var bytes = new byte[length]; await ReadExactly(stream, bytes, token);
        return JsonSerializer.Deserialize<WireMessage>(bytes, JsonOptions);
    }
    private static async Task ReadExactly(Stream stream, Memory<byte> buffer, CancellationToken token)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer[offset..], token);
            if (read == 0) throw new EndOfStreamException();
            offset += read;
        }
    }

    public async ValueTask DisposeAsync()
    {
        _shutdown.Cancel();
        _listener.Stop();
        _udp?.Dispose();
        _certificate.Dispose();
        _shutdown.Dispose();
        await Task.CompletedTask;
    }

    private sealed record DiscoveryMessage(string Id, string Name, int Port, string CertificateDer);
}
