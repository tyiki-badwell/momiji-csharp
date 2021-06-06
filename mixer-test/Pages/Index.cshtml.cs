using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;

namespace mixerTest.Pages
{
    public class IndexModel : PageModel
    {
        private readonly ILogger<IndexModel> _logger;
        private readonly IRunner _runner;

        public IndexModel(ILogger<IndexModel> logger, IRunner runner)
        {
            _logger = logger;
            _runner = runner;
        }

        public void OnGet()
        {

        }
    }
}
