# ERFX_WebServer Project Guidelines

## ERFX 시스템 연동 (필독)

이 프로젝트는 **ERFX 통합 시스템**의 일부입니다. 검사 결과 DB 저장 및 웹 대시보드를 담당합니다.

### 공유 문서 위치

```
../ERFX_Integration/
├── README.md                  # 개요
├── docs/Integration_Plan.md   # 연동 계획서
├── docs/Message_Specification.md   # 메시지 포맷 명세
└── docs/Topic_Reference.md         # 토픽 레퍼런스
```

### 관련 프로젝트

| 프로젝트 | 역할 | 경로 |
|----------|------|------|
| ERFX_Inspector | 바코드-RFID 검사 | `../ERFX_Inspector_20260125-01` |
| **ERFX_WebServer** | 웹 대시보드/DB | 현재 프로젝트 |

### 이 프로젝트의 연동 역할

| 기능 | 토픽 | 상태 |
|------|------|:----:|
| 검사 결과 수신 | `erfx/inspector/result` | ✅ 완료 |
| 서버 상태 발행 | `erfx/webserver/status` | ✅ 완료 |

### ⚠️ 메시지 수신 규칙 (중요)

**`erfx/inspector/result` 토픽은 `BoxInspectionResult` 포맷을 기대합니다.**

Inspector가 발행하는 메시지와 이 프로젝트의 `BoxInspectionResult` 모델이 **정확히 일치**해야 합니다.

```csharp
// MqttClientService.cs에서 수신
var result = JsonSerializer.Deserialize<BoxInspectionResult>(payload);
```

**필수 필드 (Inspector와 일치 필요):**
- `correlationId` (string)
- `invoiceNumber` (string)
- `isOk` (bool)
- `expectedTotal`, `actualTotal` (int)
- `expectedItems`, `actualItems` (Dictionary<string, int>)
- `epcSkuPairs` (List<EpcSkuPair>)
- `differences` (List<SkuDifference>)

### JSON 직렬화 규칙

모든 메시지는 **camelCase** 필드명을 사용합니다.

```csharp
public class BoxInspectionResult
{
    [JsonPropertyName("correlationId")]
    public string CorrelationId { get; set; }

    [JsonPropertyName("isOk")]
    public bool IsOk { get; set; }

    [JsonPropertyName("epcSkuPairs")]
    public List<EpcSkuPair> EpcSkuPairs { get; set; }
}
```

### MQTT 설정 표준

| 설정 | 값 | 비고 |
|------|-----|------|
| QoS | 1 | 최소 1회 전달 보장 |
| 브로커 (개발) | 192.168.20.2:1883 | 개발 환경 |
| 브로커 (운영) | 127.0.0.1:1883 | 실제 장비 |

---

## Project Structure

```
ERFX_WebServer_20260125-01/
├── ErfxWebServer/                # ASP.NET Core 애플리케이션
│   ├── Models/                   # 데이터 모델
│   ├── Services/                 # 서비스 레이어
│   │   ├── MqttClientService.cs  # MQTT 구독
│   │   └── InspectionService.cs  # DB 저장
│   ├── Data/                     # EF Core DbContext
│   ├── Pages/                    # Blazor 페이지
│   │   ├── Live.razor            # 실시간 모니터
│   │   └── Inspections.razor     # 검사 결과 목록
│   ├── Hubs/                     # SignalR 허브
│   └── appsettings.json          # 설정 파일
└── docs/                         # 문서
```

## 아키텍처

```
MQTT Broker (erfx/inspector/result)
        │
        ▼
MqttClientService (구독)
        │
        ▼
InspectionService (DB 저장)
        │
        ▼
SignalR Hub (브로드캐스트)
        │
        ▼
Blazor Pages (화면 표시)
```

## Development Guidelines

- Inspector의 `BoxInspectionResult` 모델과 필드명 일치 유지 필수
- 메시지 포맷 변경 시 Inspector와 동시 수정 필요
