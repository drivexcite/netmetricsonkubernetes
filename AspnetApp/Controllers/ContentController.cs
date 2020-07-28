using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using AspnetApp.Dtos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace AspnetApp.Controllers
{
    [ApiController]
    public class ContentController : ControllerBase
    {
        private readonly ILogger<ContentController> _logger;
        public static Dictionary<string, string> Hwids { get; } = new Dictionary<string, string>();

        public ContentController(ILogger<ContentController> logger)
        {
            _logger = logger;
        }

        [HttpGet]
        [Route("articles")]
        public IActionResult GetArticles()
        {
            var results = (
                from element in Hwids
                select new { hwid = element.Key, type = element.Value }
            ).ToList();

            results.ForEach(document =>
            {
                _logger.LogInformation($"Retrieved {document.hwid}");
            });

            return Ok(results);
        }

        [HttpGet]
        [Route("articles/{hwid}")]
        public IActionResult GetArticle([NotNull] string hwid)
        {
            if (!Hwids.ContainsKey(hwid))
            {
                _logger.LogWarning($"Missing {hwid}");
                return NotFound("Hwid is missing");
            }
            
            var retrievalDuration = new Random().NextDouble() * 1000;

            _logger.LogInformation($"Retrieved {hwid} in {retrievalDuration}");
            return Ok(new { hwid, type = Hwids[hwid] });
        }

        [HttpPost]
        [Route("articles/{hwid}/ratings/{rating}")]
        public IActionResult RateArticle([FromRoute, NotNull]string hwid, [FromRoute]int rating)
        {
            if (!Hwids.ContainsKey(hwid))
            {
                _logger.LogWarning($"Missing {hwid}");
                return NotFound("Hwid is missing");
            }

            _logger.LogInformation($"Rated content {hwid} as {rating}");

            return Ok();
        }

        [HttpPost]
        [Route("articles")]
        public IActionResult CreateArticle([FromBody]DocumentPost post)
        {
            var hwid = post.Hwid;

            if (Hwids.ContainsKey(hwid))
            {
                return BadRequest($"Already there! {hwid}");
            }

            Hwids.Add(hwid, DateTime.Now.Ticks % 2 == 0 ? "legacy" : "structured");

            _logger.LogWarning($"Created {hwid}");
            return new ObjectResult(hwid) { StatusCode = 201 };
        }

        [HttpDelete]
        [Route("articles/{hwid}")]
        public IActionResult DeleteArticle([NotNull] string hwid)
        {
            if (!Hwids.ContainsKey(hwid))
            {
                _logger.LogWarning($"Missing {hwid}");
                return NotFound("Hwid is missing");
            }

            Hwids.Remove(hwid);

            _logger.LogInformation($"Deleted {hwid}");
            return Ok(hwid);
        }
    }
}
