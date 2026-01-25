# ERFX WebServer Blazor UI 개발 계획서

## 프로젝트 개요
- **목표**: ERFX Inspector 검사 결과를 조회할 수 있는 Blazor Server 웹 대시보드 구현
- **기술 스택**: ASP.NET Core 10, Blazor Server, EF Core, SQLite
- **디자인**: 다크 테마 산업용 모니터링 스타일

## 현재 상태

### ✅ 완료된 작업
1. REST API 백엔드 완성
   - Models: BoxInspectionResult, EpcSkuPair, SkuDifference
   - DbContext: InspectionDbContext (SQLite)
   - Service: IInspectionService (GetAllAsync, GetStatisticsAsync 등)
   - Controller: InspectionsController (5개 엔드포인트)

2. Blazor Server 기본 구조 생성
   - Pages/_Host.cshtml (진입점)
   - App.razor (라우터)
   - _Imports.razor (전역 using - Models/Services 추가됨)
   - Shared/MainLayout.razor (레이아웃)
   - Shared/NavMenu.razor (네비게이션)

### ❌ 미완성 작업
1. CSS 스타일링 (다크 테마)
2. 대시보드 페이지 (통계 표시)
3. 검사 결과 목록 페이지 (페이징 테이블)

## 개발 작업 목록

### Phase 1: CSS 다크 테마 구현
- [ ] **Task 1.1**: `wwwroot/css/site.css` 생성 - 다크 테마 CSS 변수 정의
  - 배경색: `--bg-primary: #1a1a1a`, `--bg-secondary: #2d2d2d`
  - 텍스트색: `--text-primary: #e0e0e0`, `--text-secondary: #a0a0a0`
  - 강조색: `--accent-blue: #4a9eff`, `--success: #4ade80`, `--error: #f87171`
  - Parallelizable: No (단일 파일)

- [ ] **Task 1.2**: `site.css`에 레이아웃 스타일 추가
  - `.page`, `.sidebar`, `main` 레이아웃
  - `.top-row` 헤더 스타일
  - `.content` 컨텐츠 영역
  - Parallelizable: No (Task 1.1 의존)

- [ ] **Task 1.3**: `site.css`에 컴포넌트 스타일 추가
  - `.stats-container` (그리드 레이아웃)
  - `.stat-card` (통계 카드)
  - `.inspection-table` (테이블 스타일)
  - `.result-ok`, `.result-ng` (결과 표시)
  - `.pagination` (페이징 버튼)
  - Parallelizable: No (Task 1.2 의존)

### Phase 2: 대시보드 페이지 구현
- [ ] **Task 2.1**: `Pages/Index.razor` 생성 - 기본 구조
  - `@page "/"` 라우트 설정
  - `@inject IInspectionService` 서비스 주입
  - `PageTitle` 설정
  - Parallelizable: Yes (CSS와 독립적)

- [ ] **Task 2.2**: `Index.razor`에 통계 로딩 로직 추가
  - `OnInitializedAsync()` 메서드 구현
  - `InspectionService.GetStatisticsAsync()` 호출
  - 로딩 상태 처리 (`@if (stats == null)`)
  - Parallelizable: No (Task 2.1 의존)

- [ ] **Task 2.3**: `Index.razor`에 통계 카드 UI 추가
  - 전체 검사 수 카드
  - 오늘 검사 수 카드
  - OK 비율 카드
  - NG 비율 카드
  - Parallelizable: No (Task 2.2 의존)

### Phase 3: 검사 결과 목록 페이지 구현
- [ ] **Task 3.1**: `Pages/Inspections.razor` 생성 - 기본 구조
  - `@page "/inspections"` 라우트 설정
  - `@inject IInspectionService` 서비스 주입
  - `PageTitle` 설정
  - Parallelizable: Yes (대시보드와 독립적)

- [ ] **Task 3.2**: `Inspections.razor`에 데이터 로딩 로직 추가
  - `OnInitializedAsync()` 메서드 구현
  - `LoadInspections()` 메서드 구현
  - `InspectionService.GetAllAsync(page, pageSize)` 호출
  - 페이징 변수 (`currentPage`, `pageSize`)
  - Parallelizable: No (Task 3.1 의존)

- [ ] **Task 3.3**: `Inspections.razor`에 테이블 UI 추가
  - 테이블 헤더 (ID, Invoice Number, Inspection Time, Result)
  - 테이블 행 반복 (`@foreach`)
  - 결과 색상 표시 (OK: 녹색, NG: 빨간색)
  - Parallelizable: No (Task 3.2 의존)

- [ ] **Task 3.4**: `Inspections.razor`에 페이징 컨트롤 추가
  - 이전/다음 버튼
  - 페이지 번호 표시
  - 버튼 클릭 이벤트 핸들러
  - Parallelizable: No (Task 3.3 의존)

### Phase 4: 네비게이션 메뉴 업데이트
- [ ] **Task 4.1**: `Shared/NavMenu.razor` 업데이트
  - Dashboard 링크 확인/수정
  - Inspections 링크 추가
  - 아이콘 추가 (선택사항)
  - Parallelizable: Yes (페이지 구현과 독립적)

### Phase 5: 빌드 및 테스트
- [ ] **Task 5.1**: 빌드 검증
  - `dotnet build` 실행
  - 컴파일 에러 확인 및 수정
  - Parallelizable: No (모든 작업 완료 후)

- [ ] **Task 5.2**: 런타임 테스트
  - `dotnet run` 실행
  - 브라우저에서 `http://localhost:5000` 접속
  - 대시보드 페이지 확인
  - 검사 결과 페이지 확인
  - 페이징 동작 확인
  - Parallelizable: No (Task 5.1 의존)

## 병렬 처리 가능 그룹

### Group A (독립적으로 시작 가능)
- Task 1.1 (CSS 변수)
- Task 2.1 (대시보드 기본 구조)
- Task 3.1 (검사 목록 기본 구조)
- Task 4.1 (네비게이션 메뉴)

### Group B (Phase 1 완료 후)
- Task 2.2, 2.3 (대시보드 로직 및 UI)
- Task 3.2, 3.3, 3.4 (검사 목록 로직 및 UI)

## 예상 소요 시간
- Phase 1 (CSS): 30분
- Phase 2 (대시보드): 30분
- Phase 3 (검사 목록): 45분
- Phase 4 (네비게이션): 10분
- Phase 5 (테스트): 20분
- **총 예상 시간**: 약 2시간 15분

## 성공 기준
1. ✅ `dotnet build` 에러 없이 성공
2. ✅ 대시보드에서 통계 정상 표시
3. ✅ 검사 결과 목록 페이징 정상 동작
4. ✅ 다크 테마 일관성 있게 적용
5. ✅ 반응형 레이아웃 (데스크톱/태블릿)

## 기술적 제약사항
- LSP 도구 사용 금지 (C# 프로젝트에서 OpenCode 크래시 발생)
- 한 번에 하나의 파일만 생성/수정
- 각 단계마다 `dotnet build`로 검증
- 에이전트 작업 결과는 항상 직접 검증 필요

## 다음 단계
1. Task 1.1부터 순차적으로 시작
2. 각 작업 완료 후 TODO 리스트 업데이트
3. 빌드 에러 발생 시 즉시 수정
4. 모든 작업 완료 후 최종 테스트
