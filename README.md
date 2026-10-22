# CCR (Chrome Caption Recorder)

자막을 읽어 txt 파일에 누적 저장하는 Windows 프로그램.

- 버전 0.0.10 · 개발자 kinphw with CLAUDE
- 대상: Windows 10/11 (기본 내장 .NET Framework 4.x, 설치 불필요)

## 자막 출처 (자동 선택)

1. **국회 의사중계 AI 자막**: `[국회 의사중계 열기]`로 연 CCR 전용 크롬 창에서 생중계 플레이어가 열려 있으면,
   크롬 디버깅 포트(127.0.0.1:9223)로 페이지의 자막 칸(`#viewSubtit .incont`)을 직접 읽는다.
   토막(`segarr_*`) 단위, 글자색으로 발언자 1/2·인식 중 구분 → 확정된 토막만 순서대로 저장. 줄 나눔은 환경설정에서 고름: 발언자별(기본: 발언자 바뀌면 빈 줄로 새 문단, 같은 사람은 1분마다 새 줄) / 토막별(토막마다 새 줄). AI 자막엔 마침표도 발언자 이름도 없어(페이지의 의석 배치도도 고정 자리표뿐), 발언자는 "바뀐 지점"만 자동으로 나눈다.
   전용 창은 별도 프로필(`%LOCALAPPDATA%\CCR\browser-profile`)을 쓴다.
2. **크롬 실시간 자막**: 그 밖의 경우 크롬 "실시간 자막" 말풍선을 UI Automation으로 읽는다.
   말풍선은 밀려 올라가므로 저장한 끝부분을 찾아 이어 붙이고, 끝 몇 글자는 잠잠해질 때까지 보류.

## 사용

1. `ccr.exe` 실행 → `[기록 시작]`(새 파일) 또는 `[이어서 기록…]`(기존 파일 골라 그 끝에 계속). 기록 txt를 `ccr.exe`에 끌어다 놓아도 이어서 기록
2. 의사중계: `[국회 의사중계 열기]` → 그 창에서 생중계 선택(영상 재생) / 그 밖: 크롬 설정 → 접근성 → 실시간 자막
3. 기본은 exe 폴더(쓰기 불가면 `문서\CCR`)에 `자막_yyyyMMdd_HHmmss.txt`로 저장. `[환경설정]`에서 저장 폴더, 파일 이름 형식(`{날짜}` `{시각}`), "기록 시작할 때마다 저장 위치·이름 묻기"를 바꿀 수 있다
4. `[환경설정]`: 화면 테마(라이트/다크), 줄 나눔, 저장 폴더·파일 이름. 설정은 `%LOCALAPPDATA%\CCR\settings.txt`에 기억
5. 맨 위 줄: **투명도** 슬라이더(20~100%, 끌거나 휠로 실시간 조절, 기억됨) · **▶ 화면 오른쪽 40%**(지금 모니터 오른쪽 40%를 위아래 꽉 차게 차지하고, 의사중계 창이 열려 있으면 왼쪽 60%로 함께 옮김. 다시 누르면 둘 다 원래 자리로)
6. 연결 안 되면 `[진단 저장]`

`ccr.exe /?` : 버전 정보

## 다른 도구와 연동

`%LOCALAPPDATA%\CCR\current.json` 에 지금 기록 중인 파일을 남긴다.

```json
{"app":"CCR 0.0.10","file":"...\\자막_20261022_140012.txt","recording":true,"source":"webcast","meeting":"정무위원회 국정감사(금융위원회 등)","updated":"2026-10-22T14:00:15"}
```

- `source`: `webcast`(국회 AI 자막) / `chrome-live-caption` / `""`(찾는 중)
- 기록 파일은 UTF-8, 추가만 됨(고쳐 쓰지 않음). `[HH:mm:ss] 문장` 줄, 빈 줄 = 발언자 바뀜, `■ ` 줄 = 회의명
- 따라서 읽는 쪽은 마지막으로 읽은 바이트 위치부터 이어 읽으면 된다. 발언자별 줄 나눔에서는 한 줄을 여러 번에 걸쳐 이어 쓰므로, 줄바꿈으로 끝난 줄까지만 확정으로 본다.
- 파일이 잠깐 잠겨(동기화 폴더·백신·엑셀 등) 쓰기가 실패하면 메모리에 보관했다가 0.4초마다 다시 쓴다(기록은 멈추지 않음, 상태에 ⚠ 표시). 끝낼 때까지 못 쓰면 같은 폴더에 `…_미저장분_HHmmss.txt`로 살려 둔다.
- 이어서 기록하면 `── 이어서 기록 …` 구분선 뒤로 이어지고, 사이트 자막 칸에 남아 있던(이미 파일에 있는) 줄은 건너뛴다.

## 빌드

```bat
build.bat
```

빌드 후 `dist\CCR_v{버전}.zip`(ccr.exe + README.md)을 만든다. 버전은 빌드된 exe의 버전(`ccr.cs`의 `AppInfo.Version`·Assembly 버전 속성)을 따르므로, 버전을 올리면 새 이름의 zip이 생기고 같은 버전으로 다시 빌드하면 그 zip을 덮어쓴다.

시험: `ccr.exe --selftest 출력.txt` (누적 로직), `ccr.exe --selftest --demo` (화면), `ccr.exe --selftest web:초:출력.txt` (의사중계 창만 읽기), `ccr.exe --selftest lock:출력.txt` (파일 잠김 재시도), `ccr.exe --selftest scroll:출력.txt` (자동 스크롤이 마지막 줄을 맨 아래에 두는지), `ccr.exe --autostart`

## 구조

- `ccr.cs` : 전체 소스 (CaptionReader·Accumulator: 크롬 자막 / WebcastSource·SegmentAccumulator: 의사중계 / Writer: 파일 / Pointer: 연동 / Settings: 설정 기억 / MainForm·SettingsForm·AboutForm: 화면)
- `app.manifest` : DPI 인식 설정
- `legacy\` : 초판(PowerShell 배치, 첫 C# 버전)

의사중계 자막 구조는 Assembly Webcast Subtitle Saver 5.0.0(capture.js)과 사이트 openos_util.js를 참고했다.
