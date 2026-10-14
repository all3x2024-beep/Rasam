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

        public ImportController(ApplicationDbContext context)
        {
            _context = context;
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
            CancellationToken cancellationToken)
        {
            // اعتبارسنجی فایل
            if (file == null || file.Length == 0)
            {
                TempData["ImportError"] = "فایل اکسل انتخاب نشده است.";
                return RedirectToAction(nameof(ProfilesImport));
            }

            var extension = Path.GetExtension(file.FileName);

            if (!extension.Equals(".xlsx", StringComparison.OrdinalIgnoreCase) &&
                !extension.Equals(".xls", StringComparison.OrdinalIgnoreCase))
            {
                TempData["ImportError"] =
                    "فرمت فایل معتبر نیست. فقط فایل‌های XLS و XLSX مجاز هستند.";

                return RedirectToAction(nameof(ProfilesImport));
            }

            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            var errors = new List<string>();
            var totalRows = 0;
            var validRows = 0;
            var importedCount = 0;
            var duplicateCount = 0;
            var invalidRows = 0;
            var skippedEmptyRows = 0;

            var startedAt = DateTime.Now;
            Exception? fatalError = null;

            try
            {
                using var stream = file.OpenReadStream();
                using var reader = ExcelReaderFactory.CreateReader(stream);

                // ردیف اول اکسل به‌عنوان نام ستون‌ها خوانده می‌شود.
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
                        "هیچ Sheet قابل خواندنی در فایل اکسل پیدا نشد.");
                }

                var table = dataSet.Tables[0];
                totalRows = table.Rows.Count;

                // ستون‌های ضروری فایل اکسل
                string[] requiredColumns =
                {
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
                    .Where(column => !table.Columns.Contains(column))
                    .ToList();

                if (missingColumns.Count > 0)
                {
                    throw new InvalidDataException(
                        "ستون‌های زیر در فایل اکسل پیدا نشدند: " +
                        string.Join(", ", missingColumns));
                }

                // خواندن مقدار سلول به‌صورت رشته
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

                // تبدیل TopicId به عدد nullable
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
                        $"مقدار ستون {column} عدد صحیح معتبر نیست: «{value}»");
                }

                bool IsEmptyRow(DataRow row)
                {
                    foreach (var value in row.ItemArray)
                    {
                        if (value != null &&
                            value != DBNull.Value &&
                            !string.IsNullOrWhiteSpace(value.ToString()))
                        {
                            return false;
                        }
                    }

                    return true;
                }

                var profiles = new List<Profile>();

                var seenUsernames = new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);

                // مهم: چون UseHeaderRow = true است،
                // اولین ردیف داده، اندیس صفر دارد.
                for (var i = 0; i < table.Rows.Count; i++)
                {
                    var row = table.Rows[i];
                    var excelRowNumber = i + 2;

                    if (IsEmptyRow(row))
                    {
                        skippedEmptyRows++;
                        continue;
                    }

                    try
                    {
                        var usernameId = GetCell(row, "UsernameId");

                        if (string.IsNullOrWhiteSpace(usernameId))
                        {
                            invalidRows++;
                            errors.Add(
                                $"ردیف {excelRowNumber}: مقدار UsernameId خالی است.");
                            continue;
                        }

                        if (!seenUsernames.Add(usernameId))
                        {
                            duplicateCount++;
                            errors.Add(
                                $"ردیف {excelRowNumber}: UsernameId تکراری در فایل اکسل: {usernameId}");
                            continue;
                        }

                        var profile = new Profile
                        {
                            // Id عمداً تنظیم نشده است؛
                            // اگر کلید اصلی Identity باشد، دیتابیس آن را می‌سازد.
                            UsernameId = usernameId,
                            PID = GetCell(row, "PID"),
                            FirstName = GetCell(row, "FirstName"),
                            LastName = GetCell(row, "LastName"),
                            NationalCode = GetCell(row, "NationalCode"),
                            Phone = GetCell(row, "Phone"),
                            Country = GetCell(row, "Country"),
                            Image = GetCell(row, "Image"),
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
                            $"ردیف {excelRowNumber}: {ex.Message}");
                    }
                }

                // بررسی UsernameIdهایی که قبلاً در دیتابیس ثبت شده‌اند
                if (profiles.Count > 0)
                {
                    var usernames = profiles
                        .Select(p => p.UsernameId!)
                        .ToList();

                    var existingUsernames = await _context.Profile
                        .AsNoTracking()
                        .Where(p => usernames.Contains(p.UsernameId!))
                        .Select(p => p.UsernameId!)
                        .ToListAsync(cancellationToken);

                    var existingSet = new HashSet<string>(
                        existingUsernames,
                        StringComparer.OrdinalIgnoreCase);

                    var newProfiles = new List<Profile>();

                    foreach (var profile in profiles)
                    {
                        if (existingSet.Contains(profile.UsernameId!))
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

                // ثبت رکوردها در دیتابیس
                if (profiles.Count > 0)
                {
                    await using var transaction =
                        await _context.Database.BeginTransactionAsync(
                            cancellationToken);

                    try
                    {
                        await _context.Profile.AddRangeAsync(
                            profiles,
                            cancellationToken);

                        await _context.SaveChangesAsync(cancellationToken);

                        await transaction.CommitAsync(cancellationToken);

                        importedCount = profiles.Count;
                    }
                    catch
                    {
                        await transaction.RollbackAsync(cancellationToken);
                        throw;
                    }
                }
            }
            catch (Exception ex)
            {
                fatalError = ex;

                errors.Add("خطای اصلی عملیات: " + ex.Message);

                if (ex.InnerException != null)
                {
                    errors.Add(
                        "جزئیات خطای دیتابیس: " +
                        ex.InnerException.Message);
                }

                if (ex is DbUpdateException dbUpdateException)
                {
                    errors.Add(
                        "جزئیات ثبت در دیتابیس: " +
                        dbUpdateException.ToString());
                }
            }

            // ساخت گزارش نهایی؛ هم در موفقیت و هم در خطا
            var finishedAt = DateTime.Now;
            var report = new StringBuilder();

            report.AppendLine("گزارش نهایی Import پروفایل‌ها");
            report.AppendLine("--------------------------------");
            report.AppendLine($"نام فایل: {Path.GetFileName(file.FileName)}");
            report.AppendLine($"زمان شروع: {startedAt:yyyy/MM/dd HH:mm:ss}");
            report.AppendLine($"زمان پایان: {finishedAt:yyyy/MM/dd HH:mm:ss}");
            report.AppendLine($"مدت عملیات: {(finishedAt - startedAt).TotalSeconds:F2} ثانیه");
            report.AppendLine("--------------------------------");
            report.AppendLine($"تعداد ردیف‌های داده در اکسل: {totalRows}");
            report.AppendLine($"تعداد ردیف‌های معتبر خوانده‌شده: {validRows}");
            report.AppendLine($"تعداد ردیف‌های نامعتبر: {invalidRows}");
            report.AppendLine($"تعداد ردیف‌های خالی: {skippedEmptyRows}");
            report.AppendLine($"تعداد موارد تکراری: {duplicateCount}");
            report.AppendLine($"تعداد رکوردهای ثبت‌شده: {importedCount}");
            report.AppendLine($"تعداد پیام‌های خطا: {errors.Count}");

            if (fatalError == null && importedCount > 0)
            {
                report.AppendLine();
                report.AppendLine("نتیجه: عملیات Import با ثبت رکوردها پایان یافت.");
            }
            else if (fatalError == null && importedCount == 0)
            {
                report.AppendLine();
                report.AppendLine("نتیجه: هیچ رکورد جدیدی در دیتابیس ثبت نشد.");
            }
            else
            {
                report.AppendLine();
                report.AppendLine("نتیجه: عملیات با خطا پایان یافت.");
            }

            if (errors.Count > 0)
            {
                report.AppendLine();
                report.AppendLine("جزئیات خطاها:");

                foreach (var error in errors)
                {
                    report.AppendLine("- " + error);
                }
            }

            // طبق درخواست، گزارش نهایی همیشه در این کلید قرار می‌گیرد.
            TempData["ImportError"] = report.ToString();

            if (importedCount > 0)
            {
                TempData["ImportSuccess"] =
                    $"{importedCount} رکورد با موفقیت ثبت شد.";
            }

            return RedirectToAction(nameof(ProfilesImport));
        }
    }
}