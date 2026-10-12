using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Rasam.Data;
using Rasam.Models.MetaData;
using Rasam.Models.SocialAccount;

namespace Rasam.Controllers
{
    /// <summary>
    /// Backup / Restore / Delete-all.
    /// The Excel workbook (DATA.xlsx) is built and read in the browser (xlsx_full_min.js);
    /// this controller only exposes the database as JSON and imports rows one profile at a time.
    /// Existing data is never deleted or overwritten – duplicates are skipped.
    /// </summary>
    [Authorize]
    public class BackupController : Controller
    {
        // =====================================================
        // Adjust these two values to match how your app stores images
        // =====================================================

        /// <summary>Folder (under wwwroot) where profile images live.</summary>
        private const string ImageFolder = "uploads/profiles";

        /// <summary>
        /// true  → Profile.Image = "/uploads/profiles/abc.jpg"
        /// false → Profile.Image = "abc.jpg"
        /// (export understands both forms; this only affects newly imported images)
        /// </summary>
        private const bool StoreImageAsPath = true;

        private static readonly string[] AllowedImageExt = { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp" };
        private const long MaxImageBytes = 20L * 1024 * 1024;

        private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

        private readonly ApplicationDbContext _db;
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<BackupController> _logger;

        public BackupController(
            ApplicationDbContext db,
            IWebHostEnvironment env,
            ILogger<BackupController> logger)
        {
            _db = db;
            _env = env;
            _logger = logger;
        }

        // =====================================================
        // DTOs (JSON sent by the layout script)
        // =====================================================

        public class MetaPayload
        {
            public List<string>? Topics { get; set; }
            public List<string>? Tags { get; set; }
            public List<string>? RelationTypes { get; set; }
        }

        public class SocialPayload
        {
            public string? Platform { get; set; }
            public string? Value { get; set; }
        }

        public class ProfilePayload
        {
            public string? UsernameId { get; set; }
            public string? Pid { get; set; }
            public string? FirstName { get; set; }
            public string? LastName { get; set; }
            public string? NationalCode { get; set; }
            public string? Mobile { get; set; }
            public string? Country { get; set; }
            public string? Topic { get; set; }
            public List<string>? Tags { get; set; }
            public string? Notes { get; set; }
            public string? CreatedAt { get; set; }
            public string? CreatedBy { get; set; }
            public string? ModifiedAt { get; set; }
            public string? ModifiedBy { get; set; }
            public List<SocialPayload>? SocialLinks { get; set; }
        }

        public class RelationItem
        {
            public string? FromId { get; set; }
            public string? ToId { get; set; }
            public string? Type { get; set; }
        }

        public class RelationsPayload
        {
            public List<RelationItem>? Items { get; set; }
        }

        // =====================================================
        // EXPORT  (GET /Backup/ExportData)
        // =====================================================

        [HttpGet]
        public async Task<IActionResult> ExportData()
        {
            var topics = await _db.Topic.AsNoTracking()
                .OrderBy(x => x.Id)
                .Select(x => new { title = x.Title })
                .ToListAsync();

            var tags = await _db.Tag.AsNoTracking()
                .OrderBy(x => x.Id)
                .Select(x => new { title = x.Title })
                .ToListAsync();

            var relationTypes = await _db.Relation.AsNoTracking()
                .OrderBy(x => x.Id)
                .Select(x => new { type = x.Type })
                .ToListAsync();

            var raw = await _db.Profile.AsNoTracking()
                .AsSplitQuery()
                .OrderBy(p => p.Id)
                .Select(p => new
                {
                    p.Id,
                    p.UsernameId,
                    p.PID,
                    p.FirstName,
                    p.LastName,
                    p.NationalCode,
                    p.Phone,
                    p.Country,
                    p.Image,
                    TopicTitle = p.Topic != null ? p.Topic.Title : null,
                    Tags = p.Tag.Select(t => t.Title).ToList(),
                    Socials = p.SocialMedia.Select(s => new { s.Id, s.Name, s.Username }).ToList(),
                    p.Theory,
                    p.CreateDate,
                    p.CreateBY,
                    p.ModifyDate,
                    p.ModifyBY
                })
                .ToListAsync();

            // Stable key per profile (UsernameId; falls back to profile_<Id> when blank/duplicated)
            var keyOf = new Dictionary<int, string>();
            var usedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in raw)
            {
                var key = string.IsNullOrWhiteSpace(p.UsernameId) ? $"profile_{p.Id}" : p.UsernameId.Trim();
                if (!usedKeys.Add(key))
                {
                    key = $"{key}__{p.Id}";
                    usedKeys.Add(key);
                }
                keyOf[p.Id] = key;
            }

            var profiles = raw.Select(p => new
            {
                usernameId = keyOf[p.Id],
                pid = p.PID,
                firstName = p.FirstName,
                lastName = p.LastName,
                nationalCode = p.NationalCode,
                mobile = p.Phone,
                country = p.Country,
                topic = p.TopicTitle,
                tags = p.Tags,
                notes = p.Theory,
                createdAt = p.CreateDate,
                createdBy = p.CreateBY,
                modifiedAt = p.ModifyDate,
                modifiedBy = p.ModifyBY
            }).ToList();

            var socialLinks = raw
                .SelectMany(p => p.Socials.Select(s => new
                {
                    id = $"social_{s.Id}",
                    profileId = keyOf[p.Id],
                    platform = s.Name,
                    value = s.Username
                }))
                .ToList();

            var relRows = await _db.ProfileRelation.AsNoTracking()
                .OrderBy(r => r.Id)
                .Select(r => new { r.Id, r.FromProfileId, r.ToProfileId, Type = r.Relation.Type })
                .ToListAsync();

            var relations = relRows
                .Where(r => keyOf.ContainsKey(r.FromProfileId) && keyOf.ContainsKey(r.ToProfileId))
                .Select(r => new
                {
                    id = $"rel_{r.Id}",
                    fromId = keyOf[r.FromProfileId],
                    toId = keyOf[r.ToProfileId],
                    type = r.Type,
                    createdAt = (string?)null
                })
                .ToList();

            // Images (Profile.Image = one image per profile)
            var photos = new List<object>();
            var missingImages = 0;
            foreach (var p in raw)
            {
                if (string.IsNullOrWhiteSpace(p.Image)) continue;

                var rel = NormalizeImageRelative(p.Image);
                var physical = ResolvePhysical(rel);

                if (rel == null || physical == null || !System.IO.File.Exists(physical))
                {
                    missingImages++;
                    continue;
                }

                var url = Url.Content("~/" + string.Join("/", rel.Split('/').Select(Uri.EscapeDataString)));
                var fileName = Path.GetFileName(physical);

                photos.Add(new
                {
                    id = $"photo_{p.Id}",
                    profileId = keyOf[p.Id],
                    url,
                    fileName,
                    originalName = fileName
                });
            }

            return Json(new
            {
                topics,
                tags,
                relationTypes,
                profiles,
                relations,
                socialLinks,
                photos,
                missingImages
            });
        }

