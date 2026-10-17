using System.Globalization;
using Rasam.Data;
using Rasam.Models.Accounting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using System.Security.Cryptography;
using System.Text;


using Microsoft.EntityFrameworkCore.Metadata.Internal;

namespace Rasam.Controllers
{
    public class AccountingController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly UserManager<User> _UserManager;
        private SignInManager<User> _signManager;
        private readonly RoleManager<Role> _roleManager;
        private readonly ApplicationDbContext _context;
        private IWebHostEnvironment _webHostEnvironment;

        public AccountingController(ILogger<HomeController> logger, ApplicationDbContext context, UserManager<User> userManager, SignInManager<User> signManager, RoleManager<Role> roleManager, IWebHostEnvironment webHostEnvironment)
        {
            _logger = logger;
            _context = context;
            _UserManager = userManager;
            _signManager = signManager;
            _roleManager = roleManager;
            _webHostEnvironment = webHostEnvironment;
        }






    #region Deatail User Logger
    [HttpPost]
        [Route("Accounting/DevToolsLog")]
        public IActionResult DevToolsLog([FromBody] DevToolsLogModel model)
        {
            if (model == null) return BadRequest();


            return Ok();
        }

        // مدل ساده برای دریافت داده از جاوااسکریپت
        public class DevToolsLogModel
        {
            public string Message { get; set; } = string.Empty;
            public string Url { get; set; } = string.Empty;
        }


        #endregion

        public IActionResult UserIndex()
        {
            return View();
        }

        public IActionResult Index()
        {
            return View();
        }


        [HttpGet]
        public async Task<IActionResult> Active(string? id)
        {
            var findUser = await _UserManager.FindByIdAsync(id);

            try
            {
                if (_UserManager.IsEmailConfirmedAsync(findUser).Result)
                {
                    findUser.EmailConfirmed = !findUser.EmailConfirmed;
                    _context.Users.Update(findUser);
                    _context.SaveChanges();
                }
                else
                {
                    var token = await _UserManager.GenerateEmailConfirmationTokenAsync(findUser);
                    var res = await _UserManager.ConfirmEmailAsync(findUser, token);
                }
            }
            catch (Exception e)
            {
                return Content(e.Message);
            }

            return RedirectToAction("UserIndex", "Accounting");
        }



        [Authorize(Roles = AppRoles.Admin)]
        [HttpGet]
        public async Task<IActionResult> Access(string id)
        {
            var user = await _UserManager.FindByIdAsync(id);
            if (user == null) return NotFound();

            var roles = await _UserManager.GetRolesAsync(user);
            var claims = await _UserManager.GetClaimsAsync(user);

            return View(new UserAccessViewModel
            {
                UserId = user.Id,
                UserName = user.UserName,
                Role = roles.FirstOrDefault() ?? AppRoles.Viewer,
                Allowed = claims.Where(c => c.Type == Permissions.ClaimType).Select(c => c.Value).ToList()
            });
        }

        [Authorize(Roles = AppRoles.Admin)]
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Access(UserAccessViewModel model)
        {
            var user = await _UserManager.FindByIdAsync(model.UserId);
            if (user == null) return NotFound();

            if (!AppRoles.All.Contains(model.Role)) return BadRequest();

            var current = await _UserManager.GetRolesAsync(user);

            // ادمین آخر نباید نقشش را از دست بدهد
            if (current.Contains(AppRoles.Admin) && model.Role != AppRoles.Admin &&
                (await _UserManager.GetUsersInRoleAsync(AppRoles.Admin)).Count <= 1)
            {
                ModelState.AddModelError("", "حداقل یک مدیر باید باقی بماند.");
                return View(model);
            }

            await _UserManager.RemoveFromRolesAsync(user, current);
            await _UserManager.AddToRoleAsync(user, model.Role);

            // تیک‌ها فقط برای مشاهده‌گر ذخیره می‌شوند
            var oldClaims = (await _UserManager.GetClaimsAsync(user))
                .Where(c => c.Type == Permissions.ClaimType).ToList();
            await _UserManager.RemoveClaimsAsync(user, oldClaims);

            if (model.Role == AppRoles.Viewer)
            {
                var valid = (model.Allowed ?? new()).Where(k => Permissions.All.Any(p => p.Key == k));
                await _UserManager.AddClaimsAsync(user,
                    valid.Select(k => new System.Security.Claims.Claim(Permissions.ClaimType, k)));
            }

            await _UserManager.UpdateSecurityStampAsync(user);   // با ورود بعدی اعمال می‌شود
            return RedirectToAction("UserIndex");
        }

        [Authorize]
        public IActionResult AccessDenied() => View();

