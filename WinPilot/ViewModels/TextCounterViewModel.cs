using System.Text;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace WinPilot.ViewModels;

public partial class TextCounterViewModel : ObservableObject
{
    [ObservableProperty] private string _inputText = "";
    [ObservableProperty] private string _copyStatusText = "";

    public int TotalChars    => InputText.Length;
    public int CharsNoSpace  => InputText.Count(c => !char.IsWhiteSpace(c));
    public int WordCount     => string.IsNullOrWhiteSpace(InputText) ? 0
                                : InputText.Split([' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries).Length;
    public int LineCount     => InputText.Length == 0 ? 0 : InputText.Split('\n').Length;
    public int BytesUtf8     => Encoding.UTF8.GetByteCount(InputText);
    public int BytesUtf16    => Encoding.Unicode.GetByteCount(InputText);

    partial void OnInputTextChanged(string value)
    {
        OnPropertyChanged(nameof(TotalChars));
        OnPropertyChanged(nameof(CharsNoSpace));
        OnPropertyChanged(nameof(WordCount));
        OnPropertyChanged(nameof(LineCount));
        OnPropertyChanged(nameof(BytesUtf8));
        OnPropertyChanged(nameof(BytesUtf16));
    }

    [RelayCommand]
    private void Clear() => InputText = "";

    [RelayCommand]
    private void CopyStats()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"전체 글자: {TotalChars}");
        sb.AppendLine($"공백 제외: {CharsNoSpace}");
        sb.AppendLine($"단어 수:   {WordCount}");
        sb.AppendLine($"줄 수:     {LineCount}");
        sb.AppendLine($"UTF-8:     {BytesUtf8} bytes");
        sb.AppendLine($"UTF-16:    {BytesUtf16} bytes");
        Clipboard.SetText(sb.ToString());
        CopyStatusText = "복사됨!";
        Task.Delay(2500).ContinueWith(_ => CopyStatusText = "", TaskScheduler.FromCurrentSynchronizationContext());
    }
}
