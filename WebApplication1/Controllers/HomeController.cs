using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Momiji.Test.Run;
using System.Diagnostics;
using WebApplication1.Models;

namespace WebApplication1.Controllers
{
    public class HomeController : Controller
    {
        private IConfiguration Configuration { get; }
        private ILoggerFactory LoggerFactory { get; }
        private ILogger Logger { get; }

        public HomeController(IConfiguration configuration, ILoggerFactory loggerFactory)
        {
            Configuration = configuration;
            LoggerFactory = loggerFactory;
            Logger = LoggerFactory.CreateLogger<HomeController>();
        }

        public IActionResult Index()
        {
            return View("Index");
        }

        public IActionResult Start([FromServices]IRunner runner)
        {
            runner.Start();
            return View("Index");
        }

        public IActionResult Stop([FromServices]IRunner runner)
        {
            runner.Stop();
            return View("Index");
        }

        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
