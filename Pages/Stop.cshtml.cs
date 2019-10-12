using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using Momiji.Test.Run;

namespace mixerTest.Pages
{
    public class StopModel : PageModel
    {
        private readonly ILogger<StopModel> _logger;
        private readonly IRunner _runner;

        public StopModel(ILogger<StopModel> logger, IRunner runner)
        {
            _logger = logger;
            _runner = runner;
        }

        public void OnGet()
        {
            _runner.Stop();
        }
    }
}