        // =====================================================
        // IMPORT – topics / tags / relation types
        // =====================================================

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ImportMeta(string payload)
        {
            var data = Parse<MetaPayload>(payload);
            if (data == null)
                return Json(new { success = false, message = "داده‌های ارسالی نامعتبر است." });

            try
            {
                var topicsAdded = 0;
                var tagsAdded = 0;
                var typesAdded = 0;

                var topicSet = new HashSet<string>(
                    (await _db.Topic.AsNoTracking().Select(x => x.Title).ToListAsync()).Select(Title),
                    StringComparer.OrdinalIgnoreCase);

                foreach (var raw in data.Topics ?? new())
                {
                    var t = Title(raw);
                    if (t.Length == 0 || !topicSet.Add(t)) continue;
                    _db.Topic.Add(new Topic { Title = t });
                    topicsAdded++;
                }

                var tagSet = new HashSet<string>(
                    (await _db.Tag.AsNoTracking().Select(x => x.Title).ToListAsync()).Select(Title),
                    StringComparer.OrdinalIgnoreCase);

                foreach (var raw in data.Tags ?? new())
                {
                    var t = Title(raw);
                    if (t.Length == 0 || !tagSet.Add(t)) continue;
                    _db.Tag.Add(new Tag { Title = t });
                    tagsAdded++;
                }

                var typeSet = new HashSet<string>(
                    (await _db.Relation.AsNoTracking().Select(x => x.Type).ToListAsync()).Select(Title),
                    StringComparer.OrdinalIgnoreCase);

                foreach (var raw in data.RelationTypes ?? new())
                {
                    var t = Title(raw);
                    if (t.Length == 0 || !typeSet.Add(t)) continue;
                    _db.Relation.Add(new Relation { Type = t });
                    typesAdded++;
                }

                await _db.SaveChangesAsync();

                return Json(new { success = true, topicsAdded, tagsAdded, relationTypesAdded = typesAdded });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Backup ImportMeta failed");
                return Json(new { success = false, message = "ثبت موضوعات، تگ‌ها و انواع رابطه انجام نشد." });
            }
        }

