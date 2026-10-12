
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Rasam.Data;
using Rasam.Models.MetaData;

namespace Rasam.Controllers
{
    [Authorize]
    public class MetaDataController : Controller
    {
        private readonly ApplicationDbContext _context;

        public MetaDataController(
            ApplicationDbContext context)
        {
            _context = context;
        }


        // =========================================================
        // TOPICS
        // =========================================================

        [HttpGet]
        public async Task<IActionResult> GetTopics()
        {
            var topics =
                await _context.Topic
                    .OrderBy(x => x.Title)
                    .Select(x => new
                    {
                        id = x.Id,
                        title = x.Title
                    })
                    .ToListAsync();

            return Json(topics);
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult>CreateTopic(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "عنوان موضوع الزامی است."
                });
            }

            title = title.Trim();

            if (await _context.Topic.AnyAsync(
                x => x.Title == title))
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "این موضوع قبلاً ثبت شده است."
                });
            }

            var topic =
                new Topic
                {
                    Title = title
                };

            _context.Topic.Add(topic);

            await _context.SaveChangesAsync();

            return Json(new
            {
                success = true,
                id = topic.Id,
                title = topic.Title
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteTopic(int id)
        {
            var topic = await _context.Topic.FirstOrDefaultAsync(x => x.Id == id);
            if (topic == null)
            {
                return NotFound(new { success = false, message = "موضوع پیدا نشد." });
            }

            _context.Topic.Remove(topic);
            await _context.SaveChangesAsync();

            return Json(new { success = true });
        }
        // =========================================================
        // TAGS
        // =========================================================

        [HttpGet]
        public async Task<IActionResult> GetTags()
        {
            var tags =
                await _context.Tag
                    .OrderBy(x => x.Title)
                    .Select(x => new
                    {
                        id = x.Id,
                        title = x.Title
                    })
                    .ToListAsync();

            return Json(tags);
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateTag(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "عنوان تگ الزامی است."
                });
            }

            title = title.Trim();

            if (await _context.Tag.AnyAsync(
                x => x.Title == title))
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "این تگ قبلاً ثبت شده است."
                });
            }

            var tag =
                new Tag
                {
                    Title = title
                };

            _context.Tag.Add(tag);

            await _context.SaveChangesAsync();

            return Json(new
            {
                success = true,
                id = tag.Id,
                title = tag.Title
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteTag(int id)
        {
            var tag = await _context.Tag.FirstOrDefaultAsync(x => x.Id == id);
            if (tag == null)
            {
                return NotFound(new { success = false, message = "تگ پیدا نشد." });
            }

            _context.Tag.Remove(tag);
            await _context.SaveChangesAsync();

            return Json(new { success = true });
        }
        // =========================================================
        // RELATIONS
        // =========================================================

        [HttpGet]
        public async Task<IActionResult> GetRelations()
        {
            var relations =
                await _context.Relation
                    .OrderBy(x => x.Type)
                    .Select(x => new
                    {
                        id = x.Id,
                        type = x.Type
                    })
                    .ToListAsync();

            return Json(relations);
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult>CreateRelation(string type)
        {
            if (string.IsNullOrWhiteSpace(type))
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "نوع ارتباط الزامی است."
                });
            }

            type = type.Trim();

            if (await _context.Relation.AnyAsync(
                x => x.Type == type))
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "این نوع ارتباط قبلاً ثبت شده است."
                });
            }

            var relation =
                new Relation
                {
                    Type = type
                };

            _context.Relation.Add(relation);

            await _context.SaveChangesAsync();

            return Json(new
            {
                success = true,
                id = relation.Id,
                type = relation.Type
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteRelation(int id)
        {
            var relation = await _context.Relation.FirstOrDefaultAsync(x => x.Id == id);
            if (relation == null)
            {
                return NotFound(new { success = false, message = "نوع ارتباط پیدا نشد." });
            }

            _context.Relation.Remove(relation);
            await _context.SaveChangesAsync();

            return Json(new { success = true });
        }
        // =========================================================
        // SOCIAL MEDIA
        // =========================================================

        [HttpGet]
        public async Task<IActionResult>GetSocialMedia()
        {
            var socialMedia =
                await _context.SocialMedia
                    .OrderBy(x => x.Name)
                    .ThenBy(x => x.Username)
                    .Select(x => new
                    {
                        id = x.Id,
                        name = x.Name,
                        username = x.Username
                    })
                    .ToListAsync();

            return Json(socialMedia);
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult>
            CreateSocialMedia(
                string name,
                string? username)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "نام شبکه اجتماعی الزامی است."
                });
            }

            name = name.Trim();

            username =
                string.IsNullOrWhiteSpace(username)
                    ? null
                    : username.Trim();


            var exists =
                await _context.SocialMedia.AnyAsync(
                    x =>
                        x.Name == name &&
                        x.Username == username
                );


            if (exists)
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "این حساب شبکه اجتماعی قبلاً ثبت شده است."
                });
            }


            var socialMedia =
                new SocialMedia
                {
                    Name = name,
                    Username = username
                };


            _context.SocialMedia.Add(
                socialMedia
            );


            await _context.SaveChangesAsync();


            return Json(new
            {
                success = true,
                id = socialMedia.Id,
                name = socialMedia.Name,
                username = socialMedia.Username
            });
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult>
            DeleteSocialMedia(int id)
        {
            var social =
                await _context.SocialMedia
                    .FirstOrDefaultAsync(
                        x => x.Id == id
                    );


            if (social == null)
            {
                return NotFound(new
                {
                    success = false,
                    message =
                        "حساب شبکه اجتماعی پیدا نشد."
                });
            }


            _context.SocialMedia.Remove(
                social
            );

            await _context.SaveChangesAsync();


            return Json(new
            {
                success = true
            });
        }
    }

}
 
