using System.Windows;
using Gather.Integrations;
namespace Gather;
public partial class CalendarConnectWindow : Window
{
    private readonly string provider;
    public CalendarConnection? Connection { get; private set; }
    public CalendarConnectWindow(string provider = "Naver")
    {
        this.provider = provider; InitializeComponent();
        if (provider == "Google")
        { ConnectTitle.Text = "Google 계정 추가"; ConnectHelp.Text = "연결할 기능을 선택하세요. 다음 브라우저 창에서 원하는 Google 계정을 선택합니다. 같은 방식으로 두 번째 계정도 추가할 수 있습니다."; PasswordPanel.Visibility = Visibility.Collapsed; Height = 440; }
        if (provider == "Hiworks")
        { ConnectTitle.Text = "하이웍스 계정 추가"; ConnectHelp.Text = "회사에서 외부 메일 사용을 허용해야 합니다. 하이웍스의 전용 메일 비밀번호를 입력하세요."; UserLabel.Text = "전체 메일 주소"; CalendarChoice.IsChecked = false; CalendarChoice.Visibility = Visibility.Collapsed; }
        Loaded += (_, _) => LabelInput.Focus();
    }
    private void Connect_Click(object sender, RoutedEventArgs e)
    {
        var user = UserNameInput.Text.Trim();
        if (provider == "Naver" && user.EndsWith("@naver.com", StringComparison.OrdinalIgnoreCase)) user = user[..^10];
        if (provider == "Naver" && (user.Length == 0 || user.Any(x => !(char.IsAsciiLetterOrDigit(x) || x is '-' or '_')))) { ErrorText.Text = "네이버 아이디를 확인해 주세요."; return; }
        if (provider == "Hiworks" && (!user.Contains('@') || user.Contains(':') || user.Any(char.IsWhiteSpace))) { ErrorText.Text = "전체 메일 주소를 확인해 주세요."; return; }
        if (provider != "Google" && PasswordInput.Password.Length == 0) { ErrorText.Text = "비밀번호를 입력해 주세요."; return; }
        if (MailChoice.IsChecked != true && CalendarChoice.IsChecked != true) { ErrorText.Text = "메일 또는 캘린더를 선택해 주세요."; return; }
        Connection = new CalendarConnection { Provider = provider, Label = LabelInput.Text.Trim(), UserName = user, Password = PasswordInput.Password, MailEnabled = MailChoice.IsChecked == true, CalendarEnabled = CalendarChoice.IsChecked == true };
        PasswordInput.Clear(); DialogResult = true;
    }
}
