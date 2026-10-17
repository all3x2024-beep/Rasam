namespace Rasam.Models.Accounting
{
    // سه سطح دسترسی
    public static class AppRoles
    {
        public const string Admin  = "Admin";    // مدیر: همه‌چیز
        public const string Editor = "Editor";   // ویرایشگر: مشاهده + ویرایش رکوردها
        public const string Viewer = "Viewer";   // مشاهده‌گر: فقط مواردی که مدیر تیک زده

        // برای [Authorize(Roles = AppRoles.Editors)]
        public const string Editors = Admin + "," + Editor;

        public static readonly string[] All = { Admin, Editor, Viewer };
    }

    // مجوزهای قابل تیک‌زدن برای «مشاهده‌گر» (در جدول AspNetUserClaims ذخیره می‌شوند)
    public static class Permissions
    {
        public const string ClaimType = "perm";

        public const string RecordsView     = "records.view";      // جدول رکوردها
        public const string GraphView       = "graph.view";        // گراف روابط
        public const string AttachmentsView = "attachments.view";  // پیوست‌ها و گالری
        public const string SensitiveView   = "sensitive.view";    // کد ملی و موبایل
        public const string MersadExport    = "mersad.export";     // خروجی مرصاد

        public static readonly (string Key, string Title)[] All =
        {
            (RecordsView,     "مشاهدهٔ جدول رکوردها"),
            (GraphView,       "مشاهدهٔ گراف روابط"),
            (AttachmentsView, "مشاهدهٔ پیوست‌ها و گالری"),
            (SensitiveView,   "مشاهدهٔ کد ملی و موبایل"),
            (MersadExport,    "دانلود خروجی مرصاد"),
        };
    }

    public class UserAccessViewModel
    {
        public string UserId { get; set; } = string.Empty;
        public string? UserName { get; set; }
        public string Role { get; set; } = AppRoles.Viewer;
        public List<string> Allowed { get; set; } = new();
    }
}
