using ErfxWebServer.Services;
using ErfxWebServer.Models;
using Microsoft.AspNetCore.Mvc;

namespace ErfxWebServer.Controllers;

/// <summary>
/// 박스 검사 결과 API 컨트롤러
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class InspectionsController : ControllerBase
{
    private readonly IInspectionService _service;

    /// <summary>
    /// 생성자 - 의존성 주입
    /// </summary>
    public InspectionsController(IInspectionService service)
    {
        _service = service;
    }

    /// <summary>
    /// 전체 검사 결과 조회 (페이징)
    /// </summary>
    /// <param name="page">페이지 번호 (기본값: 1)</param>
    /// <param name="pageSize">페이지당 항목 수 (기본값: 50)</param>
    /// <returns>검사 결과 목록</returns>
    [HttpGet]
    public async Task<ActionResult<List<BoxInspectionResult>>> GetAll(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        var results = await _service.GetAllAsync(page, pageSize);
        return Ok(results);
    }

    /// <summary>
    /// ID로 검사 결과 조회
    /// </summary>
    /// <param name="id">검사 결과 ID</param>
    /// <returns>검사 결과</returns>
    [HttpGet("{id}")]
    public async Task<ActionResult<BoxInspectionResult>> GetById(long id)
    {
        var result = await _service.GetByIdAsync(id);
        if (result == null)
            return NotFound();

        return Ok(result);
    }

    /// <summary>
    /// 송장번호로 검사 결과 조회
    /// </summary>
    /// <param name="invoiceNumber">송장번호</param>
    /// <returns>검사 결과 목록</returns>
    [HttpGet("invoice/{invoiceNumber}")]
    public async Task<ActionResult<List<BoxInspectionResult>>> GetByInvoiceNumber(string invoiceNumber)
    {
        var results = await _service.GetByInvoiceNumberAsync(invoiceNumber);
        return Ok(results);
    }

    /// <summary>
    /// 오늘 검사 결과 조회 (UTC 기준)
    /// </summary>
    /// <returns>오늘의 검사 결과 목록</returns>
    [HttpGet("today")]
    public async Task<ActionResult<List<BoxInspectionResult>>> GetToday()
    {
        var results = await _service.GetTodayInspectionsAsync();
        return Ok(results);
    }

    /// <summary>
    /// 검사 통계 조회
    /// </summary>
    /// <returns>통계 정보</returns>
    [HttpGet("stats")]
    public async Task<ActionResult<InspectionStatistics>> GetStatistics()
    {
        var stats = await _service.GetStatisticsAsync();
        return Ok(stats);
    }
}
