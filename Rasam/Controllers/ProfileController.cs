using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Rasam.Data;
using Rasam.Models.SocialAccount;

namespace Rasam.Controllers
{
    [Authorize]

    public class ProfileController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _environment;

        public ProfileController(
         ApplicationDbContext context,
         IWebHostEnvironment environment)
        {
            _context = context;
            _environment = environment;
        }

        // =========================================================
        // INDEX
        // =========================================================

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var profiles = await _context.Profile
                .Include(x => x.Topic)
                .Include(x => x.Tag)
                .Include(x => x.Relation)
                .Include(x => x.SocialMedia)
                .ToListAsync();

            return View(profiles);
        }


        // =========================================================
        // GET PROFILES
        // =========================================================

        [HttpGet]
        public async Task<IActionResult> GetProfiles()
        {
            var rows = await _context.Profile
                .AsNoTracking()
                .Select(x => new
                {
                    id = x.Id,

                    usernameId = x.UsernameId,

                    topicId = x.TopicId,

                    topic = x.Topic != null
                        ? x.Topic.Title
                        : "",

                    tagIds = x.Tag
                        .Select(t => t.Id)
                        .ToList(),

                    tags = x.Tag
                        .Select(t => new
                        {
                            id = t.Id,
                            title = t.Title
                        })
                        .ToList(),

                    socialMediaIds = x.SocialMedia
                        .Select(s => s.Id)
                        .ToList(),

                    socialMedia = x.SocialMedia
                        .Select(s => new
                        {
                            id = s.Id,
                            name = s.Name,
                            username = s.Username
                        })
                        .ToList(),

                    pid = x.PID,
                    firstName = x.FirstName,
                    lastName = x.LastName,
                    nationalCode = x.NationalCode,
                    phone = x.Phone,
                    country = x.Country,
                    image = x.Image,
                    theory = x.Theory,

                    attachments = x.Attachments
                        .OrderBy(a => a.Id)
                        .Select(a => new
                        {
                            id = a.Id,
                            fileName = a.FileName,
                            fileUrl = a.FileUrl
                        })
                        .ToList()
                })
                .ToListAsync();


            // روابط واقعی رکوردها در جدول ProfileRelation ذخیره می‌شوند
            // (نه در Profile.Relation که فقط فهرست انواع رابطه است).
            var relationRows = await _context.ProfileRelation
                .AsNoTracking()
                .Select(r => new
                {
                    r.Id,
                    r.FromProfileId,
                    r.ToProfileId,
                    r.RelationId,
                    Type = r.Relation.Type
                })
                .ToListAsync();

            var usernameById = rows.ToDictionary(
                p => p.id,
                p => p.usernameId
            );

            var relationsByProfile = relationRows
                .GroupBy(r => r.FromProfileId)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(r => new
                    {
                        id = r.Id,
                        relationId = r.RelationId,
                        toProfileId = r.ToProfileId,
                        type = r.Type,
                        toUsernameId =
                            usernameById.TryGetValue(
                                r.ToProfileId,
                                out var u
                            )
                                ? u
                                : null
                    })
                    .ToList()
                );

            var profiles = rows.Select(x => new
            {
                x.id,
                x.usernameId,
                x.topicId,
                x.topic,
                x.tagIds,
                x.tags,
                x.socialMediaIds,
                x.socialMedia,

                relationIds =
                    relationsByProfile.TryGetValue(x.id, out var rel)
                        ? rel.Select(r => r.id).ToList()
                        : new List<int>(),

                relations =
                    relationsByProfile.TryGetValue(x.id, out var rel2)
                        ? rel2.Cast<object>().ToList()
                        : new List<object>(),

                x.pid,
                x.firstName,
                x.lastName,
                x.nationalCode,
                x.phone,
                x.country,
                x.image,
                x.theory,
                x.attachments
            })
            .ToList();

            return Json(profiles);
        }

        // =========================================================
        // FILE HELPERS (پیوست‌ها، نام تصویر، پوشهٔ کد ملی)
        // =========================================================

        private const long MaxAttachmentBytes = 25L * 1024 * 1024;

        // فایل‌ها داخل wwwroot (قابل دسترسی مستقیم از وب) ذخیره می‌شوند؛
        // پس نوع‌های اجرایی/اسکریپتی که می‌توانند روی سایت اجرا شوند مجاز نیستند.
        private static readonly HashSet<string> BlockedAttachmentExtensions =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ".exe", ".dll", ".com", ".bat", ".cmd", ".msi", ".scr",
                ".ps1", ".vbs", ".sh", ".jar",
                ".js", ".mjs", ".html", ".htm", ".xhtml", ".svg",
                ".aspx", ".asp", ".ashx", ".cshtml", ".razor", ".config",
                ".php", ".jsp"
            };

        private static string NormalizeDigits(string? value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            var sb = new StringBuilder(value.Length);

            foreach (var ch in value)
            {
                if (ch >= '\u06F0' && ch <= '\u06F9')
                    sb.Append((char)('0' + (ch - '\u06F0')));
                else if (ch >= '\u0660' && ch <= '\u0669')
                    sb.Append((char)('0' + (ch - '\u0660')));
                else
                    sb.Append(ch);
            }

            return sb.ToString();
        }

        // نام پوشه/فایل بر پایهٔ کد ملی؛ اگر کد ملی خالی باشد null برمی‌گرداند
        private static string? NationalCodeName(string? nationalCode)
        {
            var digits =
                new string(
                    NormalizeDigits(nationalCode)
                        .Where(char.IsLetterOrDigit)
                        .ToArray()
                );

            return digits.Length == 0 ? null : digits;
        }

        private static string SafeFileName(string? name)
        {
            var clean = Path.GetFileName(name ?? string.Empty).Trim();

            foreach (var bad in Path.GetInvalidFileNameChars())
                clean = clean.Replace(bad, '_');

            return string.IsNullOrWhiteSpace(clean) ? "file" : clean;
        }

        private static string UniqueName(HashSet<string> used, string fileName)
        {
            var name = fileName;
            var stem = Path.GetFileNameWithoutExtension(fileName);
            var ext = Path.GetExtension(fileName);
            var i = 2;

            while (!used.Add(name.ToLowerInvariant()))
            {
                name = $"{stem} ({i}){ext}";
                i++;
            }

            return name;
        }

        private static string? ValidateAttachments(IEnumerable<IFormFile>? files)
        {
            if (files == null)
                return null;

            foreach (var file in files)
            {
                if (file == null || file.Length <= 0)
                    continue;

                if (file.Length > MaxAttachmentBytes)
                    return $"حجم فایل «{Path.GetFileName(file.FileName)}» بیشتر از ۲۵ مگابایت است.";

                var ext = Path.GetExtension(file.FileName);

                if (BlockedAttachmentExtensions.Contains(ext))
                    return $"بارگذاری فایل با پسوند «{ext}» مجاز نیست.";
            }

            return null;
        }

        // مسیر فیزیکی پیوست؛ فقط داخل wwwroot/uploads/profiles پذیرفته می‌شود
        private string? ResolveAttachmentPath(ProfileAttachment attachment)
        {
            var root =
                Path.GetFullPath(
                    Path.Combine(
                        _environment.WebRootPath,
                        "uploads",
                        "profiles"
                    )
                ) + Path.DirectorySeparatorChar;

            string? candidate = attachment.FilePath;

            if (string.IsNullOrWhiteSpace(candidate) ||
                !System.IO.File.Exists(candidate))
            {
                candidate = string.IsNullOrWhiteSpace(attachment.FileUrl)
                    ? null
                    : Path.Combine(
                        _environment.WebRootPath,
                        attachment.FileUrl
                            .TrimStart('/', '\\')
                            .Replace('/', Path.DirectorySeparatorChar)
                    );
            }

            if (candidate == null)
                return null;

            var full = Path.GetFullPath(candidate);

            return full.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                ? full
                : null;
        }

        // =========================================================
        // IsValidUsernameId
        // =========================================================
        private bool IsValidUsernameId(string usernameId)
        {
            if (string.IsNullOrWhiteSpace(usernameId))
                return false;

            usernameId = usernameId.Trim();

            // نباید مسیر یا کاراکتر غیرمجاز ویندوز باشد
            if (usernameId.Contains('/') ||
                usernameId.Contains('\\') ||
                usernameId.Contains(".."))
            {
                return false;
            }

            return usernameId.IndexOfAny(
                Path.GetInvalidFileNameChars()
            ) < 0;
        }
        // =========================================================
        // CREATE
        // =========================================================

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(209715200)]
        [RequestFormLimits(MultipartBodyLengthLimit = 209715200)]
        public async Task<IActionResult> Create(Profile profile, List<int>? TagIds, List<int>? SocialMediaIds)
        {
            string? createdProfileFolder = null;

            try
            {
                // =========================================================
                // VALIDATION
                // =========================================================

                if (!ModelState.IsValid)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "اطلاعات وارد شده معتبر نیست."
                    });
                }

                if (string.IsNullOrWhiteSpace(profile.UsernameId))
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "شناسه کاربری الزامی است."
                    });
                }

                profile.UsernameId = profile.UsernameId.Trim();

                if (!IsValidUsernameId(profile.UsernameId))
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "شناسه کاربری نامعتبر است."
                    });
                }

                // =========================================================
                // CHECK DUPLICATE USERNAME
                // =========================================================

                var usernameExists =
                    await _context.Profile
                        .AnyAsync(x => x.UsernameId == profile.UsernameId);

                if (usernameExists)
                {
                    return Conflict(new
                    {
                        success = false,
                        message = "این شناسه کاربری قبلاً ثبت شده است."
                    });
                }


                // =========================================================
                // ATTACHMENT VALIDATION (قبل از ذخیرهٔ رکورد)
                // =========================================================

                var attachmentError = ValidateAttachments(profile.AttachmentsFiles);

                if (attachmentError != null)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = attachmentError
                    });
                }


                // =========================================================
                // TOPIC
                // =========================================================

                if (profile.TopicId.HasValue)
                {
                    var topicExists =
                        await _context.Topic
                            .AnyAsync(x =>
                                x.Id == profile.TopicId.Value);

                    if (!topicExists)
                    {
                        return BadRequest(new
                        {
                            success = false,
                            message = "موضوع انتخاب‌شده معتبر نیست."
                        });
                    }
                }


                // =========================================================
                // TAGS
                // =========================================================

                profile.Tag.Clear();

                if (TagIds != null && TagIds.Count > 0)
                {
                    var tags =
                        await _context.Tag
                            .Where(x => TagIds.Contains(x.Id))
                            .ToListAsync();

                    foreach (var tag in tags)
                    {
                        profile.Tag.Add(tag);
                    }
                }


                // =========================================================
                // SOCIAL MEDIA
                // =========================================================

                profile.SocialMedia.Clear();

                if (SocialMediaIds != null &&
                    SocialMediaIds.Count > 0)
                {
                    var socialMedia =
                        await _context.SocialMedia
                            .Where(x =>
                                SocialMediaIds.Contains(x.Id))
                            .ToListAsync();

                    foreach (var social in socialMedia)
                    {
                        profile.SocialMedia.Add(social);
                    }
                }


                // =========================================================
                // DATE
                // =========================================================

                profile.CreateDate =
                    DateTime.Now.ToString(
                        "yyyy-MM-dd HH:mm:ss"
                    );


                // =========================================================
                // SAVE PROFILE FIRST
                // =========================================================

                _context.Profile.Add(profile);

                await _context.SaveChangesAsync();


                // =========================================================
                // PROFILE FOLDER
                // =========================================================

                var profileFolder =
                    Path.Combine(
                        _environment.WebRootPath,
                        "uploads",
                        "profiles",
                        profile.UsernameId
                    );

                createdProfileFolder = profileFolder;

                Directory.CreateDirectory(profileFolder);

                // =========================================================
                // MAIN PROFILE IMAGE
                // =========================================================

                if (profile.MyFile != null &&
                profile.MyFile.Length > 0)
                {
                    profile.Image =
                        await SaveProfileImageAsync(
                            profile.MyFile,
                            profileFolder,
                            profile.UsernameId,
                            profile.NationalCode
                        );
                }


                // =========================================================
                // ATTACHMENTS
                // =========================================================

                if (profile.AttachmentsFiles != null &&
                    profile.AttachmentsFiles.Count > 0)
                {
                    foreach (var file in profile.AttachmentsFiles)
                    {
                        if (file == null ||
                            file.Length <= 0)
                            continue;

                        var extension =
                            Path.GetExtension(file.FileName);

                        var storedFileName =
                            $"{Guid.NewGuid():N}{extension}";

                        var physicalPath =
                            Path.Combine(
                                profileFolder,
                                storedFileName
                            );

                        await using var stream =
                            new FileStream(
                                physicalPath,
                                FileMode.CreateNew
                            );

                        await file.CopyToAsync(stream);


                        var attachment =
                            new ProfileAttachment
                            {
                                ProfileId = profile.Id,

                                FileName =
                                    Path.GetFileName(
                                        file.FileName
                                    ),

                                FilePath =
                                    physicalPath,

                                FileUrl =
                                    $"/uploads/profiles/" +
                                    $"{profile.UsernameId}/" +
                                    $"{storedFileName}",

                                CreateDate =
                                    DateTime.Now.ToString(
                                        "yyyy-MM-dd HH:mm:ss"
                                    )
                            };

                        _context.ProfileAttachment.Add(
                            attachment
                        );
                    }
                }


                // =========================================================
                // SAVE IMAGE + ATTACHMENTS
                // =========================================================

                await _context.SaveChangesAsync();


                return Json(new
                {
                    success = true,
                    id = profile.Id,
                    usernameId = profile.UsernameId,
                    image = profile.Image,

                    message =
                        "پروفایل و فایل‌های پیوست با موفقیت ذخیره شدند."
                });
            }
            catch (DbUpdateException)
            {
                return Conflict(new
                {
                    success = false,
                    message =
                        "ثبت انجام نشد. احتمالاً شناسه کاربری تکراری است."
                });
            }
            catch (Exception ex)
            {
                if (!string.IsNullOrWhiteSpace(createdProfileFolder) &&
                    Directory.Exists(createdProfileFolder))
                {
                    try
                    {
                        Directory.Delete(
                            createdProfileFolder,
                            true
                        );
                    }
                    catch
                    {
                    }
                }

                return StatusCode(500, new
                {
                    success = false,
                    message = ex.Message,
                    inner = ex.InnerException?.Message
                });
            }
        }
        // =========================================================
        // Delete
        // =========================================================

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            try
            {
                var profile = await _context.Profile
                    .Include(x => x.Attachments)
                    .FirstOrDefaultAsync(x => x.Id == id);

                if (profile == null)
                {
                    return Json(new
                    {
                        success = false,
                        message = "رکورد موردنظر پیدا نشد."
                    });
                }

                // مسیر فولدر پروفایل
                var profileFolder = Path.Combine(
                    _environment.WebRootPath,
                    "uploads",
                    "profiles",
                    profile.UsernameId ?? string.Empty
                );

                // حذف تصویر اصلی
                DeleteImage(profile.Image);

                // حذف فایل‌های Attachment
                foreach (var attachment in profile.Attachments)
                {
                    if (!string.IsNullOrWhiteSpace(attachment.FilePath) &&
                        System.IO.File.Exists(attachment.FilePath))
                    {
                        try
                        {
                            System.IO.File.Delete(
                                attachment.FilePath
                            );
                        }
                        catch
                        {
                            // خطای حذف فایل نباید حذف DB را متوقف کند.
                        }
                    }
                }

                // حذف روابط
                var relations = await _context.ProfileRelation
                    .Where(r =>
                        r.FromProfileId == id ||
                        r.ToProfileId == id)
                    .ToListAsync();

                if (relations.Count > 0)
                {
                    _context.ProfileRelation.RemoveRange(relations);
                }

                // حذف Attachmentها از دیتابیس
                if (profile.Attachments.Count > 0)
                {
                    _context.ProfileAttachment.RemoveRange(
                        profile.Attachments
                    );
                }

                // حذف Profile
                _context.Profile.Remove(profile);

                await _context.SaveChangesAsync();

                // حذف فولدر
                if (Directory.Exists(profileFolder))
                {
                    try
                    {
                        Directory.Delete(
                            profileFolder,
                            true
                        );
                    }
                    catch
                    {
                        // خطای حذف فولدر نباید حذف DB را برگرداند.
                    }
                }

                return Json(new
                {
                    success = true,
                    message = "پروفایل و فایل‌های آن با موفقیت حذف شدند."
                });
            }
            catch (DbUpdateException)
            {
                return StatusCode(409, new
                {
                    success = false,
                    message =
                        "حذف رکورد انجام نشد. احتمالاً رکورد دارای وابستگی دیگری در دیتابیس است."
                });
            }
            catch (Exception)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message =
                        "خطای داخلی هنگام حذف رکورد رخ داد."
                });
            }
        }

        // =========================================================
        // UPDATE
        // =========================================================

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(209715200)]
        [RequestFormLimits(MultipartBodyLengthLimit = 209715200)]
        public async Task<IActionResult> Update(Profile profile, List<int>? TagIds, List<int>? SocialMediaIds)
        {
            bool folderRenamed = false;

            string? oldFolder = null;
            string? newFolder = null;

            try
            {
                // =========================================================
                // BASIC VALIDATION
                // =========================================================

                if (profile == null || profile.Id <= 0)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "شناسه داخلی رکورد معتبر نیست."
                    });
                }


                if (string.IsNullOrWhiteSpace(profile.UsernameId))
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "شناسه کاربری الزامی است."
                    });
                }

                profile.UsernameId =
                    profile.UsernameId.Trim();

                if (!IsValidUsernameId(profile.UsernameId))
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "شناسه کاربری نامعتبر است."
                    });
                }
                var updateAttachmentError = ValidateAttachments(profile.AttachmentsFiles);

                if (updateAttachmentError != null)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = updateAttachmentError
                    });
                }

                // =========================================================
                // LOAD EXISTING PROFILE
                // =========================================================

                var existingProfile =
                    await _context.Profile
                        .Include(x => x.Tag)
                        .Include(x => x.SocialMedia)
                        .Include(x => x.Attachments)
                        .FirstOrDefaultAsync(
                            x => x.Id == profile.Id
                        );


                if (existingProfile == null)
                {
                    return NotFound(new
                    {
                        success = false,
                        message = "رکورد موردنظر پیدا نشد."
                    });
                }


                // =========================================================
                // MODEL STATE
                // =========================================================

                if (!ModelState.IsValid)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "اطلاعات وارد شده معتبر نیست."
                    });
                }


                // =========================================================
                // CHECK DUPLICATE USERNAME
                // =========================================================

                var duplicateUsername =
                    await _context.Profile
                        .AnyAsync(x =>
                            x.Id != profile.Id &&
                            x.UsernameId == profile.UsernameId
                        );

                if (duplicateUsername)
                {
                    return Conflict(new
                    {
                        success = false,
                        message =
                            "این شناسه کاربری قبلاً برای پروفایل دیگری ثبت شده است."
                    });
                }


                // =========================================================
                // OLD / NEW USERNAME
                // =========================================================

                var oldUsername =
                    existingProfile.UsernameId;

                var newUsername =
                    profile.UsernameId;

                var usernameChanged =
                    !string.Equals(
                        oldUsername,
                        newUsername,
                        StringComparison.OrdinalIgnoreCase
                    );


                // =========================================================
                // BASIC FIELDS
                // =========================================================

                existingProfile.UsernameId =
                    newUsername;

                existingProfile.TopicId =
                    profile.TopicId;

                existingProfile.PID =
                    profile.PID;

                existingProfile.FirstName =
                    profile.FirstName;

                existingProfile.LastName =
                    profile.LastName;

                existingProfile.NationalCode =
                    profile.NationalCode;

                existingProfile.Phone =
                    profile.Phone;

                existingProfile.Country =
                    profile.Country;

                existingProfile.Theory =
                    profile.Theory;


                // =========================================================
                // TAGS
                // =========================================================

                existingProfile.Tag.Clear();

                if (TagIds != null &&
                    TagIds.Count > 0)
                {
                    var tags =
                        await _context.Tag
                            .Where(x =>
                                TagIds.Contains(x.Id))
                            .ToListAsync();

                    foreach (var tag in tags)
                    {
                        existingProfile.Tag.Add(tag);
                    }
                }


                // =========================================================
                // SOCIAL MEDIA
                // =========================================================

                existingProfile.SocialMedia.Clear();

                if (SocialMediaIds != null &&
                    SocialMediaIds.Count > 0)
                {
                    var socialMedia =
                        await _context.SocialMedia
                            .Where(x =>
                                SocialMediaIds.Contains(x.Id))
                            .ToListAsync();

                    foreach (var social in socialMedia)
                    {
                        existingProfile.SocialMedia.Add(social);
                    }
                }


                // =========================================================
                // FOLDER PATHS
                // =========================================================

                var profilesRoot =
                    Path.Combine(
                        _environment.WebRootPath,
                        "uploads",
                        "profiles"
                    );

                oldFolder =
                   Path.Combine(
                       profilesRoot,
                       oldUsername ?? string.Empty
                   );

                newFolder =
                   Path.Combine(
                       profilesRoot,
                       newUsername
                   );


                // =========================================================
                // RENAME PROFILE FOLDER
                // =========================================================

                if (usernameChanged)
                {
                    if (Directory.Exists(newFolder))
                    {
                        return Conflict(new
                        {
                            success = false,
                            message =
                                "پوشه شناسه کاربری جدید از قبل وجود دارد."
                        });
                    }

                    if (Directory.Exists(oldFolder))
                    {
                        Directory.Move(
     oldFolder,
     newFolder
 );

                        folderRenamed = true;
                    }
                    else
                    {
                        Directory.CreateDirectory(
                            newFolder
                        );
                    }


                    // Update Attachment URLs
                    foreach (var attachment in existingProfile.Attachments)
                    {
                        var fileName =
                            Path.GetFileName(attachment.FilePath);

                        attachment.FilePath =
                            Path.Combine(
                                newFolder,
                                fileName
                            );

                        attachment.FileUrl =
                            $"/uploads/profiles/" +
                            $"{newUsername}/{fileName}";
                    }


                    // Update main image URL
                    if (!string.IsNullOrWhiteSpace(
                            existingProfile.Image))
                    {
                        var imageFileName =
                            Path.GetFileName(
                                existingProfile.Image
                            );

                        existingProfile.Image =
                            $"/uploads/profiles/" +
                            $"{newUsername}/" +
                            $"{imageFileName}";
                    }
                }
                else
                {
                    Directory.CreateDirectory(
                        newFolder
                    );
                }


                // =========================================================
                // NEW MAIN IMAGE
                // =========================================================

                if (profile.MyFile != null &&
                    profile.MyFile.Length > 0)
                {
                    var oldImage =
                        existingProfile.Image;

                    existingProfile.Image =
                        await SaveProfileImageAsync(
                            profile.MyFile,
                            newFolder,
                            newUsername,
                            existingProfile.NationalCode
                        );

                    // اگر نام و پسوند تصویر جدید با قبلی یکی باشد، فایل جایگزین شده و نباید پاک شود
                    if (!string.Equals(
                            oldImage,
                            existingProfile.Image,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        DeleteImage(oldImage);
                    }
                }


                // =========================================================
                // NEW ATTACHMENTS
                // =========================================================

                if (profile.AttachmentsFiles != null &&
                    profile.AttachmentsFiles.Count > 0)
                {
                    foreach (var file in
                             profile.AttachmentsFiles)
                    {
                        if (file == null ||
                            file.Length <= 0)
                            continue;

                        var extension =
                            Path.GetExtension(
                                file.FileName
                            );

                        var storedFileName =
                            $"{Guid.NewGuid():N}{extension}";

                        var physicalPath =
                            Path.Combine(
                                newFolder,
                                storedFileName
                            );

                        await using var stream =
                            new FileStream(
                                physicalPath,
                                FileMode.CreateNew
                            );

                        await file.CopyToAsync(stream);


                        var attachment =
                            new ProfileAttachment
                            {
                                ProfileId =
                                    existingProfile.Id,

                                FileName =
                                    Path.GetFileName(
                                        file.FileName
                                    ),

                                FilePath =
                                    physicalPath,

                                FileUrl =
                                    $"/uploads/profiles/" +
                                    $"{newUsername}/" +
                                    $"{storedFileName}",

                                CreateDate =
                                    DateTime.Now.ToString(
                                        "yyyy-MM-dd HH:mm:ss"
                                    )
                            };

                        _context.ProfileAttachment.Add(
                            attachment
                        );
                    }
                }


                // =========================================================
                // MODIFY DATE
                // =========================================================

                existingProfile.ModifyDate =
                    DateTime.Now.ToString(
                        "yyyy-MM-dd HH:mm:ss"
                    );


                // =========================================================
                // SAVE
                // =========================================================

                await _context.SaveChangesAsync();


                return Json(new
                {
                    success = true,
                    id = existingProfile.Id,
                    usernameId =
                        existingProfile.UsernameId,
                    image =
                        existingProfile.Image,
                    message =
                        "تغییرات با موفقیت ذخیره شد."
                });
            }
            catch (DbUpdateException)
            {
                return Conflict(new
                {
                    success = false,
                    message =
                        "ثبت تغییرات انجام نشد. احتمالاً شناسه کاربری تکراری است."
                });
            }
            catch (Exception ex)
            {
                if (folderRenamed &&
                    Directory.Exists(newFolder) &&
                    !Directory.Exists(oldFolder))
                {
                    try
                    {
                        Directory.Move(
                            newFolder,
                            oldFolder
                        );
                    }
                    catch
                    {
                    }
                }

                return StatusCode(500, new
                {
                    success = false,
                    message = ex.Message,
                    inner = ex.InnerException?.Message
                });
            }
        }


        // =========================================================
        // DELETE OLD IMAGE
        // =========================================================

        private void DeleteImage(string? imagePath)
        {
            if (string.IsNullOrWhiteSpace(
                    imagePath))
            {
                return;
            }


            var relative =
                imagePath.TrimStart(
                    '/',
                    '\\'
                );


            var physicalPath =
                Path.Combine(
                    _environment.WebRootPath,
                    relative.Replace(
                        '/',
                        Path.DirectorySeparatorChar
                    )
                );


            if (System.IO.File.Exists(
                    physicalPath))
            {
                try
                {
                    System.IO.File.Delete(
                        physicalPath
                    );
                }
                catch
                {
                    // حذف فایل نباید ذخیره رکورد را متوقف کند.
                }
            }
        }

        // =========================================================
        // SAVE IMAGE
        // =========================================================


        private async Task<string> SaveProfileImageAsync(
            IFormFile file,
            string profileFolder,
            string usernameId,
            string? nationalCode)
        {
            if (file == null || file.Length == 0)
                throw new ArgumentException("فایل تصویر معتبر نیست.");

            var extension =
                Path.GetExtension(file.FileName).ToLowerInvariant();

            var allowedExtensions = new[]
            {
                ".jpg",
                ".jpeg",
                ".png",
                ".webp"
            };

            if (!allowedExtensions.Contains(extension))
            {
                throw new InvalidOperationException(
                    "فرمت تصویر مجاز نیست."
                );
            }

            // نام فایل: کد ملی؛ اگر نبود، شناسهٔ رکورد (username)
            var baseName =
                SafeFileName(
                    NationalCodeName(nationalCode) ?? usernameId
                );

            var fileName = baseName + extension;

            var finalPath = Path.Combine(profileFolder, fileName);

            var tempPath =
                Path.Combine(
                    profileFolder,
                    $".{Guid.NewGuid():N}.tmp"
                );

            await using (var stream =
                new FileStream(tempPath, FileMode.CreateNew))
            {
                await file.CopyToAsync(stream);
            }

            // اگر تصویر هم‌نام قبلی وجود داشته باشد، جایگزین می‌شود
            System.IO.File.Move(tempPath, finalPath, overwrite: true);

            return
                $"/uploads/profiles/" +
                $"{usernameId}/{fileName}";
        }

        // =========================================================
        // DELETE ATTACHMENT
        // =========================================================

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteAttachment(int id)
        {
            var attachment =
                await _context.ProfileAttachment
                    .FirstOrDefaultAsync(x => x.Id == id);

            if (attachment == null)
            {
                return NotFound(new
                {
                    success = false,
                    message = "پیوست پیدا نشد."
                });
            }

            var physicalPath = ResolveAttachmentPath(attachment);

            _context.ProfileAttachment.Remove(attachment);

            await _context.SaveChangesAsync();

            if (physicalPath != null &&
                System.IO.File.Exists(physicalPath))
            {
                try
                {
                    System.IO.File.Delete(physicalPath);
                }
                catch
                {
                    // حذف فایل نباید نتیجهٔ حذف رکورد را خراب کند.
                }
            }

            return Json(new { success = true });
        }

        // =========================================================
        // MERSAD — خروجی ZIP پیوست‌های ردیف‌های انتخاب‌شده
        // برای هر کد ملی یک پوشه؛ فقط پیوست‌ها (بدون تصویر پروفایل).
        // ردیف بدون کد ملی نادیده گرفته می‌شود.
        // گزارش در هدر X-Mersad-Report (Base64 از JSON) برمی‌گردد.
        // =========================================================

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Mersad(List<int>? ids)
        {
            if (ids == null || ids.Count == 0)
            {
                return BadRequest(new
                {
                    success = false,
                    message = "هیچ ردیفی انتخاب نشده است."
                });
            }

            ids = ids.Distinct().ToList();

            var profiles =
                await _context.Profile
                    .AsNoTracking()
                    .Include(x => x.Attachments)
                    .Where(x => ids.Contains(x.Id))
                    .OrderBy(x => x.Id)
                    .ToListAsync();

            var notFound = ids.Count - profiles.Count;

            var skippedNoCode = new List<string>();
            var noAttachments = new List<string>();
            var missingFiles = new List<string>();

            var folders =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var usedNames =
                new Dictionary<string, HashSet<string>>(
                    StringComparer.OrdinalIgnoreCase
                );

            var exportedProfiles = 0;
            var filesCount = 0;

            var tempZipPath =
                Path.Combine(
                    Path.GetTempPath(),
                    $"mersad_{Guid.NewGuid():N}.zip"
                );

            var zipStream =
                new FileStream(
                    tempZipPath,
                    FileMode.Create,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    81920,
                    FileOptions.DeleteOnClose
                );

            try
            {
                using (var zip =
                    new ZipArchive(
                        zipStream,
                        ZipArchiveMode.Create,
                        leaveOpen: true))
                {
                    foreach (var profile in profiles)
                    {
                        var label =
                            string.IsNullOrWhiteSpace(profile.UsernameId)
                                ? profile.Id.ToString()
                                : profile.UsernameId;

                        var folder = NationalCodeName(profile.NationalCode);

                        if (folder == null)
                        {
                            skippedNoCode.Add(label);
                            continue;
                        }

                        // ردیف بدون پیوست نادیده گرفته می‌شود و پوشهٔ خالی ساخته نمی‌شود
                        if (profile.Attachments.Count == 0)
                        {
                            noAttachments.Add(label);
                            continue;
                        }

                        var profileFiles = 0;

                        foreach (var attachment in
                                 profile.Attachments.OrderBy(a => a.Id))
                        {
                            var physicalPath =
                                ResolveAttachmentPath(attachment);

                            if (physicalPath == null ||
                                !System.IO.File.Exists(physicalPath))
                            {
                                missingFiles.Add(
                                    label + " / " + attachment.FileName
                                );

                                continue;
                            }

                            // پوشه فقط وقتی ساخته می‌شود که حداقل یک فایل در آن برود
                            if (!usedNames.TryGetValue(folder, out var used))
                            {
                                used = new HashSet<string>();
                                usedNames[folder] = used;
                                folders.Add(folder);
                            }

                            var entryName =
                                UniqueName(
                                    used,
                                    SafeFileName(attachment.FileName)
                                );

                            var entry =
                                zip.CreateEntry(
                                    folder + "/" + entryName,
                                    CompressionLevel.Fastest
                                );

                            await using var entryStream = entry.Open();

                            await using var source =
                                new FileStream(
                                    physicalPath,
                                    FileMode.Open,
                                    FileAccess.Read,
                                    FileShare.Read
                                );

                            await source.CopyToAsync(entryStream);

                            filesCount++;
                            profileFiles++;
                        }

                        if (profileFiles > 0)
                            exportedProfiles++;
                    }
                }

                var report = new
                {
                    selected = ids.Count,
                    notFound,
                    exportedProfiles,
                    folders = folders.Count,
                    files = filesCount,
                    skippedNoNationalCode = skippedNoCode.Count,
                    skippedNames = skippedNoCode.Take(100).ToList(),
                    noAttachments = noAttachments.Count,
                    noAttachmentNames = noAttachments.Take(100).ToList(),
                    missingFiles = missingFiles.Count,
                    missingNames = missingFiles.Take(100).ToList()
                };

                if (exportedProfiles == 0)
                {
                    await zipStream.DisposeAsync();

                    return Json(new
                    {
                        success = false,
                        message =
                            "هیچ‌کدام از ردیف‌های انتخاب‌شده کد ملی و پیوست همزمان نداشتند؛ فایلی ساخته نشد.",
                        report
                    });
                }

                var reportJson =
                    JsonSerializer.Serialize(
                        report,
                        new JsonSerializerOptions(JsonSerializerDefaults.Web)
                    );

                Response.Headers["X-Mersad-Report"] =
                    Convert.ToBase64String(
                        Encoding.UTF8.GetBytes(reportJson)
                    );

                zipStream.Seek(0, SeekOrigin.Begin);

                return File(
                    zipStream,
                    "application/zip",
                    $"Mersad_{DateTime.Now:yyyyMMdd_HHmmss}.zip"
                );
            }
            catch (Exception ex)
            {
                await zipStream.DisposeAsync();

                return StatusCode(500, new
                {
                    success = false,
                    message = ex.Message
                });
            }
        }

        // =========================================================
        // CREATE PROFILE RELATION
        // =========================================================

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateProfileRelation(int fromProfileId, int toProfileId, int relationId)
        {
            if (fromProfileId <= 0 ||
                toProfileId <= 0 ||
                relationId <= 0)
            {
                return BadRequest(new
                {
                    success = false,
                    message = "اطلاعات رابطه معتبر نیست."
                });
            }


            if (fromProfileId == toProfileId)
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "یک رکورد نمی‌تواند رابطه مستقیم با خودش داشته باشد."
                });
            }


            var fromExists =
                await _context.Profile
                    .AnyAsync(
                        x => x.Id == fromProfileId
                    );

            var toExists =
                await _context.Profile
                    .AnyAsync(
                        x => x.Id == toProfileId
                    );

            var relationExists =
                await _context.Relation
                    .AnyAsync(
                        x => x.Id == relationId
                    );


            if (!fromExists ||
                !toExists ||
                !relationExists)
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "پروفایل یا نوع رابطه پیدا نشد."
                });
            }


            var exists =
                await _context.ProfileRelation
                    .AnyAsync(x =>
                        x.FromProfileId ==
                            fromProfileId &&
                        x.ToProfileId ==
                            toProfileId &&
                        x.RelationId ==
                            relationId
                    );


            if (exists)
            {
                return Json(new
                {
                    success = true,
                    message =
                        "این رابطه قبلاً ثبت شده است."
                });
            }


            var profileRelation =
                new ProfileRelation
                {
                    FromProfileId =
                        fromProfileId,

                    ToProfileId =
                        toProfileId,

                    RelationId =
                        relationId
                };


            _context.ProfileRelation.Add(
                profileRelation
            );


            await _context.SaveChangesAsync();


            return Json(new
            {
                success = true,
                id = profileRelation.Id,
                message =
                    "رابطه با موفقیت ایجاد شد."
            });
        }


        // =========================================================
        // GET PROFILE RELATIONS
        // =========================================================

        [HttpGet]
        public async Task<IActionResult> GetProfileRelations(int profileId)
        {
            var relations = await _context.ProfileRelation
                .Where(x =>
                    x.FromProfileId == profileId ||
                    x.ToProfileId == profileId)
                .Select(x => new
                {
                    id = x.Id,

                    relationId = x.RelationId,

                    fromProfileId = x.FromProfileId,

                    toProfileId = x.ToProfileId,

                    // طرف مقابل این رابطه نسبت به پروفایل فعلی
                    otherProfileId =
                        x.FromProfileId == profileId
                            ? x.ToProfileId
                            : x.FromProfileId,

                    relation = x.Relation.Type
                })
                .ToListAsync();

            return Json(relations);
        }

        // =========================================================
        // DELETE PROFILE RELATIONS
        // =========================================================

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult>
            DeleteProfileRelations(int profileId)
        {
            var relations =
                await _context.ProfileRelation
                    .Where(x =>
                        x.FromProfileId ==
                        profileId)
                    .ToListAsync();


            if (relations.Count > 0)
            {
                _context.ProfileRelation.RemoveRange(
                    relations
                );

                await _context.SaveChangesAsync();
            }


            return Json(new
            {
                success = true
            });
        }


        // =========================================================
        // GRAPH
        // =========================================================

        [HttpGet]
        public async Task<IActionResult> GetGraph()
        {
            var profiles =
                await _context.Profile
                    .Select(x => new
                    {
                        id = x.Id,

                        title =
                            !string.IsNullOrWhiteSpace(
                                x.UsernameId
                            )
                                ? x.UsernameId
                                : (
                                    (
                                        x.FirstName ??
                                        ""
                                    ) +
                                    " " +
                                    (
                                        x.LastName ??
                                        ""
                                    )
                                ).Trim()
                    })
                    .ToListAsync();


            var relations =
                await _context.ProfileRelation
                    .Include(x => x.Relation)
                    .Select(x => new
                    {
                        from =
                            x.FromProfileId,

                        to =
                            x.ToProfileId,

                        relation =
                            x.Relation.Type
                    })
                    .ToListAsync();


            var usedIds =
                relations
                    .SelectMany(
                        x => new[]
                        {
                            x.from,
                            x.to
                        }
                    )
                    .Distinct()
                    .ToHashSet();


            var nodes =
                profiles
                    .Where(x =>
                        usedIds.Contains(x.id))
                    .Select(x => new
                    {
                        id = x.id,
                        title =
                            string.IsNullOrWhiteSpace(
                                x.title
                            )
                                ? $"رکورد {x.id}"
                                : x.title
                    })
                    .ToList();


            return Json(new
            {
                nodes,
                edges = relations
            });
        }





    }
}