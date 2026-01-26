# ERFX WebServer 아키텍처 수정 계획서

## 1. 개요

### 1.1 문서 정보
- **작성일**: 2026-01-26
- **버전**: 1.0
- **대상 프로젝트**: ERFX_WebServer, ERFX_Inspector

### 1.2 현재 문제점

현재 WebServer는 Inspector의 SQLite DB 파일을 **직접 참조**하고 있습니다:

```json
// appsettings.json
"InspectionDb": "Data Source=../ERFX_Inspector_20260125-01/.../inspections.db"
```

**문제점:**
1. **파일 경로 의존성**: Inspector와 WebServer가 같은 머신, 같은 폴더 구조에 있어야 함
2. **파일 잠금 위험**: SQLite 동시 접근 시 잠금 충돌 가능성
3. **배포 제약**: WebServer를 별도 서버에 배포 불가
4. **관심사 분리 위반**: WebServer가 Inspector 내부 구현(DB 경로)에 의존

### 1.3 목표 아키텍처

```
┌─────────────────────────────────────────────────────────────────┐
│                        개선된 아키텍처                            │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│   Inspector (산업용 PC)              WebServer (웹 서버)         │
│   ┌──────────────────┐              ┌──────────────────┐       │
│   │                  │              │                  │       │
│   │  검사 로직        │              │  Blazor UI       │       │
│   │       ↓          │              │       ↑          │       │
│   │  SqliteRepo      │              │  InspectionSvc   │       │
│   │  (로컬 보관)      │              │       ↑          │       │
│   │       ↓          │              │  자체 SQLite DB  │       │
│   │  MQTT Publish    │──── MQTT ───→│  (저장)          │       │
│   │                  │              │       ↓          │       │
│   └──────────────────┘              │  SignalR 브로드   │       │
│                                     │                  │       │
│                                     └──────────────────┘       │
│                                                                 │
│   ✓ Inspector: 독립적으로 동작 (네트워크 없어도 검사 가능)         │
│   ✓ WebServer: MQTT 수신 → DB 저장 → UI 표시                    │
│   ✓ 양쪽 모두 자체 DB 소유 (데이터 중복 허용)                      │
└─────────────────────────────────────────────────────────────────┘
```

---

## 2. 수정 범위

### 2.1 영향받는 컴포넌트

| 컴포넌트 | 수정 유형 | 설명 |
|---------|----------|------|
| `InspectionDbContext.cs` | 수정 | 읽기 전용 → 읽기/쓰기 |
| `InspectionService.cs` | 수정 | 저장 메서드 추가 |
| `MqttClientService.cs` | 수정 | DB 저장 로직 추가 |
| `appsettings.json` | 수정 | DB 경로 변경 (자체 DB) |
| `Program.cs` | 수정 | 서비스 등록 변경 |
| `BoxInspectionResult.cs` | 유지 | 변경 없음 |

### 2.2 영향받지 않는 컴포넌트

| 컴포넌트 | 이유 |
|---------|------|
| `InspectionsController.cs` | IInspectionService 인터페이스 사용 |
| `Live.razor` | SignalR 기반, 변경 불필요 |
| `Inspections.razor` | IInspectionService 인터페이스 사용 |
| `InspectionHub.cs` | 변경 없음 |
| **Inspector 프로젝트 전체** | 변경 없음 |

---

## 3. 상세 수정 내용

### 3.1 Phase 1: 데이터베이스 설정 변경

#### 3.1.1 appsettings.json 수정

**Before:**
```json
{
  "ConnectionStrings": {
    "InspectionDb": "Data Source=../ERFX_Inspector_20260125-01/ERFX_Inspector_20260125-01/bin/Debug/data/inspections.db"
  }
}
```

**After:**
```json
{
  "ConnectionStrings": {
    "InspectionDb": "Data Source=./data/inspections.db"
  },
  "Database": {
    "AutoCreateDirectory": true,
    "RetentionDays": 90
  }
}
```

#### 3.1.2 Program.cs 수정 - DB 디렉토리 자동 생성

**추가할 코드:**
```csharp
// DB 디렉토리 자동 생성
var dbPath = builder.Configuration.GetConnectionString("InspectionDb");
if (!string.IsNullOrEmpty(dbPath))
{
    var match = System.Text.RegularExpressions.Regex.Match(dbPath, @"Data Source=(.+)");
    if (match.Success)
    {
        var filePath = match.Groups[1].Value;
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }
}
```

---

### 3.2 Phase 2: InspectionService 확장

#### 3.2.1 IInspectionService 인터페이스 확장

