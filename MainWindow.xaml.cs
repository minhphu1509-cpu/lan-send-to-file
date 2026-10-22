using System.Collections.ObjectModel;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text.Json;

namespace GuiFileLAN;

public partial class MainWindow : System.Windows.Window
{
    private readonly NetworkService _network = new();
    private readonly ObservableCollection<PeerViewModel> _peers = new();
    public ObservableCollection<TransferViewModel> Transfers { get; } = new();
    private readonly AppSettings _settings;
    private readonly string _settingsPath;
    private readonly ObservableCollection<TransferFile> _selection = new();
    public ObservableCollection<TransferFile> Selection => _selection;
    private CancellationTokenSource? _sendCancellation;
    private readonly Dictionary<string, TransferViewModel> _incomingTransfers = new();
    private readonly System.Windows.Threading.DispatcherTimer _peerExpiryTimer = new() { Interval = TimeSpan.FromSeconds(3) };

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        PeersList.ItemsSource = _peers;
        _settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GuiFileLAN", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
        _settings = LoadSettings();
        if (!Directory.Exists(_settings.ReceiveFolder)) _settings.ReceiveFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        ReceiveFolderBox.Text = _settings.ReceiveFolder;
        ApplyLanguage();

        _network.PeerChanged += OnPeerChanged;
        _network.OfferReceived += OnOfferReceived;
        _network.ReceiveStarted += OnReceiveStarted;
        _network.ReceiveProgress += OnReceiveProgress;
        _network.ReceiveCompleted += OnReceiveCompleted;
        _network.ReceiveFailed += OnReceiveFailed;
        Loaded += (_, _) =>
        {
            try
            {
                _network.Start(); ConnectionStatus.Text = T("Finding");
                _peerExpiryTimer.Tick += (_, _) => RemoveExpiredPeers();
                _peerExpiryTimer.Start();
            }
            catch (Exception ex) { ConnectionStatus.Text = T("PortError"); FooterStatus.Text = ex.Message; }
        };
        Closed += async (_, _) => { _peerExpiryTimer.Stop(); _sendCancellation?.Cancel(); await _network.DisposeAsync(); };
    }

    private AppSettings LoadSettings()
    {
        try { return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_settingsPath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new AppSettings(); }
        catch { return new AppSettings(); }
    }

    private void SaveSettings()
    {
        try { File.WriteAllText(_settingsPath, JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase })); }
        catch (Exception ex) { FooterStatus.Text = string.Format(T("SettingsError"), ex.Message); }
    }

    private void OnPeerChanged(PeerInfo peer) => Dispatcher.Invoke(() =>
    {
        var existing = _peers.FirstOrDefault(p => p.Info.Id == peer.Id);
        if (existing is null) _peers.Add(new PeerViewModel(peer));
        else existing.Update(peer);
        NoPeersHint.Text = _peers.Count == 0 ? T("Searching") : T("SelectPeer");
        ConnectionStatus.Text = string.Format(T("FoundDevices"), _peers.Count);
        UpdateSendButton();
    });

    private void RemoveExpiredPeers()
    {
        var now = DateTime.UtcNow;
        foreach (var peer in _peers.Where(p => now - p.Info.LastSeen > TimeSpan.FromSeconds(12)).ToArray()) _peers.Remove(peer);
        NoPeersHint.Text = _peers.Count == 0 ? T("Searching") : T("SelectPeer");
        ConnectionStatus.Text = _peers.Count == 0 ? T("Finding") : string.Format(T("FoundDevices"), _peers.Count);
        UpdateSendButton();
    }

    private void ChooseFiles_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = T("ChooseFileTitle"), Multiselect = true, CheckFileExists = true };
        if (dialog.ShowDialog(this) != true) return;
        AddFiles(dialog.FileNames.Select(path => (path, Path.GetFileName(path))));
    }

    private async void ChooseFolder_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = T("ChooseFolderTitle"), Multiselect = false };
        if (dialog.ShowDialog(this) != true || string.IsNullOrWhiteSpace(dialog.FolderName)) return;
        var root = dialog.FolderName;
        var rootName = Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        SendButton.IsEnabled = false;
        FooterStatus.Text = T("IndexingFolder");
        try
        {
            var files = await Task.Run(() => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .Select(path => (path, Path.Combine(rootName, Path.GetRelativePath(root, path)))).ToArray());
            AddFiles(files);
            FooterStatus.Text = T("Footer");
        }
        catch (Exception ex) { System.Windows.MessageBox.Show(this, string.Format(T("FolderReadError"), ex.Message), "Gửi File LAN", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error); }
        finally { UpdateSendButton(); }
    }

    private void AddFiles(IEnumerable<(string path, string relative)> files)
    {
        var existing = _selection.Select(f => f.FullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var (path, relative) in files)
        {
            if (!File.Exists(path) || !existing.Add(path)) continue;
            var info = new FileInfo(path);
            _selection.Add(new TransferFile(path, relative, info.Length));
        }
        SelectionSummary.Text = _selection.Count == 0 ? T("NoFiles") : string.Format(T("CountSize"), _selection.Count, FormatBytes(_selection.Sum(f => f.Length)));
        ClearSelectionButton.IsEnabled = _selection.Count > 0;
        UpdateSendButton();
    }

    private async void Send_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (PeersList.SelectedItem is not PeerViewModel selected || _selection.Count == 0) return;
        var peer = selected.Info;
        var files = _selection.ToList();
        var totalBytes = files.Sum(f => f.Length);
        var item = new TransferViewModel(string.Format(T("To"), peer.Name, files.Count, FormatBytes(totalBytes)), T("Connecting"));
        Transfers.Insert(0, item);
        _sendCancellation = new CancellationTokenSource();
        SendButton.IsEnabled = false; CancelButton.Visibility = System.Windows.Visibility.Visible;
        _selection.Clear(); SelectionSummary.Text = T("NoFiles");
        ClearSelectionButton.IsEnabled = false;
        var progress = new Progress<long>(sent => { item.Progress = totalBytes == 0 ? 100 : sent * 100d / totalBytes; item.Status = string.Format(T("Sending"), FormatBytes(sent), FormatBytes(totalBytes)); });
        try
        {
            item.Status = T("Waiting");
            await _network.SendAsync(peer, files, progress, _sendCancellation.Token);
            item.Progress = 100; item.Status = T("Sent"); FooterStatus.Text = string.Format(T("SentFooter"), peer.Name);
            RecentTransfers.Items.Insert(0, string.Format(T("SentRecent"), files.Count, peer.Name, DateTime.Now));
        }
        catch (OperationCanceledException ex)
        {
            item.Status = ex.Message.Contains("từ chối", StringComparison.OrdinalIgnoreCase) ? T("ReceiverDeclined") : T("Cancelled");
            FooterStatus.Text = item.Status;
            RestoreSelection(files);
        }
        catch (Exception ex)
        {
            if (ex is AuthenticationException || ex is IOException && ex.Message.Contains("EOF", StringComparison.OrdinalIgnoreCase))
            {
                item.Status = T("TlsError");
                FooterStatus.Text = string.Format(T("TlsDetail"), ex.Message);
            }
            else if (ex is SocketException socket)
            {
                item.Status = T("SocketError");
                FooterStatus.Text = string.Format(T("SocketDetail"), peer.Name, NetworkService.TransferPort, socket.Message);
            }
            else
            {
                item.Status = T("SendFailed");
                FooterStatus.Text = string.Format(T("SendFailedDetail"), peer.Name, ex.Message);
            }
            RestoreSelection(files);
        }
        finally
        {
            _sendCancellation?.Dispose(); _sendCancellation = null;
            CancelButton.Visibility = System.Windows.Visibility.Collapsed;
            UpdateSendButton();
        }
    }

    private void Cancel_Click(object sender, System.Windows.RoutedEventArgs e) => _sendCancellation?.Cancel();

    private void ClearSelection_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        _selection.Clear();
        SelectionSummary.Text = T("NoFiles");
        ClearSelectionButton.IsEnabled = false;
        UpdateSendButton();
    }

    private void PeersList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) => UpdateSendButton();

    private void RestoreSelection(IEnumerable<TransferFile> files)
    {
        var known = _selection.Select(f => f.FullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files) if (File.Exists(file.FullPath) && known.Add(file.FullPath)) _selection.Add(file);
        SelectionSummary.Text = _selection.Count == 0 ? T("NoFiles") : string.Format(T("CountSize"), _selection.Count, FormatBytes(_selection.Sum(f => f.Length)));
        ClearSelectionButton.IsEnabled = _selection.Count > 0;
    }

    private async Task<bool> OnOfferReceived(TransferOffer offer)
    {
        var details = string.Join("\n", offer.Files.Take(8).Select(f => string.Format(T("OfferFile"), Path.GetFileName(f.Path), FormatBytes(f.Length))));
        if (offer.Files.Count > 8) details += string.Format(T("MoreFiles"), offer.Files.Count - 8);
        var savePath = Path.Combine(_settings.ReceiveFolder, "Gửi File LAN", offer.SenderName);
        var message = string.Format(T("OfferPrompt"), offer.SenderName, offer.Files.Count, FormatBytes(offer.TotalBytes), details, "", savePath);
        return await Dispatcher.InvokeAsync(() => System.Windows.MessageBox.Show(this, message, T("OfferTitle"), System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question) == System.Windows.MessageBoxResult.Yes);
    }

    private void OnReceiveStarted(TransferOffer offer) => Dispatcher.Invoke(() =>
    {
        var item = new TransferViewModel(string.Format(T("From"), offer.SenderName, offer.Files.Count, FormatBytes(offer.TotalBytes)), T("Receiving"));
        _incomingTransfers[offer.Id] = item;
        Transfers.Insert(0, item);
    });

    private void OnReceiveProgress(string id, long received, long total) => Dispatcher.Invoke(() =>
    {
        if (!_incomingTransfers.TryGetValue(id, out var item)) return;
        item.Progress = total == 0 ? 100 : received * 100d / total;
        item.Status = string.Format(T("ReceivingProgress"), FormatBytes(received), FormatBytes(total));
    });

    private void OnReceiveCompleted(TransferOffer offer) => Dispatcher.Invoke(() =>
    {
        if (_incomingTransfers.Remove(offer.Id, out var item)) { item.Progress = 100; item.Status = T("Received"); }
        RecentTransfers.Items.Insert(0, string.Format(T("ReceivedRecent"), offer.Files.Count, offer.SenderName, DateTime.Now));
        FooterStatus.Text = string.Format(T("ReceivedFooter"), offer.SenderName);
    });

    private void OnReceiveFailed(TransferOffer offer, string message) => Dispatcher.Invoke(() =>
    {
        var translated = message.Contains("hủy", StringComparison.OrdinalIgnoreCase) ? T("ReceiveFailed") : T("ReceiveFailedSafe");
        if (_incomingTransfers.Remove(offer.Id, out var item)) item.Status = translated;
        FooterStatus.Text = translated;
    });

    private void ChooseReceiveFolder_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = T("ChooseSaveTitle"), Multiselect = false, InitialDirectory = _settings.ReceiveFolder };
        if (dialog.ShowDialog(this) != true || string.IsNullOrWhiteSpace(dialog.FolderName)) return;
        _settings.ReceiveFolder = dialog.FolderName; ReceiveFolderBox.Text = dialog.FolderName; SaveSettings();
    }

    private string T(string key) => UiText.Get(_settings.Language, key);

    private void SwitchLanguage_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        _settings.Language = _settings.Language.Equals("en", StringComparison.OrdinalIgnoreCase) ? "vi" : "en";
        ApplyLanguage(); SaveSettings();
    }

    private void ApplyLanguage()
    {
        AppTitleText.Text = "Gửi File LAN";
        LanguageButton.Content = _settings.Language.Equals("en", StringComparison.OrdinalIgnoreCase) ? "English · EN" : "Tiếng Việt · VI";
        SubtitleText.Text = T("Subtitle"); DevicesTitle.Text = T("DevicesTitle");
        DevicesDescription.Text = T("DevicesDescription"); SendTitle.Text = T("SendTitle");
        SendDescription.Text = T("SendDescription"); FilesButton.Content = T("ChooseFiles");
        FolderButton.Content = T("ChooseFolder"); ClearSelectionButton.Content = T("Clear");
        SendButton.Content = T("Send"); CancelButton.Content = T("Cancel");
        TransfersTitle.Text = T("Transfers"); RecentTitle.Text = T("Recent");
        SaveTitle.Text = T("SaveTitle"); SaveDescription.Text = T("SaveDescription");
        ChooseReceiveFolderButton.Content = T("ChangeFolder"); DeveloperText.Text = T("DeveloperVi");
        FooterStatus.Text = T("Footer"); SelectionSummary.Text = _selection.Count == 0
            ? T("NoFiles") : string.Format(T("CountSize"), _selection.Count, FormatBytes(_selection.Sum(f => f.Length)));
        NoPeersHint.Text = _peers.Count == 0 ? T("Searching") : T("SelectPeer");
        ConnectionStatus.Text = _peers.Count == 0 ? T("Finding") : string.Format(T("FoundDevices"), _peers.Count);
    }

    private void UpdateSendButton() => SendButton.IsEnabled = _sendCancellation is null && _selection.Count > 0 && PeersList.SelectedItem is PeerViewModel;
    private static string FormatBytes(long bytes) => bytes switch { < 1024 => $"{bytes} B", < 1024 * 1024 => $"{bytes / 1024d:0.0} KB", < 1024L * 1024 * 1024 => $"{bytes / (1024d * 1024):0.0} MB", _ => $"{bytes / (1024d * 1024 * 1024):0.00} GB" };
}
