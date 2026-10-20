using System.Globalization;
using System.Windows;
using Gather.Core;
namespace Gather;

public partial class EventEditor : Window
{
    private readonly Workspace workspace;
    private readonly string? eventId;
    public CalendarEvent? SavedEvent { get; private set; }
    public EventEditor(Workspace workspace, DateTime day, CalendarEvent? item = null)
    {
        this.workspace = workspace; eventId = item?.Id;
        InitializeComponent();
        DateInput.SelectedDate = item?.Start.LocalDateTime.Date ?? day;
        if (item != null) { EditorTitle.Text = "일정 수정"; TitleInput.Text = item.Title; StartInput.Text = item.Start.ToLocalTime().ToString("HH:mm"); EndInput.Text = item.End.ToLocalTime().ToString("HH:mm"); LocationInput.Text = item.Location; }
        Loaded += (_, _) => TitleInput.Focus();
    }
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (DateInput.SelectedDate is not DateTime day || !TimeSpan.TryParseExact(StartInput.Text.Trim(), @"hh\:mm", CultureInfo.InvariantCulture, out var start) || !TimeSpan.TryParseExact(EndInput.Text.Trim(), @"hh\:mm", CultureInfo.InvariantCulture, out var end)) { ErrorText.Text = "날짜와 HH:mm 형식의 시간을 입력해 주세요. 예: 09:30"; return; }
        try { SavedEvent = workspace.SaveLocalEvent(eventId, TitleInput.Text, day, start, end, LocationInput.Text); DialogResult = true; }
        catch (ArgumentException ex) { ErrorText.Text = ex.Message; }
    }
}