**추가할 메서드:**
```csharp
public interface IInspectionService
{
    // 기존 메서드들...
    
    /// <summary>
    /// 검사 결과 저장 (MQTT 수신 시 호출)
    /// </summary>
    Task<BoxInspectionResult> SaveAsync(BoxInspectionResult result);
    
    /// <summary>
    /// CorrelationId로 중복 확인
    /// </summary>
    Task<bool> ExistsByCorrelationIdAsync(string correlationId);
}
```

#### 3.2.2 InspectionService 구현

**추가할 메서드:**
```csharp
public class InspectionService : IInspectionService
{
    // 기존 코드...
    
    /// <summary>
    /// 검사 결과 저장
    /// </summary>
    public async Task<BoxInspectionResult> SaveAsync(BoxInspectionResult result)
    {
        // ID가 있으면 Inspector에서 온 것이므로 0으로 리셋
        result.Id = 0;
        
        _context.Inspections.Add(result);
        await _context.SaveChangesAsync();
        
        return result;
    }
    
    /// <summary>
    /// CorrelationId로 중복 확인
    /// </summary>
    public async Task<bool> ExistsByCorrelationIdAsync(string correlationId)
    {
        if (string.IsNullOrEmpty(correlationId))
            return false;
            
        return await _context.Inspections
            .AnyAsync(x => x.CorrelationId == correlationId);
    }
}
```

---

### 3.3 Phase 3: MqttClientService 수정

#### 3.3.1 의존성 추가

```csharp
public class MqttClientService : IHostedService, IDisposable
{
    private readonly ILogger<MqttClientService> _logger;
    private readonly IHubContext<InspectionHub> _hubContext;
    private readonly IConfiguration _configuration;
    private readonly IServiceScopeFactory _scopeFactory;  // 추가
    // ...
    
    public MqttClientService(
        ILogger<MqttClientService> logger,
        IHubContext<InspectionHub> hubContext,
        IConfiguration configuration,
        IServiceScopeFactory scopeFactory)  // 추가
    {
        _logger = logger;
        _hubContext = hubContext;
        _configuration = configuration;
        _scopeFactory = scopeFactory;  // 추가
    }
}
```

#### 3.3.2 OnMessageReceivedAsync 수정

**Before:**
```csharp
private async Task OnMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs e)
{
    var topic = e.ApplicationMessage.Topic;
    var payload = Encoding.UTF8.GetString(e.ApplicationMessage.Payload);

    try
    {
        var result = JsonSerializer.Deserialize<BoxInspectionResult>(payload, ...);

        if (result != null)
        {
            // SignalR로만 전송
            await _hubContext.Clients.All.SendAsync("InspectionResult", result);
        }
    }
    catch (JsonException ex)
    {
        _logger.LogWarning(ex, "Failed to parse inspection result JSON");
    }
}
```

**After:**
```csharp
private async Task OnMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs e)
{
    var topic = e.ApplicationMessage.Topic;
    var payload = Encoding.UTF8.GetString(e.ApplicationMessage.Payload);

    _logger.LogDebug("Received message on {Topic}: {PayloadLength} bytes", topic, payload.Length);

    try
    {
        var result = JsonSerializer.Deserialize<BoxInspectionResult>(payload, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (result != null)
        {
            // 1. DB에 저장 (Scoped 서비스이므로 새 Scope 생성)
            await SaveToDatabase(result);
            
            // 2. SignalR로 브로드캐스트
            await _hubContext.Clients.All.SendAsync("InspectionResult", result);
            
            _logger.LogInformation(
                "Inspection result saved and broadcast: {InvoiceNumber} - {Result}",
                result.InvoiceNumber, result.Result);
        }
    }
    catch (JsonException ex)
    {
        _logger.LogWarning(ex, "Failed to parse inspection result JSON");
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error processing inspection result");
    }
}

private async Task SaveToDatabase(BoxInspectionResult result)
{
    using var scope = _scopeFactory.CreateScope();
    var service = scope.ServiceProvider.GetRequiredService<IInspectionService>();
    
    // 중복 체크 (같은 CorrelationId가 이미 있으면 스킵)
    if (!string.IsNullOrEmpty(result.CorrelationId))
    {
        var exists = await service.ExistsByCorrelationIdAsync(result.CorrelationId);
        if (exists)
        {
            _logger.LogDebug("Skipping duplicate: {CorrelationId}", result.CorrelationId);
            return;
        }
    }
    
    await service.SaveAsync(result);
}
```

---

### 3.4 Phase 4: DB 스키마 자동 생성

#### 3.4.1 Program.cs에 마이그레이션 추가