        [HttpGet]
        [Authorize]
        public IActionResult ChangeUser()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangeUser([Bind("MyFile")] User item)
        {
            try
            {
                if (item.MyFile is not null)
                {
                    var proUser = await _UserManager.FindByNameAsync(User.Identity.Name);
                    //وجود پوشه
                    string path = Path.Combine(_webHostEnvironment.WebRootPath, $"UploadFile/Accounting/User/{proUser.UserName}");
                    if (!Directory.Exists(path))
                    {
                        Directory.CreateDirectory(path);
                    }

                    //فایل تکراری
                    var filePathIMG = Path.Combine(_webHostEnvironment.WebRootPath, $"UploadFile/Accounting/User/{proUser.UserName}", item.MyFile.FileName);
                    if (System.IO.File.Exists(filePathIMG))
                    {
                        TempData["msg"] = ".فایل بارگذاری شده تکراری است، نام فایل تغییر دهید";
                        return View();
                    }

                    using (var stream = System.IO.File.Create(filePathIMG))
                    {
                        item.MyFile.CopyTo(stream);
                    }
                    proUser.CreateBY = $"UploadFile/Accounting/User/{proUser.UserName}/" + item.MyFile.FileName;

                    _context.Update(proUser);
                    await _context.SaveChangesAsync();
            
                    TempData["msg"] = ".پروفایل شما با موفقیت ثبت شد";

                    return View();
                }
            }
            catch (Exception ex)
            {
                string msg = "";

                foreach (var err in ModelState.Values.ToList())
                {
                    foreach (var item_e in err.Errors)
                    {
                        msg = msg + " | " + item_e.ErrorMessage.ToString();
                        msg = msg + " | " + ex.Message.ToString();
                        TempData["msg"] = msg.ToString();
                    }
                }
            }
            return RedirectToAction("Details", "Companies", new { Id = item.Id });
        }

