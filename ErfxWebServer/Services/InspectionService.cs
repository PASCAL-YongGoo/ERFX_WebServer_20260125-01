using ErfxWebServer.Data;
using ErfxWebServer.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using ErfxWebServer.Hubs;

namespace ErfxWebServer.Services;

/// <summary>
/// 검사 결과 통계 DTO
/// </summary>
public class InspectionStatistics
{
    public int TotalCount { get; set; }
    public int TodayCount { get; set; }
    public int TodayOkCount { get; set; }
    public int TodayNgCount { get; set; }
    public double OkRate { get; set; }
}

/// <summary>
/// 박스 검사 결과 서비스 인터페이스
/// </summary>
public interface IInspectionService
{
    /// <summary>
    /// 전체 조회 (페이징)
    /// </summary>
    /// <param name="page">페이지 번호 (1부터 시작)</param>
    /// <param name="pageSize">페이지당 항목 수</param>
    Task<List<BoxInspectionResult>> GetAllAsync(int page = 1, int pageSize = 50);

    /// <summary>
    /// ID로 조회
    /// </summary>
    Task<BoxInspectionResult?> GetByIdAsync(long id);

    /// <summary>
    /// 송장번호로 조회
    /// </summary>
    Task<List<BoxInspectionResult>> GetByInvoiceNumberAsync(string invoiceNumber);

    /// <summary>
    /// 오늘 검사 결과 조회 (UTC 기준)
    /// </summary>
    Task<List<BoxInspectionResult>> GetTodayInspectionsAsync();

    /// <summary>
    /// 통계 조회
    /// </summary>
    Task<InspectionStatistics> GetStatisticsAsync();

    /// <summary>
    /// 전체 검사 결과 수 조회 (페이징용)
    /// </summary>
    Task<int> GetTotalCountAsync();

    /// <summary>
    /// 검사 결과 저장 (MQTT 수신 시 호출)
    /// </summary>
    /// <param name="result">저장할 검사 결과</param>
    /// <returns>저장된 검사 결과 (ID 포함)</returns>
    Task<BoxInspectionResult> SaveAsync(BoxInspectionResult result);

    /// <summary>
    /// CorrelationId로 중복 확인
    /// </summary>
    /// <param name="correlationId">검사 식별자</param>
    /// <returns>존재 여부</returns>
    Task<bool> ExistsByCorrelationIdAsync(string correlationId);

    /// <summary>
    /// 테스트용 샘플 데이터 생성
    /// </summary>
    /// <param name="count">생성할 데이터 수</param>
    /// <returns>생성된 데이터 수</returns>
    Task<int> GenerateTestDataAsync(int count = 50);

    /// <summary>
    /// 단일 검사 결과를 시뮬레이션하여 SignalR로 브로드캐스트
    /// </summary>
    /// <param name="forceResult">null이면 85% OK/15% NG 랜덤, true면 강제 OK, false면 강제 NG</param>
    /// <param name="saveToDb">true면 DB에도 저장</param>
    /// <returns>생성된 검사 결과</returns>
    Task<BoxInspectionResult> SimulateInspectionAsync(bool? forceResult = null, bool saveToDb = false);
}

/// <summary>
/// 박스 검사 결과 서비스 구현
/// </summary>
public class InspectionService : IInspectionService
{
    private readonly InspectionDbContext _context;
    private readonly IHubContext<InspectionHub> _hubContext;

    public InspectionService(InspectionDbContext context, IHubContext<InspectionHub> hubContext)
    {
        _context = context;
        _hubContext = hubContext;
    }

    /// <summary>
    /// 전체 조회 (페이징)
    /// </summary>
    public async Task<List<BoxInspectionResult>> GetAllAsync(int page = 1, int pageSize = 50)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 50;
        if (pageSize > 200) pageSize = 200;

        return await _context.Inspections
            .AsNoTracking()
            .OrderByDescending(x => x.InspectedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();
    }

