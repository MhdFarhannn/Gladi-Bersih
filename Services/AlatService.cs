using System.Data;
using Dapper;
using Gladi_Bersih.Common;
using Gladi_Bersih.Configuration;
using Gladi_Bersih.DTOs;
using Gladi_Bersih.Infrastructure;
using Gladi_Bersih.Models;
using Microsoft.Extensions.Options;

namespace Gladi_Bersih.Services;

public sealed class AlatService : IAlatService
{
    private const string ImagePathPrefix = "uploads/alat/";
    private static readonly IReadOnlyDictionary<string, string> AllowedImageTypes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".jpg"] = "image/jpeg",
            [".jpeg"] = "image/jpeg",
            [".png"] = "image/png",
            [".webp"] = "image/webp"
        };

    private readonly IDbConnectionFactory _connectionFactory;
    private readonly IActivityLogService _activityLog;
    private readonly string _imageDirectory;
    private readonly long _maxImageSizeBytes;

    public AlatService(
        IDbConnectionFactory connectionFactory,
        IActivityLogService activityLog,
        IWebHostEnvironment environment,
        IOptions<AlatImageSettings> imageSettings)
    {
        _connectionFactory = connectionFactory;
        _activityLog = activityLog;
        var webRootPath = environment.WebRootPath ?? Path.Combine(environment.ContentRootPath, "wwwroot");
        _imageDirectory = Path.Combine(webRootPath, "uploads", "alat");
        _maxImageSizeBytes = imageSettings.Value.MaxSizeBytes;
    }

    public async Task<PagedResult<AlatResponse>> GetAllAsync(
        int page,
        int pageSize,
        string? search,
        CancellationToken cancellationToken)
    {
        var pagination = Pagination.Create(page, pageSize);
        var searchPattern = string.IsNullOrWhiteSpace(search) ? null : $"%{search.Trim()}%";
        const string sql = """
            SELECT COUNT(*)
            FROM alat a
            INNER JOIN kategori k ON k.id = a.id_kategori
            WHERE a.deleted_at IS NULL
              AND k.deleted_at IS NULL
              AND (@Search IS NULL OR a.nama LIKE @Search OR k.nama LIKE @Search);

            SELECT
                a.id,
                a.id_kategori AS IdKategori,
                k.nama AS Kategori,
                a.nama,
                a.deskripsi,
                a.harga_sewa AS HargaSewa,
                a.image_path AS ImagePath,
                COALESCE(SUM(
                    CASE
                        WHEN ua.deleted_at IS NULL AND sk.nama = 'tersedia' THEN 1
                        ELSE 0
                    END
                ), 0) AS StokTersedia,
                a.created_at AS CreatedAt
            FROM alat a
            INNER JOIN kategori k ON k.id = a.id_kategori
            LEFT JOIN unit_alat ua ON ua.id_alat = a.id
            LEFT JOIN status_ketersediaan sk ON sk.id = ua.id_status_ketersediaan
            WHERE a.deleted_at IS NULL
              AND k.deleted_at IS NULL
              AND (@Search IS NULL OR a.nama LIKE @Search OR k.nama LIKE @Search)
            GROUP BY
                a.id,
                a.id_kategori,
                k.nama,
                a.nama,
                a.deskripsi,
                a.harga_sewa,
                a.image_path,
                a.created_at
            ORDER BY a.id DESC
            LIMIT @PageSize OFFSET @Offset;
            """;

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var command = new CommandDefinition(
            sql,
            new { Search = searchPattern, pagination.PageSize, pagination.Offset },
            cancellationToken: cancellationToken);
        using var result = await connection.QueryMultipleAsync(command);
        var total = await result.ReadSingleAsync<long>();
        var items = (await result.ReadAsync<AlatModel>()).Select(ToResponse).ToArray();
        return new PagedResult<AlatResponse>(items, pagination.Page, pagination.PageSize, total);
    }

    public async Task<AlatResponse> GetByIdAsync(uint id, CancellationToken cancellationToken)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        return ToResponse(await GetActiveByIdAsync(connection, id, null, cancellationToken));
    }

    public async Task<AlatResponse> CreateAsync(
        uint actorId,
        AlatRequest request,
        CancellationToken cancellationToken)
    {
        var name = NormalizeName(request.Nama);
        SavedImage? savedImage = null;
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            await EnsureActiveCategoryAsync(
                connection,
                request.IdKategori,
                transaction,
                cancellationToken);
            savedImage = await SaveImageAsync(request.Image, cancellationToken);
            const string sql = """
                INSERT INTO alat (id_kategori, nama, deskripsi, harga_sewa, image_path)
                VALUES (@IdKategori, @Nama, @Deskripsi, @HargaSewa, @ImagePath);

                SELECT LAST_INSERT_ID();
                """;
            var command = new CommandDefinition(
                sql,
                new
                {
                    request.IdKategori,
                    Nama = name,
                    Deskripsi = AuthService.NormalizeOptional(request.Deskripsi),
                    request.HargaSewa,
                    ImagePath = savedImage?.RelativePath
                },
                transaction,
                cancellationToken: cancellationToken);
            var id = checked((uint)await connection.QuerySingleAsync<ulong>(command));
            var item = await GetActiveByIdAsync(connection, id, transaction, cancellationToken);
            await _activityLog.WriteAsync(
                actorId,
                $"Membuat alat '{name}' (ID {id}).",
                transaction,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ToResponse(item);
        }
        catch
        {
            try
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }
            finally
            {
                if (savedImage is not null)
                {
                    DeletePhysicalImage(savedImage.FullPath);
                }
            }

            throw;
        }
    }

    public async Task<AlatResponse> UpdateAsync(
        uint actorId,
        uint id,
        AlatRequest request,
        CancellationToken cancellationToken)
    {
        var name = NormalizeName(request.Nama);
        SavedImage? savedImage = null;
        string? oldImagePath = null;
        AlatModel updatedItem;
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            var existingItem = await GetActiveByIdAsync(connection, id, transaction, cancellationToken, true);
            oldImagePath = existingItem.ImagePath;
            await EnsureActiveCategoryAsync(
                connection,
                request.IdKategori,
                transaction,
                cancellationToken);
            savedImage = await SaveImageAsync(request.Image, cancellationToken);
            const string sql = """
                UPDATE alat
                SET id_kategori = @IdKategori,
                    nama = @Nama,
                    deskripsi = @Deskripsi,
                    harga_sewa = @HargaSewa,
                    image_path = @ImagePath
                WHERE id = @Id
                  AND deleted_at IS NULL;
                """;
            var command = new CommandDefinition(
                sql,
                new
                {
                    Id = id,
                    request.IdKategori,
                    Nama = name,
                    Deskripsi = AuthService.NormalizeOptional(request.Deskripsi),
                    request.HargaSewa,
                    ImagePath = savedImage?.RelativePath ?? existingItem.ImagePath
                },
                transaction,
                cancellationToken: cancellationToken);
            await connection.ExecuteAsync(command);
            updatedItem = await GetActiveByIdAsync(connection, id, transaction, cancellationToken);
            await _activityLog.WriteAsync(
                actorId,
                $"Memperbarui alat '{name}' (ID {id}).",
                transaction,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            try
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }
            finally
            {
                if (savedImage is not null)
                {
                    DeletePhysicalImage(savedImage.FullPath);
                }
            }

            throw;
        }

        if (savedImage is not null)
        {
            DeleteStoredImage(oldImagePath);
        }

        return ToResponse(updatedItem);
    }

    public async Task DeleteAsync(uint actorId, uint id, CancellationToken cancellationToken)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            var item = await GetActiveByIdAsync(connection, id, transaction, cancellationToken, true);
            const string guardSql = """
                SELECT
                    EXISTS(
                        SELECT 1
                        FROM unit_alat ua
                        WHERE ua.id_alat = @Id
                          AND ua.deleted_at IS NULL
                    ) AS HasActiveUnits,
                    EXISTS(
                        SELECT 1
                        FROM detail_peminjaman dp
                        INNER JOIN peminjaman p ON p.id = dp.id_peminjaman
                        INNER JOIN status_peminjaman sp ON sp.id = p.id_status_peminjaman
                        WHERE dp.id_alat = @Id
                          AND p.deleted_at IS NULL
                          AND sp.nama IN ('diajukan', 'disetujui', 'dipinjam')
                    ) AS HasOngoingRentals;
                """;
            var guardCommand = new CommandDefinition(
                guardSql,
                new { Id = id },
                transaction,
                cancellationToken: cancellationToken);
            var guard = await connection.QuerySingleAsync<DeleteGuard>(guardCommand);
            if (guard.HasActiveUnits)
            {
                throw new AppException(
                    StatusCodes.Status409Conflict,
                    "Alat tidak dapat dihapus karena masih memiliki unit aktif.");
            }

            if (guard.HasOngoingRentals)
            {
                throw new AppException(
                    StatusCodes.Status409Conflict,
                    "Alat tidak dapat dihapus karena masih digunakan pada peminjaman berjalan.");
            }

            const string deleteSql = """
                UPDATE alat
                SET deleted_at = NOW()
                WHERE id = @Id
                  AND deleted_at IS NULL;
                """;
            var deleteCommand = new CommandDefinition(
                deleteSql,
                new { Id = id },
                transaction,
                cancellationToken: cancellationToken);
            await connection.ExecuteAsync(deleteCommand);
            await _activityLog.WriteAsync(
                actorId,
                $"Menghapus alat '{item.Nama}' (ID {id}) secara soft delete.",
                transaction,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static string NormalizeName(string value)
    {
        var name = value.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new AppException(StatusCodes.Status400BadRequest, "Nama alat wajib diisi.");
        }

        return name;
    }

    private async Task<SavedImage?> SaveImageAsync(IFormFile? image, CancellationToken cancellationToken)
    {
        if (image is null)
        {
            return null;
        }

        var extension = await ValidateImageAsync(image, cancellationToken);
        Directory.CreateDirectory(_imageDirectory);

        var fileName = $"{Guid.NewGuid():N}{extension}";
        var fullPath = Path.Combine(_imageDirectory, fileName);

        try
        {
            await using var output = new FileStream(
                fullPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                useAsync: true);
            await image.CopyToAsync(output, cancellationToken);
        }
        catch
        {
            DeletePhysicalImage(fullPath);
            throw;
        }

        return new SavedImage($"{ImagePathPrefix}{fileName}", fullPath);
    }

    private async Task<string> ValidateImageAsync(IFormFile image, CancellationToken cancellationToken)
    {
        if (image.Length == 0)
        {
            throw new AppException(StatusCodes.Status400BadRequest, "File gambar tidak boleh kosong.");
        }

        if (image.Length > _maxImageSizeBytes)
        {
            throw new AppException(
                StatusCodes.Status400BadRequest,
                $"Ukuran file gambar maksimal {FormatFileSize(_maxImageSizeBytes)}.");
        }

        var extension = Path.GetExtension(image.FileName).ToLowerInvariant();
        if (!AllowedImageTypes.TryGetValue(extension, out var expectedContentType)
            || !string.Equals(image.ContentType, expectedContentType, StringComparison.OrdinalIgnoreCase))
        {
            throw InvalidImageFormatException();
        }

        var header = new byte[16];
        var bytesRead = 0;
        await using (var input = image.OpenReadStream())
        {
            while (bytesRead < header.Length)
            {
                var read = await input.ReadAsync(header.AsMemory(bytesRead), cancellationToken);
                if (read == 0)
                {
                    break;
                }

                bytesRead += read;
            }
        }

        if (!HasValidMagicBytes(extension, header.AsSpan(0, bytesRead)))
        {
            throw InvalidImageFormatException();
        }

        return extension;
    }

    private static bool HasValidMagicBytes(string extension, ReadOnlySpan<byte> header)
    {
        return extension switch
        {
            ".jpg" or ".jpeg" => header.Length >= 3
                && header[0] == 0xff
                && header[1] == 0xd8
                && header[2] == 0xff,
            ".png" => header.Length >= 8
                && header[0] == 0x89
                && header[1] == 0x50
                && header[2] == 0x4e
                && header[3] == 0x47
                && header[4] == 0x0d
                && header[5] == 0x0a
                && header[6] == 0x1a
                && header[7] == 0x0a,
            ".webp" => header.Length >= 16
                && header[..4].SequenceEqual("RIFF"u8)
                && header.Slice(8, 4).SequenceEqual("WEBP"u8)
                && (header.Slice(12, 4).SequenceEqual("VP8 "u8)
                    || header.Slice(12, 4).SequenceEqual("VP8L"u8)
                    || header.Slice(12, 4).SequenceEqual("VP8X"u8)),
            _ => false
        };
    }

    private static AppException InvalidImageFormatException()
    {
        return new AppException(
            StatusCodes.Status400BadRequest,
            "File gambar harus berformat JPG, JPEG, PNG, atau WEBP dan berisi gambar yang valid.");
    }

    private static string FormatFileSize(long bytes)
    {
        const long megabyte = 1024 * 1024;
        const long kilobyte = 1024;

        if (bytes >= megabyte)
        {
            return $"{bytes / (double)megabyte:0.##} MB";
        }

        return $"{bytes / (double)kilobyte:0.##} KB";
    }

    private void DeleteStoredImage(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)
            || !relativePath.StartsWith(ImagePathPrefix, StringComparison.Ordinal))
        {
            return;
        }

        var fileName = relativePath[ImagePathPrefix.Length..];
        if (string.IsNullOrWhiteSpace(fileName)
            || !string.Equals(fileName, Path.GetFileName(fileName), StringComparison.Ordinal))
        {
            return;
        }

        DeletePhysicalImage(Path.Combine(_imageDirectory, fileName));
    }

    private static void DeletePhysicalImage(string fullPath)
    {
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }
    }

    private static async Task EnsureActiveCategoryAsync(
        IDbConnection connection,
        uint categoryId,
        IDbTransaction transaction,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT EXISTS(
                SELECT 1
                FROM kategori
                WHERE id = @Id
                  AND deleted_at IS NULL
            );
            """;
        var command = new CommandDefinition(
            sql,
            new { Id = categoryId },
            transaction,
            cancellationToken: cancellationToken);
        if (!await connection.ExecuteScalarAsync<bool>(command))
        {
            throw new AppException(StatusCodes.Status400BadRequest, "Kategori aktif tidak ditemukan.");
        }
    }

    private static async Task<AlatModel> GetActiveByIdAsync(
        IDbConnection connection,
        uint id,
        IDbTransaction? transaction,
        CancellationToken cancellationToken,
        bool forUpdate = false)
    {
        var sql = """
            SELECT
                a.id,
                a.id_kategori AS IdKategori,
                k.nama AS Kategori,
                a.nama,
                a.deskripsi,
                a.harga_sewa AS HargaSewa,
                a.image_path AS ImagePath,
                COALESCE(SUM(
                    CASE
                        WHEN ua.deleted_at IS NULL AND sk.nama = 'tersedia' THEN 1
                        ELSE 0
                    END
                ), 0) AS StokTersedia,
                a.created_at AS CreatedAt
            FROM alat a
            INNER JOIN kategori k ON k.id = a.id_kategori
            LEFT JOIN unit_alat ua ON ua.id_alat = a.id
            LEFT JOIN status_ketersediaan sk ON sk.id = ua.id_status_ketersediaan
            WHERE a.id = @Id
              AND a.deleted_at IS NULL
              AND k.deleted_at IS NULL
            GROUP BY
                a.id,
                a.id_kategori,
                k.nama,
                a.nama,
                a.deskripsi,
                a.harga_sewa,
                a.image_path,
                a.created_at
            LIMIT 1
            """ + (forUpdate ? " FOR UPDATE;" : ";");
        var command = new CommandDefinition(sql, new { Id = id }, transaction, cancellationToken: cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<AlatModel>(command)
            ?? throw new AppException(StatusCodes.Status404NotFound, "Alat tidak ditemukan.");
    }

    private static AlatResponse ToResponse(AlatModel model)
    {
        return new AlatResponse(
            model.Id,
            model.IdKategori,
            model.Kategori,
            model.Nama,
            model.Deskripsi,
            model.HargaSewa,
            model.ImagePath,
            model.StokTersedia,
            model.CreatedAt);
    }

    private sealed class DeleteGuard
    {
        public bool HasActiveUnits { get; init; }
        public bool HasOngoingRentals { get; init; }
    }

    private sealed record SavedImage(string RelativePath, string FullPath);
}
