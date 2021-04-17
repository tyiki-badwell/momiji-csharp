using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;

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

        public JsonResult OnPostStart()
        {
            return new JsonResult(_runner.Start());
        }
        public JsonResult OnPostCancel()
        {
            return new JsonResult(_runner.Cancel());
        }
    }
}
