# Live Monitor 기능 개선 및 15.6" FHD 최적화

**날짜:** 2026-01-28
**브랜치:** master

---

## 개요

Live Monitor 페이지에 다양한 기능 개선을 적용했습니다:
- 실시간 시뮬레이션 기능 강화 (실제 SPAO 데이터 형식)
- 바코드 총수량 불일치 경고 표시
- SKU별 EPC 목록 접기/펼치기
- 15.6" FHD Android 모니터 가독성 최적화

---

## 변경 파일

| 파일 | 변경 내용 |
|------|-----------|
| `Models/BoxInspectionResult.cs` | `DeclaredTotal`, `WarningMessage`, `HasWarning` 필드 추가 |
| `Services/InspectionService.cs` | 실제 SPAO 형식 시뮬레이션, 바코드 불일치 시뮬레이션 |
| `Pages/Live.razor` | UI 전면 개선, 접기/펼치기, 경고 배너, 대형 폰트 |
| `wwwroot/css/site.css` | 15.6" FHD 터치 최적화 스타일 |

---

## 상세 변경 사항

### 1. 바코드 총수량 불일치 처리

외부 송장 프로그램 버그로 인한 바코드 총수량 ≠ SKU 합계 상황 대응

#### 처리 방식
- **기존:** 불일치 시 파싱 실패 (검사 거부)
- **변경:** SKU 합계 우선 사용 + 경고 표시 (검사 정상 진행)

#### 추가 필드
```csharp
// BoxInspectionResult.cs
public int DeclaredTotal { get; set; }      // 바코드에 선언된 원본 총수량
public string? WarningMessage { get; set; } // 경고 메시지
public bool HasWarning => !string.IsNullOrEmpty(WarningMessage);
```

#### UI 표시
- 경고 배너: 노란색 테두리 + 경고 아이콘
- Expected 필드: `16 (선언: 19)` 형식으로 두 값 표시

---

### 2. 실시간 시뮬레이션 개선

#### 실제 SPAO 데이터 형식 적용

| 항목 | 형식 | 예시 |
|------|------|------|
| SKU | 16자리 (비교 15자리) | `SPJDF4TKG1391600` |
| EPC | 32자리 Hex | `850470001940434B3257303200700105` |
| 바코드 | `매장코드,region,송장번호,총수량,SKU1,수량1,...` | `AELS,1,5044252138537,19,SPJDF4TKG1391600,1,...` |

#### 시뮬레이션 특징
- **OK/NG 비율:** 85% OK, 15% NG
- **바코드 불일치:** 10% 확률로 총수량 불일치 시뮬레이션
- **부분 NG:** 일부 SKU만 불일치 (NG SKU 목록 상단 배치)

---

### 3. SKU별 EPC 목록 접기/펼치기

#### 기본 상태
- **NG SKU:** 펼침 (불일치 내용 즉시 확인)
- **OK SKU:** 접힘 (화면 공간 절약)

#### 기능
- 헤더 클릭으로 토글 (▶/▼ 아이콘)
- 최대 5개 EPC 표시, 나머지는 "+N개 더보기" 링크
- 새 검사 결과 수신 시 펼침 상태 자동 초기화

---

### 4. 15.6" FHD Android 모니터 최적화

터치 인터페이스 및 가독성 개선

#### 폰트 크기 변경

| 요소 | 변경 전 | 변경 후 |
|------|---------|---------|
| 기본 폰트 | 14px | 16px |
| 페이지 타이틀 | 28px | 36px |
| OK/NG 배지 (큰) | 24px | **48px** |
| OK/NG 배지 (작은) | 12px | 16px |
| 상세 값 | 18px | 26px |
| 테이블 셀 | 11-12px | 14-18px |
| EPC 항목 | 11px | 15px |

#### 터치 최적화
- 모든 터치 타겟 최소 **48-56px** 높이
- 시뮬레이션 버튼: `16px 36px` 패딩
- 스크롤바 두께 12px
- `:active` 상태 피드백 추가

---

## 데이터 흐름

### 바코드 불일치 처리 흐름
```
바코드: AELS,1,5044252138537,19,SPJDF4TKG1391600,5,...
                              ↑ 선언: 19

SKU 합계 계산: 5 + 4 + 7 = 16 (불일치!)

결과:
├─ ExpectedTotal = 16 (SKU 합계, 실제 사용)
├─ DeclaredTotal = 19 (바코드 원본, 참고용)
├─ WarningMessage = "수량 불일치: 바코드 선언(19) ≠ SKU 합계(16)"
└─ 검사 정상 진행 ✓
```

---

## 사용 방법

1. 웹서버 실행
   ```bash
   dotnet run --project ErfxWebServer
   ```

2. 브라우저에서 `http://localhost:5000/live` 접속

3. 시뮬레이션 버튼으로 테스트
   - **Random:** 85% OK, 15% NG 랜덤 (10% 바코드 불일치)
   - **OK:** 항상 OK 결과
   - **NG:** 항상 NG 결과 (일부 SKU 불일치)

---

## 참고

- Inspector 측 `BarcodeParser.cs`도 동일하게 수정됨 (불일치 시 경고)
- Inspector에서 발행하는 MQTT 메시지에 `warningMessage`, `declaredTotal` 포함
