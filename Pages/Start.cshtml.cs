using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using Momiji.Test.Run;

namespace mixerTest.Pages
{
    public class StartModel : PageModel
    {
        private readonly ILogger<StartModel> _logger;
        private readonly IRunner _runner;

        public StartModel(ILogger<StartModel> logger, IRunner runner)
        {
            _logger = logger;
            _runner = runner;
        }

        public void OnGet()
        {
            _runner.Start();
        }
    }
}
