# 프로젝트 작업 지침

한국 전통놀이 VR 체험 프로젝트의 작업 지침이다. 지속적으로 적용할 규칙만 기록하고, 현재 작업 상태는 체크리스트에 기록한다.

## 프로젝트 정보

- 목적: 사방치기, 장구 리듬 등 한국 전통놀이를 VR로 체험한다.
- 엔진: Unity 6000.3.10f1. 작업 전 `ProjectSettings/ProjectVersion.txt`로 실제 버전을 확인한다.
- 주요 패키지: XR Interaction Toolkit 3.3.2, OpenXR 1.16.1, XR Hands 1.7.3, Input System 1.18.0, URP 17.3.0. 기준은 `Packages/manifest.json`이다.
- 주요 디렉터리
  - `Assets/Script`: 게임 C# 스크립트와 사방치기 문서.
  - `Assets/Script/JangGuRhythm`: 장구 모드. 사방치기와 독립적으로 유지한다.
  - `Assets/Scenes`, `Assets/Prefabs`: 씬과 프리팹.
  - `Library`, `Temp`, `Logs`: Unity 생성 폴더. 직접 수정하지 않는다.
- 빌드·검증: 확인된 CLI 빌드나 테스트 명령은 없다. Unity Editor의 컴파일 결과, Console 오류, Play Mode로 확인한다.

## 사방치기 기준 문서

| 문서 | 용도 | 읽어야 하는 상황 |
|---|---|---|
| `Assets/Script/사방치기규칙_구체화.txt` | 세부 사양 기준 | 사방치기 규칙·판정·흐름 작업 전 |
| `Assets/Script/CHECKLIST.md` | 구현 진행 상태와 검증 기록 | 작업 시작, 재개, 주요 단계 완료 시 |
| `Assets/Script/사방치기_프로토타입_구현_가이드.md` | 프로토타입 Unity 작업 순서 | 씬 연결·프로토타입 작업 시 |
| `Assets/한국 전통놀이 VR 체험 기획서.pdf` | 전체 방향과 확장 후보 | 범위 판단이나 확장 기능 검토 시 |

- 충돌하면 사용자 요청, 구체화 규칙, 기획서 순으로 적용한다.
- 사방치기 핵심 코드는 `Assets/Script/Map.cs`, `Assets/Script/SabangStone.cs`, `Assets/Script/SabangGameManager.cs`, `Assets/Script/UIDoor.cs`이다.
- `Assets/Script`의 C# 작업에는 `unity-csharp-script` 스킬(`.claude/skills/unity-csharp-script/SKILL.md`)을 추가로 적용한다.

## 수정 범위와 기존 작업 보호

- 수정 전에 `git status`와 관련 diff로 사용자 변경을 확인하고 보존한다.
- 요청 없이 reset, stash, discard, commit을 실행하지 않는다.
- 관련 없는 씬, 에셋, 사용자 변경을 정리하거나 재작성하지 않는다.
- `Assets` 안의 파일을 이동·삭제할 때는 대응하는 `.meta` 파일을 함께 처리한다.

## 체크리스트와 작업 재개

- 여러 단계이거나 검증 항목을 추적해야 하는 사방치기 작업은 `Assets/Script/CHECKLIST.md`를 갱신한다.
- 수행과 필요한 검증이 끝난 항목만 체크한다. 실패·차단·미실행은 완료로 표시하지 않는다.
- 대화가 요약되거나 작업을 재개하면 이 문서와 체크리스트를 다시 읽고 실제 파일 상태와 비교한다.

## 실패와 불확실성

- 같은 실패를 원인 분석 없이 반복하지 않는다.
- 문서와 실제 구현이 다르면 근거를 확인하고, 의도가 불명확하면 임의로 바꾸지 않고 보고한다.

## 완료 보고

- 코드 변경은 관련 컴파일 또는 Unity Console 오류 확인을 수행한다.
- XR 입력, 물리, UI, Rigidbody, UnityEvent 변경은 컴파일 성공과 Unity Play Mode 확인을 구분해 보고한다.
- 검증할 수 없었던 동작, Inspector 재연결 필요 여부, 프로토타입 한계를 명시한다.
- 한국어 응답의 문장은 마침표, 물음표 또는 느낌표로 끝낸다.
