# 계정 연결과 검증 경계

확인일: 2026-10-20. 구현된 네트워크 어댑터와 모의 응답·WPF·설치 검증을 제공합니다. 실제 사용자 비밀번호 입력·Google 승인·네이버/하이웍스 실수신은 이 개발 과정에서 수행하지 않았습니다.

## Google 여러 계정

1. Google Cloud 프로젝트에서 Gmail API와 Calendar API를 활성화합니다. 실제로 선택할 기능만 사용할 수도 있습니다.
2. OAuth 동의 화면을 구성합니다. 외부 테스트 모드라면 연결할 **Google 계정 2개를 모두 테스트 사용자**로 추가합니다.
3. OAuth 클라이언트를 **데스크톱 앱**으로 생성하고 JSON을 내려받습니다. 웹 앱용 JSON은 받지 않습니다.
4. 모아 설정에서 Google 계정 추가 → 표시 이름·메일/캘린더 선택 → JSON 선택 → 시스템 브라우저에서 계정과 읽기 권한 승인.
5. Google 계정 추가를 다시 누릅니다. 저장한 클라이언트 설정을 재사용하고 브라우저의 계정 선택 창을 다시 열어 두 번째 계정으로 승인합니다. 토큰은 연결 UUID별로 별도로 저장합니다.

`calendar.readonly`, `gmail.readonly` 중 선택한 기능의 범위만 요청합니다. PKCE·loopback 리디렉션을 사용하는 공식 .NET 인증 라이브러리를 쓰며 JSON의 임의 인증 주소는 채택하지 않습니다. 비밀번호를 직접 받지 않습니다. 공용으로 검증된 OAuth 클라이언트는 제공하지 않으므로 각 사용자의 최초 앱 설정이 필요합니다. Google의 제한 범위 심사나 외부 공개 앱 검증 완료를 주장하지 않습니다.

메일은 받은 편지함의 최신 50개 ID 목록을 받고 새 ID의 본문만 읽습니다. Gmail full payload의 텍스트를 사용하며 첨부파일 API는 호출하지 않습니다. 프로필 이메일로 연결 계정을 구분합니다. 캘린더는 등록된 접근 가능한 캘린더의 모든 페이지를 읽고 `singleEvents=true`로 반복 인스턴스를 받습니다.

## 네이버 여러 계정

설정에서 네이버 계정 추가를 반복합니다. 아이디·비밀번호·앱 비밀번호는 이 앱의 보안 입력 창에서만 입력합니다. 2단계 인증 사용 시 앱 비밀번호를 사용하며 네이버 메일의 IMAP 사용 설정도 필요합니다. 계정별 연결 UUID·메일 캐시·일정을 분리하고 같은 아이디의 중복 추가는 막습니다.

- 메일: `imap.naver.com:993`, TLS, `FolderAccess.ReadOnly`. UIDVALIDITY/UID로 중복을 구분합니다. 읽음 상태 변경·삭제·발송은 하지 않습니다.
- 일정: `https://caldav.calendar.naver.com:443/`에서 principal → home → calendar 목록을 찾은 뒤 기간별 REPORT를 사용합니다. PROPFIND/REPORT 외 원격 변경 메서드는 사용하지 않습니다. 같은 HTTPS 호스트/443 이외의 경로·리디렉션에는 인증 정보를 전달하지 않습니다.
- 반복·종일·EXDATE·RECURRENCE-ID·취소를 Ical.Net으로 해석합니다. 공식 안내가 iOS·Mac 기준이므로 **Windows 실계정 호환은 미검증**입니다. 2단계 인증을 사용하는 단체 아이디는 앱 비밀번호 지원 제한이 있습니다.

## 하이웍스 여러 계정

회사 정책에서 외부 메일 사용을 허용한 후 전체 이메일 주소와 전용 메일 비밀번호를 입력합니다. `pop3s.hiworks.com:995` TLS와 UIDL로 최근 받은 메일을 구분합니다. DELE·DeleteMessage를 호출하지 않고 서버 사본을 유지합니다. 폴더·읽음 상태 동기화와 일정 연동은 제공하지 않습니다. IMAP 지원으로 표시하지 않습니다.

## 알림·저장·오류 처리

- 첫 조회는 메일 기준선입니다. 이후 2분마다 새 ID·안 읽음·이전 최신 수신 시각 이후의 메일만 알립니다. 재시작 때 기존 메일을 몰아서 알리지 않습니다.
- 일정 시작 0~60분 전 알림을 제공합니다. 현재 보고 있는 달이 과거/미래여도 오늘·내일 일정을 함께 조회해 알림 대상으로 삼습니다. 종일 일정의 시간 알림은 제공하지 않습니다.
- 앱·트레이가 실행 중일 때 자체 알림창을 표시합니다. 완전히 종료하면 조회·알림이 멈춥니다. Windows 알림 센터 등록은 구현하지 않았습니다.
- 계정별 자격 증명은 Windows 현재 사용자 DPAPI로 `LocalAppData/Moa/calendar-private.bin`에 저장합니다. 실제 메일·외부 일정은 메모리에만 둡니다. 토큰의 기본 평문 FileDataStore는 사용하지 않습니다.
- 한 계정·서비스 조회 실패는 그 데이터의 이전 정상 결과를 유지하고 다른 계정 처리를 계속합니다. TLS 인증서 검증을 끄지 않으며 비밀번호·토큰·서버 응답 원문을 진단 로그에 출력하지 않습니다.
- 창 닫기는 기본적으로 트레이 유지입니다. 바탕화면 바로가기를 다시 실행하면 사용자 세션의 named pipe로 기존 창을 복원합니다. 트레이의 종료로 완전히 종료합니다.

## 공식 근거

- [Google Desktop OAuth / PKCE](https://developers.google.com/identity/protocols/oauth2/native-app)
- [Gmail messages.list](https://developers.google.com/workspace/gmail/api/reference/rest/v1/users.messages/list)
- [Gmail messages.get](https://developers.google.com/workspace/gmail/api/reference/rest/v1/users.messages/get)
- [Gmail 권한 범위](https://developers.google.com/workspace/gmail/api/auth/scopes)
- [Google Calendar events.list](https://developers.google.com/workspace/calendar/api/v3/reference/events/list)
- [Google Calendar 사용 제한](https://developers.google.com/workspace/calendar/api/guides/quota)
- [네이버 IMAP 설정](https://help.naver.com/service/30029/contents/21344)
- [네이버 CalDAV](https://help.naver.com/service/5620/contents/2426?lang=ko&osType=COMMONOS)
- [하이웍스 POP3 서버](https://www.hiworks.com/cs/notice/44864)
- [하이웍스 전용 비밀번호](https://customer.gabia.com/manual/hiworks/3461/3512)

로컬 앱 로그인은 다른 AI/대화에 계정 접근 권한을 주지 않으며 이 앱은 AI나 별도 서버로 데이터를 보내지 않습니다.
