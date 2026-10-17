using System.Data;
using System.Globalization;
using System.Text;
using ExcelDataReader;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Rasam.Data;
using Rasam.Models.Accounting;
using Rasam.Models.MetaData;
using Rasam.Models.SocialAccount;

namespace Rasam.Controllers
{
    [Authorize(Roles = AppRoles.Admin)]
    public class ImportController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _environment;

        public ImportController(
            ApplicationDbContext context,
            IWebHostEnvironment environment)
        {
            _context = context;
            _environment = environment;
        }

        [HttpGet]
        public IActionResult Menu()
        {
            return View();
        }

        [HttpGet]
        public IActionResult ProfilesImport()
        {
            return View();
        }




        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ProfilesImport(
            IFormFile? file,
            List<IFormFile>? images,
            CancellationToken cancellationToken)
        {
            var errors = new List<string>();
            var copiedFiles = new List<string>();

            var totalRows = 0;
            var validRows = 0;
            var importedCount = 0;
            var duplicateCount = 0;
            var invalidRows = 0;
            var skippedEmptyRows = 0;

            var startedAt = DateTime.Now;

            try
            {
                if (file == null || file.Length == 0 ||
                    !Path.GetExtension(file.FileName).Equals(
                        ".xlsx", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        "لطفاً یک فایل اکسل معتبر با فرمت XLSX انتخاب کنید.");
                }

                if (images == null || images.Count == 0)
                {
                    throw new InvalidDataException(
                        "لطفاً تصاویر را انتخاب کنید.");
                }

                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

                var allowedExtensions = new HashSet<string>(
                    new[] { ".jpg", ".jpeg", ".png", ".webp", ".jfif" },
                    StringComparer.OrdinalIgnoreCase);

                var imageMap = new Dictionary<string, IFormFile>(
                    StringComparer.OrdinalIgnoreCase);

                var duplicateImageNames = new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);

                foreach (var image in images)
                {
                    if (image.Length == 0)
                        continue;

                    var normalizedName = image.FileName.Replace('\\', '/');
                    var imageName = Path.GetFileName(normalizedName);

                    if (string.IsNullOrWhiteSpace(imageName))
                        continue;

                    if (!allowedExtensions.Contains(Path.GetExtension(imageName)))
                        continue;

                    if (!imageMap.TryAdd(imageName, image))
                        duplicateImageNames.Add(imageName);
                }

                foreach (var duplicateName in duplicateImageNames)
                    imageMap.Remove(duplicateName);

                if (imageMap.Count == 0)
                {
                    throw new InvalidDataException(
                        "هیچ تصویر معتبر و قابل استفاده‌ای انتخاب نشده است.");
                }

                using var excelStream = file.OpenReadStream();
                using var reader = ExcelReaderFactory.CreateReader(excelStream);

                var dataSet = reader.AsDataSet(
                    new ExcelDataSetConfiguration
                    {
                        ConfigureDataTable = _ =>
                            new ExcelDataTableConfiguration
                            {
                                UseHeaderRow = true
                            }
                    });

                if (dataSet.Tables.Count == 0)
                {
                    throw new InvalidDataException(
                        "هیچ Sheet قابل خواندنی در اکسل پیدا نشد.");
                }

                var table = dataSet.Tables[0];
                totalRows = table.Rows.Count;

                string[] requiredColumns =
                {
            "Id",
            "UsernameId",
            "PID",
            "FirstName",
            "LastName",
            "NationalCode",
            "Phone",
            "Country",
            "Image",
            "TopicId",
            "Theory",
            "CreateDate",
            "CreateBY",
            "ModifyDate",
            "ModifyBY"
        };

                var missingColumns = requiredColumns
                    .Where(c => !table.Columns.Contains(c))
                    .ToList();

                if (missingColumns.Count > 0)
                {
                    throw new InvalidDataException(
                        "ستون‌های ضروری پیدا نشدند: " +
                        string.Join(", ", missingColumns));
                }

                string GetCell(DataRow row, string column)
                {
                    var value = row[column];

                    if (value == null || value == DBNull.Value)
                        return string.Empty;

                    if (value is DateTime date)
                    {
                        return date.ToString(
                            "yyyy-MM-dd HH:mm:ss",
                            CultureInfo.InvariantCulture);
                    }

                    return Convert.ToString(
                        value,
                        CultureInfo.InvariantCulture)?.Trim() ?? string.Empty;
                }

                int GetRequiredInt(DataRow row, string column)
                {
                    var value = GetCell(row, column);

                    if (string.IsNullOrWhiteSpace(value) ||
                        !int.TryParse(
                            value,
                            NumberStyles.Integer,
                            CultureInfo.InvariantCulture,
                            out var result))
                    {
                        throw new FormatException(
                            $"مقدار ستون {column} باید عدد صحیح معتبر باشد: {value}");
                    }

                    return result;
                }

                int? GetNullableInt(DataRow row, string column)
                {
                    var value = GetCell(row, column);

                    if (string.IsNullOrWhiteSpace(value))
                        return null;

                    if (int.TryParse(
                        value,
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out var result))
                    {
                        return result;
                    }

                    throw new FormatException(
                        $"مقدار ستون {column} عدد صحیح معتبر نیست: {value}");
                }

                string GetImageFileName(string? image)
                {
                    if (string.IsNullOrWhiteSpace(image))
                        return string.Empty;

                    var normalized = image.Trim().Replace('\\', '/');
                    var name = Path.GetFileName(normalized);

                    if (string.IsNullOrWhiteSpace(name) ||
                        name == "." || name == "..")
                    {
                        throw new InvalidDataException(
                            $"نام تصویر معتبر نیست: {image}");
                    }

                    if (!allowedExtensions.Contains(Path.GetExtension(name)))
                    {
                        throw new InvalidDataException(
                            $"پسوند تصویر مجاز نیست: {name}");
                    }

                    return name;
                }

                bool IsEmptyRow(DataRow row)
                {
                    return row.ItemArray.All(v =>
                        v == null ||
                        v == DBNull.Value ||
                        string.IsNullOrWhiteSpace(v.ToString()));
                }

                var profiles = new List<Profile>();
                var seenIds = new HashSet<int>();

                for (var i = 0; i < table.Rows.Count; i++)
                {
                    var row = table.Rows[i];
                    var rowNumber = i + 2;

                    if (IsEmptyRow(row))
                    {
                        skippedEmptyRows++;
                        continue;
                    }

                    try
                    {
                        var id = GetRequiredInt(row, "Id");

                        if (id <= 0)
                        {
                            throw new InvalidDataException(
                                $"Id باید بزرگ‌تر از صفر باشد: {id}");
                        }

                        if (!seenIds.Add(id))
                        {
                            duplicateCount++;
                            errors.Add(
                                $"ردیف {rowNumber}: شناسه Id تکراری است: {id}");
                            continue;
                        }

                        var usernameId = GetCell(row, "UsernameId");

                        var imageName = GetImageFileName(
                            GetCell(row, "Image"));

                        if (!string.IsNullOrWhiteSpace(imageName))
                        {
                            if (duplicateImageNames.Contains(imageName))
                            {
                                throw new InvalidDataException(
                                    $"تصویر {imageName} در فایل‌های انتخابی تکراری است.");
                            }

                            if (!imageMap.ContainsKey(imageName))
                            {
                                throw new FileNotFoundException(
                                    $"تصویر {imageName} پیدا نشد.");
                            }
                        }

                        var profile = new Profile
                        {
                            Id = id,
                            UsernameId = string.IsNullOrWhiteSpace(usernameId)
                                ? null
                                : usernameId,
                            PID = GetCell(row, "PID"),
                            FirstName = GetCell(row, "FirstName"),
                            LastName = GetCell(row, "LastName"),
                            NationalCode = GetCell(row, "NationalCode"),
                            Phone = GetCell(row, "Phone"),
                            Country = GetCell(row, "Country"),
                            Image = imageName,
                            TopicId = GetNullableInt(row, "TopicId"),
                            Theory = GetCell(row, "Theory"),
                            CreateDate = GetCell(row, "CreateDate"),
                            CreateBY = GetCell(row, "CreateBY"),
                            ModifyDate = GetCell(row, "ModifyDate"),
                            ModifyBY = GetCell(row, "ModifyBY")
                        };

                        profiles.Add(profile);
                        validRows++;
                    }
                    catch (Exception ex)
                    {
                        invalidRows++;
                        errors.Add($"ردیف {rowNumber}: {ex.Message}");
                    }
                }

                if (profiles.Count > 0)
                {
                    var ids = profiles.Select(p => p.Id).ToList();

                    var existingIds = new HashSet<int>();

                    foreach (var batch in ids.Distinct().Chunk(1000))
                    {
                        var foundIds = await _context.Profile
                            .AsNoTracking()
                            .Where(p => batch.Contains(p.Id))
                            .Select(p => p.Id)
                            .ToListAsync(cancellationToken);

                        foreach (var id in foundIds)
                            existingIds.Add(id);
                    }

                    var newProfiles = new List<Profile>();

                    foreach (var profile in profiles)
                    {
                        if (existingIds.Contains(profile.Id))
                        {
                            duplicateCount++;
                            errors.Add(
                                $"Id از قبل در دیتابیس وجود دارد: {profile.Id}");
                            continue;
                        }

                        newProfiles.Add(profile);
                    }

                    profiles = newProfiles;
                }

                if (profiles.Count > 0)
                {
                    var webRoot = _environment.WebRootPath;

                    if (string.IsNullOrWhiteSpace(webRoot))
                    {
                        webRoot = Path.Combine(
                            _environment.ContentRootPath,
                            "wwwroot");
                    }

                    // مسیر ذخیره: wwwroot/uploads/{Id}/profile/{imageName}
                    await using var transaction =
                        await _context.Database.BeginTransactionAsync(
                            cancellationToken);

                    try
                    {
                        foreach (var profile in profiles)
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            var imageName = GetImageFileName(profile.Image);

                            if (string.IsNullOrWhiteSpace(imageName))
                            {
                                profile.Image = string.Empty;
                                continue;
                            }

                            var imageFile = imageMap[imageName];

                            var idFolder =
                                profile.Id.ToString(CultureInfo.InvariantCulture);

                            var destinationDirectory = Path.Combine(
                                webRoot,
                                "uploads",
                                idFolder,
                                "profile");

                            Directory.CreateDirectory(destinationDirectory);

                            var destinationPath = Path.Combine(
                                destinationDirectory,
                                imageName);

                            if (System.IO.File.Exists(destinationPath))
                            {
                                throw new IOException(
                                    $"فایل مقصد از قبل وجود دارد: uploads/{idFolder}/profile/{imageName}");
                            }

                            await using (var source = imageFile.OpenReadStream())
                            await using (var destination = new FileStream(
                                destinationPath,
                                FileMode.CreateNew,
                                FileAccess.Write,
                                FileShare.None,
                                81920,
                                useAsync: true))
                            {
                                copiedFiles.Add(destinationPath);

                                await source.CopyToAsync(
                                    destination,
                                    cancellationToken);
                            }

                            profile.Image =
                                $"/uploads/{idFolder}/profile/{imageName}";
                        }

                        var identityInsertEnabled = false;

                        try
                        {
                            await _context.Database.ExecuteSqlRawAsync(
                                "SET IDENTITY_INSERT [dbo].[Profile] ON",
                                cancellationToken);

                            identityInsertEnabled = true;

                            await _context.Profile.AddRangeAsync(
                                profiles,
                                cancellationToken);

                            await _context.SaveChangesAsync(cancellationToken);
                        }
                        finally
                        {
                            if (identityInsertEnabled)
                            {
                                await _context.Database.ExecuteSqlRawAsync(
                                    "SET IDENTITY_INSERT [dbo].[Profile] OFF",
                                    CancellationToken.None);
                            }
                        }

                        await transaction.CommitAsync(cancellationToken);
                        importedCount = profiles.Count;
                    }
                    catch
                    {
                        try
                        {
                            await transaction.RollbackAsync(CancellationToken.None);
                        }
                        catch
                        {
                        }

                        foreach (var path in copiedFiles)
                        {
                            try
                            {
                                if (System.IO.File.Exists(path))
                                    System.IO.File.Delete(path);
                            }
                            catch (Exception cleanupEx)
                            {
                                errors.Add(
                                    $"حذف فایل ناموفق بود: {path} - {cleanupEx.Message}");
                            }
                        }

                        throw;
                    }
                }
            }
            catch (Exception ex)
            {
                errors.Add(
                    "خطای اصلی عملیات: " + ex.GetBaseException().Message);
            }

            var finishedAt = DateTime.Now;
            var report = new StringBuilder();

            report.AppendLine("گزارش نهایی Import پروفایل‌ها");
            report.AppendLine("--------------------------------");
            report.AppendLine(
                $"نام فایل: {Path.GetFileName(file?.FileName ?? "")}");
            report.AppendLine(
                $"زمان شروع: {startedAt:yyyy/MM/dd HH:mm:ss}");
            report.AppendLine(
                $"زمان پایان: {finishedAt:yyyy/MM/dd HH:mm:ss}");
            report.AppendLine(
                $"مدت عملیات: {(finishedAt - startedAt).TotalSeconds:F2} ثانیه");
            report.AppendLine("--------------------------------");
            report.AppendLine($"تعداد ردیف‌های اکسل: {totalRows}");
            report.AppendLine($"تعداد ردیف‌های معتبر: {validRows}");
            report.AppendLine($"تعداد ردیف‌های نامعتبر: {invalidRows}");
            report.AppendLine($"تعداد ردیف‌های خالی: {skippedEmptyRows}");
            report.AppendLine($"تعداد موارد تکراری: {duplicateCount}");
            report.AppendLine($"تعداد رکوردهای ثبت‌شده: {importedCount}");
            report.AppendLine($"تعداد پیام‌های خطا: {errors.Count}");

            report.AppendLine();
            report.AppendLine(
                importedCount > 0
                    ? "نتیجه: عملیات Import پایان یافت."
                    : "نتیجه: هیچ رکورد جدیدی ثبت نشد.");

            if (errors.Count > 0)
            {
                report.AppendLine();
                report.AppendLine("جزئیات خطاها:");

                foreach (var error in errors.Take(100))
                    report.AppendLine("- " + error);

                if (errors.Count > 100)
                {
                    report.AppendLine(
                        $"و {errors.Count - 100} خطای دیگر...");
                }
            }

            TempData["ImportError"] = report.ToString();

            if (importedCount > 0)
            {
                TempData["ImportSuccess"] =
                    $"{importedCount} پروفایل با موفقیت ثبت شد.";
            }

            return RedirectToAction(nameof(ProfilesImport));
        }




        [HttpGet]
        public IActionResult ProfileAttachmentsImport()
        {
            return View();
        }



        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ProfileAttachmentsImport(
            IFormFile? file,
            List<IFormFile>? images,
            CancellationToken cancellationToken)
        {
            var errors = new List<string>();
            var copiedFiles = new List<string>();

            var totalRows = 0;
            var validRows = 0;
            var importedCount = 0;
            var duplicateCount = 0;
            var invalidRows = 0;
            var skippedEmptyRows = 0;

            var startedAt = DateTime.Now;

            try
            {
                // 1. اعتبارسنجی فایل اکسل
                if (file == null ||
                    file.Length == 0 ||
                    !string.Equals(
                        Path.GetExtension(file.FileName),
                        ".xlsx",
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        "لطفاً یک فایل اکسل معتبر با فرمت XLSX انتخاب کنید.");
                }

                // 2. اعتبارسنجی فایل‌های ورودی (تصویر و ویدیو)
                if (images == null || images.Count == 0)
                {
                    throw new InvalidDataException(
                        "لطفاً حداقل یک تصویر یا ویدیو انتخاب کنید.");
                }

                Encoding.RegisterProvider(
                    CodePagesEncodingProvider.Instance);

                // پسوندهای مجاز تصاویر و ویدیوها
                var allowedExtensions = new HashSet<string>(
                    new[]
                    {
                // Images
                ".jpg",
                ".jpeg",
                ".png",
                ".webp",
                ".jfif",

                // Videos
                ".mp4",
                ".webm",
                ".mov",
                ".m4v",
                ".avi"
                    },
                    StringComparer.OrdinalIgnoreCase);

                // 3. ساخت نقشه فایل‌ها بر اساس نام فایل
                var mediaMap = new Dictionary<string, IFormFile>(
                    StringComparer.OrdinalIgnoreCase);

                var duplicateMediaNames = new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);

                foreach (var media in images)
                {
                    if (media == null || media.Length == 0)
                        continue;

                    var normalizedName = media.FileName.Replace('\\', '/');
                    var mediaName = Path.GetFileName(normalizedName);

                    if (string.IsNullOrWhiteSpace(mediaName))
                        continue;

                    var extension = Path.GetExtension(mediaName);

                    if (!allowedExtensions.Contains(extension))
                        continue;

                    if (!mediaMap.TryAdd(mediaName, media))
                    {
                        duplicateMediaNames.Add(mediaName);
                    }
                }

                // حذف نام‌هایی که بیش از یک بار انتخاب شده‌اند
                foreach (var duplicateName in duplicateMediaNames)
                {
                    mediaMap.Remove(duplicateName);
                }

                if (mediaMap.Count == 0)
                {
                    throw new InvalidDataException(
                        "هیچ تصویر یا ویدیوی معتبر و قابل استفاده‌ای انتخاب نشده است.");
                }

                // 4. خواندن فایل اکسل
                using var excelStream = file.OpenReadStream();

                using var reader =
                    ExcelReaderFactory.CreateReader(excelStream);

                var dataSet = reader.AsDataSet(
                    new ExcelDataSetConfiguration
                    {
                        ConfigureDataTable = _ =>
                            new ExcelDataTableConfiguration
                            {
                                UseHeaderRow = true
                            }
                    });

                if (dataSet.Tables.Count == 0)
                {
                    throw new InvalidDataException(
                        "هیچ Sheet قابل خواندنی در اکسل پیدا نشد.");
                }

                var table = dataSet.Tables[0];
                totalRows = table.Rows.Count;

                // ستون‌های ضروری اکسل
                string[] requiredColumns =
                {
            "ProfileId",
            "FilePath"
        };

                var missingColumns = requiredColumns
                    .Where(c => !table.Columns.Contains(c))
                    .ToList();

                if (missingColumns.Count > 0)
                {
                    throw new InvalidDataException(
                        "ستون‌های ضروری در اکسل پیدا نشدند: " +
                        string.Join(", ", missingColumns));
                }

                string GetCell(DataRow row, string column)
                {
                    if (!table.Columns.Contains(column))
                        return string.Empty;

                    var value = row[column];

                    if (value == null || value == DBNull.Value)
                        return string.Empty;

                    if (value is DateTime date)
                    {
                        return date.ToString(
                            "yyyy-MM-dd HH:mm:ss",
                            CultureInfo.InvariantCulture);
                    }

                    return Convert.ToString(
                        value,
                        CultureInfo.InvariantCulture)?.Trim()
                        ?? string.Empty;
                }

                int GetRequiredInt(DataRow row, string column)
                {
                    var value = GetCell(row, column);

                    if (int.TryParse(
                            value,
                            NumberStyles.Integer,
                            CultureInfo.InvariantCulture,
                            out var result) &&
                        result > 0)
                    {
                        return result;
                    }

                    throw new FormatException(
                        $"مقدار ستون {column} معتبر نیست: {value}");
                }

                // نام فایل فقط از ستون FilePath اکسل خوانده می‌شود
                // نمونه: media/1713_0023681081.JPG  ->  1713_0023681081.JPG
                string GetMediaFileName(string? value)
                {
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        throw new InvalidDataException(
                            "مقدار FilePath الزامی است.");
                    }

                    var normalized = value.Trim().Replace('\\', '/');
                    var name = Path.GetFileName(normalized);

                    if (string.IsNullOrWhiteSpace(name) ||
                        name == "." ||
                        name == ".." ||
                        name.IndexOfAny(
                            Path.GetInvalidFileNameChars()) >= 0 ||
                        name.Contains('/') ||
                        name.Contains('\\'))
                    {
                        throw new InvalidDataException(
                            $"نام فایل معتبر نیست: {value}");
                    }

                    if (!allowedExtensions.Contains(
                            Path.GetExtension(name)))
                    {
                        throw new InvalidDataException(
                            $"فرمت تصویر یا ویدیو مجاز نیست: {name}");
                    }

                    return name;
                }

                bool IsEmptyRow(DataRow row)
                {
                    return row.ItemArray.All(v =>
                        v == null ||
                        v == DBNull.Value ||
                        string.IsNullOrWhiteSpace(v.ToString()));
                }

                // 5. اعتبارسنجی ردیف‌های اکسل
                var attachments = new List<ProfileAttachment>();

                var seenRows = new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);

                for (var i = 0; i < table.Rows.Count; i++)
                {
                    var row = table.Rows[i];
                    var rowNumber = i + 2;

                    if (IsEmptyRow(row))
                    {
                        skippedEmptyRows++;
                        continue;
                    }

                    try
                    {
                        var profileId = GetRequiredInt(
                            row, "ProfileId");

                        var mediaName = GetMediaFileName(
                            GetCell(row, "FilePath"));

                        if (duplicateMediaNames.Contains(mediaName))
                        {
                            throw new InvalidDataException(
                                $"فایل {mediaName} در فایل‌های انتخاب‌شده نام تکراری دارد.");
                        }

                        if (!mediaMap.ContainsKey(mediaName))
                        {
                            throw new FileNotFoundException(
                                $"فایل {mediaName} در فایل‌های انتخاب‌شده پیدا نشد.");
                        }

                        var rowKey = $"{profileId}|{mediaName}";

                        if (!seenRows.Add(rowKey))
                        {
                            duplicateCount++;

                            errors.Add(
                                $"ردیف {rowNumber}: ترکیب ProfileId و FilePath تکراری است.");

                            continue;
                        }

                        var attachment = new ProfileAttachment
                        {
                            ProfileId = profileId,
                            FileName = mediaName,
                            FilePath = string.Empty,
                            FileUrl = string.Empty,

                            CreateDate = GetCell(row, "CreateDate"),
                            CreateBY = GetCell(row, "CreateBY")
                        };

                        attachments.Add(attachment);
                        validRows++;
                    }
                    catch (Exception ex)
                    {
                        invalidRows++;

                        errors.Add(
                            $"ردیف {rowNumber}: {ex.GetBaseException().Message}");
                    }
                }

                // 6. بررسی وجود پروفایل‌ها
                if (attachments.Count > 0)
                {
                    var profileIds = attachments
                        .Select(a => a.ProfileId)
                        .Distinct()
                        .ToList();

                    var existingProfileIds = new HashSet<int>();

                    foreach (var batch in profileIds.Chunk(1000))
                    {
                        var found = await _context.Profile
                            .AsNoTracking()
                            .Where(p => batch.Contains(p.Id))
                            .Select(p => p.Id)
                            .ToListAsync(cancellationToken);

                        foreach (var pid in found)
                            existingProfileIds.Add(pid);
                    }

                    var validAttachments = new List<ProfileAttachment>();

                    foreach (var attachment in attachments)
                    {
                        if (!existingProfileIds.Contains(attachment.ProfileId))
                        {
                            invalidRows++;

                            errors.Add(
                                $"پروفایل با Id برابر {attachment.ProfileId} در دیتابیس وجود ندارد.");

                            continue;
                        }

                        validAttachments.Add(attachment);
                    }

                    attachments = validAttachments;
                }

                // 7. جلوگیری از ثبت پیوست تکراری در دیتابیس
                if (attachments.Count > 0)
                {
                    var profileIds = attachments
                        .Select(a => a.ProfileId)
                        .Distinct()
                        .ToList();

                    var fileNames = attachments
                        .Select(a => a.FileName)
                        .Distinct()
                        .ToList();

                    var existingAttachments = await _context
                        .Set<ProfileAttachment>()
                        .AsNoTracking()
                        .Where(a =>
                            profileIds.Contains(a.ProfileId) &&
                            fileNames.Contains(a.FileName))
                        .Select(a => new
                        {
                            a.ProfileId,
                            a.FileName
                        })
                        .ToListAsync(cancellationToken);

                    var existingKeys = existingAttachments
                        .Select(a => $"{a.ProfileId}|{a.FileName}")
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);

                    var newAttachments = new List<ProfileAttachment>();

                    foreach (var attachment in attachments)
                    {
                        var key =
                            $"{attachment.ProfileId}|{attachment.FileName}";

                        if (existingKeys.Contains(key))
                        {
                            duplicateCount++;

                            errors.Add(
                                $"پیوست از قبل وجود دارد: " +
                                $"ProfileId={attachment.ProfileId}, " +
                                $"FileName={attachment.FileName}");

                            continue;
                        }

                        newAttachments.Add(attachment);
                    }

                    attachments = newAttachments;
                }

                // 8. آماده‌سازی مسیر ذخیره فایل‌ها
                if (attachments.Count > 0)
                {
                    var webRoot = _environment.WebRootPath;

                    if (string.IsNullOrWhiteSpace(webRoot))
                    {
                        webRoot = Path.Combine(
                            _environment.ContentRootPath,
                            "wwwroot");
                    }

                    // مسیر ذخیره: wwwroot/uploads/{ProfileId}/attachment/{نام فایل}
                    // 9. کپی فایل‌ها و ثبت اطلاعات در دیتابیس
                    await using var transaction =
                        await _context.Database.BeginTransactionAsync(
                            cancellationToken);

                    try
                    {
                        foreach (var attachment in attachments)
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            var idFolder =
                                attachment.ProfileId.ToString(
                                    CultureInfo.InvariantCulture);

                            var mediaName = attachment.FileName;
                            var mediaFile = mediaMap[mediaName];

                            // مسیر:
                            // wwwroot/uploads/{ProfileId}/attachment/{FileName}
                            var destinationDirectory = Path.Combine(
                                webRoot,
                                "uploads",
                                idFolder,
                                "attachment");

                            Directory.CreateDirectory(destinationDirectory);

                            // اگر نام فایل موجود باشد، نام جدید تولید می‌شود.
                            // نام واقعی فایل در دیتابیس و URL نیز ثبت خواهد شد.
                            var destinationPath = Path.Combine(
                                destinationDirectory,
                                mediaName);

                            var finalFileName = mediaName;

                            if (System.IO.File.Exists(destinationPath))
                            {
                                var nameWithoutExtension =
                                    Path.GetFileNameWithoutExtension(mediaName);

                                var extension = Path.GetExtension(mediaName);

                                var counter = 1;

                                do
                                {
                                    finalFileName =
                                        $"{nameWithoutExtension}_ASCopy{counter}{extension}";

                                    destinationPath = Path.Combine(
                                        destinationDirectory,
                                        finalFileName);

                                    counter++;
                                }
                                while (System.IO.File.Exists(destinationPath));
                            }

                            // FileMode.CreateNew مانع بازنویسی فایل موجود می‌شود.
                            // فایل پیش از شروع کپی به لیست پاک‌سازی اضافه می‌شود.
                            await using (var source =
                                mediaFile.OpenReadStream())
                            {
                                await using var destination = new FileStream(
                                    destinationPath,
                                    FileMode.CreateNew,
                                    FileAccess.Write,
                                    FileShare.None,
                                    81920,
                                    useAsync: true);

                                copiedFiles.Add(destinationPath);

                                await source.CopyToAsync(
                                    destination,
                                    cancellationToken);
                            }

                            // ثبت مسیر فیزیکی و نام واقعی فایل
                            attachment.FileName = finalFileName;
                            attachment.FilePath = destinationPath;

                            // آدرس عمومی فایل
                            attachment.FileUrl =
                                $"/uploads/{idFolder}/attachment/{finalFileName}";
                        }

                        await _context.Set<ProfileAttachment>()
                            .AddRangeAsync(
                                attachments,
                                cancellationToken);

                        await _context.SaveChangesAsync(cancellationToken);

                        await transaction.CommitAsync(cancellationToken);

                        importedCount = attachments.Count;
                    }
                    catch (Exception ex)
                    {
                        try
                        {
                            await transaction.RollbackAsync(
                                CancellationToken.None);
                        }
                        catch (Exception rollbackEx)
                        {
                            errors.Add(
                                "خطا در Rollback دیتابیس: " +
                                rollbackEx.GetBaseException().Message);
                        }

                        // حذف فایل‌هایی که این عملیات ایجاد کرده است
                        foreach (var path in copiedFiles)
                        {
                            try
                            {
                                if (System.IO.File.Exists(path))
                                {
                                    System.IO.File.Delete(path);
                                }
                            }
                            catch (Exception cleanupEx)
                            {
                                errors.Add(
                                    $"حذف فایل ناموفق بود: {path} - " +
                                    cleanupEx.GetBaseException().Message);
                            }
                        }

                        errors.Add(
                            "خطا هنگام کپی فایل یا ذخیره دیتابیس: " +
                            ex.ToString());

                        throw new InvalidOperationException(
                            "عملیات ذخیره‌سازی با خطا مواجه شد.",
                            ex);
                    }
                }
            }
            catch (Exception ex)
            {
                errors.Add(
                    "خطای اصلی عملیات: " + ex.ToString());
            }

            // 10. تولید گزارش نهایی
            var finishedAt = DateTime.Now;
            var report = new StringBuilder();

            report.AppendLine("گزارش نهایی Import پیوست‌های پروفایل");
            report.AppendLine("--------------------------------");
            report.AppendLine(
                $"نام فایل: {Path.GetFileName(file?.FileName ?? "")}");
            report.AppendLine(
                $"زمان شروع: {startedAt:yyyy/MM/dd HH:mm:ss}");
            report.AppendLine(
                $"زمان پایان: {finishedAt:yyyy/MM/dd HH:mm:ss}");
            report.AppendLine(
                $"مدت عملیات: {(finishedAt - startedAt).TotalSeconds:F2} ثانیه");
            report.AppendLine("--------------------------------");
            report.AppendLine($"تعداد ردیف‌های اکسل: {totalRows}");
            report.AppendLine($"تعداد ردیف‌های معتبر: {validRows}");
            report.AppendLine($"تعداد ردیف‌های نامعتبر: {invalidRows}");
            report.AppendLine($"تعداد ردیف‌های خالی: {skippedEmptyRows}");
            report.AppendLine($"تعداد موارد تکراری: {duplicateCount}");
            report.AppendLine($"تعداد پیوست‌های ثبت‌شده: {importedCount}");
            report.AppendLine($"تعداد پیام‌های خطا: {errors.Count}");
            report.AppendLine("--------------------------------");

            report.AppendLine(
                importedCount > 0
                    ? "نتیجه: عملیات Import پایان یافت."
                    : "نتیجه: هیچ پیوست جدیدی ثبت نشد.");

            if (errors.Count > 0)
            {
                report.AppendLine();
                report.AppendLine("جزئیات خطاها:");

                foreach (var error in errors.Take(100))
                {
                    report.AppendLine("- " + error);
                }

                if (errors.Count > 100)
                {
                    report.AppendLine(
                        $"و {errors.Count - 100} خطای دیگر...");
                }
            }

            TempData["ImportError"] = report.ToString();

            if (importedCount > 0)
            {
                TempData["ImportSuccess"] =
                    $"{importedCount} پیوست (تصویر یا ویدیو) با موفقیت ثبت شد.";
            }

            return RedirectToAction(nameof(ProfileAttachmentsImport));
        }

        // =====================================================================
        // Import روابط بین پروفایل‌ها (ProfileRelation)
        // ستون‌های اکسل: Id, FromProfileId, ToProfileId, RelationId
        // =====================================================================
        [HttpGet]
        public IActionResult ProfileRelationsImport()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ProfileRelationsImport(
            IFormFile? file,
            CancellationToken cancellationToken)
        {
            var errors = new List<string>();

            var totalRows = 0;
            var validRows = 0;
            var importedCount = 0;
            var duplicateCount = 0;
            var invalidRows = 0;
            var skippedEmptyRows = 0;

            var startedAt = DateTime.Now;

            try
            {
                if (file == null || file.Length == 0 ||
                    !Path.GetExtension(file.FileName).Equals(
                        ".xlsx", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        "لطفاً یک فایل اکسل معتبر با فرمت XLSX انتخاب کنید.");
                }

                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

                using var excelStream = file.OpenReadStream();
                using var reader = ExcelReaderFactory.CreateReader(excelStream);

                var dataSet = reader.AsDataSet(
                    new ExcelDataSetConfiguration
                    {
                        ConfigureDataTable = _ =>
                            new ExcelDataTableConfiguration
                            {
                                UseHeaderRow = true
                            }
                    });

                if (dataSet.Tables.Count == 0)
                {
                    throw new InvalidDataException(
                        "هیچ Sheet قابل خواندنی در اکسل پیدا نشد.");
                }

                var table = dataSet.Tables[0];
                totalRows = table.Rows.Count;

                string[] requiredColumns =
                {
                    "Id",
                    "FromProfileId",
                    "ToProfileId",
                    "RelationId"
                };

                var missingColumns = requiredColumns
                    .Where(c => !table.Columns.Contains(c))
                    .ToList();

                if (missingColumns.Count > 0)
                {
                    throw new InvalidDataException(
                        "ستون‌های ضروری پیدا نشدند: " +
                        string.Join(", ", missingColumns));
                }

                string GetCell(DataRow row, string column)
                {
                    var value = row[column];

                    if (value == null || value == DBNull.Value)
                        return string.Empty;

                    // اعداد اکسل ممکن است به شکل double بیایند (مثلاً 724)
                    if (value is double d)
                        return d.ToString("0.#", CultureInfo.InvariantCulture);

                    return Convert.ToString(
                        value,
                        CultureInfo.InvariantCulture)?.Trim() ?? string.Empty;
                }

                int GetPositiveInt(DataRow row, string column)
                {
                    var value = GetCell(row, column);

                    if (int.TryParse(
                            value,
                            NumberStyles.Integer,
                            CultureInfo.InvariantCulture,
                            out var result) &&
                        result > 0)
                    {
                        return result;
                    }

                    throw new FormatException(
                        $"مقدار ستون {column} باید عدد صحیح بزرگ‌تر از صفر باشد: {value}");
                }

                bool IsEmptyRow(DataRow row)
                {
                    return row.ItemArray.All(v =>
                        v == null ||
                        v == DBNull.Value ||
                        string.IsNullOrWhiteSpace(v.ToString()));
                }

                // 1. اعتبارسنجی ردیف‌های اکسل
                var relations = new List<ProfileRelation>();
                var seenIds = new HashSet<int>();
                var seenKeys = new HashSet<string>();

                for (var i = 0; i < table.Rows.Count; i++)
                {
                    var row = table.Rows[i];
                    var rowNumber = i + 2;

                    if (IsEmptyRow(row))
                    {
                        skippedEmptyRows++;
                        continue;
                    }

                    try
                    {
                        var id = GetPositiveInt(row, "Id");
                        var fromId = GetPositiveInt(row, "FromProfileId");
                        var toId = GetPositiveInt(row, "ToProfileId");
                        var relationId = GetPositiveInt(row, "RelationId");

                        if (fromId == toId)
                        {
                            throw new InvalidDataException(
                                $"FromProfileId و ToProfileId نباید یکسان باشند: {fromId}");
                        }

                        if (!seenIds.Add(id))
                        {
                            duplicateCount++;
                            errors.Add(
                                $"ردیف {rowNumber}: شناسه Id تکراری است: {id}");
                            continue;
                        }

                        var key = $"{fromId}|{toId}|{relationId}";

                        if (!seenKeys.Add(key))
                        {
                            duplicateCount++;
                            errors.Add(
                                $"ردیف {rowNumber}: ترکیب From/To/Relation تکراری است: {key}");
                            continue;
                        }

                        relations.Add(new ProfileRelation
                        {
                            Id = id,
                            FromProfileId = fromId,
                            ToProfileId = toId,
                            RelationId = relationId
                        });

                        validRows++;
                    }
                    catch (Exception ex)
                    {
                        invalidRows++;
                        errors.Add($"ردیف {rowNumber}: {ex.Message}");
                    }
                }

                // 2. وجود پروفایل‌ها و Relation در دیتابیس
                if (relations.Count > 0)
                {
                    var profileIds = relations
                        .SelectMany(r => new[] { r.FromProfileId, r.ToProfileId })
                        .Distinct()
                        .ToList();

                    var existingProfileIds = new HashSet<int>();

                    foreach (var batch in profileIds.Chunk(1000))
                    {
                        var found = await _context.Profile
                            .AsNoTracking()
                            .Where(p => batch.Contains(p.Id))
                            .Select(p => p.Id)
                            .ToListAsync(cancellationToken);

                        foreach (var pid in found)
                            existingProfileIds.Add(pid);
                    }

                    var relationIds = relations
                        .Select(r => r.RelationId)
                        .Distinct()
                        .ToList();

                    var existingRelationIds = new HashSet<int>();

                    foreach (var batch in relationIds.Chunk(1000))
                    {
                        var found = await _context.Set<Relation>()
                            .AsNoTracking()
                            .Where(r => batch.Contains(r.Id))
                            .Select(r => r.Id)
                            .ToListAsync(cancellationToken);

                        foreach (var rid in found)
                            existingRelationIds.Add(rid);
                    }

                    var checkedRelations = new List<ProfileRelation>();

                    foreach (var r in relations)
                    {
                        if (!existingProfileIds.Contains(r.FromProfileId))
                        {
                            invalidRows++;
                            errors.Add(
                                $"Id={r.Id}: پروفایل مبدأ (FromProfileId={r.FromProfileId}) در دیتابیس وجود ندارد.");
                            continue;
                        }

                        if (!existingProfileIds.Contains(r.ToProfileId))
                        {
                            invalidRows++;
                            errors.Add(
                                $"Id={r.Id}: پروفایل مقصد (ToProfileId={r.ToProfileId}) در دیتابیس وجود ندارد.");
                            continue;
                        }

                        if (!existingRelationIds.Contains(r.RelationId))
                        {
                            invalidRows++;
                            errors.Add(
                                $"Id={r.Id}: نوع رابطه (RelationId={r.RelationId}) در دیتابیس وجود ندارد.");
                            continue;
                        }

                        checkedRelations.Add(r);
                    }

                    relations = checkedRelations;
                }

                // 3. جلوگیری از ثبت تکراری (Id یا ترکیب From/To/Relation)
                if (relations.Count > 0)
                {
                    var existingIds = new HashSet<int>();
                    var existingKeys = new HashSet<string>();

                    foreach (var batch in relations
                                 .Select(r => r.Id).Distinct().Chunk(1000))
                    {
                        var found = await _context.Set<ProfileRelation>()
                            .AsNoTracking()
                            .Where(r => batch.Contains(r.Id))
                            .Select(r => r.Id)
                            .ToListAsync(cancellationToken);

                        foreach (var id in found)
                            existingIds.Add(id);
                    }

                    foreach (var batch in relations
                                 .Select(r => r.FromProfileId).Distinct().Chunk(1000))
                    {
                        var found = await _context.Set<ProfileRelation>()
                            .AsNoTracking()
                            .Where(r => batch.Contains(r.FromProfileId))
                            .Select(r => new
                            {
                                r.FromProfileId,
                                r.ToProfileId,
                                r.RelationId
                            })
                            .ToListAsync(cancellationToken);

                        foreach (var f in found)
                        {
                            existingKeys.Add(
                                $"{f.FromProfileId}|{f.ToProfileId}|{f.RelationId}");
                        }
                    }

                    var newRelations = new List<ProfileRelation>();

                    foreach (var r in relations)
                    {
                        if (existingIds.Contains(r.Id))
                        {
                            duplicateCount++;
                            errors.Add(
                                $"Id از قبل در دیتابیس وجود دارد: {r.Id}");
                            continue;
                        }

                        var key =
                            $"{r.FromProfileId}|{r.ToProfileId}|{r.RelationId}";

                        if (existingKeys.Contains(key))
                        {
                            duplicateCount++;
                            errors.Add(
                                $"Id={r.Id}: این رابطه از قبل ثبت شده است ({key}).");
                            continue;
                        }

                        newRelations.Add(r);
                    }

                    relations = newRelations;
                }

                // 4. ثبت در دیتابیس (با حفظ Id اکسل)
                if (relations.Count > 0)
                {
                    var entityType =
                        _context.Model.FindEntityType(typeof(ProfileRelation))
                        ?? throw new InvalidOperationException(
                            "مدل ProfileRelation در DbContext پیدا نشد.");

                    var tableName = entityType.GetTableName();
                    var schema = entityType.GetSchema() ?? "dbo";

                    var qualifiedTable =
                        $"[{schema.Replace("]", "]]")}].[{tableName!.Replace("]", "]]")}]";

                    await using var transaction =
                        await _context.Database.BeginTransactionAsync(
                            cancellationToken);

                    try
                    {
                        var identityInsertEnabled = false;

                        try
                        {
#pragma warning disable EF1002
                            await _context.Database.ExecuteSqlRawAsync(
                                $"SET IDENTITY_INSERT {qualifiedTable} ON",
                                cancellationToken);
#pragma warning restore EF1002

                            identityInsertEnabled = true;

                            await _context.Set<ProfileRelation>().AddRangeAsync(
                                relations,
                                cancellationToken);

                            await _context.SaveChangesAsync(cancellationToken);
                        }
                        finally
                        {
                            if (identityInsertEnabled)
                            {
#pragma warning disable EF1002
                                await _context.Database.ExecuteSqlRawAsync(
                                    $"SET IDENTITY_INSERT {qualifiedTable} OFF",
                                    CancellationToken.None);
#pragma warning restore EF1002
                            }
                        }

                        await transaction.CommitAsync(cancellationToken);
                        importedCount = relations.Count;
                    }
                    catch
                    {
                        try
                        {
                            await transaction.RollbackAsync(CancellationToken.None);
                        }
                        catch
                        {
                        }

                        throw;
                    }
                }
            }
            catch (Exception ex)
            {
                errors.Add(
                    "خطای اصلی عملیات: " + ex.GetBaseException().Message);
            }

            var finishedAt = DateTime.Now;
            var report = new StringBuilder();

            report.AppendLine("گزارش نهایی Import روابط پروفایل‌ها");
            report.AppendLine("--------------------------------");
            report.AppendLine(
                $"نام فایل: {Path.GetFileName(file?.FileName ?? "")}");
            report.AppendLine(
                $"زمان شروع: {startedAt:yyyy/MM/dd HH:mm:ss}");
            report.AppendLine(
                $"زمان پایان: {finishedAt:yyyy/MM/dd HH:mm:ss}");
            report.AppendLine(
                $"مدت عملیات: {(finishedAt - startedAt).TotalSeconds:F2} ثانیه");
            report.AppendLine("--------------------------------");
            report.AppendLine($"تعداد ردیف‌های اکسل: {totalRows}");
            report.AppendLine($"تعداد ردیف‌های معتبر: {validRows}");
            report.AppendLine($"تعداد ردیف‌های نامعتبر: {invalidRows}");
            report.AppendLine($"تعداد ردیف‌های خالی: {skippedEmptyRows}");
            report.AppendLine($"تعداد موارد تکراری: {duplicateCount}");
            report.AppendLine($"تعداد روابط ثبت‌شده: {importedCount}");
            report.AppendLine($"تعداد پیام‌های خطا: {errors.Count}");
            report.AppendLine("--------------------------------");

            report.AppendLine(
                importedCount > 0
                    ? "نتیجه: عملیات Import پایان یافت."
                    : "نتیجه: هیچ رابطه جدیدی ثبت نشد.");

            if (errors.Count > 0)
            {
                report.AppendLine();
                report.AppendLine("جزئیات خطاها:");

                foreach (var error in errors.Take(100))
                    report.AppendLine("- " + error);

                if (errors.Count > 100)
                {
                    report.AppendLine(
                        $"و {errors.Count - 100} خطای دیگر...");
                }
            }

            TempData["ImportError"] = report.ToString();

            if (importedCount > 0)
            {
                TempData["ImportSuccess"] =
                    $"{importedCount} رابطه با موفقیت ثبت شد.";
            }

            return RedirectToAction(nameof(ProfileRelationsImport));
        }

        // =====================================================================
        // Import شبکه‌های اجتماعی پروفایل‌ها (SocialMedia)
        // ستون‌های اکسل: Name, Username, ProfileId  (نام Profiled هم پذیرفته می‌شود)
        // Id خودکار تولید می‌شود و در اکسل لازم نیست.
        // =====================================================================
        [HttpGet]
        public IActionResult SocialMediaImport()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SocialMediaImport(
            IFormFile? file,
            CancellationToken cancellationToken)
        {
            var errors = new List<string>();

            var totalRows = 0;
            var validRows = 0;
            var importedCount = 0;
            var duplicateCount = 0;
            var invalidRows = 0;
            var skippedEmptyRows = 0;

            var startedAt = DateTime.Now;

            try
            {
                if (file == null || file.Length == 0 ||
                    !Path.GetExtension(file.FileName).Equals(
                        ".xlsx", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        "لطفاً یک فایل اکسل معتبر با فرمت XLSX انتخاب کنید.");
                }

                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

                using var excelStream = file.OpenReadStream();
                using var reader = ExcelReaderFactory.CreateReader(excelStream);

                var dataSet = reader.AsDataSet(
                    new ExcelDataSetConfiguration
                    {
                        ConfigureDataTable = _ =>
                            new ExcelDataTableConfiguration
                            {
                                UseHeaderRow = true
                            }
                    });

                if (dataSet.Tables.Count == 0)
                {
                    throw new InvalidDataException(
                        "هیچ Sheet قابل خواندنی در اکسل پیدا نشد.");
                }

                var table = dataSet.Tables[0];
                totalRows = table.Rows.Count;

                // ستون پروفایل: ProfileId یا (املای اکسل نمونه) Profiled
                var profileColumn =
                    table.Columns.Contains("ProfileId") ? "ProfileId" :
                    table.Columns.Contains("Profiled") ? "Profiled" :
                    null;

                var missingColumns = new List<string>();

                if (!table.Columns.Contains("Name"))
                    missingColumns.Add("Name");

                if (!table.Columns.Contains("Username"))
                    missingColumns.Add("Username");

                if (profileColumn == null)
                    missingColumns.Add("ProfileId");

                if (missingColumns.Count > 0)
                {
                    throw new InvalidDataException(
                        "ستون‌های ضروری پیدا نشدند: " +
                        string.Join(", ", missingColumns));
                }

                string GetCell(DataRow row, string column)
                {
                    var value = row[column];

                    if (value == null || value == DBNull.Value)
                        return string.Empty;

                    if (value is double d)
                        return d.ToString("0.#", CultureInfo.InvariantCulture);

                    return Convert.ToString(
                        value,
                        CultureInfo.InvariantCulture)?.Trim() ?? string.Empty;
                }

                bool IsEmptyRow(DataRow row)
                {
                    return row.ItemArray.All(v =>
                        v == null ||
                        v == DBNull.Value ||
                        string.IsNullOrWhiteSpace(v.ToString()));
                }

                // 1. اعتبارسنجی ردیف‌های اکسل
                var rows = new List<(int ProfileId, string Name, string Username)>();
                var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                for (var i = 0; i < table.Rows.Count; i++)
                {
                    var row = table.Rows[i];
                    var rowNumber = i + 2;

                    if (IsEmptyRow(row))
                    {
                        skippedEmptyRows++;
                        continue;
                    }

                    try
                    {
                        var profileIdText = GetCell(row, profileColumn!);

                        if (!int.TryParse(
                                profileIdText,
                                NumberStyles.Integer,
                                CultureInfo.InvariantCulture,
                                out var profileId) ||
                            profileId <= 0)
                        {
                            throw new FormatException(
                                $"مقدار {profileColumn} باید عدد صحیح بزرگ‌تر از صفر باشد: {profileIdText}");
                        }

                        var name = GetCell(row, "Name");
                        var username = GetCell(row, "Username");

                        if (string.IsNullOrWhiteSpace(name))
                            throw new InvalidDataException("نام شبکه اجتماعی الزامی است.");

                        if (string.IsNullOrWhiteSpace(username))
                            throw new InvalidDataException("حساب کاربری الزامی است.");

                        if (name.Length > 100)
                            throw new InvalidDataException("نام شبکه اجتماعی بیش از ۱۰۰ کاراکتر است.");

                        if (username.Length > 100)
                            throw new InvalidDataException("حساب کاربری بیش از ۱۰۰ کاراکتر است.");

                        var key = $"{profileId}|{name}|{username}";

                        if (!seenKeys.Add(key))
                        {
                            duplicateCount++;
                            errors.Add(
                                $"ردیف {rowNumber}: ردیف تکراری در اکسل ({name} / {username} / پروفایل {profileId}).");
                            continue;
                        }

                        rows.Add((profileId, name, username));
                        validRows++;
                    }
                    catch (Exception ex)
                    {
                        invalidRows++;
                        errors.Add($"ردیف {rowNumber}: {ex.Message}");
                    }
                }

                // 2. بارگذاری پروفایل‌ها (با شبکه‌های فعلی) و افزودن از طریق Navigation
                //    تا نیازی به دانستن نام کلید خارجی نباشد.
                if (rows.Count > 0)
                {
                    var profileIds = rows
                        .Select(r => r.ProfileId)
                        .Distinct()
                        .ToList();

                    var profileMap = new Dictionary<int, Profile>();

                    foreach (var batch in profileIds.Chunk(1000))
                    {
                        var found = await _context.Profile
                            .Include(p => p.SocialMedia)
                            .Where(p => batch.Contains(p.Id))
                            .ToListAsync(cancellationToken);

                        foreach (var p in found)
                            profileMap[p.Id] = p;
                    }

                    foreach (var r in rows)
                    {
                        if (!profileMap.TryGetValue(r.ProfileId, out var profile))
                        {
                            invalidRows++;
                            errors.Add(
                                $"پروفایل با Id برابر {r.ProfileId} در دیتابیس وجود ندارد ({r.Name} / {r.Username}).");
                            continue;
                        }

                        var exists = profile.SocialMedia.Any(s =>
                            string.Equals(s.Name, r.Name,
                                StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(s.Username, r.Username,
                                StringComparison.OrdinalIgnoreCase));

                        if (exists)
                        {
                            duplicateCount++;
                            errors.Add(
                                $"از قبل ثبت شده است: پروفایل {r.ProfileId} / {r.Name} / {r.Username}");
                            continue;
                        }

                        profile.SocialMedia.Add(new SocialMedia
                        {
                            Name = r.Name,
                            Username = r.Username
                        });

                        importedCount++;
                    }

                    // 3. ثبت (SaveChanges یک تراکنش واحد است؛ در صورت خطا هیچ‌چیز ثبت نمی‌شود)
                    if (importedCount > 0)
                    {
                        try
                        {
                            await _context.SaveChangesAsync(cancellationToken);
                        }
                        catch
                        {
                            importedCount = 0;
                            throw;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                errors.Add(
                    "خطای اصلی عملیات: " + ex.GetBaseException().Message);
            }

            var finishedAt = DateTime.Now;
            var report = new StringBuilder();

            report.AppendLine("گزارش نهایی Import شبکه‌های اجتماعی");
            report.AppendLine("--------------------------------");
            report.AppendLine(
                $"نام فایل: {Path.GetFileName(file?.FileName ?? "")}");
            report.AppendLine(
                $"زمان شروع: {startedAt:yyyy/MM/dd HH:mm:ss}");
            report.AppendLine(
                $"زمان پایان: {finishedAt:yyyy/MM/dd HH:mm:ss}");
            report.AppendLine(
                $"مدت عملیات: {(finishedAt - startedAt).TotalSeconds:F2} ثانیه");
            report.AppendLine("--------------------------------");
            report.AppendLine($"تعداد ردیف‌های اکسل: {totalRows}");
            report.AppendLine($"تعداد ردیف‌های معتبر: {validRows}");
            report.AppendLine($"تعداد ردیف‌های نامعتبر: {invalidRows}");
            report.AppendLine($"تعداد ردیف‌های خالی: {skippedEmptyRows}");
            report.AppendLine($"تعداد موارد تکراری: {duplicateCount}");
            report.AppendLine($"تعداد شبکه‌های ثبت‌شده: {importedCount}");
            report.AppendLine($"تعداد پیام‌های خطا: {errors.Count}");
            report.AppendLine("--------------------------------");

            report.AppendLine(
                importedCount > 0
                    ? "نتیجه: عملیات Import پایان یافت."
                    : "نتیجه: هیچ شبکه اجتماعی جدیدی ثبت نشد.");

            if (errors.Count > 0)
            {
                report.AppendLine();
                report.AppendLine("جزئیات خطاها:");

                foreach (var error in errors.Take(100))
                    report.AppendLine("- " + error);

                if (errors.Count > 100)
                {
                    report.AppendLine(
                        $"و {errors.Count - 100} خطای دیگر...");
                }
            }

            TempData["ImportError"] = report.ToString();

            if (importedCount > 0)
            {
                TempData["ImportSuccess"] =
                    $"{importedCount} شبکه اجتماعی با موفقیت ثبت شد.";
            }

            return RedirectToAction(nameof(SocialMediaImport));
        }

    }
}