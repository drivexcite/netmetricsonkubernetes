using System.Diagnostics;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Prometheus;

namespace AspnetApp.Controllers
{
    [ApiController]
    public class MetricsController : ControllerBase
    {
        private static readonly HttpClient HttpClient = new HttpClient();
        private static readonly Histogram SomeOperationDuration = Metrics.CreateHistogram("some_operation_get_duration", "Some Operation - Histogram of request time to underlying dependency (in ms)");

        private readonly ILogger<MetricsController> _logger;
        

        public MetricsController(ILogger<MetricsController> logger)
        {
            _logger = logger;
        }

        [HttpGet]
        [Route("someoperation")]
        public async Task<IActionResult> GetPage()
        {
            var stopwWatch = new Stopwatch();
            stopwWatch.Start();

            var response = await HttpClient.GetAsync("https://google.com");
            var result =  new ContentResult
            {
                StatusCode = 200, Content = await response.Content.ReadAsStringAsync(),
                ContentType = response.Content.Headers.ContentType.MediaType
            };

            stopwWatch.Stop();
            SomeOperationDuration.Observe(stopwWatch.ElapsedMilliseconds);

            return result;
        }
    }
}
