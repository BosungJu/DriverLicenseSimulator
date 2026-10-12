# CLAUDE.md

이 프로젝트의 AI 에이전트 공통 지침은 `AGENTS.md`에 있습니다 (Codex와 공유).
지침을 수정할 때는 이 파일이 아니라 `AGENTS.md`를 수정하세요.

@AGENTS.md

## Claude Code 전용 메모

- 이 파일에는 Claude Code에만 해당하는 내용만 추가합니다.

### Codex 연동

- 교차 리뷰: 코드 수정 작업을 마치면 `AGENTS.md`의 "Cross Review" 규칙에 따라 `cross-review` MCP의 `review_with_codex`를 호출합니다. 사용자 요청 없이 자동으로 수행합니다.
- `.mcp.json`의 `codex` 서버(`codex`, `codex-reply` 도구)는 사용자가 Codex에게 작업 위임이나 이어지는 대화를 명시적으로 요청할 때만 사용합니다. 임의로 작업을 넘기지 않습니다.
- `codex` 도구로 넘길 때는 대상 파일, 목적, 제약(AGENTS.md 준수, 커밋 금지)을 프롬프트에 명시하고 `sandbox`는 기본적으로 `read-only`로 요청합니다. 파일 수정이 필요하면 사용자 승인 후 `workspace-write`를 씁니다.
- Codex 결과(리뷰 포함)는 그대로 믿지 말고 직접 확인한 뒤, 무엇을 Codex가 제안했고 무엇을 반영했는지 구분해서 보고합니다.
