using System.Text.Json.Serialization;

namespace ErfxWebServer.Models;

/// <summary>
/// EPC-SKU 매핑 쌍
/// </summary>
public class EpcSkuPair
{
    [JsonPropertyName("epc")]
    public string Epc { get; set; } = string.Empty;

    [JsonPropertyName("sku")]
    public string? Sku { get; set; }
}

/// <summary>
/// SKU별 차이 정보
/// </summary>
public class SkuDifference
{
    [JsonPropertyName("sku")]
    public string Sku { get; set; } = string.Empty;

    [JsonPropertyName("expected")]
    public int Expected { get; set; }

    [JsonPropertyName("actual")]
    public int Actual { get; set; }

    [JsonPropertyName("difference")]
    public int Difference => Actual - Expected;

    [JsonPropertyName("type")]
    public string Type => Difference > 0 ? "Over" : (Difference < 0 ? "Under" : "Match");
}

/// <summary>
/// 박스 검사 결과 (DB 저장 및 API 응답용)
/// </summary>
public class BoxInspectionResult
{
    #region 식별 정보

    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("correlationId")]
    public string CorrelationId { get; set; } = string.Empty;

    [JsonPropertyName("invoiceNumber")]
    public string? InvoiceNumber { get; set; }

    [JsonPropertyName("barcodeRaw")]
    public string? BarcodeRaw { get; set; }

    [JsonPropertyName("inspectedAtUtc")]
    public DateTime InspectedAtUtc { get; set; }

    #endregion

    #region 요약

    [JsonPropertyName("isOk")]
    public bool IsOk { get; set; }

    [JsonPropertyName("result")]
    public string Result => IsOk ? "OK" : "NG";

    [JsonPropertyName("expectedTotal")]
    public int ExpectedTotal { get; set; }

    [JsonPropertyName("actualTotal")]
    public int ActualTotal { get; set; }

    [JsonPropertyName("elapsedMs")]
    public long ElapsedMs { get; set; }

    #endregion

    #region 상세

    [JsonPropertyName("differences")]
    public List<SkuDifference> Differences { get; set; } = new();

    [JsonPropertyName("expectedItems")]
    public Dictionary<string, int> ExpectedItems { get; set; } = new();

    [JsonPropertyName("actualItems")]
    public Dictionary<string, int> ActualItems { get; set; } = new();

    [JsonPropertyName("epcSkuPairs")]
    public List<EpcSkuPair> EpcSkuPairs { get; set; } = new();

    #endregion

    #region 오류 정보

    [JsonPropertyName("failedEpcCount")]
    public int FailedEpcCount { get; set; }

    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; set; }

    #endregion

    #region 업로드 상태

    [JsonPropertyName("uploadedAtUtc")]
    public DateTime? UploadedAtUtc { get; set; }

    [JsonPropertyName("isUploaded")]
    public bool IsUploaded => UploadedAtUtc.HasValue;

    [JsonPropertyName("uploadAttempts")]
    public int UploadAttempts { get; set; }

    [JsonPropertyName("isUploadPermanentlyFailed")]
    public bool IsUploadPermanentlyFailed { get; set; }

    #endregion
}
