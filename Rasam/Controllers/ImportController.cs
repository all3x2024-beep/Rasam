using System.Data;
using System.Globalization;
using System.Text;
using ExcelDataReader;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Rasam.Data;
using Rasam.Models.SocialAccount;

namespace Rasam.Controllers
{
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
                // 1. اعتبارسنجی فایل اکسل
                if (file == null || file.Length == 0 ||
                    !Path.GetExtension(file.FileName).Equals(
                        ".xlsx", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        "لطفاً یک فایل اکسل معتبر با فرمت XLSX انتخاب کنید.");
                }

                // 2. اعتبارسنجی تصاویر
                if (images == null || images.Count == 0)
                {
                    throw new InvalidDataException(
                        "لطفاً تصاویر را انتخاب کنید.");
                }

                Encoding.RegisterProvider(
                    CodePagesEncodingProvider.Instance);

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

                    if (!allowedExtensions.Contains(
                            Path.GetExtension(imageName)))
                    {
                        continue;
                    }

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

                // 3. خواندن اکسل
                using var excelStream = file.OpenReadStream();

                using var reader = ExcelReaderFactory.CreateReader(
                    excelStream);

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

                // ستون Id اکنون الزامی است.
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
                        CultureInfo.InvariantCulture)?.Trim()
                        ?? string.Empty;
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

                    if (!allowedExtensions.Contains(
                            Path.GetExtension(name)))
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

                void ValidateUsernameId(string usernameId)
                {
                    if (string.IsNullOrWhiteSpace(usernameId) ||
                        usernameId == "." ||
                        usernameId == ".." ||
                        usernameId.IndexOfAny(
                            Path.GetInvalidFileNameChars()) >= 0 ||
                        usernameId.Contains('/') ||
                        usernameId.Contains('\\'))
                    {
                        throw new InvalidDataException(
                            $"UsernameId معتبر نیست: {usernameId}");
                    }
                }

                var profiles = new List<Profile>();

                var seenUsernames = new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);

                var seenIds = new HashSet<int>();

                // 4. اعتبارسنجی ردیف‌های اکسل
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

                        // شناسه صفر یا منفی پذیرفته نمی‌شود.
                        if (id <= 0)
                        {
                            throw new InvalidDataException(
                                $"Id باید بزرگ‌تر از صفر باشد: {id}");
                        }

                        // جلوگیری از تکرار Id در اکسل
                        if (!seenIds.Add(id))
                        {
                            duplicateCount++;

                            errors.Add(
                                $"ردیف {rowNumber}: شناسه Id تکراری است: {id}");

                            continue;
                        }

                        var usernameId = GetCell(row, "UsernameId");

                        ValidateUsernameId(usernameId);

                        if (!seenUsernames.Add(usernameId))
                        {
                            duplicateCount++;

                            errors.Add(
                                $"ردیف {rowNumber}: UsernameId تکراری است: {usernameId}");

                            continue;
                        }

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
                            // شناسه دقیقاً از اکسل گرفته می‌شود.
                            Id = id,

                            UsernameId = usernameId,
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

                        errors.Add(
                            $"ردیف {rowNumber}: {ex.Message}");
                    }
                }

                // 5. بررسی شناسه‌های موجود در دیتابیس
                if (profiles.Count > 0)
                {
                    var ids = profiles
                        .Select(p => p.Id)
                        .ToList();

                    var usernames = profiles
                        .Select(p => p.UsernameId!)
                        .ToList();

                    var existingIds = new HashSet<int>();
                    var existingUsernames = new HashSet<string>(
                        StringComparer.OrdinalIgnoreCase);

                    // تقسیم به دسته‌های کوچک برای جلوگیری از
                    // محدودیت تعداد پارامترهای SQL Server.
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

                    foreach (var batch in usernames.Distinct(
                                 StringComparer.OrdinalIgnoreCase).Chunk(1000))
                    {
                        var foundUsernames = await _context.Profile
                            .AsNoTracking()
                            .Where(p => batch.Contains(p.UsernameId!))
                            .Select(p => p.UsernameId!)
                            .ToListAsync(cancellationToken);

                        foreach (var username in foundUsernames)
                            existingUsernames.Add(username);
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

                        if (existingUsernames.Contains(profile.UsernameId!))
                        {
                            duplicateCount++;

                            errors.Add(
                                $"UsernameId از قبل در دیتابیس وجود دارد: {profile.UsernameId}");

                            continue;
                        }

                        newProfiles.Add(profile);
                    }

                    profiles = newProfiles;
                }

                // 6. کپی تصاویر و ذخیره در دیتابیس
                if (profiles.Count > 0)
                {
                    var webRoot = _environment.WebRootPath;

                    if (string.IsNullOrWhiteSpace(webRoot))
                    {
                        webRoot = Path.Combine(
                            _environment.ContentRootPath,
                            "wwwroot");
                    }

                    var uploadRoot = Path.Combine(
                        webRoot, "uploads" ,"profiles");

                    Directory.CreateDirectory(uploadRoot);

                    await using var transaction =
                        await _context.Database.BeginTransactionAsync(
                            cancellationToken);

                    try
                    {
                        foreach (var profile in profiles)
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            var imageName = GetImageFileName(
                                profile.Image);

                            if (string.IsNullOrWhiteSpace(imageName))
                            {
                                profile.Image = string.Empty;
                                continue;
                            }

                            var imageFile = imageMap[imageName];

                            var destinationDirectory = Path.Combine(
                                uploadRoot,
                                profile.UsernameId!);

                            Directory.CreateDirectory(
                                destinationDirectory);

                            var destinationPath = Path.Combine(
                                destinationDirectory,
                                imageName);

                            if (System.IO.File.Exists(destinationPath))
                            {
                                throw new IOException(
                                    $"فایل مقصد از قبل وجود دارد: " +
                                    $"{profile.UsernameId}/{imageName}");
                            }

                            await using (var source =
                                imageFile.OpenReadStream())
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
                                $"/uploads/profiles/{profile.UsernameId}/{imageName}";
                        }

                        // نکته مهم:
                        // SET IDENTITY_INSERT باید روی همان اتصال
                        // و در همان تراکنش اجرا شود.
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

                            await _context.SaveChangesAsync(
                                cancellationToken);
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

                        await transaction.CommitAsync(
                            cancellationToken);

                        importedCount = profiles.Count;
                    }
                    catch
                    {
                        try
                        {
                            await transaction.RollbackAsync(
                                CancellationToken.None);
                        }
                        catch
                        {
                            // خطای Rollback نباید خطای اصلی را بپوشاند.
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
                    "خطای اصلی عملیات: " +
                    ex.GetBaseException().Message);
            }

            // 7. تهیه گزارش نهایی
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

                // 2. اعتبارسنجی تصاویر
                if (images == null || images.Count == 0)
                {
                    throw new InvalidDataException(
                        "لطفاً تصاویر موردنیاز را انتخاب کنید.");
                }

                Encoding.RegisterProvider(
                    CodePagesEncodingProvider.Instance);

                var allowedExtensions = new HashSet<string>(
                    new[] { ".jpg", ".jpeg", ".png", ".webp", ".jfif" },
                    StringComparer.OrdinalIgnoreCase);

                // 3. ساخت نقشه تصاویر براساس نام فایل
                var imageMap = new Dictionary<string, IFormFile>(
                    StringComparer.OrdinalIgnoreCase);

                var duplicateImageNames = new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);

                foreach (var image in images)
                {
                    if (image == null || image.Length == 0)
                        continue;

                    var normalizedName = image.FileName.Replace('\\', '/');
                    var imageName = Path.GetFileName(normalizedName);

                    if (string.IsNullOrWhiteSpace(imageName))
                        continue;

                    if (!allowedExtensions.Contains(
                            Path.GetExtension(imageName)))
                    {
                        continue;
                    }

                    if (!imageMap.TryAdd(imageName, image))
                    {
                        duplicateImageNames.Add(imageName);
                    }
                }

                // تصاویر دارای نام تکراری قابل استفاده نیستند
                foreach (var duplicateName in duplicateImageNames)
                {
                    imageMap.Remove(duplicateName);
                }

                if (imageMap.Count == 0)
                {
                    throw new InvalidDataException(
                        "هیچ تصویر معتبر و قابل استفاده‌ای انتخاب نشده است.");
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

                // فقط این دو ستون برای ورود اطلاعات ضروری هستند
                string[] requiredColumns =
                {
            "ProfileId",
            "FileName"
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

                string GetImageFileName(string? value)
                {
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        throw new InvalidDataException(
                            "مقدار FileName الزامی است.");
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
                            $"نام تصویر معتبر نیست: {value}");
                    }

                    if (!allowedExtensions.Contains(
                            Path.GetExtension(name)))
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

                        var imageName = GetImageFileName(
                            GetCell(row, "FileName"));

                        if (duplicateImageNames.Contains(imageName))
                        {
                            throw new InvalidDataException(
                                $"تصویر {imageName} در فایل‌های انتخاب‌شده نام تکراری دارد.");
                        }

                        if (!imageMap.ContainsKey(imageName))
                        {
                            throw new FileNotFoundException(
                                $"تصویر {imageName} در فایل‌های انتخاب‌شده پیدا نشد.");
                        }

                        var rowKey = $"{profileId}|{imageName}";

                        if (!seenRows.Add(rowKey))
                        {
                            duplicateCount++;

                            errors.Add(
                                $"ردیف {rowNumber}: ترکیب ProfileId و FileName تکراری است.");

                            continue;
                        }

                        var attachment = new ProfileAttachment
                        {
                            ProfileId = profileId,
                            FileName = imageName,
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

                // 6. بررسی وجود پروفایل‌ها و دریافت UsernameId
                if (attachments.Count > 0)
                {
                    var profileIds = attachments
                        .Select(a => a.ProfileId)
                        .Distinct()
                        .ToList();

                    var existingProfiles = await _context.Profile
                        .AsNoTracking()
                        .Where(p => profileIds.Contains(p.Id))
                        .Select(p => new
                        {
                            p.Id,
                            p.UsernameId
                        })
                        .ToListAsync(cancellationToken);

                    var profileMap = existingProfiles.ToDictionary(
                        p => p.Id,
                        p => p.UsernameId);

                    var existingProfileIds = profileMap.Keys.ToHashSet();

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

                        var usernameId = profileMap[attachment.ProfileId];

                        if (string.IsNullOrWhiteSpace(usernameId))
                        {
                            invalidRows++;

                            errors.Add(
                                $"UsernameId پروفایل با Id برابر {attachment.ProfileId} خالی است.");

                            continue;
                        }

                        usernameId = usernameId.Trim();

                        if (usernameId == "." ||
                            usernameId == ".." ||
                            usernameId.IndexOfAny(
                                Path.GetInvalidFileNameChars()) >= 0 ||
                            usernameId.Contains('/') ||
                            usernameId.Contains('\\'))
                        {
                            invalidRows++;

                            errors.Add(
                                $"UsernameId پروفایل با Id برابر {attachment.ProfileId} معتبر نیست.");

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

                // 8. آماده‌سازی مسیر wwwroot
                if (attachments.Count > 0)
                {
                    var webRoot = _environment.WebRootPath;

                    if (string.IsNullOrWhiteSpace(webRoot))
                    {
                        webRoot = Path.Combine(
                            _environment.ContentRootPath,
                            "wwwroot");
                    }

                    var uploadRoot = Path.Combine(
                        webRoot,
                        "uploads");

                    Directory.CreateDirectory(uploadRoot);

                    // دریافت UsernameId برای استفاده در مرحله کپی
                    var idsToImport = attachments
                        .Select(a => a.ProfileId)
                        .Distinct()
                        .ToList();

                    var profilesForImport = await _context.Profile
                        .AsNoTracking()
                        .Where(p => idsToImport.Contains(p.Id))
                        .Select(p => new
                        {
                            p.Id,
                            p.UsernameId
                        })
                        .ToListAsync(cancellationToken);

                    var usernameMap = profilesForImport.ToDictionary(
                        p => p.Id,
                        p => p.UsernameId);

                    // 9. کپی تصاویر و ثبت دیتابیس در تراکنش
                    await using var transaction =
                        await _context.Database.BeginTransactionAsync(
                            cancellationToken);

                    try
                    {
                        foreach (var attachment in attachments)
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            if (!usernameMap.TryGetValue(
                                    attachment.ProfileId,
                                    out var usernameId) ||
                                string.IsNullOrWhiteSpace(usernameId))
                            {
                                throw new InvalidDataException(
                                    $"UsernameId برای ProfileId={attachment.ProfileId} پیدا نشد.");
                            }

                            usernameId = usernameId.Trim();

                            if (usernameId == "." ||
                                usernameId == ".." ||
                                usernameId.IndexOfAny(
                                    Path.GetInvalidFileNameChars()) >= 0 ||
                                usernameId.Contains('/') ||
                                usernameId.Contains('\\'))
                            {
                                throw new InvalidDataException(
                                    $"UsernameId برای ProfileId={attachment.ProfileId} معتبر نیست.");
                            }

                            var imageName = attachment.FileName;
                            var imageFile = imageMap[imageName];

                            // مسیر فیزیکی:
                            // wwwroot/uploads/{UsernameId}/{FileName}
                            var destinationDirectory = Path.Combine(
                                uploadRoot,
                                usernameId);

                            Directory.CreateDirectory(destinationDirectory);

                            var destinationPath = Path.Combine(
                                destinationDirectory,
                                imageName);

                            if (System.IO.File.Exists(destinationPath))
                            {
                                destinationPath = Path.Combine(
                                destinationDirectory,
                                "AS Copy"+imageName);
                            }

                            await using (var source =
                                imageFile.OpenReadStream())
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

                            attachment.FilePath = destinationPath;

                            attachment.FileUrl =
                                $"/uploads/{Uri.EscapeDataString(usernameId)}" +
                                $"/{Uri.EscapeDataString(imageName)}";
                        }

                        await _context.Set<ProfileAttachment>()
                            .AddRangeAsync(attachments, cancellationToken);

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
                                $"خطا در Rollback دیتابیس: " +
                                rollbackEx.GetBaseException().Message);
                        }

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

                        // نگه‌داشتن جزئیات خطای واقعی برای گزارش
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
                    $"{importedCount} پیوست با موفقیت ثبت شد.";
            }

            return RedirectToAction(nameof(ProfileAttachmentsImport));
        }


    }
}