        // =====================================================
        // IMPORT – one profile (JSON + optional image)
        // =====================================================

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(104857600)]
        [RequestFormLimits(MultipartBodyLengthLimit = 104857600)]
        public async Task<IActionResult> ImportProfile(string payload, IFormFile[]? photos)
        {
            var data = Parse<ProfilePayload>(payload);
            if (data == null)
                return Json(new { success = false, message = "داده‌های ارسالی نامعتبر است." });

            var uid = Cut(data.UsernameId, 100);
            if (uid.Length == 0)
                return Json(new { success = false, message = "شناسه (نام کاربری) خالی است." });

            var nationalCode = DigitsOnly(data.NationalCode);
            var nationalKey = Regex.IsMatch(nationalCode, @"^\d{10}$") ? nationalCode : "";

            string? savedPhysical = null;

            try
            {
                // ---- duplicate check: same UsernameId OR same 10-digit national code ----
                var dup = await _db.Profile.AsNoTracking()
                    .Where(p => p.UsernameId == uid)
                    .Select(p => new { p.UsernameId })
                    .FirstOrDefaultAsync();

                if (dup != null)
                {
                    return Json(new
                    {
                        success = true,
                        status = "duplicate",
                        usernameId = dup.UsernameId,
                        reason = "شناسه از قبل ثبت شده است."
                    });
                }

                if (nationalKey.Length > 0)
                {
                    var dupNid = await _db.Profile.AsNoTracking()
                        .Where(p => p.NationalCode == nationalKey)
                        .Select(p => new { p.UsernameId })
                        .FirstOrDefaultAsync();

                    if (dupNid != null)
                    {
                        return Json(new
                        {
                            success = true,
                            status = "duplicate",
                            usernameId = dupNid.UsernameId,
                            reason = "کد ملی از قبل ثبت شده است."
                        });
                    }
                }

                var warnings = new List<string>();

                var profile = new Profile
                {
                    UsernameId = uid,
                    PID = NullIfEmpty(Cut(data.Pid, 100)),
                    FirstName = NullIfEmpty(Cut(data.FirstName, 100)),
                    LastName = NullIfEmpty(Cut(data.LastName, 100)),
                    NationalCode = NullIfEmpty(Cut(nationalCode, 20)),
                    Phone = NullIfEmpty(Cut(DigitsOrRaw(data.Mobile), 30)),
                    Country = NullIfEmpty(Cut(data.Country, 100)),
                    Theory = string.IsNullOrWhiteSpace(data.Notes) ? null : data.Notes,
                    CreateDate = NullIfEmpty(Cut(data.CreatedAt, 100)) ?? NowPersian(),
                    CreateBY = NullIfEmpty(Cut(data.CreatedBy, 100)) ?? User.Identity?.Name,
                    ModifyDate = NullIfEmpty(Cut(data.ModifiedAt, 100)),
                    ModifyBY = NullIfEmpty(Cut(data.ModifiedBy, 100))
                };

                // ---- topic ----
                var topicTitle = Title(data.Topic);
                if (topicTitle.Length > 0)
                {
                    var topic = await _db.Topic.FirstOrDefaultAsync(t => t.Title == topicTitle);
                    if (topic == null)
                    {
                        topic = new Topic { Title = topicTitle };
                        _db.Topic.Add(topic);
                    }
                    profile.Topic = topic;
                }

                // ---- tags ----
                var tagTitles = (data.Tags ?? new())
                    .Select(Title)
                    .Where(t => t.Length > 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (tagTitles.Count > 0)
                {
                    if (TagsAreManyToMany)
                    {
                        foreach (var title in tagTitles)
                        {
                            var tag = await _db.Tag.FirstOrDefaultAsync(t => t.Title == title);
                            if (tag == null)
                            {
                                tag = new Tag { Title = title };
                                _db.Tag.Add(tag);
                            }
                            profile.Tag.Add(tag);
                        }
                    }
                    else
                    {
                        // Profile.Tag is a one-to-many (Tag has no back navigation), so a Tag row can belong
                        // to ONE profile only. Attaching shared tags here would steal them from other profiles.
                        warnings.Add("تگ‌های پروفایل‌ها اضافه نشدند: در مدل فعلی هر تگ فقط می‌تواند به یک پروفایل تعلق داشته باشد (رابطه یک‌به‌چند).");
                    }
                }

                // ---- social media ----
                foreach (var s in data.SocialLinks ?? new())
                {
                    var name = Cut(s.Platform, 100);
                    var username = Cut(s.Value, 100);
                    if (name.Length == 0 || username.Length == 0) continue;

                    profile.SocialMedia.Add(new SocialMedia { Name = name, Username = username });
                }

                // ---- image (the model keeps a single image per profile) ----
                var file = photos?.FirstOrDefault(f => f != null && f.Length > 0);
                if (file != null)
                {
                    var ext = Path.GetExtension(file.FileName ?? "").ToLowerInvariant();

                    if (!AllowedImageExt.Contains(ext))
                    {
                        warnings.Add($"فرمت تصویر «{file.FileName}» مجاز نیست و وارد نشد.");
                    }
                    else if (file.Length > MaxImageBytes)
                    {
                        warnings.Add($"حجم تصویر «{file.FileName}» بیش از حد مجاز است و وارد نشد.");
                    }
                    else
                    {
                        var dir = Path.Combine(_env.WebRootPath, ImageFolder.Replace('/', Path.DirectorySeparatorChar));
                        Directory.CreateDirectory(dir);

                        var fileName = Guid.NewGuid().ToString("N") + ext;
                        savedPhysical = Path.Combine(dir, fileName);

                        await using (var fs = new FileStream(savedPhysical, FileMode.Create))
                        {
                            await file.CopyToAsync(fs);
                        }

                        profile.Image = StoreImageAsPath ? $"/{ImageFolder}/{fileName}" : fileName;
                    }
                }

                _db.Profile.Add(profile);
                await _db.SaveChangesAsync();

                return Json(new
                {
                    success = true,
                    status = "added",
                    usernameId = profile.UsernameId,
                    photos = profile.Image != null ? 1 : 0,
                    warning = warnings.Count > 0 ? string.Join(" | ", warnings) : null
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Backup ImportProfile failed for {Id}", uid);

                if (savedPhysical != null)
                {
                    try { System.IO.File.Delete(savedPhysical); } catch { }
                }

                // make sure a half-built graph is not saved by a later request on this scoped context
                _db.ChangeTracker.Clear();

                return Json(new { success = false, message = "ثبت رکورد در پایگاه داده انجام نشد." });
            }
        }

        // =====================================================
        // IMPORT – relations between profiles
        // =====================================================

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ImportRelations(string payload)
        {
            var data = Parse<RelationsPayload>(payload);
            if (data?.Items == null)
                return Json(new { success = false, message = "داده‌های ارسالی نامعتبر است." });

            try
            {
                var items = data.Items
                    .Where(i => !string.IsNullOrWhiteSpace(i.FromId)
                             && !string.IsNullOrWhiteSpace(i.ToId)
                             && !string.IsNullOrWhiteSpace(i.Type))
                    .ToList();

                var usernames = items
                    .SelectMany(i => new[] { i.FromId!.Trim(), i.ToId!.Trim() })
                    .Distinct()
                    .ToList();

                var profileRows = await _db.Profile.AsNoTracking()
                    .Where(p => p.UsernameId != null && usernames.Contains(p.UsernameId))
                    .Select(p => new { p.Id, p.UsernameId })
                    .ToListAsync();

                var profileIdByUsername = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (var p in profileRows)
                    profileIdByUsername.TryAdd(p.UsernameId!, p.Id);

                var relationByType = new Dictionary<string, Relation>(StringComparer.OrdinalIgnoreCase);
                foreach (var r in await _db.Relation.ToListAsync())
                    relationByType.TryAdd(Title(r.Type), r);

                var involved = profileIdByUsername.Values.Distinct().ToList();

                var existing = new HashSet<(int From, int To, int Rel)>();
                if (involved.Count > 0)
                {
                    var rows = await _db.ProfileRelation.AsNoTracking()
                        .Where(r => involved.Contains(r.FromProfileId) && involved.Contains(r.ToProfileId))
                        .Select(r => new { r.FromProfileId, r.ToProfileId, r.RelationId })
                        .ToListAsync();

                    foreach (var r in rows)
                        existing.Add((r.FromProfileId, r.ToProfileId, r.RelationId));
                }

                var added = 0;
                var skipped = 0;

                // dedupe inside this batch too (covers relation types created a moment ago, which have no Id yet)
                var batch = new HashSet<(int From, int To, string Type)>();

                foreach (var item in items)
                {
                    if (!profileIdByUsername.TryGetValue(item.FromId!.Trim(), out var fromId) ||
                        !profileIdByUsername.TryGetValue(item.ToId!.Trim(), out var toId) ||
                        fromId == toId)
                    {
                        skipped++;
                        continue;
                    }

                    var typeTitle = Title(item.Type);
                    if (typeTitle.Length == 0)
                    {
                        skipped++;
                        continue;
                    }

                    var typeKey = typeTitle.ToUpperInvariant();

                    if (batch.Contains((fromId, toId, typeKey)) || batch.Contains((toId, fromId, typeKey)))
                    {
                        skipped++;
                        continue;
                    }

                    if (!relationByType.TryGetValue(typeTitle, out var relation))
                    {
                        relation = new Relation { Type = typeTitle };
                        _db.Relation.Add(relation);
                        relationByType[typeTitle] = relation;
                    }

                    var relId = relation.Id; // 0 for a brand-new type

                    if (relId != 0 &&
                        (existing.Contains((fromId, toId, relId)) || existing.Contains((toId, fromId, relId))))
                    {
                        skipped++;
                        continue;
                    }

                    _db.ProfileRelation.Add(new ProfileRelation
                    {
                        FromProfileId = fromId,
                        ToProfileId = toId,
                        Relation = relation
                    });

                    batch.Add((fromId, toId, typeKey));
                    added++;
                }

                await _db.SaveChangesAsync();

                return Json(new { success = true, added, skipped });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Backup ImportRelations failed");
                _db.ChangeTracker.Clear();
                return Json(new { success = false, message = "ثبت روابط انجام نشد." });
            }
        }

        // =====================================================
        // DELETE ALL DATA
        // Removes profiles, profile relations, social media, topics and profile images.
        // Tags and relation types (the settings lists) are kept.
        // =====================================================

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteAllData()
        {
            try
            {
                // remember image files first; they are removed only after the DB commit succeeds
                var imagePaths = await _db.Profile.AsNoTracking()
                    .Where(p => p.Image != null && p.Image != "")
                    .Select(p => p.Image!)
                    .ToListAsync();

                int relations, socialLinks, topics;
                var profiles = 0;

                await using (var tx = await _db.Database.BeginTransactionAsync())
                {
                    relations = await _db.ProfileRelation.ExecuteDeleteAsync();
                    socialLinks = await _db.SocialMedia.ExecuteDeleteAsync();

                    // Delete profiles in batches through the change tracker: EF detaches the Tag / Relation
                    // rows that point at them (they stay in the settings lists) whatever the FK mapping is.
                    while (true)
                    {
                        var batch = await _db.Profile
                            .Include(p => p.Tag)
                            .Include(p => p.Relation)
                            .OrderBy(p => p.Id)
                            .Take(500)
                            .ToListAsync();

                        if (batch.Count == 0) break;

                        _db.Profile.RemoveRange(batch);
                        await _db.SaveChangesAsync();
                        profiles += batch.Count;
                        _db.ChangeTracker.Clear();
                    }

                    topics = await _db.Topic.ExecuteDeleteAsync();

                    await tx.CommitAsync();
                }

                // ---- image files (only inside the profile image folder) ----
                var imageRoot = Path.GetFullPath(
                    Path.Combine(_env.WebRootPath, ImageFolder.Replace('/', Path.DirectorySeparatorChar)));
                var imagesDeleted = 0;

                foreach (var image in imagePaths.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    try
                    {
                        var physical = ResolvePhysical(NormalizeImageRelative(image));
                        if (physical == null ||
                            !physical.StartsWith(imageRoot, StringComparison.OrdinalIgnoreCase) ||
                            !System.IO.File.Exists(physical))
                            continue;

                        System.IO.File.Delete(physical);
                        imagesDeleted++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Could not delete image {Image}", image);
                    }
                }

                _logger.LogWarning("All data deleted by {User}: {Profiles} profiles, {Relations} relations",
                    User.Identity?.Name, profiles, relations);

                return Json(new { success = true, profiles, relations, socialLinks, topics, images = imagesDeleted });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Backup DeleteAllData failed");
                _db.ChangeTracker.Clear();
                return Json(new { success = false, message = "حذف اطلاعات انجام نشد؛ تغییری در پایگاه داده اعمال نشد." });
            }
        }

        // =====================================================
        // Helpers
        // =====================================================

        private bool TagsAreManyToMany =>
            _db.Model.FindEntityType(typeof(Profile))?.FindSkipNavigation(nameof(Profile.Tag)) != null;

        private static T? Parse<T>(string? json) where T : class
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try { return JsonSerializer.Deserialize<T>(json, JsonOpts); }
            catch { return null; }
        }

        private static string Cut(string? value, int max)
        {
            var s = (value ?? "").Trim();
            return s.Length > max ? s[..max] : s;
        }

        private static string? NullIfEmpty(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value;

        /// <summary>Trim, unify Arabic ي/ك to Persian ی/ک, collapse spaces, max 100 chars.</summary>
        private static string Title(string? value)
        {
            var s = (value ?? "")
                .Replace('\u064A', '\u06CC')
                .Replace('\u0643', '\u06A9');

            s = Regex.Replace(s, @"\s+", " ").Trim();
            return s.Length > 100 ? s[..100] : s;
        }

        private static string LatinDigits(string? value)
        {
            if (string.IsNullOrEmpty(value)) return "";

            var chars = value.ToCharArray();
            for (var i = 0; i < chars.Length; i++)
            {
                var c = chars[i];
                if (c >= '\u06F0' && c <= '\u06F9') chars[i] = (char)('0' + (c - '\u06F0'));
                else if (c >= '\u0660' && c <= '\u0669') chars[i] = (char)('0' + (c - '\u0660'));
            }
            return new string(chars);
        }

        private static string DigitsOnly(string? value) =>
            Regex.Replace(LatinDigits(value), @"\D", "");

        private static string DigitsOrRaw(string? value) => LatinDigits(value).Trim();

        /// <summary>Used only when the file has no creation date. Change to match your existing date format.</summary>
        private static string NowPersian()
        {
            var pc = new PersianCalendar();
            var n = DateTime.Now;
            return $"{pc.GetYear(n):0000}/{pc.GetMonth(n):00}/{pc.GetDayOfMonth(n):00} {n:HH:mm}";
        }

        private static string? NormalizeImageRelative(string? image)
        {
            if (string.IsNullOrWhiteSpace(image)) return null;

            var s = image.Trim().Replace('\\', '/');
            if (s.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return null; // external image

            s = s.TrimStart('~').TrimStart('/');
            if (!s.Contains('/')) s = $"{ImageFolder}/{s}";

            return s;
        }

        private string? ResolvePhysical(string? relative)
        {
            if (relative == null) return null;

            var root = Path.GetFullPath(_env.WebRootPath);
            var full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));

            // block path traversal
            return full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? full : null;
        }
    }
}
