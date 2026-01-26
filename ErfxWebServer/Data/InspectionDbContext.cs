using System.Text.Json;
using ErfxWebServer.Models;
using Microsoft.EntityFrameworkCore;

namespace ErfxWebServer.Data;

/// <summary>
/// SQLite 검사 결과 데이터베이스 컨텍스트
/// - BoxInspectionResult 엔티티 매핑
/// - JSON 컬럼 변환 처리
/// - 읽기 전용 (마이그레이션 없음)
/// </summary>
public class InspectionDbContext : DbContext
{
    public InspectionDbContext(DbContextOptions<InspectionDbContext> options)
        : base(options)
    {
    }

    public DbSet<BoxInspectionResult> Inspections { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        var entity = modelBuilder.Entity<BoxInspectionResult>();

        // 테이블명
        entity.ToTable("inspections");

        // 기본 키
        entity.HasKey(e => e.Id);
        entity.Property(e => e.Id)
            .HasColumnName("id")
            .ValueGeneratedOnAdd();

        // 식별 정보
        entity.Property(e => e.CorrelationId)
            .HasColumnName("correlation_id")
            .IsRequired()
            .HasMaxLength(500);

        entity.Property(e => e.InvoiceNumber)
            .HasColumnName("invoice_number")
            .HasMaxLength(100);

        entity.Property(e => e.BarcodeRaw)
            .HasColumnName("barcode_raw")
            .HasMaxLength(500);

        entity.Property(e => e.InspectedAtUtc)
            .HasColumnName("inspected_at_utc")
            .IsRequired()
            .HasColumnType("TEXT")
            .HasConversion(
                v => v.ToString("o"),
                v => DateTime.Parse(v, null, System.Globalization.DateTimeStyles.RoundtripKind)
            );

        // 요약
        entity.Property(e => e.IsOk)
            .HasColumnName("is_ok")
            .IsRequired()
            .HasConversion<int>();

        entity.Property(e => e.ExpectedTotal)
            .HasColumnName("expected_total")
            .IsRequired();

        entity.Property(e => e.ActualTotal)
            .HasColumnName("actual_total")
            .IsRequired();

        entity.Property(e => e.ElapsedMs)
            .HasColumnName("elapsed_ms")
            .IsRequired();

        // JSON 컬럼 - Differences
        entity.Property(e => e.Differences)
            .HasColumnName("differences_json")
            .HasConversion(
                v => JsonSerializer.Serialize(v),
                v => string.IsNullOrEmpty(v)
                    ? new List<SkuDifference>()
                    : JsonSerializer.Deserialize<List<SkuDifference>>(v, (JsonSerializerOptions?)null) ?? new List<SkuDifference>()
            )
            .HasColumnType("TEXT");

        // JSON 컬럼 - ExpectedItems
        entity.Property(e => e.ExpectedItems)
            .HasColumnName("expected_items_json")
            .HasConversion(
                v => JsonSerializer.Serialize(v),
                v => string.IsNullOrEmpty(v)
                    ? new Dictionary<string, int>()
                    : JsonSerializer.Deserialize<Dictionary<string, int>>(v, (JsonSerializerOptions?)null) ?? new Dictionary<string, int>()
            )
            .HasColumnType("TEXT");

        // JSON 컬럼 - ActualItems
        entity.Property(e => e.ActualItems)
            .HasColumnName("actual_items_json")
            .HasConversion(
                v => JsonSerializer.Serialize(v),
                v => string.IsNullOrEmpty(v)
                    ? new Dictionary<string, int>()
                    : JsonSerializer.Deserialize<Dictionary<string, int>>(v, (JsonSerializerOptions?)null) ?? new Dictionary<string, int>()
            )
            .HasColumnType("TEXT");

        // JSON 컬럼 - EpcSkuPairs
        entity.Property(e => e.EpcSkuPairs)
            .HasColumnName("raw_epcs_json")
            .HasConversion(
                v => JsonSerializer.Serialize(v),
                v => string.IsNullOrEmpty(v)
                    ? new List<EpcSkuPair>()
                    : JsonSerializer.Deserialize<List<EpcSkuPair>>(v, (JsonSerializerOptions?)null) ?? new List<EpcSkuPair>()
            )
            .HasColumnType("TEXT");

        // 오류 정보
        entity.Property(e => e.FailedEpcCount)
            .HasColumnName("failed_epc_count")
            .HasDefaultValue(0);

        entity.Property(e => e.ErrorMessage)
            .HasColumnName("error_message")
            .HasMaxLength(2000);

        // 업로드 상태
        entity.Property(e => e.UploadedAtUtc)
            .HasColumnName("uploaded_at_utc")
            .HasColumnType("TEXT")
            .HasConversion(
                v => v.HasValue ? v.Value.ToString("o") : null,
                v => !string.IsNullOrEmpty(v) ? DateTime.Parse(v, null, System.Globalization.DateTimeStyles.RoundtripKind) : null
            );

        entity.Property(e => e.UploadAttempts)
            .HasColumnName("upload_attempts")
            .HasDefaultValue(0);

        entity.Property(e => e.IsUploadPermanentlyFailed)
            .HasColumnName("is_upload_permanently_failed")
            .HasDefaultValue(false)
            .HasConversion<int>();

        // 인덱스
        entity.HasIndex(e => e.CorrelationId)
            .IsUnique()
            .HasDatabaseName("idx_inspections_correlation_id");

        entity.HasIndex(e => e.InvoiceNumber)
            .HasDatabaseName("idx_inspections_invoice");

        entity.HasIndex(e => e.InspectedAtUtc)
            .HasDatabaseName("idx_inspections_date");

        entity.HasIndex(e => e.IsOk)
            .HasDatabaseName("idx_inspections_result");

        entity.HasIndex(e => e.UploadedAtUtc)
            .HasDatabaseName("idx_inspections_uploaded");
    }
}
