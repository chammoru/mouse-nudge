# GitHub 비교와 반영 기록

조사일: 2026-11-01. 각 프로젝트의 공식 README와 릴리스 설명을 확인했습니다. 기능 설명에 대한 비교이며 성능 측정 결과는 아닙니다.

| 프로젝트 | 참고할 장점 | Mouse Nudge 1.2에 반영 |
| --- | --- | --- |
| [Arkane Systems Mouse Jiggler](https://github.com/arkane-systems/mousejiggler/blob/master/README.md) · [3.1.0](https://github.com/arkane-systems/mousejiggler/releases/tag/3.1.0) | 전역 단축키, 알림 영역 조작, 실행 중인 인스턴스를 정지하는 명령 | Ctrl+Shift+J, 키 반복 방지, 숨긴 창의 일시정지·복원·종료 명령 |
| [M-ybeme/Jiggle](https://github.com/M-ybeme/Jiggle/blob/main/README.md) | 실행 시간 설정과 남은 시간, Pause/Resume, 트레이 메뉴 | 선택형 자동 정지 예약, 실제 동작 시간을 기준으로 한 카운트다운, 일시정지 시 시간 보존 |
| [FluAutoClicker](https://github.com/Agzes/FluAutoClicker/blob/next/README.md) | 프로필을 통한 설정 재사용, 전역 단축키와 CLI 제어 | 자주 쓰는 세 가지 프리셋, 저장 전 변경사항 표시와 취소, 일관된 제어 경로 |
| [stevereich/mousejiggler](https://github.com/stevereich/mousejiggler/blob/master/README.md) | 유휴 상태에 작동하고 트레이에서 조작하는 간결한 흐름 | 상태와 주 동작을 먼저 배치하는 화면 구성에 참고 |

## 화면 구성

1. **상태 → 주 동작 → 설정** 순서로 배치했습니다. 앱을 열면 실행 중인지, 언제 움직이는지, 무엇을 눌러야 하는지 먼저 보입니다.
2. **현재 적용**과 **편집 중**을 구분했습니다. 입력값을 바꾸어도 저장·적용 전에는 실제 동작이 바뀌지 않습니다.
3. 프리셋은 숫자를 채우고 **저장·적용**으로 확정합니다. 기존 자동 정지 예약을 덮어쓰지 않습니다.
4. **변경 취소**는 현재 적용 중인 설정으로 돌아갑니다.
5. 동작 중 설정을 바꾸어도 이전 이동을 먼저 되돌린 뒤 새 거리를 사용합니다.
6. 자동 정지는 완료 상태와 재시작 방법을 표시합니다.

각 저장소는 서로 다른 라이선스를 사용합니다. 이번 변경은 기능·사용 흐름을 참고해 기존 코드에 독립적으로 구현했으며 외부 코드·로고·이미지·바이너리를 포함하지 않습니다.

Windows API 구현 참고: [RegisterHotKey](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerhotkey), [UnregisterHotKey](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-unregisterhotkey), [SendInput](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendinput).
