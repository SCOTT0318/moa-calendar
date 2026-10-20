namespace Gather.Core;

public static class DemoFactory
{
    public static DemoState Create(DateTime today)
    {
        var state = new DemoState { Accounts = [new("gmail-demo", "개인 메일", "personal@example.invalid", Provider.Gmail), new("naver-demo", "생활 메일", "daily@example.invalid", Provider.Naver), new("hiworks-demo", "회사 메일", "work@example.invalid", Provider.Hiworks)] };
        string[] senders = ["디자인 팀", "프로젝트 매니저", "모아 캘린더", "북클럽", "기술 뉴스레터", "운영 팀", "여행 노트", "모아 시작 가이드"];
        string[] subjects = ["이번 주 디자인 리뷰 자료를 공유합니다", "다음 단계와 일정을 확인해 주세요", "오후 팀 미팅을 잊지 마세요", "이번 달 함께 읽을 책", "작게 시작하는 더 나은 업무 습관", "10월 운영 안내", "다음 여행을 위한 작은 아이디어", "메일과 일정을 한곳에서 시작해 보세요"];
        for (var i = 0; i < subjects.Length; i++)
            state.Messages.Add(new MailMessage { Id = $"mail-{i}", AccountId = state.Accounts[i % 3].Id, Sender = senders[i], SenderAddress = $"sample{i}@example.invalid", Subject = subjects[i], ReceivedAt = new DateTimeOffset(today.Date.AddHours(10).AddMinutes(-i * 87)), IsRead = i > 2, IsStarred = i is 0 or 3,
                Body = $"안녕하세요.\n\n{subjects[i]}.\n\n여러 계정의 메일을 한곳에서 확인하고 중요한 내용을 차분하게 살펴보세요. 계정별 필터, 검색, 별표, 읽음 표시를 사용할 수 있습니다.\n\n이 화면의 발신자·주소·내용·일정은 모두 가상의 데모 데이터입니다. 실제 메일 전송이나 일정 변경은 수행되지 않습니다.\n\n감사합니다.\n{senders[i]} 드림" });
        state.Events = [new("event-1", "hiworks-demo", "팀 체크인 · 예시", At(today, 10), At(today, 10.5), "회의실 A · 가상 일정"), new("event-2", "gmail-demo", "디자인 리뷰 · 예시", At(today, 14), At(today, 15), "온라인 · 가상 일정"), new("event-3", "naver-demo", "저녁 독서 모임 · 예시", At(today, 19), At(today, 20), "동네 서점 · 가상 일정"), new("event-4", "gmail-demo", "다음 주 계획 · 예시", At(today.AddDays(3), 11), At(today.AddDays(3), 12), "개인 일정 · 가상 일정"), new("event-5", "hiworks-demo", "프로젝트 점검 · 예시", At(today.AddDays(7), 15), At(today.AddDays(7), 16), "회의실 B · 가상 일정")];
        return state;
    }
    private static DateTimeOffset At(DateTime day, double hour) => new(day.Date.AddHours(hour));
}
