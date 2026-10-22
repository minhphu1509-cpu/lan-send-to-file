using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace GuiFileLAN;

public sealed record PeerInfo(string Id, string Name, string Address, int Port, string CertificateDer, DateTime LastSeen);
public sealed record TransferFile(string FullPath, string RelativePath, long Length);
public sealed record TransferOffer(string Id, string SenderId, string SenderName, List<OfferedFile> Files, long TotalBytes);
public sealed record OfferedFile(string Path, long Length);
public sealed record WireMessage(string Type, TransferOffer? Offer = null, bool Accepted = false, string? Error = null);

public sealed class PeerViewModel : INotifyPropertyChanged
{
    public PeerViewModel(PeerInfo info) => Info = info;
    public PeerInfo Info { get; private set; }
    public string Name => Info.Name;
    public string Address => Info.Address;
    public string Display => $"{Name}  ·  {Address}";
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Update(PeerInfo info) { Info = info; OnPropertyChanged(nameof(Name)); OnPropertyChanged(nameof(Address)); OnPropertyChanged(nameof(Display)); }
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class TransferViewModel : INotifyPropertyChanged
{
    private string _status;
    private double _progress;
    public TransferViewModel(string summary, string status = "Đang chờ") { Summary = summary; _status = status; }
    public string Summary { get; }
    public string Status { get => _status; set { _status = value; OnPropertyChanged(); } }
    public double Progress { get => _progress; set { _progress = value; OnPropertyChanged(); } }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class AppSettings
{
    public string ReceiveFolder { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    public string Language { get; set; } = "vi";
}

public static class UiText
{
    private static readonly Dictionary<string, (string Vi, string En)> Text = new()
    {
        ["Subtitle"] = ("Chia sẻ file trực tiếp trong mạng Wi-Fi của bạn", "Share files directly over your Wi-Fi network"),
        ["DevicesTitle"] = ("Máy tính trong mạng", "Computers on your network"),
        ["DevicesDescription"] = ("Mở ứng dụng trên máy nhận để máy xuất hiện ở đây.", "Open the app on the receiving computer to see it here."),
        ["Searching"] = ("Đang tìm máy tính cùng mạng…", "Searching for computers on your network…"),
        ["IndexingFolder"] = ("Đang quét thư mục…", "Scanning folder…"),
        ["SelectPeer"] = ("Chọn máy tính rồi chọn file hoặc thư mục.", "Choose a computer, then select files or a folder."),
        ["SendTitle"] = ("Chọn nội dung gửi", "Choose what to send"),
        ["SendDescription"] = ("Gửi một hoặc nhiều file, hoặc giữ nguyên cấu trúc thư mục.", "Send one or more files, or preserve a folder structure."),
        ["ChooseFiles"] = ("Chọn file", "Choose files"), ["ChooseFolder"] = ("Chọn thư mục", "Choose folder"),
        ["Clear"] = ("Bỏ chọn", "Clear selection"), ["NoFiles"] = ("Chưa chọn file nào", "No files selected"),
        ["Send"] = ("Gửi an toàn", "Send securely"), ["Cancel"] = ("Hủy lượt gửi", "Cancel transfer"),
        ["Transfers"] = ("Lượt truyền", "Transfers"), ["Recent"] = ("Gần đây", "Recent"),
        ["SaveTitle"] = ("Nơi lưu file nhận", "Receive location"),
        ["SaveDescription"] = ("File chỉ được lưu sau khi bạn chấp thuận lượt nhận.", "Files are saved only after you accept an incoming transfer."),
        ["ChangeFolder"] = ("Đổi thư mục lưu", "Change save folder"),
        ["Footer"] = ("File được truyền trực tiếp và mã hóa trong mạng nội bộ.", "Files are transferred directly and encrypted across your local network."),
        ["DeveloperVi"] = ("Phát triển bởi PhuDong · 0766771509", "Developer: PhuDong · 0766771509"),
        ["Starting"] = ("Đang khởi động…", "Starting…"), ["Finding"] = ("Đang tìm thiết bị", "Finding devices"),
        ["FoundDevices"] = ("Đã tìm thấy {0} máy", "Found {0} device(s)"),
        ["PortError"] = ("Không thể mở cổng mạng", "Could not open the network port"),
        ["ChooseFileTitle"] = ("Chọn file cần gửi", "Choose files to send"),
        ["ChooseFolderTitle"] = ("Chọn thư mục cần gửi", "Choose a folder to send"),
        ["ChooseSaveTitle"] = ("Chọn nơi lưu file nhận", "Choose where to save received files"),
        ["FolderReadError"] = ("Không thể đọc thư mục: {0}", "Could not read folder: {0}"),
        ["CountSize"] = ("{0:N0} file · {1}", "{0:N0} file(s) · {1}"),
        ["To"] = ("Đến {0}: {1} file · {2}", "To {0}: {1} file(s) · {2}"),
        ["From"] = ("Từ {0}: {1} file · {2}", "From {0}: {1} file(s) · {2}"),
        ["Connecting"] = ("Đang kết nối…", "Connecting…"), ["Waiting"] = ("Đang chờ máy nhận chấp thuận…", "Waiting for the receiver to accept…"),
        ["Sending"] = ("Đang gửi · {0} / {1}", "Sending · {0} / {1}"), ["Receiving"] = ("Đang nhận…", "Receiving…"),
        ["ReceivingProgress"] = ("Đang nhận · {0} / {1}", "Receiving · {0} / {1}"),
        ["Sent"] = ("Đã gửi xong", "Sent"), ["Received"] = ("Đã nhận xong", "Received"),
        ["ReceiverDeclined"] = ("Máy nhận đã từ chối", "The receiver declined"),
        ["Cancelled"] = ("Đã hủy hoặc kết nối bị ngắt", "Cancelled or connection lost"),
        ["TlsError"] = ("Không bắt tay TLS · cần cập nhật ứng dụng hai máy", "TLS handshake failed · update the app on both computers"),
        ["TlsDetail"] = ("Máy nhận chưa thiết lập được TLS. Cài bản 1.1.0 trên cả hai máy rồi thử lại. Chi tiết: {0}", "The receiver could not establish TLS. Install version 1.1.0 on both computers and try again. Details: {0}"),
        ["SocketError"] = ("Không kết nối được · kiểm tra máy nhận và Firewall", "Connection failed · check the receiver and Firewall"),
        ["SocketDetail"] = ("Không kết nối được đến {0} (cổng {1}): {2}", "Could not connect to {0} (port {1}): {2}"),
        ["SendFailed"] = ("Gửi thất bại · có thể thử lại", "Transfer failed · you can try again"),
        ["SendFailedDetail"] = ("Không gửi được đến {0}: {1}", "Could not send to {0}: {1}"),
        ["SentFooter"] = ("Đã gửi file đến {0}.", "Files sent to {0}."), ["ReceivedFooter"] = ("Đã nhận file từ {0}.", "Files received from {0}."),
        ["SentRecent"] = ("Đã gửi {0} file đến {1} · {2:HH:mm}", "Sent {0} file(s) to {1} · {2:HH:mm}"),
        ["ReceivedRecent"] = ("Đã nhận {0} file từ {1} · {2:HH:mm}", "Received {0} file(s) from {1} · {2:HH:mm}"),
        ["OfferTitle"] = ("Yêu cầu nhận file", "Incoming transfer"),
        ["OfferPrompt"] = ("Máy {0} muốn gửi {1} file ({2}):\n\n{3}{4}\n\nLưu vào: {5}\n\nBạn có chấp nhận không?", "{0} wants to send {1} file(s) ({2}):\n\n{3}{4}\n\nSave to: {5}\n\nDo you want to accept?"),
        ["MoreFiles"] = ("\n… và {0} file khác", "\n… and {0} more file(s)"),
        ["OfferFile"] = ("• {0} · {1}", "• {0} · {1}"),
        ["ReceiveFailed"] = ("Đã hủy hoặc kết nối bị ngắt.", "Cancelled or connection lost."),
        ["ReceiveFailedSafe"] = ("Không thể nhận đủ file. Hãy kiểm tra dung lượng và quyền ghi thư mục.", "Could not receive all files. Check available space and folder permissions."),
        ["SettingsError"] = ("Không lưu được cài đặt: {0}", "Could not save settings: {0}")
    };

    public static string Get(string language, string key) => Text.TryGetValue(key, out var value)
        ? (language.Equals("en", StringComparison.OrdinalIgnoreCase) ? value.En : value.Vi)
        : key;
}

public static class RecentTransfers
{
    public static ObservableCollection<string> Items { get; } = new();
}
