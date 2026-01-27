using ErfxWebServer.Data;
using ErfxWebServer.Models;
using Microsoft.EntityFrameworkCore;

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
}

/// <summary>
/// 박스 검사 결과 서비스 구현
/// </summary>
public class InspectionService : IInspectionService
{
    private readonly InspectionDbContext _context;

    public InspectionService(InspectionDbContext context)
    {
        _context = context;
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
}
