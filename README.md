# ERFX WebServer

ERFX WebServer는 **ERFX Inspector** 시스템의 검사 결과를 조회하고 관리할 수 있는 RESTful API 서버입니다. 
검수 자동화 라인의 마지막 단계에서 생성된 데이터를 실시간 또는 과거 이력 기반으로 제공합니다.

## 1. 프로젝트 개요

- **목적**: Inspector가 판정한 바코드-RFID 비교 검사 결과에 대한 외부 조회 인터페이스 제공
- **역할**: ERFX 시스템의 데이터 허브 역할을 수행하며, MES 또는 대시보드 시스템과의 연동을 지원
- **특징**:
  - Inspector의 SQLite DB 직접 연동 (Read-only 권장)
  - 페이징 처리를 통한 대용량 데이터 조회 최적화
  - 통계 API를 통한 일일 검수 현황 제공

## 2. 기술 스택

- **Framework**: ASP.NET Core 10
- **ORM**: Entity Framework Core (EF Core)
- **Database**: SQLite (Inspector DB 공유)
- **Messaging**: MQTTnet (시스템 이벤트 모니터링용)
- **API Documentation**: Swagger (OpenAPI)

## 3. ERFX 시스템 구성

ERFX 시스템은 다음과 같은 프로젝트들로 구성되어 있습니다:

1. **ERFX_PLC_BARCODE**: PLC 제어 및 바코드 스캔 통합 관리
2. **ERFX_BlueBird_FR900**: RFID 태그 정보 수집
3. **ERFX_Inspector**: 바코드와 RFID 정보를 비교하여 OK/NG 판정 및 결과 저장 (SQLite)
4. **ERFX_WebServer**: Inspector DB를 조회하는 REST API 제공 (본 프로젝트)

## 4. API 엔드포인트

| Method | Endpoint | Description |
|--------|----------|-------------|
| **GET** | `/api/inspections` | 전체 검사 결과 목록 조회 (페이징: `page`, `pageSize`) |
| **GET** | `/api/inspections/{id}` | 특정 ID의 검사 상세 결과 조회 |
| **GET** | `/api/inspections/invoice/{invoiceNumber}` | 송장번호(Invoice Number)로 검사 결과 조회 |
| **GET** | `/api/inspections/today` | 오늘(금일) 수행된 검사 결과 목록 조회 |
| **GET** | `/api/inspections/stats` | 검사 통계 정보 조회 (전체/OK/NG 수량 등) |

## 5. 설정 및 실행 방법

### 5.1 데이터베이스 설정

`ErfxWebServer/appsettings.json` 파일에서 Inspector의 DB 경로를 설정합니다.

```json
"ConnectionStrings": {
  "InspectionDb": "Data Source=../ERFX_Inspector_20260125-01/ERFX_Inspector_20260125-01/bin/Debug/data/inspections.db"
}
```

### 5.2 실행 방법

프로젝트 루트 폴더에서 다음 명령어를 실행합니다:

```bash
# 프로젝트 폴더로 이동
cd ErfxWebServer

# 서버 실행
dotnet run
```

실행 후 브라우저에서 `http://localhost:5000/swagger` (또는 설정된 포트)에 접속하여 API 명세를 확인할 수 있습니다.

## 6. ERFX 시스템 연동 설명

본 웹 서버는 **ERFX_Inspector** 프로젝트의 결과 데이터를 활용합니다.

- **데이터 소스**: Inspector가 실시간으로 기록하는 `inspections.db` 파일을 참조합니다.
- **연동 방식**: Inspector가 검사를 완료하고 DB에 Commit하면, WebServer에서 해당 데이터를 API를 통해 즉시 조회할 수 있습니다.
- **MQTT 연동**: `erfx/inspector/result` 등의 토픽을 구독하여 실시간 상태를 모니터링할 수 있도록 설계되었습니다.