```csharp
var app = builder.Build();

// DB 스키마 자동 생성 (개발 환경에서만 또는 항상)
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<InspectionDbContext>();
    await context.Database.EnsureCreatedAsync();
}

// 나머지 설정...
```

---

## 4. 마이그레이션 전략

### 4.1 기존 데이터 마이그레이션 (선택사항)

기존 Inspector DB의 데이터를 WebServer로 마이그레이션하려면:

```csharp
// 일회성 마이그레이션 스크립트 (별도 실행)
public class DataMigrationService
{
    public async Task MigrateFromInspectorDb(string inspectorDbPath)
    {
        using var sourceConn = new SqliteConnection($"Data Source={inspectorDbPath}");
        await sourceConn.OpenAsync();
        
        // SELECT all from source
        // INSERT into destination
    }
}
```

### 4.2 권장: 신규 데이터만 수집

- 기존 데이터 마이그레이션 없이 **새로운 검사 결과부터 수집**
- 이유: 과거 데이터는 Inspector에서 조회 가능, 복잡성 감소

---

## 5. 구현 순서

```
┌─────────────────────────────────────────────────────────────┐
│                    구현 단계                                  │
├─────────────────────────────────────────────────────────────┤
│                                                             │
│  Step 1: appsettings.json 수정                              │
│     └── DB 경로를 자체 경로로 변경                             │
│                                                             │
│  Step 2: Program.cs 수정                                    │
│     ├── DB 디렉토리 자동 생성 로직                            │
│     └── EnsureCreatedAsync() 호출                           │
│                                                             │
│  Step 3: IInspectionService 확장                            │
│     └── SaveAsync, ExistsByCorrelationIdAsync 추가          │
│                                                             │
│  Step 4: InspectionService 구현                             │
│     └── 새 메서드 구현                                       │
│                                                             │
│  Step 5: MqttClientService 수정                             │
│     ├── IServiceScopeFactory 주입                           │
│     └── OnMessageReceivedAsync에 저장 로직 추가              │
│                                                             │
│  Step 6: 테스트                                              │
│     ├── MQTT 메시지 수신 → DB 저장 확인                       │
│     ├── 중복 방지 확인 (같은 CorrelationId)                   │
│     └── UI 표시 확인 (Inspections 페이지)                     │
│                                                             │
└─────────────────────────────────────────────────────────────┘
```

---

## 6. 테스트 계획

### 6.1 단위 테스트

| 테스트 케이스 | 검증 내용 |
|-------------|----------|
| `SaveAsync_NewResult_SavesSuccessfully` | 새 결과 저장 |
| `SaveAsync_DuplicateCorrelationId_Skips` | 중복 방지 |
| `GetAllAsync_ReturnsPagedResults` | 페이징 동작 |

### 6.2 통합 테스트

| 시나리오 | 단계 |
|---------|------|
| MQTT → DB 저장 | Inspector 실행 → 검사 수행 → WebServer DB 확인 |
| 실시간 표시 | Live.razor 페이지에서 실시간 업데이트 확인 |
| 이력 조회 | Inspections.razor 페이지에서 저장된 결과 조회 |

### 6.3 장애 시나리오

| 시나리오 | 예상 동작 |
|---------|----------|
| MQTT 브로커 다운 | 자동 재연결, 그 동안 UI에 연결 끊김 표시 |
| WebServer 재시작 | 기존 DB 데이터 유지, MQTT 재연결 |
| Inspector만 실행 | Inspector 정상 동작, WebServer 없어도 검사 가능 |

---

## 7. 롤백 계획

문제 발생 시:

1. `appsettings.json`의 `InspectionDb`를 원래 경로로 복원
2. `MqttClientService`의 저장 로직 주석 처리
3. 기존처럼 Inspector DB 직접 참조로 동작

---

## 8. 예상 소요 시간

| 단계 | 예상 시간 |
|-----|----------|
| Phase 1: 설정 변경 | 15분 |
| Phase 2: Service 확장 | 30분 |
| Phase 3: MQTT 서비스 수정 | 30분 |
| Phase 4: DB 스키마 설정 | 15분 |
| 테스트 | 30분 |
| **총합** | **약 2시간** |

---

## 9. 결론

이 수정을 통해:

1. **Inspector 독립성 보장**: 네트워크 없이도 검사 가능
2. **WebServer 배포 유연성**: 별도 서버 배치 가능
3. **파일 잠금 문제 해결**: 각자 자체 DB 소유
4. **기존 기능 유지**: Inspector 코드 변경 없음

**권장**: 이 계획서를 검토 후 승인되면 순차적으로 구현 진행.