    /// <summary>
    /// ID로 조회
    /// </summary>
    public async Task<BoxInspectionResult?> GetByIdAsync(long id)
    {
        return await _context.Inspections
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id);
    }

    /// <summary>
    /// 송장번호로 조회
    /// </summary>
    public async Task<List<BoxInspectionResult>> GetByInvoiceNumberAsync(string invoiceNumber)
    {
        if (string.IsNullOrWhiteSpace(invoiceNumber))
            return new List<BoxInspectionResult>();

        return await _context.Inspections
            .AsNoTracking()
            .Where(x => x.InvoiceNumber == invoiceNumber)
            .OrderByDescending(x => x.InspectedAtUtc)
            .ToListAsync();
    }

    /// <summary>
    /// 오늘 검사 결과 조회 (UTC 기준)
    /// </summary>
    public async Task<List<BoxInspectionResult>> GetTodayInspectionsAsync()
    {
        var todayUtc = DateTime.UtcNow.Date;
        var tomorrowUtc = todayUtc.AddDays(1);

        return await _context.Inspections
            .AsNoTracking()
            .Where(x => x.InspectedAtUtc >= todayUtc && x.InspectedAtUtc < tomorrowUtc)
            .OrderByDescending(x => x.InspectedAtUtc)
            .ToListAsync();
    }

    /// <summary>
    /// 통계 조회
    /// </summary>
    public async Task<InspectionStatistics> GetStatisticsAsync()
    {
        var todayUtc = DateTime.UtcNow.Date;
        var tomorrowUtc = todayUtc.AddDays(1);

        var totalCount = await _context.Inspections.CountAsync();
        var todayInspections = await _context.Inspections
            .AsNoTracking()
            .Where(x => x.InspectedAtUtc >= todayUtc && x.InspectedAtUtc < tomorrowUtc)
            .ToListAsync();

        var todayCount = todayInspections.Count;
        var todayOkCount = todayInspections.Count(x => x.IsOk);
        var todayNgCount = todayCount - todayOkCount;

        var okRate = totalCount > 0
            ? Math.Round((double)(await _context.Inspections.CountAsync(x => x.IsOk)) / totalCount * 100, 2)
            : 0;

        return new InspectionStatistics
        {
            TotalCount = totalCount,
            TodayCount = todayCount,
            TodayOkCount = todayOkCount,
            TodayNgCount = todayNgCount,
            OkRate = okRate
        };
    }

    /// <summary>
    /// 전체 검사 결과 수 조회 (페이징용)
    /// </summary>
    public async Task<int> GetTotalCountAsync()
    {
        return await _context.Inspections.CountAsync();
    }

    /// <summary>
    /// 검사 결과 저장 (MQTT 수신 시 호출)
    /// </summary>
    public async Task<BoxInspectionResult> SaveAsync(BoxInspectionResult result)
    {
        result.Id = 0;
        _context.Inspections.Add(result);

        try
        {
            await _context.SaveChangesAsync();
            return result;
        }
        catch (DbUpdateException ex) when (ex.InnerException?.Message?.Contains("UNIQUE constraint") == true)
        {
            // CorrelationId가 이미 존재하는 경우 (경쟁 조건)
            // 무시하고 기존 데이터 반환
            _context.Entry(result).State = EntityState.Detached;

            var existing = await _context.Inspections
                .FirstOrDefaultAsync(x => x.CorrelationId == result.CorrelationId);

            return existing ?? result;
        }
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

    /// <summary>
    /// 테스트용 샘플 데이터 생성
    /// </summary>
    public async Task<int> GenerateTestDataAsync(int count = 50)
    {
        var random = new Random();
        var skus = new[] { "SKU-A001", "SKU-B002", "SKU-C003", "SKU-D004", "SKU-E005" };
        var testData = new List<BoxInspectionResult>();

        for (int i = 0; i < count; i++)
        {
            var isOk = random.NextDouble() > 0.15; // 85% OK rate
            var expectedTotal = random.Next(5, 20);
            var actualTotal = isOk ? expectedTotal : expectedTotal + random.Next(-3, 4);
            var invoiceNumber = $"INV-{DateTime.Now:yyyyMMdd}-{random.Next(1000, 9999)}";

            var expectedItems = new Dictionary<string, int>();
            var actualItems = new Dictionary<string, int>();
            var differences = new List<SkuDifference>();
            var epcSkuPairs = new List<EpcSkuPair>();

            // Generate SKU items
            var usedSkus = skus.OrderBy(_ => random.Next()).Take(random.Next(2, 5)).ToList();
            foreach (var sku in usedSkus)
            {
                var expected = random.Next(1, 5);
                var actual = isOk ? expected : expected + random.Next(-1, 2);
                if (actual < 0) actual = 0;

                expectedItems[sku] = expected;
                actualItems[sku] = actual;

                if (expected != actual)
                {
                    differences.Add(new SkuDifference
                    {
                        Sku = sku,
                        Expected = expected,
                        Actual = actual
                    });
                }

                // Generate EPC tags for actual items
                for (int j = 0; j < actual; j++)
                {
                    epcSkuPairs.Add(new EpcSkuPair
                    {
                        Epc = $"E2{random.Next(10000000, 99999999):X8}{random.Next(10000000, 99999999):X8}",
                        Sku = sku
                    });
                }
            }

            var result = new BoxInspectionResult
            {
                CorrelationId = Guid.NewGuid().ToString(),
                InvoiceNumber = invoiceNumber,
                BarcodeRaw = $"BC{random.Next(100000, 999999)}",
                InspectedAtUtc = DateTime.UtcNow.AddMinutes(-random.Next(0, 1440)), // Random within last 24h
                IsOk = isOk,
                ExpectedTotal = expectedItems.Values.Sum(),
                ActualTotal = actualItems.Values.Sum(),
                ElapsedMs = random.Next(100, 2000),
                ExpectedItems = expectedItems,
                ActualItems = actualItems,
                Differences = differences,
                EpcSkuPairs = epcSkuPairs,
                FailedEpcCount = isOk ? 0 : random.Next(0, 3)
            };

            testData.Add(result);
        }

        _context.Inspections.AddRange(testData);
        await _context.SaveChangesAsync();

        return testData.Count;
    }

    /// <summary>
    /// 단일 검사 결과를 시뮬레이션하여 SignalR로 브로드캐스트
    /// 실제 SPAO EPC/SKU 형식 사용
    /// 바코드 형식: 매장코드,region,송장번호,전체수량,SKU1,수량1,SKU2,수량2,...
    /// </summary>
    public async Task<BoxInspectionResult> SimulateInspectionAsync(bool? forceResult = null, bool saveToDb = false)
    {
        var random = new Random();

        // 실제 SPAO SKU 형식 (16자리)
        // 형식: [브랜드2][아이템2][연도1][시즌1][월1][복종1][디자인2][색상2][사이즈3][차수2]
        // 예: SPJDF4TKG1391600
        var realSkus = new[]
        {
            "SPJDF4TKG1391600",  // 청바지
            "SPTHE37G5119075",   // 티셔츠
            "SPCKE12W02390900",  // 체크셔츠
            "SPBNE25W01100950",  // 블라우스
            "SPRWE25C04101050",  // 원피스
            "SPMTE31W02390800",  // 맨투맨
            "SPJKE42W01560900",  // 자켓
            "SPPTE21W02390750"   // 팬츠
        };

        // 실제 SPAO EPC 형식 (32자리 Hex): 850470001940434B3257303200700105
        // 각 SKU에 대응하는 EPC 베이스 (마지막 6자리는 시리얼)
        var epcBases = new Dictionary<string, string>
        {
            ["SPJDF4TKG1391600"] = "850470001940434B32573032007001",
            ["SPTHE37G5119075"] = "850180000281544837473531005008",
            ["SPCKE12W02390900"] = "850470002544434B32573032007002",
            ["SPBNE25W01100950"] = "850280000129424E35573031014015",
            ["SPRWE25C04101050"] = "850270002819527535433034014023",
            ["SPMTE31W02390800"] = "850370002819544D32573032007002",
            ["SPJKE42W01560900"] = "850480001234544A32573031005001",
            ["SPPTE21W02390750"] = "850270003456545032573032009001"
        };

        // 실제 매장코드 예시
        var storeCodes = new[] { "AELS", "BSHN", "CSWD", "DKRG", "EJNP" };

        // OK/NG 결정
        var isOk = forceResult ?? (random.NextDouble() > 0.15);

        // 송장번호: 13자리 숫자 (실제 형식)
        var invoiceNumber = $"{random.NextInt64(1000000000000, 9999999999999)}";
        var storeCode = storeCodes[random.Next(storeCodes.Length)];
        var region = random.Next(1, 4).ToString();

        var expectedItems = new Dictionary<string, int>();
        var actualItems = new Dictionary<string, int>();
        var differences = new List<SkuDifference>();
        var epcSkuPairs = new List<EpcSkuPair>();

        // SKU 선택 (3~5개로 늘려서 일부 OK/일부 NG 시연)
        var usedSkus = realSkus.OrderBy(_ => random.Next()).Take(random.Next(3, 6)).ToList();

        // NG일 경우, 어떤 SKU들이 불일치할지 미리 결정 (일부만 NG)
        var ngSkuIndices = new HashSet<int>();
        if (!isOk)
        {
            // 최소 1개, 최대 절반+1개 SKU가 NG
            var ngCount = random.Next(1, Math.Max(2, usedSkus.Count / 2 + 1));
            while (ngSkuIndices.Count < ngCount)
            {
                ngSkuIndices.Add(random.Next(usedSkus.Count));
            }
        }

        for (int i = 0; i < usedSkus.Count; i++)
        {
            var sku = usedSkus[i];
            var expected = random.Next(2, 6);
            int actual;

            if (isOk)
            {
                // OK: 모든 SKU 일치
                actual = expected;
            }
            else if (ngSkuIndices.Contains(i))
            {
                // NG SKU: 불일치 (반드시 차이 발생)
                var diff = random.Next(1, 3) * (random.Next(2) == 0 ? 1 : -1); // +1~2 또는 -1~-2
                actual = Math.Max(0, expected + diff);
                if (actual == expected) actual = expected - 1; // 여전히 같으면 강제로 1 감소
                if (actual < 0) actual = 0;
            }
            else
            {
                // OK SKU: 일치
                actual = expected;
            }

            // SKU 비교는 15자리 (차수 제외)
            var skuKey = sku.Length >= 15 ? sku.Substring(0, 15) : sku;
            expectedItems[skuKey] = expected;
            actualItems[skuKey] = actual;

            if (expected != actual)
            {
                differences.Add(new SkuDifference
                {
                    Sku = skuKey,
                    Expected = expected,
                    Actual = actual
                });
            }

            // 실제 EPC 형식으로 태그 생성
            var epcBase = epcBases.GetValueOrDefault(sku, "8504700000000000000000000000");
            for (int j = 0; j < actual; j++)
            {
                // EPC 베이스 + 2자리 시리얼 = 32자리
                var serialSuffix = $"{random.Next(0, 99):D2}";
                var fullEpc = (epcBase + serialSuffix).PadRight(32, '0').Substring(0, 32);

                epcSkuPairs.Add(new EpcSkuPair
                {
                    Epc = fullEpc,
                    Sku = skuKey
                });
            }
        }

        // 실제 바코드 형식: 매장코드,region,송장번호,전체수량,SKU1,수량1,SKU2,수량2,...
        // 예: AELS,1,5044252138537,19,SPJDF4TKG1391600,1,...
        var calculatedTotal = expectedItems.Values.Sum();

        // 10% 확률로 바코드 총수량 불일치 시뮬레이션 (송장 프로그램 버그)
        var hasMismatch = random.NextDouble() < 0.1;
        var declaredTotal = hasMismatch ? calculatedTotal + random.Next(1, 4) : calculatedTotal;
        var warningMessage = hasMismatch
            ? $"수량 불일치: 바코드 선언({declaredTotal}) ≠ SKU 합계({calculatedTotal}). SKU 합계를 사용합니다."
            : null;

        var skuQuantityPairs = string.Join(",", expectedItems.Select(kv => $"{kv.Key},{kv.Value}"));
        var barcodeRaw = $"{storeCode},{region},{invoiceNumber},{declaredTotal},{skuQuantityPairs}";

        var result = new BoxInspectionResult
        {
            CorrelationId = $"BOX_{DateTime.UtcNow:yyyyMMdd}_{random.Next(1, 9999):D4}",
            StoreCode = storeCode,
            Region = region,
            InvoiceNumber = invoiceNumber,
            BarcodeRaw = barcodeRaw,
            InspectedAtUtc = DateTime.UtcNow,
            IsOk = isOk,
            ExpectedTotal = calculatedTotal,
            DeclaredTotal = declaredTotal,
            ActualTotal = actualItems.Values.Sum(),
            ElapsedMs = random.Next(80, 500),
            ExpectedItems = expectedItems,
            ActualItems = actualItems,
            Differences = differences,
            EpcSkuPairs = epcSkuPairs,
            FailedEpcCount = isOk ? 0 : random.Next(0, 2),
            WarningMessage = warningMessage
        };

        // SignalR 브로드캐스트
        await _hubContext.Clients.All.SendAsync("InspectionResult", result);

        // 선택적 DB 저장
        if (saveToDb)
        {
            _context.Inspections.Add(result);
            await _context.SaveChangesAsync();
        }

        return result;
    }
}
