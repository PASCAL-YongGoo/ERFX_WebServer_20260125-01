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
}