        [HttpGet]
        [Authorize]
        public IActionResult ChangePassword()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangePassword(string? CurrentPassword, string? NewPassword)
        {
            if (ModelState.IsValid)
            {
                var finduser = await _UserManager.FindByNameAsync(User.Identity.Name);

                var result = await _UserManager.ChangePasswordAsync(finduser, CurrentPassword, NewPassword);
                if (result.Succeeded)
                {
                    TempData["msg"] = "رمز عبور جدید شما با موفقیت ثبت شد.";
               

                    return View();
                }
                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }
            }
            return View();
        }

        [HttpGet]

        public async Task<IActionResult> ResetPassword(string id)
        {
            var user = await _UserManager.FindByIdAsync(id);
            if (user == null)
            {
                return NotFound("کاربر پیدا نشد.");
            }
            string token = await _UserManager.GeneratePasswordResetTokenAsync(user);
            // تغییر پسورد به "123456"
            var result = await _UserManager.ResetPasswordAsync(user, token, "Aa@123456");

            if (result.Succeeded)
            {
                TempData["msg"] = $"رمز عبور کاربر {user.UserName} به «Aa@123456» با موفقیت تغییر کرد.";

              
            }

            // در صورت بروز خطا، خطاها را به مدل اضافه کنید
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
                TempData["msg"] = error.Description;
            }

            return RedirectToAction("UserIndex", "Accounting");
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([FromForm] User item)
        {
            PersianCalendar p = new PersianCalendar();
            DateTime now = DateTime.Now;
            string date = $"{p.GetYear(now):0000}/{p.GetMonth(now):00}/{p.GetDayOfMonth(now):00}";
            string clock = $"{p.GetHour(now):00}:{p.GetMinute(now):00}";

            try
            {
                item.UserName = item.UsernameTemp;
                item.DateRegister = $"{date} - {clock}";
                item.CreateBY = User.Identity?.Name;

                // بررسی تکراری بودن نام کاربری
                var findUser = await _UserManager.FindByNameAsync(item.UsernameTemp);
                if (findUser is not null)
                {
                    TempData["msg"] = "این نام کاربری قبلاً ثبت شده است.";
                    return RedirectToAction("UserIndex", "Accounting");
                }

                // ایجاد کاربر جدید
                IdentityResult identityResult = await _UserManager.CreateAsync(item, item.PassWordTemp);

                if (identityResult.Succeeded)
                {
                    TempData["msg"] = "ثبت‌نام کاربر با موفقیت انجام شد.";
                }
                else
                {
                    // برسی خطاهای Identity (مانند قوانین پیچیدگی رمز عبور)
                    string errors = string.Join(" | ", identityResult.Errors.Select(e => e.Description));

                    if (errors.Contains("uppercase") || errors.Contains("digit") || errors.Contains("alphanumeric"))
                    {
                        TempData["msg"] = "رمز عبور باید شامل حروف کوچک، بزرگ، اعداد و کاراکترهای خاص باشد.";
                    }
                    else
                    {
                        TempData["msg"] = "خطا در ثبت کاربر: " + errors;
                    }
                }
            }
            catch (Exception e)
            {
                TempData["msg"] = "خطای غیرمنتظره: " + e.Message;
            }

            return RedirectToAction("UserIndex", "Accounting");
        }

        [HttpGet]
        public async Task<IActionResult> Edit(string? Id)
        {
            var user = await _UserManager.FindByIdAsync(Id);
            return View(user);
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit([FromForm] User item)
        {
            var user = await _UserManager.FindByIdAsync(item.Id);
            user.FirstName = item.FirstName;
            user.LastName = item.LastName;


            #region Role
            // گرفتن نقش‌های فعلی کاربر
            var currentRoles = await _UserManager.GetRolesAsync(user);

            // حذف نقش‌های فعلی
            var removeResult = await _UserManager.RemoveFromRolesAsync(user, currentRoles);
            if (!removeResult.Succeeded)
            {
                TempData["msg"] = "خطا در حذف نقش‌های قبلی";
                return RedirectToAction("UserIndex", "Accounting");
            }
            var roleExist = await _roleManager.RoleExistsAsync(item.RoleTemp);
            if (!roleExist)
            {
                var roleResult = await _roleManager.CreateAsync(new Role { Name = item.RoleTemp });
            }
            var addResult = await _UserManager.AddToRoleAsync(user, item.RoleTemp);
            if (!addResult.Succeeded)
            {
                TempData["msg"] = "خطا در اضافه کردن نقش جدید";
                return RedirectToAction("UserIndex", "Accounting");
            }

            if (item.RoleTemp == "انتخاب کنید" )
            {
                TempData["msg"] = "نقش انتخاب نشد!";
                return RedirectToAction("UserIndex", "Accounting");
            }
            user.RoleTemp = item.RoleTemp;

            #endregion
            try
            {
                _context.Update(user);
                await _context.SaveChangesAsync();
            }
            catch (Exception e)
            {
                TempData["msg"] = e.Message.ToString();
                return View(user);
            }
         
            TempData["msg"] = "ویرایش   با موفقیت انجام شد.";
            return RedirectToAction("UserIndex", "Accounting");
        }



        [HttpGet]
        public IActionResult LogIn()
        {
            return View();
        }
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> LogIn([Bind("UsernameTemp,PassWordTemp,TowFactorCode")] User item)
        {
            string s = "";
            try
            {
                var user = await _UserManager.FindByNameAsync(item.UsernameTemp);
                if (user == null)
                {
                    TempData["msg"] = "شما ثبت نام نکرده‌اید";
                    return View(item);
                }
                if (!await _UserManager.IsEmailConfirmedAsync(user))
                {
                    TempData["msg"] = "حساب کاربری توسط مدیرسیستم فعال نشده است. با شماره ×××××× تماس حاصل فرمایید.";
                    return View(item);
                }
                // بررسی رمز عبور
                if (!await _UserManager.CheckPasswordAsync(user, item.PassWordTemp))
                {
                    TempData["msg"] = "نام کاربری یا رمز عبور صحیح نیست.";
                    return View(item);
                }

             

                // ورود موفق
                await _signManager.SignInAsync(user, isPersistent: false);
                return RedirectToAction("Index", "Home");
            }
            catch (Exception e)
            {
                TempData["msg"] = e.Message.ToString() + " | " + s;
                return View(item);
            }
        }


        [HttpGet]
        public async Task<IActionResult> CreateRoles()
        {
            string[] roleNames = { "Admin", "User ", "Manager" };
            IdentityResult roleResult;

            foreach (var roleName in roleNames)
            {
                var roleExist = await _roleManager.RoleExistsAsync(roleName);
                if (!roleExist)
                {
                    Role role = new Role();
                    role.Name = roleName;
                    roleResult = await _roleManager.CreateAsync(role);
                }
            }
            TempData["msg"] = "نقش ها با موفقیت ایجاد شد.";
            return RedirectToAction("UserIndex", "Accounting");
        }

        [HttpGet]
        public IActionResult LogOut()
        {
            try
            {
                _signManager.SignOutAsync();
                TempData["msg"] = "از حساب کاربری خارج شدید.";
                return RedirectToAction("login", "accounting");
            }
            catch (Exception e)
            {
                TempData["msg"] = e.Message;
            }

            return View();
        }


        [HttpGet]
        public async Task<IActionResult> Delete(string? id)
        {
            var findUser = await _UserManager.FindByIdAsync(id);

            try
            {
                var del = await _UserManager.DeleteAsync(findUser);
            }
            catch (Exception e)
            {
                return Content(e.Message);
            }
            return RedirectToAction("UserIndex", "Accounting");
        }
    }
}